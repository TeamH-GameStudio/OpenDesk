"""
마크다운 → Unity TMP 리치텍스트 변환기

Claude 응답의 마크다운을 TextMeshPro가 렌더링할 수 있는 리치텍스트로 변환.
delta(스트리밍 중)에서는 raw 전달, final에서 전체 변환.
"""

import re


def markdown_to_tmp(text: str) -> str:
    """마크다운 텍스트를 Unity TMP 리치텍스트로 변환"""
    if not text:
        return text

    lines = text.split("\n")
    result_lines = []
    in_code_block = False
    code_lang = ""

    for line in lines:
        # 코드블록 시작/끝
        if line.strip().startswith("```"):
            if not in_code_block:
                # 코드블록 시작
                code_lang = line.strip()[3:].strip()
                label = f"--- {code_lang} ---" if code_lang else "---"
                result_lines.append(f"<color=#6272A4>{label}</color>")
                in_code_block = True
            else:
                # 코드블록 끝
                result_lines.append("<color=#6272A4>---------</color>")
                in_code_block = False
                code_lang = ""
            continue

        if in_code_block:
            # 코드블록 내부: 색상만 적용, 마크다운 변환 안 함
            result_lines.append(f"<color=#F8F8F2>{_escape_tmp_tags(line)}</color>")
            continue

        # 제목
        if line.startswith("### "):
            content = _inline_format(line[4:])
            result_lines.append(f"<b>{content}</b>")
            continue
        if line.startswith("## "):
            content = _inline_format(line[3:])
            result_lines.append(f"<size=115%><b>{content}</b></size>")
            continue
        if line.startswith("# "):
            content = _inline_format(line[2:])
            result_lines.append(f"<size=130%><b>{content}</b></size>")
            continue

        # 구분선
        if re.match(r"^-{3,}$", line.strip()):
            result_lines.append('<color=#555555>\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500</color>')
            continue

        # 인용문
        if line.startswith("> "):
            content = _inline_format(line[2:])
            result_lines.append(f"<color=#888888>| {content}</color>")
            continue

        # 리스트 (순서 없는)
        m = re.match(r"^(\s*)[-*]\s+(.+)$", line)
        if m:
            indent = m.group(1)
            content = _inline_format(m.group(2))
            result_lines.append(f"{indent}  - {content}")
            continue

        # 리스트 (순서 있는)
        m = re.match(r"^(\s*)(\d+)\.\s+(.+)$", line)
        if m:
            indent = m.group(1)
            num = m.group(2)
            content = _inline_format(m.group(3))
            result_lines.append(f"{indent}  {num}. {content}")
            continue

        # 일반 줄: 인라인 포맷만 적용
        result_lines.append(_inline_format(line))

    return "\n".join(result_lines)


def _inline_format(text: str) -> str:
    """인라인 마크다운 → TMP 태그 변환"""
    # 인라인 코드 (백틱 1개)
    text = re.sub(r"`([^`]+)`", r"<color=#8BE9FD>\1</color>", text)

    # 볼드+이탤릭 (***text***)
    text = re.sub(r"\*\*\*(.+?)\*\*\*", r"<b><i>\1</i></b>", text)

    # 볼드 (**text**)
    text = re.sub(r"\*\*(.+?)\*\*", r"<b>\1</b>", text)

    # 이탤릭 (*text*)
    text = re.sub(r"\*(.+?)\*", r"<i>\1</i>", text)

    # 링크 [text](url) → text만
    text = re.sub(r"\[([^\]]+)\]\([^)]+\)", r"\1", text)

    return text


def _escape_tmp_tags(text: str) -> str:
    """코드블록 내부에서 TMP 태그로 해석될 수 있는 <> 이스케이프"""
    # TMP는 <b>, <i> 등을 해석하므로 코드 내 꺾쇠는 변환
    text = text.replace("<", "\u2039")   # ‹
    text = text.replace(">", "\u203A")   # ›
    return text


# ── 액션 태그 파싱 ──

_ACTION_RE = re.compile(r"\[ACTION:(\w+)\]\s*$")

VALID_ACTIONS = {
    "idle", "typing", "walk", "cheering",
    "sitting", "drinking", "dancing",
}


def extract_action(text: str) -> tuple[str, str | None]:
    """응답 텍스트에서 [ACTION:xxx] 태그를 추출하고 제거.

    Returns:
        (clean_text, action) — action이 없으면 None
    """
    if not text:
        return text, None

    m = _ACTION_RE.search(text)
    if not m:
        return text, None

    action = m.group(1).lower()
    if action not in VALID_ACTIONS:
        return text, None

    clean = text[:m.start()].rstrip()
    return clean, action
