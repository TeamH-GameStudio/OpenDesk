"""
AnthropicApiProvider — Anthropic Python SDK + 공통 McpServerHub 로 직접 호출.

인증은 두 가지 모드 지원:
  1) ANTHROPIC_API_KEY 환경변수 (`x-api-key` 헤더, 일반 API)
  2) Claude Max OAuth 토큰 (`Authorization: Bearer ...` + `anthropic-beta` 헤더)
     - `claude login` 으로 발급된 토큰을 `oauth_credentials.load_oauth_credentials` 로 로드
     - API key 보다 OAuth 우선 (사용자가 Pro/Max 구독자라고 가정)
     - refresh_token 으로 자동 갱신

CLI provider 와 정확히 동일한 동작 보장이 목표:
  - 같은 stdio MCP 서버를 같은 `mcp` 패키지로 띄운다 (McpServerHub).
  - In-process ToolRegistry + MCP hub 도구를 합쳐 모델에 노출.
  - tool_use 응답이 오면 in-process 우선 → MCP hub 폴백.
  - 텍스트 delta 는 그대로 흘려보내고, tool_use 진행은 on_status 콜백으로 알린다.
"""

from __future__ import annotations

import logging
import os
from typing import Any, AsyncIterator, Optional

from hooks.protocol import UsageSnapshot
from mcp_client import McpServerHub
from oauth_credentials import (
    ANTHROPIC_OAUTH_BETA,
    OAuthCredentials,
    load_oauth_credentials,
)
from .base import (
    MessageStopEvent,
    ProviderBase,
    ProviderCallbacks,
    StreamEvent,
    TextDeltaEvent,
    ToolUseResultEvent,
    ToolUseStartEvent,
)
from .registry import register_provider

logger = logging.getLogger("provider.anthropic_api")

DEFAULT_MODEL = "claude-sonnet-4-5"
DEFAULT_MAX_TOKENS = 4096
# tool_use 라운드 상한. 라운드마다 system+tools+messages 가 재전송되므로
# 너무 크면 분당 토큰 한도(429) 직격이다. 8 라운드로 충분한 case 가 대부분.
# 호출자가 run_stream(max_tool_rounds=N) 으로 override 할 수 있다.
MAX_TOOL_ROUNDS = 8


def _extract_usage(final_message: Any) -> UsageSnapshot:
    """Anthropic SDK 의 final_message.usage 를 UsageSnapshot 으로 변환.

    SDK 에 따라 usage 가 없거나 일부 필드만 존재할 수 있어 안전하게 0 폴백.
    """
    usage = getattr(final_message, "usage", None)
    if usage is None:
        return UsageSnapshot(available=False)
    return UsageSnapshot(
        input_tokens=int(getattr(usage, "input_tokens", 0) or 0),
        output_tokens=int(getattr(usage, "output_tokens", 0) or 0),
        cache_creation_input_tokens=int(
            getattr(usage, "cache_creation_input_tokens", 0) or 0
        ),
        cache_read_input_tokens=int(
            getattr(usage, "cache_read_input_tokens", 0) or 0
        ),
        available=True,
    )


def _add_usage(a: UsageSnapshot, b: UsageSnapshot) -> UsageSnapshot:
    """두 UsageSnapshot 을 합산. 한쪽이라도 available=False 면 결과도 False."""
    return UsageSnapshot(
        input_tokens=a.input_tokens + b.input_tokens,
        output_tokens=a.output_tokens + b.output_tokens,
        cache_creation_input_tokens=a.cache_creation_input_tokens + b.cache_creation_input_tokens,
        cache_read_input_tokens=a.cache_read_input_tokens + b.cache_read_input_tokens,
        available=a.available and b.available,
    )

# Prompt caching ephemeral 마커 — system/tools/마지막 user 메시지에 부착.
# 동일 prefix 재사용 시 input 토큰 청구가 0.1배 (cache hit) 로 떨어진다.
_CACHE_EPHEMERAL = {"type": "ephemeral"}

# Unity 측 model 식별자의 [variant] 접미사 → Anthropic beta 헤더 매핑.
# Unity 드롭다운(ChatPanelView.AVAILABLE_MODELS) 의 "Opus 4.7 (1M context)" 항목은
# "claude-opus-4-7[1m]" 을 PlayerPrefs 에 저장하지만, 이는 Anthropic API 가 모르는
# 변종 표기 — 그대로 보내면 404 not_found_error 가 난다. 여기서 변종을 분해해
# (정식 model ID, 활성화할 beta 헤더 리스트) 로 변환한다.
_BETA_BY_VARIANT: dict[str, str] = {
    "[1m]": "context-1m-2025-08-07",  # 1M context window
}


