"""route_capability — 스킬이 capability 만 호출하면 미들웨어가 플러그인을 라우팅한다.

"4 Noes" 원칙: 스킬 작성자는 어떤 플러그인이 그 capability 를 제공하는지 신경 쓰지 않는다.
스킬 한 줄 ``plugin = route_capability("calendar.create_event")`` 로 끝나야 하고,
미들웨어가 다음 분기를 자동 수행한다:

  0 호환  → ``need_plugin`` 결과 — 사용자가 직접 마켓에서 설치하도록 안내
  1 호환  → 묻지 않고 그 플러그인 즉시 반환
  선호  → 묻지 않고 저장된 선호 반환 (사용자가 과거에 "다음부터 자동" 체크함)
  2+ & 선호 없음 → AskUserPort 로 인라인 카드 띄움 (payload_kind="capability_pick")
                  사용자 응답에 ``remember=true`` 면 RoutePreferenceStore 에 저장.

ask_user 와 같은 라운드트립 인프라 (AskUserPort) 를 재사용하지만 payload 에 discriminator
``payload_kind="capability_pick"`` + ``capability`` + ``compatible_plugins`` 를 실어
Unity 측이 enriched picker 카드를 렌더한다.
"""

from __future__ import annotations

import json
import logging
import uuid
from typing import Protocol

from route_capability_infra import (
    PluginEntry,
    PluginRegistryPort,
    RoutePreferenceStorePort,
)

from .base import BaseTool

logger = logging.getLogger("route_capability")


class AskUserPort(Protocol):
    """``tools.ask_user.AskUserPort`` 와 시그니처 동일 — 명시적 협의 위해 재선언."""

    async def ask(self, agent_id: str, tool_use_id: str, payload: dict) -> dict: ...


class RouteCapabilityTool(BaseTool):
    def __init__(
        self,
        registry: PluginRegistryPort,
        preferences: RoutePreferenceStorePort,
        ask_port: AskUserPort,
        agent_id: str,
        timeout_seconds: float = 300.0,
    ):
        self._registry = registry
        self._preferences = preferences
        self._ask_port = ask_port
        self._agent_id = agent_id
        self._timeout = timeout_seconds

    @property
    def name(self) -> str:
        return "route_capability"

    @property
    def description(self) -> str:
        return (
            "Resolve a capability name to a concrete plugin_id installed for this agent. "
            "Use this BEFORE calling any plugin tool — never hardcode plugin_ids in skills. "
            "Returns {plugin_id, plugin_name, vendor, capability}. "
            "If no compatible plugin is installed, returns {error:'no_compatible_plugin'} — "
            "in that case tell the user which plugin to install, do not retry."
        )

    @property
    def parameters(self) -> dict:
        return {
            "type": "object",
            "properties": {
                "capability": {
                    "type": "string",
                    "description": (
                        "The capability the skill needs (e.g. 'calendar.create_event', "
                        "'mail.send', 'doc.create'). Use dotted namespacing — never the "
                        "plugin's own internal action name."
                    ),
                },
                "ask_message": {
                    "type": "string",
                    "description": (
                        "Optional colleague-voiced question shown if the user needs to pick "
                        "between multiple compatible plugins. Example: '회의 일정을 잡으려는데, "
                        "어떤 캘린더에 추가할까요?'. If omitted a generic prompt is used."
                    ),
                },
            },
            "required": ["capability"],
        }

    # ── 핵심 분기 ─────────────────────────────────────────

    async def execute(self, args: dict) -> str:
        capability = (args.get("capability") or "").strip()
        if not capability:
            return json.dumps({"error": "missing_capability"}, ensure_ascii=False)

        compatible = self._registry.list_compatible(capability)
        if not compatible:
            return json.dumps(
                {
                    "error": "no_compatible_plugin",
                    "capability": capability,
                    "message": (
                        f"이 작업에 필요한 도구가 아직 연결되어 있지 않아요. "
                        f"플러그인 마켓에서 '{capability}' 를 지원하는 플러그인을 설치한 뒤 다시 시도해주세요."
                    ),
                },
                ensure_ascii=False,
            )

        if len(compatible) == 1:
            return self._success_json(compatible[0], capability, source="single")

        preferred_id = self._preferences.get(self._agent_id, capability)
        if preferred_id:
            preferred = next((p for p in compatible if p.id == preferred_id), None)
            if preferred is not None:
                return self._success_json(preferred, capability, source="preference")
            # 저장된 선호가 더 이상 호환 목록에 없음 — 무시하고 사용자에게 다시 묻는다.

        chosen, remember = await self._ask_user(capability, args.get("ask_message"), compatible)
        if chosen is None:
            return json.dumps(
                {
                    "error": "user_skipped",
                    "capability": capability,
                    "message": "사용자가 도구 선택을 미뤘어요. 다음에 다시 물어볼게요.",
                },
                ensure_ascii=False,
            )

        if remember:
            self._preferences.set(self._agent_id, capability, chosen.id)

        return self._success_json(chosen, capability, source="ask", remembered=remember)

    # ── 인라인 카드 라운드트립 ────────────────────────────

    async def _ask_user(
        self,
        capability: str,
        ask_message: str | None,
        compatible: list[PluginEntry],
    ) -> tuple[PluginEntry | None, bool]:
        tool_use_id = f"route_{uuid.uuid4().hex[:16]}"
        ask = (ask_message or "").strip() or _default_ask_for(capability)

        payload = {
            "payload_kind": "capability_pick",
            "capability": capability,
            "question": ask,
            "header": "도구 선택",
            "multi_select": False,
            # 옵션은 ask_user 호환 형식으로도 같이 실어 보낸다 — Unity 가 capability_pick 카드를
            # 렌더하지 못하는 구버전이어도 일반 단일 선택 카드로 fallback 가능.
            "options": [
                {"label": p.display_name, "description": _describe_plugin(p)}
                for p in compatible
            ],
            "compatible_plugins": [
                {
                    "id": p.id,
                    "display_name": p.display_name,
                    "vendor": p.vendor,
                    "author": p.author,
                }
                for p in compatible
            ],
        }

        try:
            answer = await self._ask_port.ask(self._agent_id, tool_use_id, payload)
        except Exception as e:
            logger.warning("route_capability ask 실패: %s", e)
            return None, False

        return _interpret_answer(answer, compatible)

    # ── 결과 직렬화 ───────────────────────────────────────

    @staticmethod
    def _success_json(
        plugin: PluginEntry, capability: str, *, source: str, remembered: bool = False
    ) -> str:
        return json.dumps(
            {
                "plugin_id": plugin.id,
                "plugin_name": plugin.display_name,
                "vendor": plugin.vendor,
                "capability": capability,
                "source": source,           # single | preference | ask
                "remembered": remembered,
            },
            ensure_ascii=False,
        )


