@echo off
title SONA Deposito - quitar el servicio
net session >nul 2>&1
if errorlevel 1 ( echo Hay que ejecutarlo como ADMINISTRADOR. & pause & exit /b 1 )
sc stop SonaDeposito >nul 2>&1
timeout /t 5 /nobreak >nul
sc delete SonaDeposito
netsh advfirewall firewall delete rule name="SONA Deposito" >nul 2>&1
echo Servicio quitado. Los DATOS NO se borraron: siguen en C:\ProgramData\SONA\Deposito
pause
