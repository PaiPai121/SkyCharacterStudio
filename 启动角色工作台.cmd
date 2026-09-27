@echo off
setlocal
cd /d "%~dp0"
if exist "dist-summon\SkyCharacterStudio.exe" (
    start "Sky1st Character Studio" "dist-summon\SkyCharacterStudio.exe"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "run.ps1"
)