# ── 헬퍼 ────────────────────────────────────────────────


def _describe_plugin(p: PluginEntry) -> str:
    parts = []
    if p.vendor:
        parts.append(p.vendor)
    if p.author and p.author != p.vendor:
        parts.append(f"by {p.author}")
    return " · ".join(parts) if parts else "플러그인"


def _default_ask_for(capability: str) -> str:
    """Capability 카테고리에 따라 자연스러운 1인칭 카피를 생성."""
    cap = capability.lower()
    if cap.startswith("calendar"):
        return "어떤 캘린더에 추가할까요?"
    if cap.startswith("mail"):
        return "어떤 메일 계정으로 보낼까요?"
    if cap.startswith("doc") or cap.startswith("file"):
        return "어떤 문서 도구를 쓸까요?"
    if cap.startswith("chat") or cap.startswith("message"):
        return "어떤 메신저로 보낼까요?"
    if cap.startswith("issue"):
        return "어떤 이슈 트래커에 등록할까요?"
    return f"이 작업({capability}) 에 어떤 도구를 쓸까요?"


def _interpret_answer(
    answer: dict | None, compatible: list[PluginEntry]
) -> tuple[PluginEntry | None, bool]:
    """ask_user 응답 형식 (``{response, selected, remember}``) → (선택된 플러그인, 기억 여부)."""
    if not isinstance(answer, dict):
        return None, False

    remember = bool(answer.get("remember", False))
    selected = answer.get("selected") or []
    by_name = {p.display_name: p for p in compatible}
    by_id = {p.id: p for p in compatible}

    if isinstance(selected, list) and selected:
        label = selected[0]
        if isinstance(label, str):
            plugin = by_name.get(label) or by_id.get(label)
            if plugin is not None:
                return plugin, remember

    # 자유 입력으로 id/이름 정확히 적은 경우 (fallback)
    free = (answer.get("response") or "").strip()
    if free:
        plugin = by_id.get(free) or by_name.get(free)
        if plugin is not None:
            return plugin, remember

    return None, False
