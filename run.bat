@echo off
chcp 65001 >nul
dotnet restore
if errorlevel 1 exit /b %errorlevel%

dotnet run --project src/ArduManager.App
if errorlevel 1 exit /b %errorlevel%
