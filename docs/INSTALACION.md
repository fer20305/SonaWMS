# Instalación

## Dónde corre el servidor

Hay tres formas, y todas usan el mismo núcleo y la misma carpeta de datos:

| Forma | Cuándo conviene | Cómo |
|---|---|---|
| **Servicio de Windows** (recomendada) | Un servidor o PC siempre encendida | `servidor/instalacion/windows/instalar-servicio.bat` (como administrador) |
| **Panel integrado** | Prueba rápida, o una sola PC | Abrir `SonaDeposito.exe` en modo *integrado*: el panel es el servidor |
| **Linux / Docker** | Servidor Linux | `servidor/instalacion/linux/instalar.sh` o `docker compose up -d` desde la raíz del repo |

> **No usar dos a la vez en la misma PC.** El panel en modo integrado y el servicio usan el mismo puerto (8080). En la PC del servicio, el panel tiene que estar en modo **remoto**.

### Requisitos

- 64 bits, 2 GB de RAM y 2 GB de disco, más lo que ocupen las fotos y las carátulas.
- **IP fija**: los celulares y los paneles apuntan a esa dirección.
- Acceso por red al WMS (local y, si se usa, el externo), al LES y al e-TMS.
- Puerto **8080** abierto para la red del depósito. El instalador abre el firewall:
  ```
  netsh advfirewall firewall add rule name="SONA Deposito" dir=in action=allow protocol=TCP localport=8080
  ```
- No hace falta instalar .NET: viene incluido en el ejecutable.

### Servicio de Windows

1. Copiar el contenido publicado (`SonaServidor.exe`, `wwwroot/` y los `.bat` de `instalacion/windows`) a una carpeta.
2. Ejecutar como administrador `instalar-servicio.bat`. Hace tres cosas:
   - copia el programa a la carpeta de instalación;
   - crea el servicio **SonaDeposito**, con inicio automático y reinicio si se cae;
   - abre el puerto.
3. Probar desde otra PC: `http://IP-DEL-SERVIDOR:8080/api/estado`. Tiene que devolver la versión.

**Actualizar.** Copiar la versión nueva al lado de `actualizar.bat` y ejecutarlo como administrador. El bat:

- pregunta a Windows dónde está instalado el servicio;
- lo detiene y espera a que se detenga de verdad;
- reemplaza el `.exe` y la carpeta `wwwroot`;
- lo arranca y espera a que quede funcionando. Si no arranca, muestra el motivo.

**Si no arranca.** Ejecutar `diagnostico-servicio.bat` como administrador. Genera `diagnostico_servicio.txt` con el estado del servicio, el puerto 8080, los eventos de Windows y una prueba del exe en consola.

## Mover el servidor a otra PC

Copiar **entera** la carpeta de datos `C:\ProgramData\SONA\Deposito` a la PC nueva, con `sona.db`, `config.json` y **`secreto.key`**, entre otros.

Sin `secreto.key` no se pueden descifrar las claves guardadas: hay que volver a cargar las claves de las plataformas y todos los usuarios tienen que ingresar de nuevo.

## Primera configuración

1. Entrar al panel con un usuario del WMS. El primer usuario que entra al panel queda como administrador.
2. **ZCFG · Configuración de plataformas**:
   - direcciones del WMS (red local y externa), del LES (buscador, account y sitio) y del e-TMS;
   - usuario general del LES (para fotos y fichas);
   - direcciones que usan los celulares.

   Después, **Probar conexiones**.
3. **ZZON**: *Sincronizar layout del WMS*.
4. **ZUSR**: dar perfiles y permisos.
5. **ZPAR**: parámetros, por ejemplo el Telegram del control de solicitud.
6. **ZIMP**: impresoras del servidor. Solo para las etiquetas de bultos que pide la app. Lo demás se imprime desde cada PC.
7. **Panel de producción** (`/produccion` › ⚙): el usuario del WMS del panel, el criterio de prioridad y dónde imprime cada PC.

## Celulares

1. Instalar el APK: lo publica el panel en **ZAPP** y los celulares se actualizan solos.
2. En la app, *Configurar servidor*: la dirección local y la externa del servidor. También se puede escanear el QR que muestra el panel.
3. Cada operario entra con su usuario del WMS.

## Paneles en otras PCs

- **Panel de escritorio**: `SonaDeposito.exe` en modo **remoto**, apuntando a `http://IP-DEL-SERVIDOR:8080`.
- **Panel web**: `http://IP-DEL-SERVIDOR:8080`, más `/produccion`, `/ingresos` y `/supervisor`. No hace falta instalar nada.

## Impresión desde cada PC

Las carátulas de producción e ingreso y las etiquetas de contenedor se imprimen **en la PC que las pide**, con el diálogo de impresión del navegador:

- **Zebra:** elegir el papel de la etiqueta (100 × 70 mm), márgenes *ninguno* y escala *100 %*. Chrome recuerda la última impresora usada.
- **Hojas A4:** la orientación se elige sola según la hoja.
