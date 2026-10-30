# API del servidor

Todas las rutas (salvo `/api/login`, `/api/estado` y las páginas) necesitan `Authorization: Bearer <token>`. Las páginas para imprimir y los PDF aceptan `?token=`.
El servidor controla los permisos de cada ruta. Las respuestas son JSON. Los errores devuelven `{ "error": "mensaje para el usuario" }` con el código HTTP correspondiente.

Lista generada desde el código (`servidor/src/Sona.Core/Api`). Para regenerarla: buscar `app.Map(Get|Post|Put|Delete)`.

## App de los celulares

| Método | Ruta |
|---|---|
| `GET` | `/api/app/descargar` |
| `GET` | `/api/app/equipos` |
| `POST` | `/api/app/subir` |
| `GET` | `/api/app/version` |

## Artículos

| Método | Ruta |
|---|---|
| `GET` | `/api/articulo/{a}` |
| `GET` | `/api/articulo/{a}/fotos` |
| `GET` | `/api/articulos/buscar` |
| `GET` | `/api/articulos/catalogo` |
| `POST` | `/api/articulos/catalogo/actualizar` |
| `GET` | `/api/articulos/sugerir` |
| `DELETE` | `/api/catalogo/fotos/{id:long}` |
| `GET` | `/api/catalogo/{articulo}` |
| `POST` | `/api/catalogo/{articulo}/fotos` |

## Auditoria

| Método | Ruta |
|---|---|
| `GET` | `/api/auditoria` |

## Autorizaciones

| Método | Ruta |
|---|---|
| `GET` | `/api/autorizaciones` |
| `GET` | `/api/autorizaciones/{id:long}` |
| `POST` | `/api/autorizaciones/{id:long}` |
| `POST` | `/api/autorizaciones/{id:long}/cancelar` |

## Cajas

| Método | Ruta |
|---|---|
| `GET` | `/api/cajas/destinos/{cont}` |

## Configuración

| Método | Ruta |
|---|---|
| `GET` | `/api/config` |
| `PUT` | `/api/config` |
| `POST` | `/api/config/probar` |
| `GET` | `/api/parametros` |
| `PUT` | `/api/parametros/{clave}` |

## Consulta

| Método | Ruta |
|---|---|
| `GET` | `/api/consulta/{codigo}` |

## Contenedor

| Método | Ruta |
|---|---|
| `GET` | `/api/contenedor/{cont}` |

## Contenedores y etiquetas

| Método | Ruta |
|---|---|
| `POST` | `/api/contenedores/crear` |
| `POST` | `/api/contenedores/info` |
| `GET` | `/api/contenedores/{cont}/etiqueta.html` |
| `GET` | `/api/contenedores/{cont}/etiqueta.png` |
| `POST` | `/api/contenedores/{cont}/imprimir` |

## Conteos

| Método | Ruta |
|---|---|
| `POST` | `/api/conteo/articulo` |
| `GET` | `/api/conteo/diferencias` |
| `POST` | `/api/conteo/lineas` |
| `POST` | `/api/conteo/lineas/{id:long}/revision` |
| `GET` | `/api/conteo/posicion/{codigo}` |
| `POST` | `/api/conteo/reconteo` |
| `POST` | `/api/conteo/tareas` |
| `GET` | `/api/conteo/tareas` |
| `GET` | `/api/conteo/tareas/{id:long}` |
| `POST` | `/api/conteo/tareas/{id:long}/cerrar` |

## Control de bulto

| Método | Ruta |
|---|---|
| `PUT` | `/api/bultos/impresion` |
| `GET` | `/api/bultos/impresion` |
| `POST` | `/api/bultos/impresion/probar` |
| `POST` | `/api/bultos/{nro}` |
| `GET` | `/api/bultos/{nro}` |
| `GET` | `/api/bultos/{nro}/etiqueta/{bulto:long}` |
| `POST` | `/api/bultos/{nro}/imprimir` |

## Control-pedidos

