> Documentación vigente del proyecto: [README](../README.md) y carpeta [docs](../docs). Este archivo es la guía original del panel 2.0.

# SONA Depósito 2.0 (C# / .NET 8)

El panel de escritorio **es el servidor**. Atiende a los celulares, guarda la base y carga los movimientos en el WMS. Las direcciones de todas las plataformas se configuran desde el mismo panel. El mismo servidor también puede correr en **Docker**.

```
SonaDeposito.exe   panel WPF + servidor integrado (un solo archivo, no hace falta instalar .NET)
Servidor/          el mismo servidor sin pantalla (Docker u otra PC)
src/               código fuente: Sona.Core (servidor), Sona.Panel (WPF), Sona.Servidor (consola/Docker)
pruebas/           prueba de punta a punta contra un WMS de prueba armado con las pantallas reales del handheld
android/           app SONA Depósito (no cambia: usa la misma API)
```

## Modo 1: panel integrado (recomendado para una sola PC)

1. Copiá `SonaDeposito.exe` a la PC del depósito (IP fija 10.0.50.207).
2. Abrilo y entrá con tu usuario y clave del **WMS**. Con "Mantener la sesión" tildado, se reconecta solo cada vez que abre.
3. Andá a **Sistema › Configuración de plataformas (ZCFG)**.
   - Cargá las direcciones del WMS (red local e IP externa), el buscador SONA, tradebox con usuario y clave para las fotos, y las direcciones que usan los celulares.
   - Tocá **Probar conexiones**: cada plataforma muestra ✔ o ✕ con el tiempo de respuesta.
4. Andá a **ZZON** y tocá **Sincronizar layout del WMS**.
5. Abrí el puerto 8080 en el firewall de Windows, una sola vez, como administrador:
   `netsh advfirewall firewall add rule name="SONA Deposito" dir=in action=allow protocol=TCP localport=8080`

Para que los celulares no se queden sin servidor:
- Si cerrás la ventana, el programa **sigue en la bandeja del sistema** (al lado del reloj) y el servidor sigue atendiendo.
- Para salir de verdad: clic derecho en el ícono de la bandeja y elegí **Salir**.
- En **Sistema › Servidor: integrado / Docker…** se puede marcar que **abra al iniciar Windows**.

Base de datos:
- Queda en `C:\ProgramData\SONA\Deposito\` (archivo `sona.db`, configuración, fotos).
- Todos los días se hace una copia de seguridad en la subcarpeta `respaldos`, y se guardan las últimas 30.
- **ZEST › Respaldar base ahora** hace una copia en el momento.

## Modo 2: servidor en Docker

```
docker compose up -d          # construye y levanta el contenedor (puerto 8080, datos en ./datos)
docker compose logs -f        # ver el registro
```

Después, en el panel, andá a **Sistema › Servidor: integrado / Docker…**, elegí **remoto** y poné la dirección del contenedor, por ejemplo `http://10.0.50.207:8080`. El panel se usa igual que en el modo integrado y la configuración de plataformas también se hace desde ZCFG: queda guardada en `./datos/config.json` del contenedor.

Los dos modos usan el mismo código y la misma base (SQLite). Para pasar de uno a otro, alcanza con copiar la carpeta de datos.

## Transacciones del panel

| Código | Pantalla |
|---|---|
| ZMON | Monitor en vivo: movimientos por hora, operarios, pendientes y errores del WMS |
| ZMOV | Movimientos con filtros por operario, fechas, tipo, estado, empresa y texto. Exporta a Excel y permite reintentar o anular. |
| ZAUT | Autorizaciones: destino en zona de otro cliente. Se contesta Sí o No, avisa con sonido y en la bandeja, y el celular recibe la respuesta al instante. |
| ZCON | Consultar Ubic.Ctrl, posición, contenedor o artículo, con fotos |
| ZPOS / ZZON / ZMAP | Fichas de posiciones, Layout Objetivo por cara y mapa del pasillo |
| ZCNT / ZDIF | Tareas de conteo ciego y revisión de diferencias |
| ZUSR / ZPAR / ZEMP / ZAUD | Usuarios y roles, parámetros, empresas y alias, auditoría |
| ZCFG | **Configuración de plataformas** (WMS, buscador, tradebox, servidor) con prueba de conexión |
| ZEST / ZLOG | Estado y conexiones, y registro en vivo del servidor integrado |

Atajos: **Ctrl+K** abre el código de transacción, **F3** cierra la pestaña, **F5** actualiza y **F8** ejecuta en ZMOV.

## Compilar desde el código

Hace falta el SDK de .NET 8. Ejecutá `compilar.bat`: deja `publicado\Panel\SonaDeposito.exe` y `publicado\Servidor`. Para correr la prueba de punta a punta:

```
dotnet run --project pruebas\Sona.Pruebas -c Release
```

Sale `TODO OK` si pasan todos los casos: login, consultas, sugerencias, bloqueos, autorización, transferencia, cambio de estado con foto, guardado, conteo, Excel, configuración, respaldo y eventos en vivo.
