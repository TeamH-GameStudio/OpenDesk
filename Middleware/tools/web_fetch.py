"""
웹 페이지 내용 가져오기 도구.

web_search가 검색 결과 목록만 주는 반면,
이 도구는 특정 URL의 실제 내용을 가져와서 텍스트로 반환.
"""

import re
import aiohttp
from .base import BaseTool

# 차단할 도메인/패턴
BLOCKED_DOMAINS = {
    "localhost", "127.0.0.1", "0.0.0.0", "::1",
    "169.254.169.254",  # AWS metadata
    "metadata.google.internal",
}


class WebFetchTool(BaseTool):
    def __init__(self, timeout: int = 15, max_length: int = 30000):
        self._timeout = timeout
        self._max_length = max_length

    @property
    def name(self):
        return "web_fetch"

    @property
    def description(self):
        return (
            "Fetch the content of a web page by URL. "
            "Returns the text content (HTML tags stripped). "
            "Use this after web_search to read the actual page content."
        )

    @property
    def parameters(self):
        return {
            "type": "object",
            "properties": {
                "url": {
                    "type": "string",
                    "description": "The URL to fetch (must start with http:// or https://)",
                }
            },
            "required": ["url"],
        }

    async def execute(self, args: dict) -> str:
        url = args.get("url", "").strip()
        if not url:
            return "Error: URL is empty."

        if not url.startswith(("http://", "https://")):
            return "Error: URL must start with http:// or https://"

        # 내부 네트워크 차단
        for blocked in BLOCKED_DOMAINS:
            if blocked in url:
                return "Error: Access to internal/local addresses is not allowed."

        try:
            timeout = aiohttp.ClientTimeout(total=self._timeout)
            headers = {
                "User-Agent": "Mozilla/5.0 (compatible; OpenDesk-Agent/1.0)",
                "Accept": "text/html,application/xhtml+xml,text/plain",
            }

            async with aiohttp.ClientSession(timeout=timeout) as session:
                async with session.get(url, headers=headers, allow_redirects=True) as resp:
                    if resp.status != 200:
                        return f"Error: HTTP {resp.status} {resp.reason}"

                    content_type = resp.headers.get("Content-Type", "")
                    if "text" not in content_type and "json" not in content_type:
                        return f"Error: Not a text page (Content-Type: {content_type})"

                    html = await resp.text(errors="replace")

            text = _html_to_text(html)

            if len(text) > self._max_length:
                text = text[: self._max_length] + "\n\n... (truncated)"

            return text.strip() or "(empty page)"

        except aiohttp.ClientError as e:
            return f"Error: Connection failed - {e}"
        except Exception as e:
            return f"Error: {e}"


def _html_to_text(html: str) -> str:
    """HTML에서 텍스트만 추출 (간단한 태그 제거)"""
    # script, style 블록 제거
    text = re.sub(r"<script[^>]*>.*?</script>", "", html, flags=re.DOTALL | re.IGNORECASE)
    text = re.sub(r"<style[^>]*>.*?</style>", "", text, flags=re.DOTALL | re.IGNORECASE)
    text = re.sub(r"<nav[^>]*>.*?</nav>", "", text, flags=re.DOTALL | re.IGNORECASE)
    text = re.sub(r"<header[^>]*>.*?</header>", "", text, flags=re.DOTALL | re.IGNORECASE)
    text = re.sub(r"<footer[^>]*>.*?</footer>", "", text, flags=re.DOTALL | re.IGNORECASE)

    # 블록 태그 → 줄바꿈
    text = re.sub(r"<br\s*/?>", "\n", text, flags=re.IGNORECASE)
    text = re.sub(r"</(p|div|li|h[1-6]|tr|blockquote)>", "\n", text, flags=re.IGNORECASE)

    # 나머지 태그 제거
    text = re.sub(r"<[^>]+>", "", text)

    # HTML 엔티티
    text = text.replace("&amp;", "&")
    text = text.replace("&lt;", "<")
    text = text.replace("&gt;", ">")
    text = text.replace("&quot;", '"')
    text = text.replace("&#39;", "'")
    text = text.replace("&nbsp;", " ")

    # 연속 빈 줄 정리
    text = re.sub(r"\n{3,}", "\n\n", text)
    text = re.sub(r" {2,}", " ", text)

    return text.strip()