def _resolve_model_variant(raw: str) -> tuple[str, list[str]]:
    """Unity 측 model 식별자에서 [variant] 접미사를 분리.

    반환: (정식 Anthropic model ID, 활성화할 beta 헤더 리스트)
    """
    if not raw:
        return ("", [])
    stripped = raw.strip()
    for suffix, beta in _BETA_BY_VARIANT.items():
        if stripped.endswith(suffix):
            return (stripped[: -len(suffix)], [beta])
    # 알려진 매핑이 없어도 [..] 접미사가 있으면 strip — Anthropic API 는 어차피
    # 못 알아들으니 404 보다는 기본 모델로 동작하는 편이 낫다.
    if stripped.endswith("]") and "[" in stripped:
        bracket = stripped.rfind("[")
        return (stripped[:bracket], [])
    return (stripped, [])


class AnthropicApiProvider(ProviderBase):
    name = "anthropic_api"

    def __init__(self, api_key: str = "", default_model: str = DEFAULT_MODEL):
        self._api_key = api_key or os.environ.get("ANTHROPIC_API_KEY", "")
        self._default_model = default_model
        self._hub: Optional[McpServerHub] = None
        self._cancelled = False
        self._oauth: Optional[OAuthCredentials] = None
        # OAuth 시도 여부 — check_available / chat 첫 호출에서 lazy load.
        self._oauth_attempted = False

    async def _ensure_oauth(self) -> None:
        """OAuth credentials lazy 로드. API key 없을 때만 의미 있음."""
        if self._oauth_attempted:
            return
        self._oauth_attempted = True
        config_dir = os.environ.get("CLAUDE_CONFIG_DIR")
        try:
            self._oauth = await load_oauth_credentials(config_dir=config_dir)
        except Exception as e:  # noqa: BLE001
            logger.warning("OAuth credentials 로드 실패: %s", e)
            self._oauth = None

    def invalidate_oauth_cache(self) -> None:
        """`claude login` 같은 외부 인증 이벤트 후 호출. 다음 chat 시 credentials 를 다시 로드."""
        self._oauth_attempted = False
        self._oauth = None

    async def check_available(self) -> tuple[bool, str]:
        try:
            import anthropic  # noqa: F401
        except ImportError:
            return False, "anthropic_sdk_not_installed"

        if self._api_key:
            return True, "anthropic-sdk (api_key)"

        await self._ensure_oauth()
        if self._oauth is not None and self._oauth.access_token:
            return True, "anthropic-sdk (oauth)"

        return False, "anthropic_api_key_missing"

    async def chat(
        self,
        *,
        messages: list[dict[str, Any]],
        system_prompt: str = "",
        mcp_config: Optional[dict[str, Any]] = None,
        model: str = "",
        callbacks: ProviderCallbacks,
        in_process_tools=None,
    ) -> None:
        if not messages:
            await callbacks.on_error("메시지가 비어있습니다", "empty_message")
            return

        try:
            from anthropic import AsyncAnthropic  # noqa: F401
        except ImportError:
            await callbacks.on_error(
                "anthropic 패키지가 필요합니다. `pip install anthropic`",
                "anthropic_sdk_not_installed",
            )
            return

        client = await self._build_client()
        if client is None:
            await callbacks.on_error(
                "Anthropic 인증이 필요합니다. ANTHROPIC_API_KEY 를 설정하거나 `claude login` 으로 OAuth 인증을 완료하세요.",
                "anthropic_api_key_missing",
            )
            return

        self._cancelled = False

        # MCP 서버 hub 준비
        await self._ensure_hub(mcp_config)
        tools_for_anthropic = await self._build_tools(in_process_tools)
        # Unity 측 model 의 [variant] 접미사 → Anthropic model + beta 헤더 분해.
        # 예: "claude-opus-4-7[1m]" → ("claude-opus-4-7", ["context-1m-2025-08-07"]).
        chosen_model, variant_betas = _resolve_model_variant(model or self._default_model)

        # OAuth path 는 _build_client 가 oauth-2025-04-20 베타를 default_headers 에
        # 부착하므로 그것까지 합쳐서 stream 호출에 넘긴다 (extra_headers 가 default 를
        # override 할 수 있어 직접 합산하는 게 안전).
        beta_set: list[str] = list(variant_betas)
        if self._oauth is not None and not self._api_key and ANTHROPIC_OAUTH_BETA not in beta_set:
            beta_set.append(ANTHROPIC_OAUTH_BETA)

        # 입력 messages 를 복사해서 tool_use 루프 동안 누적시킨다.
        working_messages: list[dict[str, Any]] = [dict(m) for m in messages]
        accumulated_text = ""
        total_cost = 0.0

        try:
            for _round in range(MAX_TOOL_ROUNDS):
                if self._cancelled:
                    await callbacks.on_error("사용자에 의해 중단됨", "user_cancelled")
                    return

                stream_args: dict[str, Any] = dict(
                    model=chosen_model,
                    max_tokens=DEFAULT_MAX_TOKENS,
                    messages=_with_message_cache(working_messages),
                )
                if beta_set:
                    stream_args["extra_headers"] = {"anthropic-beta": ",".join(beta_set)}
                if system_prompt:
                    stream_args["system"] = _system_with_cache(system_prompt)
                if tools_for_anthropic:
                    stream_args["tools"] = _tools_with_cache(tools_for_anthropic)

                final_message = await self._stream_round(
                    client=client,
                    stream_args=stream_args,
                    callbacks=callbacks,
                    accumulated_text_in=accumulated_text,
                )
                if final_message is None:
                    return  # error 콜백 이미 호출됨

                # 비용 누적 (usage.input_tokens / output_tokens 기반 가격 추산 — 정확한 값은 Anthropic 콘솔에서)
                # SDK 가 직접 비용을 제공하지 않으므로 0.0 유지 (CostMonitor 가 별도 추적)

                stop_reason = getattr(final_message, "stop_reason", "")
                # assistant 메시지 누적
                working_messages.append({"role": "assistant", "content": final_message.content})

                if stop_reason == "tool_use":
                    tool_results = await self._execute_tool_uses(final_message, callbacks, in_process_tools)
                    if tool_results is None:
                        return  # error 콜백 호출됨
                    working_messages.append({"role": "user", "content": tool_results})
                    continue

                # 최종 텍스트 추출
                text_parts: list[str] = []
                for block in final_message.content:
                    if getattr(block, "type", "") == "text":
                        text_parts.append(getattr(block, "text", ""))
                final_text = "".join(text_parts)
                await callbacks.on_final(final_text, total_cost)
                return

            await callbacks.on_error(
                f"tool_use 루프가 {MAX_TOOL_ROUNDS}회를 초과했습니다",
                "tool_loop_exceeded",
            )

        except Exception as e:  # noqa: BLE001
            logger.error("anthropic_api chat 실패: %s", e)
            await callbacks.on_error(str(e), "anthropic_api_error")

    def kill_active(self) -> None:
        self._cancelled = True

    # ── 신규 스트림 인터페이스 (native) ────────────────────────────

    async def run_stream(
        self,
        *,
        messages: list[dict[str, Any]],
        system_prompt: str = "",
        mcp_config: Optional[dict[str, Any]] = None,
        model: str = "",
        in_process_tools=None,
        max_tool_rounds: Optional[int] = None,
    ) -> AsyncIterator[StreamEvent]:
        """Native 스트림. content_block_delta 를 그대로 yield.

        tool_use 라운드 사이에는 ToolUseStart/Result 이벤트가 끼어든다.
        호출자는 각 라운드의 텍스트 발화 시작/종료를 직접 감지할 수 있다.

        max_tool_rounds 가 None 이면 모듈 상수 `MAX_TOOL_ROUNDS` 사용.
        Dynamic max_tool_rounds (cache hit 률 기반 조정) 는 HookedProvider 가 결정해 주입.
        """
        tool_round_limit = (
            int(max_tool_rounds) if max_tool_rounds is not None and max_tool_rounds > 0
            else MAX_TOOL_ROUNDS
        )
        if not messages:
            yield MessageStopEvent(
                reason="error",
                error_message="메시지가 비어있습니다",
                error_code="empty_message",
            )
            return

        try:
            from anthropic import AsyncAnthropic  # noqa: F401
        except ImportError:
            yield MessageStopEvent(
                reason="error",
                error_message="anthropic 패키지가 필요합니다",
                error_code="anthropic_sdk_not_installed",
            )
            return

        client = await self._build_client()
        if client is None:
            yield MessageStopEvent(
                reason="error",
                error_message="Anthropic 인증이 필요합니다. ANTHROPIC_API_KEY 또는 `claude login` OAuth 가 필요합니다.",
                error_code="anthropic_api_key_missing",
            )
            return

        self._cancelled = False
        await self._ensure_hub(mcp_config)
        tools_for_anthropic = await self._build_tools(in_process_tools)
        chosen_model = model or self._default_model

        working_messages: list[dict[str, Any]] = [dict(m) for m in messages]
        accumulated_text_parts: list[str] = []
        # tool round 별 usage 를 누적. 각 round 의 final_message.usage 가 별도로 보고됨.
        total_usage = UsageSnapshot()

        try:
            for _round in range(tool_round_limit):
                if self._cancelled:
                    yield MessageStopEvent(
                        reason="interrupted",
                        accumulated_text="".join(accumulated_text_parts),
                        error_message="사용자에 의해 중단됨",
                        error_code="user_cancelled",
                        usage=total_usage,
                    )
                    return

                stream_args: dict[str, Any] = dict(
                    model=chosen_model,
                    max_tokens=DEFAULT_MAX_TOKENS,
                    messages=_with_message_cache(working_messages),
                )
                if system_prompt:
                    stream_args["system"] = _system_with_cache(system_prompt)
                if tools_for_anthropic:
                    stream_args["tools"] = _tools_with_cache(tools_for_anthropic)

                final_message = None
                try:
                    async with client.messages.stream(**stream_args) as stream:
                        async for event in stream:
                            if self._cancelled:
                                break
                            event_type = getattr(event, "type", "")
                            if event_type == "content_block_delta":
                                delta = getattr(event, "delta", None)
                                delta_type = getattr(delta, "type", "") if delta else ""
                                if delta_type == "text_delta":
                                    text = getattr(delta, "text", "")
                                    if text:
                                        accumulated_text_parts.append(text)
                                        yield TextDeltaEvent(text=text)
                        final_message = await stream.get_final_message()
                except Exception as e:  # noqa: BLE001
                    logger.error("anthropic stream 실패: %s", e)
                    yield MessageStopEvent(
                        reason="error",
                        accumulated_text="".join(accumulated_text_parts),
                        error_message=str(e),
                        error_code="anthropic_api_error",
                        usage=total_usage,
                    )
                    return

                if final_message is None:
                    yield MessageStopEvent(
                        reason="interrupted",
                        accumulated_text="".join(accumulated_text_parts),
                        usage=total_usage,
                    )
                    return

                # 이번 round 의 usage 를 누적.
                total_usage = _add_usage(total_usage, _extract_usage(final_message))

                stop_reason = getattr(final_message, "stop_reason", "")
                working_messages.append({"role": "assistant", "content": final_message.content})

                if stop_reason == "tool_use":
                    tool_results: list[dict[str, Any]] = []
                    for block in final_message.content:
                        if getattr(block, "type", "") != "tool_use":
                            continue
                        tool_name = getattr(block, "name", "")
                        tool_input = getattr(block, "input", {}) or {}
                        tool_id = getattr(block, "id", "")
                        yield ToolUseStartEvent(
                            tool_use_id=tool_id,
                            name=tool_name,
                            input=tool_input,
                        )
                        # in-process 우선 → MCP hub 폴백.
                        result_text = await self._call_merged_tool(
                            tool_name, tool_input, in_process_tools,
                        )
                        yield ToolUseResultEvent(
                            tool_use_id=tool_id,
                            name=tool_name,
                            result=result_text,
                        )
                        tool_results.append({
                            "type": "tool_result",
                            "tool_use_id": tool_id,
                            "content": result_text,
                        })
                    working_messages.append({"role": "user", "content": tool_results})
                    continue

                yield MessageStopEvent(
                    reason="complete",
                    accumulated_text="".join(accumulated_text_parts),
                    usage=total_usage,
                )
                return

            yield MessageStopEvent(
                reason="error",
                accumulated_text="".join(accumulated_text_parts),
                error_message=f"tool_use 루프가 {tool_round_limit}회를 초과했습니다",
                error_code="tool_loop_exceeded",
                usage=total_usage,
            )
        except Exception as e:  # noqa: BLE001
            logger.error("anthropic_api run_stream 실패: %s", e)
            yield MessageStopEvent(
                reason="error",
                accumulated_text="".join(accumulated_text_parts),
                error_message=str(e),
                error_code="anthropic_api_error",
                usage=total_usage,
            )

    # ── 내부 ────────────────────────────────────────────────────

    async def _build_client(self):
        """API key 또는 OAuth 토큰으로 AsyncAnthropic 인스턴스 생성.

        우선순위: API key → OAuth. 둘 다 없으면 None 반환.
        OAuth 일 경우 anthropic-beta 헤더를 자동 부착.
        """
        try:
            from anthropic import AsyncAnthropic
        except ImportError:
            return None

        if self._api_key:
            return AsyncAnthropic(api_key=self._api_key)

        await self._ensure_oauth()
        if self._oauth is None or not self._oauth.access_token:
            return None

        # 만료 직전이면 refresh 시도. 실패해도 일단 보낸다 (서버가 401 주면 그때 처리).
        try:
            await self._oauth.ensure_fresh()
        except Exception as e:  # noqa: BLE001
            logger.warning("OAuth refresh 실패 (계속 진행): %s", e)

        return AsyncAnthropic(
            auth_token=self._oauth.access_token,
            default_headers={"anthropic-beta": ANTHROPIC_OAUTH_BETA},
        )

    async def _ensure_hub(self, mcp_config: Optional[dict[str, Any]]) -> None:
        servers = (mcp_config or {}).get("servers") or []
        if not servers:
            if self._hub is not None:
                await self._hub.shutdown()
                self._hub = None
            return
        if self._hub is None:
            self._hub = McpServerHub()
        try:
            await self._hub.set_servers(servers)
        except Exception as e:  # noqa: BLE001
            logger.error("MCP hub 초기화 실패: %s", e)

    async def _build_tools(self, in_process_tools=None) -> list[dict[str, Any]]:
        """in-process 도구 + MCP hub 도구를 합쳐 Anthropic tools 스키마 반환.
        이름 충돌 시 in-process 가 우선 (호스트 도구가 신뢰성 높다).
        """
        result: list[dict[str, Any]] = []
        seen: set[str] = set()

        if in_process_tools is not None:
            for schema in in_process_tools.to_anthropic_schemas():
                name = schema.get("name")
                if not name or name in seen:
                    continue
                seen.add(name)
                result.append(schema)

        if self._hub is not None:
            infos = await self._hub.list_tools()
            for info in infos:
                if info.name in seen:
                    logger.warning(
                        "tool name conflict: '%s' (in-process 우선, MCP hub 버전 무시)", info.name
                    )
                    continue
                seen.add(info.name)
                schema = info.input_schema or {"type": "object", "properties": {}}
                result.append({
                    "name": info.name,
                    "description": info.description,
                    "input_schema": schema,
                })
        return result

    async def _call_merged_tool(
        self, name: str, args: dict[str, Any], in_process_tools=None,
    ) -> str:
        """in-process 우선 → MCP hub 폴백 도구 호출."""
        if in_process_tools is not None:
            tool = in_process_tools.get(name)
            if tool is not None:
                try:
                    return await tool.execute(args)
                except Exception as e:  # noqa: BLE001
                    logger.exception("in-process tool '%s' 실행 실패", name)
                    return f"Tool error: {e}"
        if self._hub is not None:
            return await self._hub.call_tool(name, args)
        return f"Error: tool '{name}' not found (no in-process registry, no MCP hub)"

    async def _stream_round(
        self,
        *,
        client: Any,
        stream_args: dict[str, Any],
        callbacks: ProviderCallbacks,
        accumulated_text_in: str,
    ):
        try:
            async with client.messages.stream(**stream_args) as stream:
                async for event in stream:
                    if self._cancelled:
                        break
                    event_type = getattr(event, "type", "")
                    if event_type == "content_block_delta":
                        delta = getattr(event, "delta", None)
                        delta_type = getattr(delta, "type", "") if delta else ""
                        if delta_type == "text_delta":
                            text = getattr(delta, "text", "")
                            if text:
                                await callbacks.on_delta(text)
                    elif event_type == "content_block_start":
                        block = getattr(event, "content_block", None)
                        if block and getattr(block, "type", "") == "tool_use" and callbacks.on_status:
                            await callbacks.on_status(f"도구 호출: {getattr(block, 'name', '도구')}")
                return await stream.get_final_message()
        except Exception as e:  # noqa: BLE001
            logger.error("anthropic stream 실패: %s", e)
            await callbacks.on_error(str(e), "anthropic_api_error")
            return None

    async def _execute_tool_uses(
        self, final_message: Any, callbacks: ProviderCallbacks, in_process_tools=None,
    ) -> Optional[list[dict[str, Any]]]:
        # in-process 도구도 hub 도구도 없으면 에러.
        if self._hub is None and (in_process_tools is None or len(getattr(in_process_tools, "_tools", {})) == 0):
            await callbacks.on_error(
                "도구가 활성화되지 않았는데 tool_use 가 요청됨 (MCP hub + in-process 둘 다 없음)",
                "anthropic_api_error",
            )
            return None

        tool_results: list[dict[str, Any]] = []
        for block in final_message.content:
            if getattr(block, "type", "") != "tool_use":
                continue
            tool_name = getattr(block, "name", "")
            tool_input = getattr(block, "input", {}) or {}
            tool_id = getattr(block, "id", "")

            if callbacks.on_status:
                await callbacks.on_status(f"도구 실행: {tool_name}")

            result_text = await self._call_merged_tool(tool_name, tool_input, in_process_tools)

            if callbacks.on_status:
                await callbacks.on_status("도구 결과 수신")

            # tool_journal 에 활동 기록 — LLM history 와 분리. AI 가
            # read_tool_history 도구로 명시적 조회할 때만 사용된다.
            if callbacks.on_tool_round:
                try:
                    await callbacks.on_tool_round(tool_name, tool_input, result_text)
                except Exception as e:  # noqa: BLE001
                    logger.warning("on_tool_round 콜백 실패 (무시): %s", e)

            tool_results.append({
                "type": "tool_result",
                "tool_use_id": tool_id,
                "content": result_text,
            })
        return tool_results


