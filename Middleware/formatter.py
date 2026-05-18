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

    i = 0
    while i < len(lines):
        line = lines[i]

        # 코드블록 시작/끝
        if line.strip().startswith("```"):
            if not in_code_block:
                code_lang = line.strip()[3:].strip()
                label = f"--- {code_lang} ---" if code_lang else "---"
                result_lines.append(f"<color=#6272A4>{label}</color>")
                in_code_block = True
            else:
                result_lines.append("<color=#6272A4>---------</color>")
                in_code_block = False
                code_lang = ""
            i += 1
            continue

        if in_code_block:
            # 코드블록 내부: 색상만 적용, 마크다운 변환 안 함
            result_lines.append(f"<color=#F8F8F2>{_escape_tmp_tags(line)}</color>")
            i += 1
            continue

        # 파이프 테이블 (GFM): 헤더 + 구분선 + N 데이터 행
        if (_is_table_row(line)
                and i + 1 < len(lines)
                and _is_table_separator(lines[i + 1])):
            j = i + 2
            while j < len(lines) and _is_table_row(lines[j]):
                j += 1
            header_cells = _parse_table_row(lines[i])
            body_rows = [_parse_table_row(lines[k]) for k in range(i + 2, j)]
            result_lines.extend(_render_table(header_cells, body_rows))
            i = j
            continue

        # 제목
        if line.startswith("### "):
            content = _inline_format(line[4:])
            result_lines.append(f"<b>{content}</b>")
            i += 1
            continue
        if line.startswith("## "):
            content = _inline_format(line[3:])
            result_lines.append(f"<size=115%><b>{content}</b></size>")
            i += 1
            continue
        if line.startswith("# "):
            content = _inline_format(line[2:])
            result_lines.append(f"<size=130%><b>{content}</b></size>")
            i += 1
            continue

        # 구분선
        if re.match(r"^-{3,}$", line.strip()):
            result_lines.append('<color=#555555>────────────</color>')
            i += 1
            continue

        # 인용문
        if line.startswith("> "):
            content = _inline_format(line[2:])
            result_lines.append(f"<color=#888888>| {content}</color>")
            i += 1
            continue

        # 리스트 (순서 없는)
        m = re.match(r"^(\s*)[-*]\s+(.+)$", line)
        if m:
            indent = m.group(1)
            content = _inline_format(m.group(2))
            result_lines.append(f"{indent}  - {content}")
            i += 1
            continue

        # 리스트 (순서 있는)
        m = re.match(r"^(\s*)(\d+)\.\s+(.+)$", line)
        if m:
            indent = m.group(1)
            num = m.group(2)
            content = _inline_format(m.group(3))
            result_lines.append(f"{indent}  {num}. {content}")
            i += 1
            continue

        # 일반 줄: 인라인 포맷만 적용
        result_lines.append(_inline_format(line))
        i += 1

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
    text = text.replace("<", "‹")   # ‹
    text = text.replace(">", "›")   # ›
    return text


# ── 파이프 테이블 (GFM) ──

_TABLE_SEP_RE = re.compile(r"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$")


def _is_table_row(line: str) -> bool:
    """`| a | b |` 또는 `a | b` 형태 감지. 최소 1개의 `|` 필요."""
    s = line.strip()
    if not s or "|" not in s:
        return False
    inner = s.strip("|")
    return "|" in inner or s.startswith("|") or s.endswith("|")


def _is_table_separator(line: str) -> bool:
    """`|---|---|` 또는 `:---:|---:` 등 GFM 구분선."""
    return bool(_TABLE_SEP_RE.match(line))


def _parse_table_row(line: str) -> list[str]:
    """`| a | b | c |` -> ['a', 'b', 'c']. 양 끝 `|` 와 공백 제거."""
    s = line.strip()
    if s.startswith("|"):
        s = s[1:]
    if s.endswith("|"):
        s = s[:-1]
    return [cell.strip() for cell in s.split("|")]


def _render_table(header: list[str], body: list[list[str]]) -> list[str]:
    """파싱된 테이블을 TMP 리치텍스트 라인으로 변환.

    UI Toolkit Label 은 모노스페이스 정렬을 보장하지 않으므로
    헤더 볼드 + 셀 구분자(' | ') + thin rule 로 가독성을 확보한다.
    """
    sep = " <color=#555555>│</color> "
    rule = '<color=#555555>────────────</color>'
    lines: list[str] = []

    header_text = sep.join(_inline_format(c) for c in header)
    lines.append(f"<b>{header_text}</b>")
    lines.append(rule)

    for row in body:
        # 셀 개수가 헤더와 달라도 그대로 렌더 — 정보 손실 방지.
        lines.append(sep.join(_inline_format(c) for c in row))

    return lines


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