| Método | Ruta |
|---|---|
| `GET` | `/api/control-pedidos` |

## Empresas

| Método | Ruta |
|---|---|
| `GET` | `/api/empresas` |
| `PUT` | `/api/empresas/{codigo:int}` |

## Estado

| Método | Ruta |
|---|---|
| `GET` | `/api/estado` |

## Etiquetas

| Método | Ruta |
|---|---|
| `GET` | `/api/etiquetas/{cont}` |

## Faltantes

| Método | Ruta |
|---|---|
| `GET` | `/api/faltantes` |
| `POST` | `/api/faltantes/{id:long}/atender` |

## Fotos

| Método | Ruta |
|---|---|
| `POST` | `/api/fotos` |
| `GET` | `/api/fotos/ia/{id}` |
| `GET` | `/api/fotos/{id}` |

## Guardado

| Método | Ruta |
|---|---|
| `GET` | `/api/guardado/pendientes` |

## Historia

| Método | Ruta |
|---|---|
| `GET` | `/api/historia` |

## Imagen

| Método | Ruta |
|---|---|
| `GET` | `/api/imagen` |

## Impresión

| Método | Ruta |
|---|---|
| `PUT` | `/api/impresion/etiquetas` |
| `GET` | `/api/impresion/etiquetas` |

## Ingreso

| Método | Ruta |
|---|---|
| `GET` | `/api/ingresos` |
| `GET` | `/api/ingresos/capturas.zip` |
| `POST` | `/api/ingresos/diagnostico` |
| `GET` | `/api/ingresos/fotos/{fid:long}` |
| `GET` | `/api/ingresos/imagen-les` |
| `GET` | `/api/ingresos/{id:long}` |
| `POST` | `/api/ingresos/{id:long}/articulos/configurar` |
| `POST` | `/api/ingresos/{id:long}/articulos/verificar` |
| `GET` | `/api/ingresos/{id:long}/caratulas.pdf` |
| `POST` | `/api/ingresos/{id:long}/caratulas/imprimir` |
| `GET` | `/api/ingresos/{id:long}/caratulas/imprimir.html` |
| `POST` | `/api/ingresos/{id:long}/cerrar` |
| `PUT` | `/api/ingresos/{id:long}/contenedores` |
| `POST` | `/api/ingresos/{id:long}/contenedores/les` |
| `POST` | `/api/ingresos/{id:long}/contenedores/sona` |
| `PUT` | `/api/ingresos/{id:long}/descarga` |
| `POST` | `/api/ingresos/{id:long}/descarga/confirmar` |
| `POST` | `/api/ingresos/{id:long}/fotos` |
| `GET` | `/api/ingresos/{id:long}/proveedores` |
| `POST` | `/api/ingresos/{id:long}/recepcion` |
| `GET` | `/api/ingresos/{id:long}/recepcion.pdf` |
| `POST` | `/api/ingresos/{id:long}/recibir` |

## Ingresos-egresos

| Método | Ruta |
|---|---|
| `GET` | `/api/ingresos-egresos/{articulo}` |

## Login

| Método | Ruta |
|---|---|
| `POST` | `/api/login` |

## Mensajes

| Método | Ruta |
|---|---|
| `POST` | `/api/mensajes` |
| `GET` | `/api/mensajes` |
| `GET` | `/api/mensajes/conversaciones` |
| `POST` | `/api/mensajes/leido` |
| `GET` | `/api/mensajes/sin-leer` |

## Monitor

| Método | Ruta |
|---|---|
| `GET` | `/api/monitor` |
| `GET` | `/api/monitor/ocupacion` |

## Movimientos

| Método | Ruta |
|---|---|
| `POST` | `/api/movimientos` |
| `GET` | `/api/movimientos` |
| `GET` | `/api/movimientos/export.xlsx` |
| `GET` | `/api/movimientos/{id}` |
| `POST` | `/api/movimientos/{id}/anular` |
| `POST` | `/api/movimientos/{id}/reintentar` |

