@echo off
chcp 65001 >nul
rem Pax Pixelia: собрать C# и запустить игру (главное меню)
cd /d "%~dp0game"
dotnet build -nologo -v q
if errorlevel 1 ( echo Сборка не удалась & pause & exit /b 1 )
start "" "%~dp0tools\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64.exe" --path "%~dp0game"
