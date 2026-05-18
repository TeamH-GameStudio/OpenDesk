@echo off
REM ──────────────────────────────────────────────────────────────────
REM OpenDesk Middleware -> PyInstaller 단일 바이너리 빌드 (Windows)
REM
REM 산출물: Middleware\dist\Middleware.exe
REM 후속:   Assets\StreamingAssets\Middleware\Middleware.exe 로 자동 복사
REM ──────────────────────────────────────────────────────────────────

setlocal ENABLEDELAYEDEXPANSION
pushd "%~dp0"

set "VENV=%~dp0.venv"
if not exist "%VENV%" (
    echo [build] .venv not found - creating...
    python -m venv .venv
    "%VENV%\Scripts\pip" install --upgrade pip
    "%VENV%\Scripts\pip" install -r requirements.txt
)

"%VENV%\Scripts\pip" install --quiet pyinstaller

if exist build  rmdir /S /Q build
if exist dist   rmdir /S /Q dist

"%VENV%\Scripts\pyinstaller" ^
  --onefile ^
  --name Middleware ^
  --add-data "opendesk_skills_mcp.py;." ^
  --hidden-import providers.anthropic_cli ^
  --hidden-import providers.anthropic_api ^
  --hidden-import apscheduler.triggers.cron ^
  --hidden-import apscheduler.schedulers.asyncio ^
  --hidden-import anthropic ^
  --hidden-import mcp ^
  --hidden-import websockets ^
  --hidden-import aiohttp ^
  --hidden-import dotenv ^
  --collect-submodules apscheduler ^
  --collect-submodules anthropic ^
  --collect-submodules mcp ^
  server.py

if not exist "dist\Middleware.exe" (
    echo [build] ERROR: artifact not found: dist\Middleware.exe
    exit /b 1
)

set "STREAMING_DIR=%~dp0..\Assets\StreamingAssets\Middleware"
if not exist "%STREAMING_DIR%" mkdir "%STREAMING_DIR%"
copy /Y "dist\Middleware.exe" "%STREAMING_DIR%\Middleware.exe"

echo --------------------------------------------------------------
echo [build] OK
echo   artifact   : %~dp0dist\Middleware.exe
echo   copied to  : %STREAMING_DIR%\Middleware.exe
echo --------------------------------------------------------------

popd
endlocal
