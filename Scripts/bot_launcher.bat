@echo off
REM ============================================================
REM  HearthBot 自动更新启动器
REM  - 放到 bot 安装目录，双击运行
REM  - 每 60 秒检查一次更新，发现新版本自动更新并重启
REM  - 关闭此窗口停止一切
REM ============================================================

set SERVER=http://70.39.201.9:5000
set BOT_DIR=%~dp0
set VERSION_FILE=%BOT_DIR%version.txt
set BOT_EXE=BotMain.exe
set CHECK_INTERVAL=60

title HearthBot Launcher

:LOOP
echo [%date% %time%] 启动 %BOT_EXE% ...
start "" /D "%BOT_DIR%" "%BOT_DIR%%BOT_EXE%"

:CHECK
timeout /t %CHECK_INTERVAL% /nobreak >nul

REM 下载服务器版本号
powershell -NoProfile -Command ^
  "try { $r = Invoke-WebRequest '%SERVER%/bot/version.txt' -UseBasicParsing -TimeoutSec 5; ^
    [IO.File]::WriteAllText('%TEMP%\hb_remote_ver.txt', $r.Content.Trim()) } ^
  catch { [IO.File]::WriteAllText('%TEMP%\hb_remote_ver.txt', 'ERROR') }"

set /p REMOTE_VER=<"%TEMP%\hb_remote_ver.txt"

REM 如果下载失败，跳过本轮
if "%REMOTE_VER%"=="ERROR" (
    goto CHECK
)

REM 读取本地版本号
if exist "%VERSION_FILE%" (
    set /p LOCAL_VER=<"%VERSION_FILE%"
) else (
    set LOCAL_VER=none
)

REM 版本一致，继续等待
if "%REMOTE_VER%"=="%LOCAL_VER%" goto CHECK

echo.
echo [%date% %time%] 发现新版本！
echo   本地: %LOCAL_VER%
echo   远程: %REMOTE_VER%
echo.

REM 关闭正在运行的 BotMain
echo [更新] 关闭 %BOT_EXE% ...
taskkill /F /IM %BOT_EXE% >nul 2>&1
timeout /t 3 /nobreak >nul

REM 下载新版本
echo [更新] 下载新版本...
powershell -NoProfile -Command ^
  "Invoke-WebRequest '%SERVER%/bot/Hearthbot.zip' -OutFile '%TEMP%\hb_update.zip' -UseBasicParsing"
if errorlevel 1 (
    echo [更新] 下载失败，跳过本次更新
    goto LOOP
)

REM 解压覆盖（保留 cloud.json, settings.json, accounts.json 等配置）
echo [更新] 解压安装...
powershell -NoProfile -Command ^
  "Expand-Archive -Path '%TEMP%\hb_update.zip' -DestinationPath '%BOT_DIR%' -Force"
if errorlevel 1 (
    echo [更新] 解压失败，跳过本次更新
    del "%TEMP%\hb_update.zip" 2>nul
    goto LOOP
)

REM 写入新版本号
echo %REMOTE_VER%> "%VERSION_FILE%"
del "%TEMP%\hb_update.zip" 2>nul

echo [更新] 更新完成！重新启动...
echo.
goto LOOP
