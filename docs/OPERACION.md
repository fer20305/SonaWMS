# Operación y soporte

## Dónde mirar

| Qué | Dónde |
|---|---|
| Estado del servidor, versión y conexiones | `http://SERVIDOR:8080/api/estado` · panel **ZEST** |
| Registro del servidor | `C:\ProgramData\SONA\Deposito\registro\servidor_AAAAMMDD.log` · panel **ZLOG** (modo integrado) |
| Errores de arranque del servicio | `registro\arranque_error.txt` |
| Movimientos con error en el WMS | panel **ZMOV**, filtro *Estado = error*: se pueden reintentar o anular |
| Lo que respondió el WMS o el LES en una operación | `capturas\` (Ingreso las guarda siempre; la *Prueba* de Mis credenciales también) |
| Quién cambió qué | panel **ZAUD** (auditoría) |

## Respaldos

- Todos los días se copia la base a `respaldos\` y se guardan las últimas 30 copias.
- **ZEST › Respaldar base ahora** hace una copia en el momento.
- **Para restaurar:**
  1. detener el servicio;
  2. reemplazar `sona.db` por la copia;
  3. borrar `sona.db-wal` y `sona.db-shm` si existen;
  4. arrancar el servicio.

## Problemas frecuentes

| Síntoma | Causa probable | Qué hacer |
|---|---|---|
| El servicio no arranca, "puerto 8080 en uso" | El panel `SonaDeposito.exe` está abierto en modo integrado en la misma PC | Cerrarlo o pasarlo a modo remoto |
| "No arrancó" después de actualizar | El `.exe` no se reemplazó, o el servicio está instalado en otra carpeta | Ejecutar `actualizar.bat` (detecta la carpeta) y, si sigue, `diagnostico-servicio.bat` |
| Los celulares no conectan | IP del servidor cambiada o firewall cerrado | IP fija, puerto 8080 abierto, *Configurar servidor* en la app |
| Un movimiento queda "pendiente WMS" | El WMS no responde o rechazó el movimiento | ZMOV: ver la respuesta, reintentar o anular |
| Ingreso: "el LES no aceptó el usuario" | Clave vencida o distinta en el LES | Probar en el sitio del LES; actualizar en **Mis credenciales** |
| La etiqueta Zebra sale chica o corrida | El papel elegido no es el de la etiqueta | En el diálogo: papel 100 × 70 mm, sin márgenes, escala 100 % |
| La app no se actualiza sola | El APK nuevo está firmado con otra clave | Compilar siempre con `android/firma/` (ver DESARROLLO) |

## Usuarios

- **Alta**: el usuario entra con su usuario y su clave del WMS. En ZUSR se le asignan perfiles y permisos.
- **Administrador**: además de los perfiles, la lista `admins` de `config.json`. Si no hay ningún administrador con clave guardada, el primero que entra al panel queda como administrador.
- **Credenciales de otras plataformas**: cada usuario las carga en **Mis credenciales** (panel web, botón 🔑, o ZCRE en el panel de escritorio).
