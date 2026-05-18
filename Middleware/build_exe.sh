#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────────
# OpenDesk Middleware → PyInstaller 단일 바이너리 빌드
#
# 산출물: Middleware/dist/Middleware  (mac/linux)  또는  dist/Middleware.exe (windows)
# 후속: Unity 빌드 시 자동 포함되도록 Assets/StreamingAssets/Middleware/ 에 복사.
#
# 사용:
#   chmod +x build_exe.sh
#   ./build_exe.sh
#
# Windows 는 동등 스크립트 build_exe.bat 또는 직접 .venv\Scripts\pyinstaller 를 실행.
# ──────────────────────────────────────────────────────────────────

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

VENV="${SCRIPT_DIR}/.venv"
if [[ ! -d "$VENV" ]]; then
  echo "[build] .venv not found — creating..."
  python3 -m venv .venv
  "$VENV/bin/pip" install --upgrade pip -q
  "$VENV/bin/pip" install -r requirements.txt
fi

# PyInstaller 설치 (idempotent)
"$VENV/bin/pip" install --quiet pyinstaller

# clean
rm -rf build dist

# 빌드 — 진입점은 server.py (현재 MiddlewareLauncher 가 기동하는 표준 진입점)
# --add-data: 런타임에 필요한 보조 파일 (PROTOCOL.md/config.json 은 코드가 직접 import 하지 않으므로 생략).
# --hidden-import: PyInstaller 가 dynamic import 를 못 잡는 모듈을 명시.
"$VENV/bin/pyinstaller" \
  --onefile \
  --name Middleware \
  --add-data "opendesk_skills_mcp.py:." \
  --hidden-import providers.anthropic_cli \
  --hidden-import providers.anthropic_api \
  --hidden-import apscheduler.triggers.cron \
  --hidden-import apscheduler.schedulers.asyncio \
  --hidden-import anthropic \
  --hidden-import mcp \
  --hidden-import websockets \
  --hidden-import aiohttp \
  --hidden-import dotenv \
  --collect-submodules apscheduler \
  --collect-submodules anthropic \
  --collect-submodules mcp \
  server.py

# 산출물 경로
case "$(uname -s)" in
  Darwin|Linux) OUT_NAME="Middleware" ;;
  MINGW*|MSYS*|CYGWIN*) OUT_NAME="Middleware.exe" ;;
  *) OUT_NAME="Middleware" ;;
esac

ARTIFACT="dist/${OUT_NAME}"
if [[ ! -f "$ARTIFACT" ]]; then
  echo "[build] ERROR: artifact not found: ${ARTIFACT}"
  exit 1
fi

# Unity StreamingAssets 로 복사 (Unity 빌드 시 Player 폴더에 자동 포함)
STREAMING_DIR="${SCRIPT_DIR}/../Assets/StreamingAssets/Middleware"
mkdir -p "$STREAMING_DIR"
cp "$ARTIFACT" "$STREAMING_DIR/${OUT_NAME}"
chmod +x "$STREAMING_DIR/${OUT_NAME}" 2>/dev/null || true

echo "──────────────────────────────────────────────"
echo "[build] OK"
echo "  artifact   : ${SCRIPT_DIR}/dist/${OUT_NAME}"
echo "  size       : $(du -h "$ARTIFACT" | cut -f1)"
echo "  copied to  : ${STREAMING_DIR}/${OUT_NAME}"
echo ""
echo "Unity 측 MiddlewareLauncher 가 빌드 모드에서 StreamingAssets/Middleware/${OUT_NAME} 를 자동 실행합니다."
echo "──────────────────────────────────────────────"
