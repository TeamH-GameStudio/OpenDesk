"""웹 검색 도구 — Brave Search API"""

import aiohttp
from .base import BaseTool


class WebSearchTool(BaseTool):
    def __init__(self, api_key: str):
        self._api_key = api_key

    @property
    def name(self):
        return "web_search"

    @property
    def description(self):
        return "Search the web for current information."

    @property
    def parameters(self):
        return {
            "type": "object",
            "properties": {
                "query": {
                    "type": "string",
                    "description": "Search query",
                }
            },
            "required": ["query"],
        }

    async def execute(self, args: dict) -> str:
        url = "https://api.search.brave.com/res/v1/web/search"
        headers = {
            "Accept": "application/json",
            "X-Subscription-Token": self._api_key,
        }
        try:
            async with aiohttp.ClientSession() as s:
                async with s.get(
                    url,
                    headers=headers,
                    params={"q": args["query"], "count": 5},
                ) as r:
                    data = await r.json()
                    results = data.get("web", {}).get("results", [])
                    return (
                        "\n---\n".join(
                            f"Title: {item['title']}\nURL: {item['url']}\nDesc: {item.get('description', '')}"
                            for item in results[:5]
                        )
                        or "No results."
                    )
        except Exception as e:
            return f"Search error: {e}"
