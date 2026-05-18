"""
Claude Code OAuth credentials loader & refresh.

Claude CLI(`claude login`)가 발급/저장한 OAuth 토큰을 읽어,
Anthropic SDK 가 `Authorization: Bearer <accessToken>` 로 직접 호출할 수 있게 한다.
이로써 사용자가 별도 ANTHROPIC_API_KEY 를 입력하지 않고 Claude Max 구독으로 미들웨어 도구를 쓸 수 있다.

== 저장 위치 ==
1) `<CLAUDE_CONFIG_DIR>/.credentials.json`         ← OpenDesk 격리 dir (1순위)
2) `~/.claude/.credentials.json`                    ← 글로벌 (mac/linux/win 공통 파일 폴백)
3) macOS Keychain: `Claude Code-credentials`        ← 글로벌 (macOS 기본 저장소)

== 포맷 ==
```json
{
  "claudeAiOauth": {
    "accessToken":  "...",
    "refreshToken": "...",
    "expiresAt":    1775643612000,
    "scopes":       ["user:inference"]
  }
}
```
(키 이름이 버전에 따라 미세하게 다를 수 있어 광범위 매칭한다)

== 주의 ==
공식 SDK 가 OAuth bearer 를 받기는 하지만, Anthropic 이 `anthropic-beta: oauth-2025-04-20`
헤더로 Claude Max OAuth 클라이언트를 구분한다. 헤더가 없으면 `auth_invalid_key` 로 거부.
이 베타 헤더 키는 Anthropic 변경 시 깨질 수 있는 비공식 사용. 깨지면 CLI provider 로 폴백.
"""

from __future__ import annotations

import asyncio
import json
import logging
import os
import sys
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Optional

import httpx

logger = logging.getLogger("oauth_credentials")


# Claude Max OAuth 클라이언트 식별 베타 헤더 — Anthropic API 가 이 값을 요구.
ANTHROPIC_OAUTH_BETA = "oauth-2025-04-20"

# Token refresh endpoint (Claude Code 공개 OAuth 클라이언트 ID 와 동일 패턴).
# 변경되면 깨질 수 있어 환경변수로 오버라이드 허용.
DEFAULT_TOKEN_ENDPOINT = "https://console.anthropic.com/v1/oauth/token"

# Keychain 항목 이름 — macOS only.
KEYCHAIN_SERVICE_NAME = "Claude Code-credentials"

# 만료 직전 이 정도 시간 남았으면 미리 refresh.
REFRESH_LEEWAY_SECONDS = 60


@dataclass
class _RawCreds:
    access_token: str
    refresh_token: str
    expires_at_ms: int  # unix epoch milliseconds


def _parse_creds_dict(data: dict) -> Optional[_RawCreds]:
    """credentials JSON 의 inner dict 에서 토큰을 추출. 키 변형에 관대."""
    if not isinstance(data, dict):
        return None
    # 흔한 wrapper 키: claudeAiOauth, oauth, claudeai_oauth …
    inner = data
    for k in ("claudeAiOauth", "claudeai_oauth", "oauth", "credentials"):
        v = data.get(k)
        if isinstance(v, dict):
            inner = v
            break

    access = inner.get("accessToken") or inner.get("access_token") or ""
    refresh = inner.get("refreshToken") or inner.get("refresh_token") or ""
    expires = inner.get("expiresAt") or inner.get("expires_at") or 0
    try:
        expires = int(expires)
    except (TypeError, ValueError):
        expires = 0

    if not access:
        return None
    return _RawCreds(
        access_token=str(access),
        refresh_token=str(refresh),
        expires_at_ms=expires,
    )


async def _read_file(path: Path) -> Optional[_RawCreds]:
    if not path.is_file():
        return None
    try:
        text = await asyncio.to_thread(path.read_text, encoding="utf-8")
        return _parse_creds_dict(json.loads(text))
    except (OSError, json.JSONDecodeError) as e:
        logger.warning("credential 파일 파싱 실패 %s: %s", path, e)
        return None


async def _read_keychain_macos() -> Optional[_RawCreds]:
    """macOS Keychain 의 'Claude Code-credentials' generic password 읽기.

    `security find-generic-password -s <name> -w` 가 값 한 줄 출력.
    값은 보통 위 _RawCreds 와 같은 JSON 문자열.
    """
    if sys.platform != "darwin":
        return None
    try:
        proc = await asyncio.create_subprocess_exec(
            "/usr/bin/security",
            "find-generic-password",
            "-s",
            KEYCHAIN_SERVICE_NAME,
            "-w",
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.PIPE,
        )
    except FileNotFoundError:
        return None
    try:
        stdout, _ = await asyncio.wait_for(proc.communicate(), timeout=5.0)
    except asyncio.TimeoutError:
        try:
            proc.kill()
        except ProcessLookupError:
            pass
        return None
    if proc.returncode != 0:
        return None
    raw = stdout.decode("utf-8", errors="replace").strip()
    if not raw:
        return None
    try:
        data = json.loads(raw)
    except json.JSONDecodeError:
        # 토큰 자체만 저장된 케이스 (드물지만 가능)
        return _RawCreds(access_token=raw, refresh_token="", expires_at_ms=0)
    return _parse_creds_dict(data)


