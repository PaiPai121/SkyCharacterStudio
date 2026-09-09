@echo off
setlocal
cd /d "%~dp0"
if exist "dist-summon\Sky1stCharacterStudio.exe" (
    start "Sky1st Character Studio" "dist-summon\Sky1stCharacterStudio.exe"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "run.ps1"
)
