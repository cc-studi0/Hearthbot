@echo off
setlocal

set "HS=H:\Hearthstone"
set "BEPINEX=/tmp/bepinex"
set "SCRIPT_DIR=%~dp0"
for %%I in ("%SCRIPT_DIR%..") do set "REPO_ROOT=%%~fI"
set "RESOLVE_SCRIPT=%SCRIPT_DIR%ResolveLatestPayloadBuild.ps1"

for /f "usebackq delims=" %%I in (`powershell -NoProfile -ExecutionPolicy Bypass -File "%RESOLVE_SCRIPT%" -RepoRoot "%REPO_ROOT%"`) do set "PLUGIN=%%I"
if not defined PLUGIN (
echo Failed to resolve latest HearthstonePayload.dll.
pause
exit /b 1
)

for /f "usebackq delims=" %%I in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "(Get-FileHash '%PLUGIN%' -Algorithm SHA256).Hash"`) do set "PLUGIN_HASH=%%I"

echo [1/3] Copying BepInEx runtime into Hearthstone...
copy /Y "%BEPINEX%\winhttp.dll" "%HS%\"
copy /Y "%BEPINEX%\doorstop_config.ini" "%HS%\"
xcopy /E /I /Y "%BEPINEX%\BepInEx" "%HS%\BepInEx"

echo [2/3] Ensuring plugins directory exists...
if not exist "%HS%\BepInEx\plugins" mkdir "%HS%\BepInEx\plugins"

echo [3/3] Copying plugin DLL...
echo [payload] Source: %PLUGIN%
echo [payload] Source SHA256: %PLUGIN_HASH%
copy /Y "%PLUGIN%" "%HS%\BepInEx\plugins\"
for /f "usebackq delims=" %%I in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "(Get-FileHash '%HS%\BepInEx\plugins\HearthstonePayload.dll' -Algorithm SHA256).Hash"`) do set "DEST_HASH=%%I"
echo [payload] Destination: %HS%\BepInEx\plugins\HearthstonePayload.dll
echo [payload] Destination SHA256: %DEST_HASH%

echo Deploy complete. Restart Hearthstone to load the updated plugin.
pause
