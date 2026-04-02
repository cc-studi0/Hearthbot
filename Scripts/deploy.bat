@echo off
chcp 65001 >nul
title HearthBot 部署工具
color 0A

:MENU
cls
echo.
echo  ╔═══════════════════════════════════╗
echo  ║     HearthBot 一键部署工具        ║
echo  ╠═══════════════════════════════════╣
echo  ║                                   ║
echo  ║   1. 上传云控服务器               ║
echo  ║   2. 上传 Bot 脚本（含混淆）      ║
echo  ║   3. 上传 Bot 脚本（跳过混淆）    ║
echo  ║   4. 全部上传（云控 + 脚本）      ║
echo  ║   0. 退出                         ║
echo  ║                                   ║
echo  ╚═══════════════════════════════════╝
echo.
set "choice="
set /p "choice=请选择 [0-4]: "

if "%choice%"=="1" goto CLOUD
if "%choice%"=="2" goto BOT
if "%choice%"=="3" goto BOT_FAST
if "%choice%"=="4" goto ALL
if "%choice%"=="0" exit
goto MENU

:CLOUD
cls
echo.
echo  === 上传云控服务器 ===
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy_cloud.ps1"
echo.
pause
goto MENU

:BOT
cls
echo.
echo  === 上传 Bot 脚本（含混淆）===
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy_bot.ps1"
echo.
pause
goto MENU

:BOT_FAST
cls
echo.
echo  === 上传 Bot 脚本（跳过混淆）===
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy_bot.ps1" -SkipObfuscation
echo.
pause
goto MENU

:ALL
cls
echo.
echo  === 上传云控服务器 ===
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy_cloud.ps1"
echo.
echo  === 上传 Bot 脚本（跳过混淆）===
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy_bot.ps1" -SkipObfuscation
echo.
pause
goto MENU
