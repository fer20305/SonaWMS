@echo off
title SONA Deposito - servidor en consola (prueba)
echo Arranca el servidor en esta ventana (sin servicio). Para cortarlo: cerrar la ventana o Ctrl+C.
echo Si el servicio esta corriendo, detenelo antes: sc stop SonaDeposito
"%~dp0SonaServidor.exe"
pause