def _system_with_cache(system_prompt: str) -> list[dict[str, Any]]:
    """system 을 단일 text block 리스트로 감싸고 끝에 cache_control 부착.
    동일 system prompt 재사용 시 입력 토큰의 90% 가 캐시 hit 로 환급.
    """
    return [
        {"type": "text", "text": system_prompt, "cache_control": _CACHE_EPHEMERAL},
    ]


def _tools_with_cache(tools: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """tools 의 마지막 정의에 cache_control 부착 → tools 블록 전체 캐싱.
    MCP 서버 set 이 바뀌지 않는 한 tool schema 도 캐시 hit.
    """
    if not tools:
        return tools
    out = list(tools[:-1])
    last = dict(tools[-1])
    last["cache_control"] = _CACHE_EPHEMERAL
    out.append(last)
    return out


def _with_message_cache(messages: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """대화 prefix 를 캐싱하기 위해 마지막 user 메시지의 마지막 content block 에
    cache_control 부착. assistant 메시지(SDK 객체) 는 건드리지 않는다.

    이렇게 하면 다음 라운드에서 이전까지의 system+tools+messages prefix 가
    cache hit → tool_use 루프 중에도 input 토큰이 폭발하지 않는다.
    """
    if not messages:
        return messages
    out: list[dict[str, Any]] = list(messages[:-1])
    last = messages[-1]
    if not isinstance(last, dict) or last.get("role") != "user":
        out.append(last)
        return out

    content = last.get("content")
    if isinstance(content, str):
        new_last = {
            **last,
            "content": [
                {"type": "text", "text": content, "cache_control": _CACHE_EPHEMERAL},
            ],
        }
    elif isinstance(content, list) and content:
        # tool_result 등 블록 리스트 — 마지막 dict 블록에만 cache_control 추가.
        new_content: list[Any] = []
        marked = False
        for block in reversed(content):
            if not marked and isinstance(block, dict):
                new_content.append({**block, "cache_control": _CACHE_EPHEMERAL})
                marked = True
            else:
                new_content.append(block)
        new_content.reverse()
        new_last = {**last, "content": new_content}
    else:
        new_last = last
    out.append(new_last)
    return out


def _make_provider() -> AnthropicApiProvider:
    api_key = os.environ.get("ANTHROPIC_API_KEY", "")
    default_model = os.environ.get("OPENDESK_ANTHROPIC_MODEL", DEFAULT_MODEL)
    return AnthropicApiProvider(api_key=api_key, default_model=default_model)


register_provider("anthropic_api", _make_provider)
