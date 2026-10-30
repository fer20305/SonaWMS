# Seguridad

## Autenticación

- Se entra con el **usuario y la clave del WMS**. SONA los valida contra el WMS.
- Si el WMS no responde, deja entrar solo con la última clave validada.
- La sesión es un token Bearer: lo usan el panel web, la app y el panel de escritorio.
- La clave del WMS se guarda **cifrada**, porque SONA la necesita para operar en el WMS en nombre del usuario.

## Autorización

- **Perfiles**: maquinista, picker, conteo, supervisor, producción, analista y administrador. Cada perfil tiene un conjunto de permisos.
- **Por usuario**: se pueden sumar o quitar permisos puntuales a cada persona.
- **El servidor controla cada ruta**: no alcanza con esconder un botón. Hay pruebas que lo verifican.
- **Destino en la zona de otro cliente**: el movimiento queda esperando la autorización de un supervisor (ZAUT).

## Secretos

| Secreto | Dónde vive | En el repositorio |
|---|---|---|
| Llave de cifrado de la instalación | `datos/secreto.key` | **No** |
| Claves de plataformas (LES, e-TMS, usuario de producción) | `datos/config.json`, cifradas | **No** |
| Claves de cada usuario (WMS, *Mis credenciales*) | `datos/sona.db`, cifradas | **No** |
| Token de Telegram | parámetros (ZPAR), en la base | **No** |
| Firma de la app Android | `android/firma/` | **No** |

`.gitignore` excluye la carpeta `datos/`, las bases, las claves, el keystore, las capturas y las entregas armadas.

- **Para pasar la firma de la app a otra persona:** que el equipo de IT la guarde en un gestor de secretos.
- **Si se pierde la firma:** las actualizaciones de la app no se instalan encima de la versión anterior. Hay que desinstalar y volver a instalar la app en cada celular.

## Datos personales y de clientes

- La base guarda nombres de usuario, movimientos, fotos de mercadería y remitos.
- Las **capturas de diagnóstico** guardan pantallas del WMS y del LES con las claves tapadas. Pueden tener datos de clientes: no compartirlas fuera de la empresa.
- Para mostrar el proyecto afuera se usa la **vitrina** (`herramientas/vitrina.py`). Reemplaza por datos de ejemplo:
  - las personas y los usuarios;
  - los clientes y los locales;
  - las IP y los dominios.

## Recomendaciones para IT

- Servidor con IP fija y el puerto 8080 abierto **solo para la red del depósito**. Para acceso externo, ponerlo detrás de un proxy con HTTPS.
- Respaldar la carpeta de datos **completa**, incluido `secreto.key`, fuera del servidor.
- Mantener el repositorio completo como **privado**. La vitrina pública no incluye datos reales.
