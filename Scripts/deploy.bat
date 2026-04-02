@echo off
title HearthBot Deploy
color 0A

:MENU
cls
echo.
echo  +===================================+
echo  ^|     HearthBot Deploy Tool          ^|
echo  +===================================+
echo  ^|                                   ^|
echo  ^|   1. Deploy Cloud Server          ^|
echo  ^|   2. Deploy Bot (with obfuscate)  ^|
echo  ^|   3. Deploy Bot (skip obfuscate)  ^|
echo  ^|   4. Deploy All (cloud + bot)     ^|
echo  ^|   0. Exit                         ^|
echo  ^|                                   ^|
echo  +===================================+
echo.
set "choice="
set /p "choice=Select [0-4]: "

if "%choice%"=="1" goto CLOUD
if "%choice%"=="2" goto BOT
if "%choice%"=="3" goto BOT_FAST
if "%choice%"=="4" goto ALL
if "%choice%"=="0" exit
goto MENU

:CLOUD
cls
echo.
echo  === Deploy Cloud Server ===
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy_cloud.ps1"
echo.
pause
goto MENU

:BOT
cls
echo.
echo  === Deploy Bot (with obfuscation) ===
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy_bot.ps1"
echo.
pause
goto MENU

:BOT_FAST
cls
echo.
echo  === Deploy Bot (skip obfuscation) ===
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy_bot.ps1" -SkipObfuscation
echo.
pause
goto MENU

:ALL
cls
echo.
echo  === Deploy Cloud Server ===
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy_cloud.ps1"
echo.
echo  === Deploy Bot (skip obfuscation) ===
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy_bot.ps1" -SkipObfuscation
echo.
pause
goto MENU
