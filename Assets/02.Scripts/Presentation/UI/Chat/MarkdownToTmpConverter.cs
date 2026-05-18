using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenDesk.Presentation.UI.Chat
{
    /// <summary>
    /// 마크다운 텍스트를 Unity UI Toolkit Label (TextCore) 이 렌더링 가능한 TMP-스타일
    /// 리치텍스트로 변환한다. <c>Middleware/formatter.py</c> 의 1:1 포팅이며,
    /// 스트리밍 도중 매 delta 마다 호출되어도 안전하도록 다음 원칙을 지킨다:
    /// <list type="bullet">
    /// <item>미완성 토큰 ( <c>**bo</c>, 닫히지 않은 코드블록, 헤더 없는 파이프 행) 은
    /// 변환되지 않고 raw 로 통과 — 다음 토큰이 도착하면 자연스럽게 스냅 변환.</item>
    /// <item>순수 함수 — 입력 동일 시 출력 동일. 매 호출마다 누적 버퍼 전체를 변환해도 안전.</item>
    /// </list>
    /// </summary>
    public static class MarkdownToTmpConverter
    {
        private static readonly Regex DividerRe = new(@"^-{3,}$", RegexOptions.Compiled);
        private static readonly Regex BulletRe = new(@"^(\s*)[-*]\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex OrderedRe = new(@"^(\s*)(\d+)\.\s+(.+)$", RegexOptions.Compiled);

        private static readonly Regex InlineCodeRe = new(@"`([^`]+)`", RegexOptions.Compiled);
        private static readonly Regex BoldItalicRe = new(@"\*\*\*(.+?)\*\*\*", RegexOptions.Compiled);
        private static readonly Regex BoldRe = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
        private static readonly Regex ItalicRe = new(@"\*(.+?)\*", RegexOptions.Compiled);
        private static readonly Regex LinkRe = new(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.Compiled);

        private static readonly Regex TableSepRe =
            new(@"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$", RegexOptions.Compiled);

        private const string Divider = "<color=#555555>────────────</color>";
        private const string TableCellSep = " <color=#555555>│</color> ";

        public static string Convert(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var lines = text.Split('\n');
            var sb = new StringBuilder(text.Length + 64);
            var inCodeBlock = false;
            string codeLang = string.Empty;

            for (int i = 0; i < lines.Length;)
            {
                var line = lines[i];

                // 코드블록 시작/끝
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("```", System.StringComparison.Ordinal))
                {
                    if (!inCodeBlock)
                    {
                        codeLang = trimmed.Substring(3).Trim();
                        var label = string.IsNullOrEmpty(codeLang) ? "---" : $"--- {codeLang} ---";
                        AppendLine(sb, $"<color=#6272A4>{label}</color>");
                        inCodeBlock = true;
                    }
                    else
                    {
                        AppendLine(sb, "<color=#6272A4>---------</color>");
                        inCodeBlock = false;
                        codeLang = string.Empty;
                    }
                    i++;
                    continue;
                }

                if (inCodeBlock)
                {
                    // 코드 내부: 색상만 적용, 태그 이스케이프
                    AppendLine(sb, $"<color=#F8F8F2>{EscapeTags(line)}</color>");
                    i++;
                    continue;
                }

                // 파이프 테이블 (GFM): 헤더 + 구분선 + N 데이터 행
                if (IsTableRow(line)
                    && i + 1 < lines.Length
                    && IsTableSeparator(lines[i + 1]))
                {
                    int j = i + 2;
                    while (j < lines.Length && IsTableRow(lines[j])) j++;

                    var header = ParseTableRow(lines[i]);
                    var body = new List<List<string>>(j - i - 2);
                    for (int k = i + 2; k < j; k++) body.Add(ParseTableRow(lines[k]));

                    RenderTable(sb, header, body);
                    i = j;
                    continue;
                }

                // 제목
                if (line.StartsWith("### ", System.StringComparison.Ordinal))
                {
                    AppendLine(sb, $"<b>{InlineFormat(line.Substring(4))}</b>");
                    i++;
                    continue;
                }
                if (line.StartsWith("## ", System.StringComparison.Ordinal))
                {
                    AppendLine(sb, $"<size=115%><b>{InlineFormat(line.Substring(3))}</b></size>");
                    i++;
                    continue;
                }
                if (line.StartsWith("# ", System.StringComparison.Ordinal))
                {
                    AppendLine(sb, $"<size=130%><b>{InlineFormat(line.Substring(2))}</b></size>");
                    i++;
                    continue;
                }

                // 구분선
                if (DividerRe.IsMatch(line.Trim()))
                {
                    AppendLine(sb, Divider);
                    i++;
                    continue;
                }

                // 인용문
                if (line.StartsWith("> ", System.StringComparison.Ordinal))
                {
                    AppendLine(sb, $"<color=#888888>| {InlineFormat(line.Substring(2))}</color>");
                    i++;
                    continue;
                }

                // 리스트 (순서 없는)
                var bullet = BulletRe.Match(line);
                if (bullet.Success)
                {
                    AppendLine(sb, $"{bullet.Groups[1].Value}  - {InlineFormat(bullet.Groups[2].Value)}");
                    i++;
                    continue;
                }

                // 리스트 (순서 있는)
                var ordered = OrderedRe.Match(line);
                if (ordered.Success)
                {
                    AppendLine(sb, $"{ordered.Groups[1].Value}  {ordered.Groups[2].Value}. {InlineFormat(ordered.Groups[3].Value)}");
                    i++;
                    continue;
                }

                // 일반 줄
                AppendLine(sb, InlineFormat(line));
                i++;
            }

            // 마지막 trailing \n 제거 — 원본이 끝에 \n 가 없으면 동일하게.
            if (sb.Length > 0 && sb[sb.Length - 1] == '\n')
                sb.Length--;
            return sb.ToString();
        }

        private static void AppendLine(StringBuilder sb, string s)
        {
            sb.Append(s);
            sb.Append('\n');
        }

        private static string InlineFormat(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = InlineCodeRe.Replace(text, "<color=#8BE9FD>$1</color>");
            text = BoldItalicRe.Replace(text, "<b><i>$1</i></b>");
            text = BoldRe.Replace(text, "<b>$1</b>");
            text = ItalicRe.Replace(text, "<i>$1</i>");
            text = LinkRe.Replace(text, "$1");
            return text;
        }

        private static string EscapeTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return text.Replace("<", "‹").Replace(">", "›");
        }

        // ── 파이프 테이블 ──

        private static bool IsTableRow(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            var s = line.Trim();
            if (s.Length == 0 || s.IndexOf('|') < 0) return false;
            var inner = s.Trim('|');
            return inner.IndexOf('|') >= 0 || s[0] == '|' || s[s.Length - 1] == '|';
        }

        private static bool IsTableSeparator(string line) => TableSepRe.IsMatch(line ?? string.Empty);

        private static List<string> ParseTableRow(string line)
        {
            var s = (line ?? string.Empty).Trim();
            if (s.Length > 0 && s[0] == '|') s = s.Substring(1);
            if (s.Length > 0 && s[s.Length - 1] == '|') s = s.Substring(0, s.Length - 1);
            var parts = s.Split('|');
            var cells = new List<string>(parts.Length);
            foreach (var p in parts) cells.Add(p.Trim());
            return cells;
        }

        private static void RenderTable(StringBuilder sb, List<string> header, List<List<string>> body)
        {
            // 헤더
            sb.Append("<b>");
            for (int i = 0; i < header.Count; i++)
            {
                if (i > 0) sb.Append(TableCellSep);
                sb.Append(InlineFormat(header[i]));
            }
            sb.Append("</b>\n");

            // thin rule
            sb.Append(Divider);
            sb.Append('\n');

            // body
            foreach (var row in body)
            {
                for (int i = 0; i < row.Count; i++)
                {
                    if (i > 0) sb.Append(TableCellSep);
                    sb.Append(InlineFormat(row[i]));
                }
                sb.Append('\n');
            }
        }
    }
}
