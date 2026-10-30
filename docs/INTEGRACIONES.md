# Integraciones

SONA no accede a las bases de datos de otros sistemas: usa sus pantallas web o sus endpoints HTTP, con el usuario de cada persona. Las direcciones se configuran en **ZCFG (Configuración de plataformas)**, con un botón de *Probar conexiones*.

## WMS (GeneXus)

Hay dos accesos:

- **Handheld** (`Wms/Handheld.cs`): son las pantallas RF que usan las pistolas. Por acá pasan casi todos los movimientos.
- **Escritorio** (`Wms/Escritorio.cs`): se usa para producción, es decir, las bandejas de preparación, la simulación de faltantes, la asignación y la confirmación.

| Uso en SONA | Pantallas | Lee / escribe |
|---|---|---|
| Login y menú RF | `hinicio.aspx`, `hmenurf` | lee |
| Transferir / reubicar contenedor | `hreubpl1.aspx` (opción 4) | **escribe** |
| Guardar recepciones | opción 2 del menú RF | **escribe** |
| Cambiar estado, ajustes | `hgrpart.aspx`, `tgrpart`, chequeos y ajustes | **escribe** |
| Ver un hueco / estado real del contenedor | `hselconsubicac.aspx`, `hdatubicaciones.aspx` | lee |
| Ubicaciones (fichas de posiciones) | `hvolumenes.aspx` | lee |
| Historia de contenedor | `hconshistplt.aspx` | lee |
| Autorizaciones pendientes | `hpenddeautorizar.aspx` | lee |
| Picking | `hselpck`, `hpck0`…`hpck9` | **escribe** (confirma picks) |
| Ingreso / recepción por contenedor | `hingeve.aspx`, `hingeve2`, `hingetqextrf` | **escribe** |
| Producción: bandejas, simulación, asignar | `hasigusuario.aspx` (escritorio) y "Ver OP" (`overoppdf.aspx`) | **escribe** |

Consideraciones:

- **Las pantallas se identifican por su contenido**, no solo por la URL. Si aparece una pantalla inesperada, se guarda en `datos/wms_paginas` o en `capturas/` para revisarla.
- **Cada operario entra con su propio usuario** del WMS. Así la auditoría del WMS muestra quién hizo cada cosa.
- **Diagnóstico**: el panel tiene un "diagnóstico solo lectura" que recorre pantallas y arma un zip sin claves (`Wms/Diagnostico.cs`).

## LES (tradebox)

| Uso | Dónde | Credenciales |
|---|---|---|
| Buscador (contenedores, artículos, pedidos) | `Api/Buscador/GetInfoBusqueda` | usuario general (ZCFG) |
| Fotos y fichas de artículos | sitio LES | usuario general (ZCFG) |
| Ingresos pendientes (módulo de Ingreso, crear contenedor) | `IngresoPendiente/*` | **las del usuario** (*Mis credenciales*). Si no cargó, las del WMS |
| Bultos / medidas (control de bulto) | `Api/Bultos/GetBultos`, `PostGuardarBultos` | — |

## e-TMS

`Les/Etms.cs` consulta los ingresos y egresos de un artículo, es decir, a qué punto de venta salió y con qué remito. Para entrar usa, en este orden:

1. las credenciales propias del usuario en *Mis credenciales*;
2. el usuario general de ZCFG;
3. el usuario del WMS.

## Telegram

El control de solicitud envía la foto del bulto y el resumen a un grupo de Telegram. El token del bot y el chat se cargan en **ZPAR** (`telegram_token`, `telegram_chat`): quedan en la base del servidor y **nunca van al repositorio**.

## Cuando una plataforma cambia

1. Reproducir el problema con el **diagnóstico** o mirar `capturas/` (cada operación del módulo de Ingreso guarda las pantallas, con las claves tapadas).
2. Agregar la pantalla nueva a `pruebas/Sona.Pruebas/paginas/` y ajustar el simulador (`Fake*.cs`).
3. Corregir el lector o escritor en `Wms/` o `Les/` hasta que la prueba pase.
