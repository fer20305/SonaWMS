@echo off
setlocal
title SONA - diagnostico del servicio
net session >nul 2>&1
if errorlevel 1 ( echo Hay que ejecutarlo como ADMINISTRADOR ^(clic derecho - Ejecutar como administrador^). & pause & exit /b 1 )
set "OUT=%~dp0diagnostico_servicio.txt"
echo Juntando datos (tarda unos 30 segundos)...
(
echo ===== SONA diagnostico %date% %time%
echo ===== PC
hostname
powershell -NoProfile -Command "Get-NetIPAddress -AddressFamily IPv4 | ? IPAddress -notlike '127.*' | % IPAddress"
echo ===== Servicio
sc query SonaDeposito
sc qc SonaDeposito
powershell -NoProfile -Command "Get-CimInstance Win32_Service | ? Name -eq 'SonaDeposito' | fl Name,State,StartMode,PathName,StartName,ExitCode"
echo ===== Procesos SONA
tasklist | findstr /i "Sona"
echo ===== Puerto 8080
netstat -ano | findstr ":8080"
echo ===== Carpetas
dir "C:\SONA" 2>nul
dir "C:\SONA\Servidor" 2>nul
dir "C:\ProgramData\SONA\Deposito" 2>nul
dir "C:\ProgramData\SONA\Deposito\registro" 2>nul
echo ===== Ultimas lineas de arranque_error.txt
powershell -NoProfile -Command "if (Test-Path 'C:\ProgramData\SONA\Deposito\registro\arranque_error.txt') { Get-Content -Tail 15 'C:\ProgramData\SONA\Deposito\registro\arranque_error.txt' }"
echo ===== Eventos de Windows (ultimas 3 horas)
powershell -NoProfile -Command "Get-WinEvent -FilterHashtable @{LogName='System'; StartTime=(Get-Date).AddHours(-3)} -ErrorAction SilentlyContinue | ? { $_.Message -match 'SonaDeposito|SONA' } | select -First 12 TimeCreated,Id,Message | fl"
powershell -NoProfile -Command "Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddHours(-3)} -ErrorAction SilentlyContinue | ? { $_.ProviderName -match '.NET Runtime|Application Error|SonaDeposito' } | select -First 8 TimeCreated,ProviderName,Message | fl"
) > "%OUT%" 2>&1

rem si el servicio no esta corriendo, se prueba el exe del servicio en consola 20 segundos para ver el error real
sc query SonaDeposito | find "RUNNING" >nul
if not errorlevel 1 goto fin
for /f "usebackq delims=" %%d in (`powershell -NoProfile -Command "$p=(Get-CimInstance Win32_Service | ? Name -eq 'SonaDeposito').PathName; if($p){ if($p.StartsWith([char]34)){ $p.Split([char]34)[1] } else { $p.Split(' ')[0] } }"`) do set "EXE=%%d"
echo ===== Prueba en consola: %EXE% >> "%OUT%"
if not defined EXE ( echo No encontre el exe del servicio. >> "%OUT%" & goto fin )
if not exist "%EXE%" ( echo NO EXISTE el archivo %EXE% >> "%OUT%" & goto fin )
powershell -NoProfile -Command "$o=[IO.Path]::GetTempFileName(); $e=[IO.Path]::GetTempFileName(); $p=Start-Process -FilePath '%EXE%' -WorkingDirectory (Split-Path '%EXE%') -RedirectStandardOutput $o -RedirectStandardError $e -PassThru -WindowStyle Hidden; Start-Sleep 20; $vivo = -not $p.HasExited; if($vivo){ Stop-Process -Id $p.Id -Force; 'Siguio funcionando 20 s (arranca bien en consola)' } else { 'Se cerro con codigo ' + $p.ExitCode }; '--- salida ---'; Get-Content $o; '--- errores ---'; Get-Content $e" >> "%OUT%" 2>&1
:fin
echo.
echo LISTO: se guardo %OUT%
echo Mandaselo a Claude (si esta PC no es la tuya, adjuntalo en el chat).
pause
