@echo off
chcp 65001 >nul
rem Pax Pixelia: открыть проект в редакторе Godot .NET (обычный Godot без .NET C#-проект не откроет)
start "" "%~dp0tools\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64.exe" --path "%~dp0game" --editor
