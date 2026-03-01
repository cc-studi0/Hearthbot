@echo off
setlocal
set HS=H:\Hearthstone
set BEPINEX=/tmp/bepinex
set PLUGIN=H:\桌面\炉石脚本\HearthstonePayload\bin\Release\net472\HearthstonePayload.dll

echo [1/3] 复制 BepInEx 运行时到炉石目录...
copy /Y "%BEPINEX%\winhttp.dll" "%HS%\"
copy /Y "%BEPINEX%\doorstop_config.ini" "%HS%\"
xcopy /E /I /Y "%BEPINEX%\BepInEx" "%HS%\BepInEx"

echo [2/3] 创建 plugins 目录...
if not exist "%HS%\BepInEx\plugins" mkdir "%HS%\BepInEx\plugins"

echo [3/3] 复制插件 DLL...
copy /Y "%PLUGIN%" "%HS%\BepInEx\plugins\"

echo 部署完成！启动炉石即可自动加载插件。
pause
