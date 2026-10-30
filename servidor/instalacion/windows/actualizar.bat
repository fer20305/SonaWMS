@echo off
setlocal
title SONA Deposito - actualizar servidor
net session >nul 2>&1
if errorlevel 1 ( echo Hay que ejecutarlo como ADMINISTRADOR. & pause & exit /b 1 )
set "REG=C:\ProgramData\SONA\Deposito\registro"

rem carpeta donde esta instalado el servicio (la que dice Windows, no una fija)
set "DEST="
for /f "usebackq delims=" %%d in (`powershell -NoProfile -Command "$p=(Get-CimInstance Win32_Service | ? Name -eq 'SonaDeposito').PathName; if($p){ if($p.StartsWith([char]34)){ $p=$p.Split([char]34)[1] } else { $p=$p.Split(' ')[0] }; Split-Path $p }"`) do set "DEST=%%d"
if not defined DEST ( echo No esta instalado el servicio SonaDeposito: usa instalar-servicio.bat. & pause & exit /b 1 )
echo Servicio instalado en: %DEST%

echo Deteniendo el servicio...
sc stop SonaDeposito >nul 2>&1
set /a n=0
:espera
sc query SonaDeposito | find "STOPPED" >nul && goto detenido
set /a n+=1
if %n% geq 40 goto forzar
timeout /t 1 /nobreak >nul
goto espera
:forzar
echo El servicio tarda en detenerse: se cierra a la fuerza.
taskkill /f /im SonaServidor.exe >nul 2>&1
timeout /t 3 /nobreak >nul
:detenido
taskkill /f /im SonaServidor.exe >nul 2>&1

echo Copiando la version nueva...
set /a n=0
:copiar
copy /y "%~dp0SonaServidor.exe" "%DEST%\SonaServidor.exe" >nul 2>&1 && goto copiado
set /a n+=1
if %n% geq 10 ( echo ERROR: no se pudo reemplazar %DEST%\SonaServidor.exe ^(algo lo tiene abierto^). & pause & exit /b 1 )
timeout /t 2 /nobreak >nul
goto copiar
:copiado
if exist "%~dp0wwwroot" xcopy /e /i /y "%~dp0wwwroot" "%DEST%\wwwroot" >nul
echo Version copiada:
powershell -NoProfile -Command "(Get-Item '%DEST%\SonaServidor.exe').LastWriteTime"

echo Arrancando el servicio...
sc start SonaDeposito
set /a n=0
:arranque
sc query SonaDeposito | find "RUNNING" >nul && goto ok
set /a n+=1
if %n% geq 60 goto fallo
timeout /t 1 /nobreak >nul
goto arranque

:ok
echo.
echo LISTO: servidor actualizado y funcionando.
pause
exit /b 0

:fallo
echo.
echo No arranco. Estado del servicio:
sc query SonaDeposito
echo Ultimas lineas del registro:
powershell -NoProfile -Command "if (Test-Path '%REG%\arranque_error.txt') { Get-Content -Tail 4 '%REG%\arranque_error.txt' }"
echo.
echo Si dice que el puerto 8080 esta en uso: en esta PC esta abierto el panel SonaDeposito.exe en modo integrado.
echo Cerralo (o pasalo a modo remoto) y volve a correr este archivo. Si no, corre diagnostico-servicio.bat.
pause
exit /b 1
