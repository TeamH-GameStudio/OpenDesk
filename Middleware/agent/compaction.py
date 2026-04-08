"""
대화 압축 (Compaction).

대화가 길어지면 토큰이 급증하므로, 이전 대화를 요약본으로 교체.
- Haiku 모델로 요약 (저비용)
- 원본 메시지 배열 -> [요약 user/assistant 2턴] + 최근 N턴
- runner.py에서 메시지 수 기준으로 자동 트리거
"""

import anthropic
import logging

logger = logging.getLogger("compaction")

# 압축 트리거 기준
COMPACTION_THRESHOLD = 30  # 메시지 수 이 이상이면 압축
KEEP_RECENT = 6            # 최근 N개 메시지는 유지 (압축 대상에서 제외)
SUMMARY_MODEL = "claude-haiku-4-5-20251001"


async def compact_messages(
    client: anthropic.Anthropic,
    messages: list,
    agent_role: str = "AI",  # noqa: ARG001 — reserved for future role-aware summaries
    model: str = SUMMARY_MODEL,
) -> list:
    """
    긴 대화를 요약하여 압축된 메시지 배열을 반환.

    Returns:
        [요약 user msg, 요약 assistant msg] + messages[-KEEP_RECENT:]
    """
    if len(messages) <= COMPACTION_THRESHOLD:
        return messages  # 압축 불필요

    # 압축 대상: 최근 KEEP_RECENT 제외한 이전 메시지
    old_messages = messages[:-KEEP_RECENT]
    recent_messages = messages[-KEEP_RECENT:]

    # 요약용 대화 구성
    summary_conversation = _build_summary_conversation(old_messages)
    if not summary_conversation:
        return messages  # 요약할 내용 없음

    summary_prompt = (
        "Summarize our conversation so far in Korean. Include:\n"
        "1. What the user asked for\n"
        "2. What has been done (tools used, files created, searches made)\n"
        "3. Key findings, decisions, or results\n"
        "4. Any pending tasks or follow-ups\n\n"
        "Be concise but preserve important context. "
        "Do not lose any file names, URLs, or specific data."
    )

    try:
        response = client.messages.create(
            model=model,
            max_tokens=2000,
            messages=[
                *summary_conversation,
                {"role": "user", "content": summary_prompt},
            ],
        )
        summary = response.content[0].text
        logger.info(
            f"Compaction: {len(old_messages)} msgs -> summary ({len(summary)} chars), "
            f"keeping {len(recent_messages)} recent"
        )
    except Exception as e:
        logger.error(f"Compaction failed: {e}")
        return messages  # 실패 시 원본 유지

    # 압축된 메시지 배열
    compacted = [
        {
            "role": "user",
            "content": f"[이전 대화 요약]\n{summary}",
        },
        {
            "role": "assistant",
            "content": (
                "네, 이전 대화 내용을 파악했습니다. "
                "이어서 도와드릴게요."
            ),
        },
        *recent_messages,
    ]

    return compacted


def should_compact(messages: list) -> bool:
    """압축이 필요한지 판단"""
    return len(messages) >= COMPACTION_THRESHOLD


def _build_summary_conversation(messages: list) -> list:
    """
    요약 API에 보낼 수 있는 형태로 메시지 정리.
    Pydantic 객체/tool_result 등을 텍스트로 변환.
    """
    result = []
    for msg in messages:
        role = msg.get("role", "")
        content = msg.get("content")

        if role == "user":
            if isinstance(content, str):
                result.append({"role": "user", "content": content})
            elif isinstance(content, list):
                # tool_result 리스트 -> 텍스트 요약
                texts = []
                for item in content:
                    if isinstance(item, dict):
                        if item.get("type") == "tool_result":
                            tool_content = item.get("content", "")
                            texts.append(f"[도구 결과] {str(tool_content)[:200]}")
                        else:
                            texts.append(str(item.get("content", item))[:200])
                if texts:
                    result.append({"role": "user", "content": "\n".join(texts)})

        elif role == "assistant":
            text = _extract_assistant_text(content)
            if text:
                result.append({"role": "assistant", "content": text})

    # API 규칙: 첫 메시지는 user여야 함
    while result and result[0].get("role") != "user":
        result.pop(0)

    # user/assistant 교대 보장 (연속 같은 role 병합)
    merged = []
    for msg in result:
        if merged and merged[-1]["role"] == msg["role"]:
            merged[-1]["content"] += "\n" + msg["content"]
        else:
            merged.append(msg)

    # 마지막이 user면 제거 (assistant 응답이 필요하므로)
    if merged and merged[-1]["role"] == "user":
        merged.pop()

    return merged


def _extract_assistant_text(content) -> str:
    """assistant content에서 텍스트만 추출"""
    if isinstance(content, str):
        return content

    if isinstance(content, list):
        texts = []
        for block in content:
            if isinstance(block, dict):
                if block.get("type") == "text":
                    texts.append(block.get("text", ""))
                elif block.get("type") == "tool_use":
                    texts.append(f"[도구 호출: {block.get('name', '?')}]")
            elif hasattr(block, "type"):
                if block.type == "text":
                    texts.append(block.text)
                elif block.type == "tool_use":
                    texts.append(f"[도구 호출: {block.name}]")
        return "\n".join(texts)

    return str(content)[:500]
