# Pendientes y próximos pasos

## En curso

- **Artículos por empresa (ZART) desde el WMS.** Hoy la lista sale del catálogo de SONA y del LES. Tiene que traer el stock y la ubicación como *Buscar contenedores* del WMS de escritorio. Falta relevar esa pantalla (solo lectura) e implementarla.
- **Historia del artículo.** Hay que basarla en *Movimientos por Artículo y Cuenta* del WMS de escritorio (`hconarct.aspx`). Falta relevar la pantalla e implementarla con su prueba.
- **Servicio de Windows en la PC del servidor.** Se corrigió la detención (2.20.1) y `actualizar.bat` ahora detecta la carpeta del servicio. Falta confirmar en la instalación real con `diagnostico-servicio.bat`.
- **Login del LES en Ingreso.** Se alineó con el login del navegador y ya no cierra la sesión de SONA cuando falla. Falta confirmarlo con los usuarios del sector de ingreso.

## Ideas y mejoras

- *Mis credenciales* en la app Android (hoy está en el panel web y en el de escritorio).
- Medición de bultos con realidad aumentada en el control de bulto, como opción (ARCore ya está incluido).
- Publicación de la app en Play Store (ficha, capturas, política de privacidad).
- HTTPS con un proxy inverso para el acceso externo.
- Integración continua: compilar el panel web, el servidor y la app y correr las pruebas en cada cambio, con GitHub Actions.

## Cosas a tener en cuenta

- Las pruebas usan **pantallas reales capturadas** del WMS. Si el WMS se actualiza, primero hay que capturar las pantallas nuevas y actualizar las pruebas.
- **Nunca** se prueban escrituras contra el WMS de producción.
- La firma de la app es única: sin ella no se pueden actualizar los celulares (ver DESARROLLO).