async def load_oauth_credentials(
    config_dir: Optional[str] = None,
) -> Optional["OAuthCredentials"]:
    """우선순위: 격리 dir 파일 → ~/.claude 파일 → macOS Keychain."""
    candidates: list[Path] = []
    if config_dir:
        candidates.append(Path(config_dir).expanduser() / ".credentials.json")
    candidates.append(Path("~/.claude/.credentials.json").expanduser())

    for path in candidates:
        raw = await _read_file(path)
        if raw is not None:
            logger.info("OAuth credentials 로드: %s", path)
            return OAuthCredentials(raw=raw, source_file=str(path))

    raw = await _read_keychain_macos()
    if raw is not None:
        logger.info("OAuth credentials 로드: macOS Keychain")
        return OAuthCredentials(raw=raw, source_file=None)

    logger.info("OAuth credentials 없음 — API key 필요")
    return None


class OAuthCredentials:
    """단일 OAuth 자격증명 holder. 만료 시 refresh 시도."""

    def __init__(self, raw: _RawCreds, source_file: Optional[str]):
        self._raw = raw
        self._source_file = source_file
        self._lock = asyncio.Lock()

    @property
    def access_token(self) -> str:
        return self._raw.access_token

    @property
    def refresh_token(self) -> str:
        return self._raw.refresh_token

    @property
    def expires_at_ms(self) -> int:
        return self._raw.expires_at_ms

    def is_expired(self) -> bool:
        if self._raw.expires_at_ms <= 0:
            return False  # 만료 정보 없으면 valid 가정 (호출 실패 시 SDK 가 401)
        now_ms = int(time.time() * 1000)
        return now_ms >= (self._raw.expires_at_ms - REFRESH_LEEWAY_SECONDS * 1000)

    async def ensure_fresh(self, client_id: Optional[str] = None) -> bool:
        """만료됐으면 refresh. 성공 시 True. refresh_token 없거나 endpoint 실패면 False."""
        if not self.is_expired():
            return True
        async with self._lock:
            if not self.is_expired():
                return True
            return await self._refresh(client_id)

    async def _refresh(self, client_id: Optional[str]) -> bool:
        if not self._raw.refresh_token:
            logger.warning("OAuth refresh 불가 — refresh_token 없음")
            return False

        endpoint = os.environ.get("OPENDESK_OAUTH_TOKEN_ENDPOINT", DEFAULT_TOKEN_ENDPOINT)
        payload = {
            "grant_type": "refresh_token",
            "refresh_token": self._raw.refresh_token,
        }
        if client_id:
            payload["client_id"] = client_id

        try:
            async with httpx.AsyncClient(timeout=15.0) as client:
                resp = await client.post(endpoint, json=payload)
        except httpx.HTTPError as e:
            logger.warning("OAuth refresh 요청 실패: %s", e)
            return False

        if resp.status_code != 200:
            logger.warning(
                "OAuth refresh 거부 (status %d): %s",
                resp.status_code, resp.text[:200],
            )
            return False

        try:
            data = resp.json()
        except ValueError:
            return False

        new_access = data.get("access_token") or data.get("accessToken")
        new_refresh = data.get("refresh_token") or data.get("refreshToken") or self._raw.refresh_token
        expires_in = data.get("expires_in") or data.get("expiresIn") or 0
        try:
            expires_in = int(expires_in)
        except (TypeError, ValueError):
            expires_in = 0

        if not new_access:
            return False

        new_expires_at_ms = int(time.time() * 1000) + max(expires_in, 0) * 1000

        self._raw = _RawCreds(
            access_token=str(new_access),
            refresh_token=str(new_refresh),
            expires_at_ms=new_expires_at_ms,
        )

        # 파일 소스에서 로드한 경우만 디스크에 다시 쓴다 (keychain 은 CLI 가 관리).
        if self._source_file:
            try:
                wrapper = {
                    "claudeAiOauth": {
                        "accessToken": self._raw.access_token,
                        "refreshToken": self._raw.refresh_token,
                        "expiresAt": self._raw.expires_at_ms,
                    }
                }
                Path(self._source_file).write_text(
                    json.dumps(wrapper, ensure_ascii=False, indent=2),
                    encoding="utf-8",
                )
                os.chmod(self._source_file, 0o600)
            except OSError as e:
                logger.warning("refresh 후 credential 파일 갱신 실패: %s", e)

        logger.info("OAuth token refresh 성공 (만료 %ds 후)", expires_in)
        return True

    def __repr__(self) -> str:
        tail = self.access_token[-4:] if self.access_token else "(none)"
        return f"OAuthCredentials(token=…{tail}, expires={self._raw.expires_at_ms})"
