# Desarrollo

## Requisitos

| Parte | Necesita |
|---|---|
| Servidor y panel | .NET SDK 8. El panel WPF se compila en Windows, o en Linux con `-p:EnableWindowsTargeting=true` |
| Panel web | Node.js 20 o superior |
| App Android | JDK 17, Android SDK 35 (Android Studio o línea de comandos) |

## Panel web

```bash
cd web
npm ci
npm run dev        # desarrollo en http://localhost:5173, con /api redirigido al servidor de prueba (127.0.0.1:18080)
npm run build      # genera servidor/panel-web (lo sirve el servidor como wwwroot)
```

> Hay que compilar el panel web **antes** de publicar el servidor o el panel de escritorio: los dos incluyen `servidor/panel-web`.

## Servidor

```bash
cd servidor
dotnet build pruebas/Sona.Pruebas -c Release -p:EnableWindowsTargeting=true
dotnet run --no-build --project pruebas/Sona.Pruebas -c Release
```

Las pruebas levantan el servidor real contra un WMS, LES y e-TMS simulados y recorren todos los flujos. Tienen que terminar en **`TODO OK`**. Si algo falla, se ve con `✘` y el motivo.

### Demo con datos de ejemplo

```bash
SONA_DEMO=1 SONA_PRUEBAS=1 dotnet run --no-build --project pruebas/Sona.Pruebas -c Release
```

Después de correr las pruebas, deja el servidor andando 20 minutos en `http://127.0.0.1:18080`, en `/`, `/produccion`, `/ingresos` y `/supervisor`. El usuario y la clave de prueba son los que usa `Program.cs` (por ejemplo, clave `pw`).

### Publicar (Windows, un solo `.exe`)

```bash
cd servidor
rm -rf src/*/obj src/*/bin
dotnet publish src/Sona.Servidor -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableWindowsTargeting=true -o salida/servidor
dotnet publish src/Sona.Panel -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableWindowsTargeting=true -o salida/panel
```

- Conviene borrar `obj/` y `bin/` antes, para que no quede nada viejo dentro del `.exe`.
- Para verificar que el `.exe` incluye las librerías nativas, buscá `libSkiaSharp.dll` y `onnxruntime.dll` dentro del archivo.
- Los archivos para instalar el servicio de Windows están en `servidor/instalacion/windows`: `instalar-servicio.bat`, `actualizar.bat` y `diagnostico-servicio.bat`.

### Docker

```bash
docker compose up -d      # desde la raíz: compila el panel web y el servidor en la imagen
```

## App Android

```bash
cd android
./gradlew :app:assembleRelease
```

- **Firma:** los celulares solo aceptan actualizaciones firmadas con **la misma clave**. La clave **no está en el repositorio**: se pone en `android/firma/sona.keystore` con sus datos en `android/firma/keystore.properties`:
  ```properties
  storePassword=...
  keyAlias=...
  keyPassword=...
  ```
  Sin esa carpeta, el APK sale firmado con la clave de debug y **no se instala encima** de la versión que ya tienen los celulares.
- **Versión:** `versionCode` y `versionName` en `android/app/build.gradle.kts`. El panel (ZAPP) publica el APK y los celulares se actualizan solos.

## Versionado

- **Servidor:** la constante `Version` en `servidor/src/Sona.Core/Api/Rutas.cs`. Se muestra en el panel y en `/api/estado`.
- **App:** `versionCode` y `versionName`.
- Para cada entrega se arma un zip con el panel, el servidor (con su `actualizar.bat`) y un `LEEME.txt` con los cambios.

## Convenciones

- Código, nombres y textos en **castellano rioplatense**, igual que la operación.
- Los mensajes de error se escriben para el operario, diciendo qué pasó y qué hacer. Se usa `ErrorUsuario(mensaje, códigoHttp)`.
- Toda función nueva que toque el WMS, el LES o el e-TMS lleva su prueba en `pruebas/Sona.Pruebas/Program.cs`, con la pantalla real en `paginas/` si hace falta.
- **Nunca** se prueban escrituras reales contra el WMS de producción. Las pruebas usan el simulador.

## Vitrina pública

`herramientas/vitrina.py` genera una copia del repositorio con los datos reales reemplazados por datos de ejemplo: personas, clientes, locales, IP y dominios. La copia compila y pasa las mismas pruebas.

```bash
python3 herramientas/vitrina.py ../sona-deposito-vitrina
```

Al final imprime lo que no pudo reemplazar. Revisalo antes de publicar.
