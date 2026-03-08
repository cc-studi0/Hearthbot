@echo off
setlocal

set "HS=H:\Hearthstone"
set "BEPINEX=/tmp/bepinex"
set "SCRIPT_DIR=%~dp0"
for %%I in ("%SCRIPT_DIR%..") do set "REPO_ROOT=%%~fI"

set "PLUGIN=%REPO_ROOT%\HearthstonePayload\bin\Release\net472\HearthstonePayload.dll"
if not exist "%PLUGIN%" set "PLUGIN=%REPO_ROOT%\HearthstonePayload\obj\Release\net472\HearthstonePayload.dll"

echo [1/3] Copying BepInEx runtime into Hearthstone...
copy /Y "%BEPINEX%\winhttp.dll" "%HS%\"
copy /Y "%BEPINEX%\doorstop_config.ini" "%HS%\"
xcopy /E /I /Y "%BEPINEX%\BepInEx" "%HS%\BepInEx"

echo [2/3] Ensuring plugins directory exists...
if not exist "%HS%\BepInEx\plugins" mkdir "%HS%\BepInEx\plugins"

echo [3/3] Copying plugin DLL...
copy /Y "%PLUGIN%" "%HS%\BepInEx\plugins\"

echo Deploy complete. Restart Hearthstone to load the updated plugin.
pause