## Nn

| Método | Ruta |
|---|---|
| `GET` | `/api/nn` |

## Pedidos

| Método | Ruta |
|---|---|
| `GET` | `/api/pedido/{nro}` |
| `POST` | `/api/pedido/{nro}/control` |

## Picking

| Método | Ruta |
|---|---|
| `POST` | `/api/picking/accion` |
| `GET` | `/api/picking/actual` |
| `POST` | `/api/picking/elegir` |
| `GET` | `/api/picking/empresas` |
| `GET` | `/api/picking/encurso` |
| `GET` | `/api/picking/eventos` |
| `POST` | `/api/picking/iniciar` |
| `GET` | `/api/picking/mis-ops` |
| `POST` | `/api/picking/pausar` |
| `POST` | `/api/picking/retomar` |
| `POST` | `/api/picking/salir` |

## Producción

| Método | Ruta |
|---|---|
| `POST` | `/api/produccion/asignar` |
| `GET` | `/api/produccion/caratula` |
| `GET` | `/api/produccion/caratulas/imprimir.html` |
| `PUT` | `/api/produccion/config` |
| `GET` | `/api/produccion/config` |
| `GET` | `/api/produccion/flujo` |
| `GET` | `/api/produccion/historial` |
| `POST` | `/api/produccion/prioridad` |
| `POST` | `/api/produccion/probar-impresora` |
| `POST` | `/api/produccion/reasignar` |
| `POST` | `/api/produccion/reimprimir` |
| `GET` | `/api/produccion/tablero` |

## Productividad

| Método | Ruta |
|---|---|
| `GET` | `/api/productividad` |
| `GET` | `/api/productividad/actividad` |

## Reconocimiento por foto (IA)

| Método | Ruta |
|---|---|
| `POST` | `/api/ia/confirmar` |
| `POST` | `/api/ia/descartar` |
| `GET` | `/api/ia/estado` |
| `GET` | `/api/ia/historial` |
| `POST` | `/api/ia/indexar` |
| `POST` | `/api/ia/ninguno` |
| `POST` | `/api/ia/reconocer` |

## Respaldo

| Método | Ruta |
|---|---|
| `POST` | `/api/respaldo` |

## Sugerencia

| Método | Ruta |
|---|---|
| `POST` | `/api/sugerencia` |

## Ubicaciones

| Método | Ruta |
|---|---|
| `GET` | `/api/layout/estado` |
| `POST` | `/api/layout/sincronizar` |
| `GET` | `/api/posicion/{codigo}` |
| `GET` | `/api/posiciones` |
| `PUT` | `/api/posiciones` |
| `GET` | `/api/posiciones/{codigo}` |
| `GET` | `/api/zonas` |
| `PUT` | `/api/zonas/{cara}` |
| `DELETE` | `/api/zonas/{cara}` |

## Usuario actual y Mis credenciales

| Método | Ruta |
|---|---|
| `GET` | `/api/yo` |
| `GET` | `/api/yo/credenciales` |
| `PUT` | `/api/yo/credenciales/{sistema}` |
| `DELETE` | `/api/yo/credenciales/{sistema}` |
| `POST` | `/api/yo/credenciales/{sistema}/probar` |
| `GET` | `/api/yo/les` |
| `PUT` | `/api/yo/les` |

## Usuarios y permisos

| Método | Ruta |
|---|---|
| `PUT` | `/api/perfiles/{codigo}` |
| `DELETE` | `/api/perfiles/{codigo}` |
| `GET` | `/api/permisos` |
| `GET` | `/api/usuarios` |
| `PUT` | `/api/usuarios/{usuario}` |
| `GET` | `/api/usuarios/{usuario}/permisos` |
| `PUT` | `/api/usuarios/{usuario}/permisos` |

## Validar

| Método | Ruta |
|---|---|
| `POST` | `/api/validar` |

## Otros

| Método | Ruta |
|---|---|
| `GET` | `/` |
