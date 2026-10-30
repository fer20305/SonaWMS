@echo off
cd /d "%~dp0"
dotnet run --project pruebas\Sona.Pruebas -c Release --no-build
