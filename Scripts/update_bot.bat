@echo off
REM === 一键更新 Bot（在 bot 机器上运行） ===
REM 用法: update_bot.bat [安装目录]
REM 默认安装到当前目录

set SERVER=http://70.39.201.9:5000
set INSTALL_DIR=%~1
if "%INSTALL_DIR%"=="" set INSTALL_DIR=%~dp0

echo [1/3] 下载最新版本...
powershell -Command "Invoke-WebRequest '%SERVER%/bot/Hearthbot.zip' -OutFile '%TEMP%\hb_update.zip'"
if errorlevel 1 (
    echo 下载失败！请检查网络连接
    pause
    exit /b 1
)

echo [2/3] 解压到 %INSTALL_DIR% ...
powershell -Command "Expand-Archive -Path '%TEMP%\hb_update.zip' -DestinationPath '%INSTALL_DIR%' -Force"
if errorlevel 1 (
    echo 解压失败！
    pause
    exit /b 1
)

echo [3/3] 清理临时文件...
del "%TEMP%\hb_update.zip" 2>nul

echo.
echo === 更新完成 ===
echo 安装目录: %INSTALL_DIR%
pause
