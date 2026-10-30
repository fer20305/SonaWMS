@echo off
title SONA Deposito - instalar servidor como servicio de Windows
net session >nul 2>&1
if errorlevel 1 ( echo Hay que ejecutarlo como ADMINISTRADOR: clic derecho ^> Ejecutar como administrador. & pause & exit /b 1 )
set "DEST=C:\SONA\Servidor"
echo Copiando el servidor a %DEST% ...
sc query SonaDeposito >nul 2>&1 && ( sc stop SonaDeposito >nul 2>&1 & timeout /t 5 /nobreak >nul )
if not exist "%DEST%" mkdir "%DEST%"
copy /y "%~dp0SonaServidor.exe" "%DEST%\" >nul
xcopy /e /i /y "%~dp0wwwroot" "%DEST%\wwwroot" >nul
sc query SonaDeposito >nul 2>&1
if errorlevel 1 (
  sc create SonaDeposito binPath= "\"%DEST%\SonaServidor.exe\"" start= delayed-auto DisplayName= "SONA Deposito" >nul
  sc description SonaDeposito "SONA Deposito: servidor para celulares, panel y panel web de produccion (puerto 8080)" >nul
)
sc failure SonaDeposito reset= 86400 actions= restart/10000/restart/30000/restart/60000 >nul
netsh advfirewall firewall delete rule name="SONA Deposito" >nul 2>&1
netsh advfirewall firewall add rule name="SONA Deposito" dir=in action=allow protocol=TCP localport=8080 >nul
sc start SonaDeposito >nul
timeout /t 8 /nobreak >nul
sc query SonaDeposito | find "RUNNING" >nul && (
  echo.
  echo  LISTO: el servicio "SONA Deposito" esta funcionando y arranca solo con Windows.
  echo  Datos:      C:\ProgramData\SONA\Deposito
  echo  Registro:   C:\ProgramData\SONA\Deposito\registro
  echo  Probar:     http://localhost:8080/api/estado
  echo  Produccion: http://IP-DE-ESTE-SERVIDOR:8080/produccion
) || ( echo. & echo  El servicio no arranco. Motivo: & type "C:\ProgramData\SONA\Deposito\registro\arranque_error.txt" 2>nul & echo. & echo  Si no dice nada, ejecuta probar-en-consola.bat y mira el error en la ventana. )
echo.
pause
