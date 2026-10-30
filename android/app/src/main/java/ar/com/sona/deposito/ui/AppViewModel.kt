package ar.com.sona.deposito.ui

import android.app.Application
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.os.VibrationEffect
import android.os.Vibrator
import android.util.LruCache
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import ar.com.sona.deposito.data.Ajustes
import ar.com.sona.deposito.data.Api
import ar.com.sona.deposito.data.Cola
import ar.com.sona.deposito.data.ErrorApi
import ar.com.sona.deposito.data.Eventos
import ar.com.sona.deposito.data.ItemCola
import ar.com.sona.deposito.data.txt
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.json.JSONObject

const val VERSION = "1.22"

/** "1.17" > "1.16", "1.16.1" > "1.16". */
fun esMasNueva(nueva: String, actual: String): Boolean {
    fun partes(v: String) = Regex("\\d+").findAll(v).map { it.value.toInt() }.toList()
    val a = partes(nueva); val b = partes(actual)
    for (i in 0 until maxOf(a.size, b.size)) {
        val x = a.getOrElse(i) { 0 }; val y = b.getOrElse(i) { 0 }
        if (x != y) return x > y
    }
    return false
}

/** Pantallas de la app. */
sealed class Pantalla {
    data object Login : Pantalla()
    data object Menu : Pantalla()
    data class Consultar(val codigo: String? = null) : Pantalla()
    data object Transferir : Pantalla()
    data class TransfContenedor(val contenedor: String? = null) : Pantalla()
    data class TransfPosicion(val posicion: String? = null, val nivel: Int? = null) : Pantalla()
    data object TransfVarios : Pantalla()
    data object Guardar : Pantalla()
    data class CambiarEstado(val contenedor: String? = null) : Pantalla()
    data class ReubicarCajas(val origen: String? = null) : Pantalla()
    data object Mensajes : Pantalla()
    data object TransfMulti : Pantalla()
    data class EliminarContenedor(val contenedor: String? = null) : Pantalla()
    data class AjusteContenedor(val contenedor: String? = null) : Pantalla()
    data object CrearContenedor : Pantalla()
    data class EnviarNn(val contenedores: List<String> = emptyList()) : Pantalla()
    data object Reconocer : Pantalla()
    data object IaPendientes : Pantalla()
    data object Picking : Pantalla()
    data object ControlBulto : Pantalla()
    data class ConsultarPedido(val nro: String? = null) : Pantalla()
    data class ControlPedido(val nro: String? = null) : Pantalla()
    data object ConteoTareas : Pantalla()
    data class ConteoTarea(val id: Int, val nombre: String) : Pantalla()
    data class ConteoPosicion(val tareaId: Int?, val posicion: String?, val modo: String = "ciego") : Pantalla()
    data object MisMovimientos : Pantalla()
    data object Autorizaciones : Pantalla()
    data object Ajustes : Pantalla()
    data class Esperando(val grupo: String, val titulo: String) : Pantalla()
}

/** Botones del menú y el permiso (ZUSR) que habilita cada uno, en el orden en que se muestran. */
val OpcionesPermiso = listOf(
    "consultar" to "consultar", "ingreso" to "ingreso", "picking" to "picking", "bultos" to "control_bultos", "control" to "control_pedido",
    "pedido" to "consultar_pedido", "transferir" to "transferir", "cajas" to "reubicar_cajas", "guardar" to "guardar", "ajustes" to "ajustar",
    "crear" to "crear_contenedor", "nn" to "enviar_nn", "conteo" to "conteo", "estado" to "cambiar_estado", "autorizaciones" to "autorizar",
    "reconocer" to "reconocer_foto", "eliminar" to "eliminar", "mensajes" to "mensajes", "movs" to "mis_movimientos",
)

/** Opciones del menú según el rol (solo si el servidor no manda permisos: versiones viejas). */
fun opcionesDe(rol: String): List<String> = when (rol) {
    "maquinista" -> listOf("consultar", "transferir", "guardar", "estado", "mensajes", "movs")
    "conteo" -> listOf("consultar", "conteo", "estado", "mensajes", "movs")
    "picker" -> listOf("picking", "bultos", "control", "pedido", "consultar", "mensajes", "movs")
    "supervisor", "produccion" -> listOf("consultar", "pedido", "mensajes")
    // Guardar es solo para maquinistas. Analistas y administradores: Ajustes, Crear contenedor y Enviar a NN
    "analista" -> listOf("consultar", "ingreso", "transferir", "ajustes", "crear", "nn", "conteo", "estado", "pedido", "autorizaciones", "mensajes", "movs")
    "admin" -> listOf("consultar", "ingreso", "transferir", "ajustes", "crear", "nn", "conteo", "estado", "pedido", "control", "autorizaciones", "mensajes", "movs", "reconocer", "eliminar")
    else -> listOf("consultar", "mensajes", "movs")
}

class AppViewModel(app: Application) : AndroidViewModel(app) {
    val ajustes = Ajustes(app)
    val api = Api(ajustes, VERSION)
    val cola = Cola(app, api)
    val eventos = Eventos(api, ajustes, viewModelScope)

    private val pila = mutableStateListOf<Pantalla>(if (ajustes.token.isNotBlank()) Pantalla.Menu else Pantalla.Login).apply {
        // Android cerró la app mientras estaba abierta la cámara de "Reconocer por foto": se vuelve a esa pantalla para procesar la foto
        if (ajustes.token.isNotBlank() && ajustes.fotoIaPendiente.isNotBlank() && System.currentTimeMillis() - ajustes.fotoIaHora < 30 * 60_000) add(Pantalla.Reconocer)
    }
    val pantalla: Pantalla get() = pila.last()
    var mensaje by mutableStateOf<Pair<String, Boolean>?>(null)   // texto, esError
    var rol by mutableStateOf(ajustes.rol)
    /** Permisos del usuario que manda el servidor (null = servidor viejo: se usa el rol). */
    var permisos by mutableStateOf(leerPermisos())
    private fun leerPermisos(): Set<String>? = ajustes.permisos.takeIf { it.isNotBlank() }?.split(",")?.toSet()

    /** ¿El usuario tiene este permiso? (el administrador siempre). */
    fun puede(permiso: String): Boolean = rol == "admin" || (permisos?.contains(permiso)
        ?: (OpcionesPermiso.firstOrNull { it.second == permiso }?.first?.let { it in opcionesDe(rol) } ?: false))

    /** Botones del menú que le corresponden al usuario. */
    fun opciones(): List<String> {
        val p = permisos ?: return opcionesDe(rol)
        return OpcionesPermiso.filter { (op, perm) -> (rol == "admin" || perm in p) && !(op == "cajas" && (rol == "admin" || "transferir" in p)) }.map { it.first }
    }

    /** Vuelve a leer rol y permisos del servidor (si un administrador los cambió en ZUSR). */
    fun refrescarPermisos() {
        if (ajustes.token.isBlank()) return
        viewModelScope.launch {
            try {
                val yo = api.obj("/api/yo?version=$VERSION")
                yo.txt("rol")?.let { rol = it; ajustes.rol = it }
                yo.optJSONArray("permisos")?.let { a -> ajustes.permisos = (0 until a.length()).joinToString(",") { a.getString(it) }; permisos = leerPermisos() }
            } catch (e: ErrorApi) { if (e.codigo == 401) salir(false) } catch (_: Exception) {}
        }
    }
    var reconectando by mutableStateOf(false)
    /** Mensajes del panel sin leer (insignia del menú). */
    var mensajesSinLeer by mutableStateOf(0)
    /** Picking en pausa (OP y artículo): el menú muestra el cartel para retomarlo. null = no hay picking en pausa. */
    var pickingEnPausa by mutableStateOf<String?>(null)
    /** Versión nueva de la app publicada en el servidor (null = está al día). */
    var actualizacion by mutableStateOf<JSONObject?>(null)
    /** Avance de la descarga de la app nueva (null = no está bajando). */
    var bajandoApp by mutableStateOf<Float?>(null)

    private val imagenes = object : LruCache<String, Bitmap>(24 * 1024 * 1024) {
        override fun sizeOf(key: String, value: Bitmap) = value.byteCount
    }

    init {
        cola.iniciar(viewModelScope)
        viewModelScope.launch {
            eventos.eventos.collect { e -> alEvento(e) }
        }
        if (ajustes.token.isNotBlank()) {
            eventos.conectar()
            // Al abrir la app con sesión guardada se valida (y si venció, se reconecta solo con la clave guardada)
            refrescarPermisos()
        } else if (ajustes.usuario.isNotBlank() && ajustes.clave.isNotBlank()) {
            // Sesión cerrada pero con usuario guardado: reconecta solo
            viewModelScope.launch { reconectando = true; ingresar(ajustes.usuario, ajustes.clave); reconectando = false }
        }
    }

    // ------------------------------------------------------------------ actualización de la app (desde el servidor)
    fun revisarActualizacion() {
        viewModelScope.launch {
            try {
                val v = api.obj("/api/app/version")
                val nueva = v.txt("version")
                actualizacion = if (nueva != null && esMasNueva(nueva, VERSION)) v else null
            } catch (_: Exception) {}
        }
    }

    /** Baja el APK del servidor y abre el instalador de Android (la primera vez Android pide permitir "instalar apps de SONA"). */
    fun instalarActualizacion(ctx: android.content.Context) {
        if (bajandoApp != null) return
        val pm = ctx.packageManager
        if (android.os.Build.VERSION.SDK_INT >= 26 && !pm.canRequestPackageInstalls()) {
            avisar("Permití \"Instalar apps desconocidas\" para SONA y volvé a tocar Actualizar.")
            try {
                ctx.startActivity(android.content.Intent(android.provider.Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, android.net.Uri.parse("package:" + ctx.packageName))
                    .addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK))
            } catch (_: Exception) {}
            return
        }
        bajandoApp = 0f
        viewModelScope.launch {
            try {
                val archivo = java.io.File(java.io.File(ctx.cacheDir, "actualizacion"), "SONA_Deposito.apk")
                api.descargar("/api/app/descargar", archivo) { p -> viewModelScope.launch(Dispatchers.Main) { bajandoApp = p } }
                bajandoApp = null
                val uri = androidx.core.content.FileProvider.getUriForFile(ctx, ctx.packageName + ".archivos", archivo)
                ctx.startActivity(android.content.Intent(android.content.Intent.ACTION_VIEW).setDataAndType(uri, "application/vnd.android.package-archive")
                    .addFlags(android.content.Intent.FLAG_GRANT_READ_URI_PERMISSION or android.content.Intent.FLAG_ACTIVITY_NEW_TASK))
            } catch (e: Exception) {
                bajandoApp = null
                avisar(e.message ?: "No se pudo bajar la app nueva.", true)
            }
        }
    }

    // ------------------------------------------------------------------ navegación
    fun ir(p: Pantalla) { pila.add(p) }
    fun reemplazar(p: Pantalla) { if (pila.size > 1) pila.removeAt(pila.lastIndex); pila.add(p) }
    fun atras(): Boolean { if (pila.size <= 1) return false; pila.removeAt(pila.lastIndex); return true }
    fun alMenu() { pila.clear(); pila.add(Pantalla.Menu) }

    fun leerSinLeer() {
        viewModelScope.launch { try { mensajesSinLeer = api.obj("/api/mensajes/sin-leer").optInt("operario") } catch (_: Exception) {} }
    }

    fun avisar(t: String, error: Boolean = false) { mensaje = t to error; if (error) vibrar(250) }

    /** Sonido de notificación + vibración larga: para que el picker se entere aunque tenga el celular en el bolsillo. */
    fun alertar() {
        try {
            val v = getApplication<Application>().getSystemService(Vibrator::class.java)
            v?.vibrate(VibrationEffect.createWaveform(longArrayOf(0, 500, 200, 500, 200, 800), -1))
        } catch (_: Exception) {}
        try {
            val uri = android.media.RingtoneManager.getDefaultUri(android.media.RingtoneManager.TYPE_NOTIFICATION)
            android.media.RingtoneManager.getRingtone(getApplication(), uri)?.play()
        } catch (_: Exception) {}
    }

    fun vibrar(ms: Long = 60) {
        try {
            @Suppress("DEPRECATION")
            val v = getApplication<Application>().getSystemService(Vibrator::class.java)
            v?.vibrate(VibrationEffect.createOneShot(ms, VibrationEffect.DEFAULT_AMPLITUDE))
        } catch (_: Exception) {}
    }

    /** Ejecuta una llamada al servidor mostrando el error si falla. */
    fun <T> lanzar(alTerminar: (T) -> Unit = {}, alFallar: (String) -> Unit = {}, bloque: suspend () -> T) {
        viewModelScope.launch {
            try { alTerminar(bloque()) } catch (e: Exception) {
                val m = e.message ?: "Error"
                avisar(m, true); alFallar(m)
            }
        }
    }

    // ------------------------------------------------------------------ sesión
    var ingresando by mutableStateOf(false)
    var errorLogin by mutableStateOf<String?>(null)

    fun ingresar(usuario: String, clave: String) {
        ingresando = true; errorLogin = null
        viewModelScope.launch {
            try {
                val r = api.login(usuario, clave)
                rol = ajustes.rol
                permisos = leerPermisos()
                eventos.conectar()
                cola.empujar()
                if (r.optBoolean("sinWms")) avisar("El WMS no responde: los movimientos quedan en cola hasta que vuelva.")
                alMenu()
                revisarActualizacion()
            } catch (e: Exception) {
                errorLogin = e.message
            } finally { ingresando = false }
        }
    }

    fun salir(olvidarClave: Boolean) {
        ajustes.cerrarSesion(olvidarClave)
        eventos.cerrar()
        pila.clear(); pila.add(Pantalla.Login)
    }

    // ------------------------------------------------------------------ movimientos
    /** Cartel con el resultado del WMS cuando termina de cargarse un movimiento (o un grupo: posición completa, NN, multicontenedor). */
    data class ResultadoWms(val ok: Boolean, val titulo: String, val lineas: List<Pair<Boolean, String>>)
    var resultadoWms by mutableStateOf<ResultadoWms?>(null)
    /** Alerta de OP nueva asignada por el panel de producción (cartel con imagen, sonido y vibración). */
    var alertaOp by mutableStateOf<JSONObject?>(null)
    private val gruposMostrados = mutableSetOf<String>()

    private fun nombreTipo(t: String) = when (t) {
        "transferencia" -> "Transferencia"; "guardado" -> "Guardado"; "cambio_estado" -> "Cambio de estado"; "reubicar_cajas" -> "Reubicar cajas"
        "ajuste" -> "Ajuste"; else -> t.replaceFirstChar { it.uppercase() }
    }

    /** Cuando todos los movimientos de un grupo hecho en este celular tienen respuesta del WMS, se muestra el cartel. */
    private fun revisarGrupo(grupo: String) {
        val items = cola.items.value.filter { it.grupo == grupo }
        if (items.isEmpty() || grupo in gruposMostrados) return
        val finales = setOf("aplicado", "error", "rechazado", "anulado")
        if (items.any { it.estado !in finales }) return
        gruposMostrados.add(grupo)
        val ok = items.all { it.estado == "aplicado" }
        val tipo = nombreTipo(items.first().tipo)
        val titulo = when {
            ok -> if (items.size == 1) "✔ $tipo aprobada por el WMS" else "✔ ${items.size} movimientos aprobados por el WMS"
            items.none { it.estado == "aplicado" } -> if (items.size == 1) "✕ $tipo NO aprobada" else "✕ Ningún movimiento aprobado"
            else -> "⚠ ${items.count { it.estado == "aplicado" }} de ${items.size} aprobados"
        }
        val lineas = items.map { i ->
            val dest = i.destino?.let { " → $it" + (i.nivel?.let { n -> " nivel $n" } ?: "") } ?: ""
            (i.estado == "aplicado") to ("${i.contenedor}$dest" + (i.mensaje?.takeIf { it.isNotBlank() }?.let { "\n$it" } ?: ""))
        }
        resultadoWms = ResultadoWms(ok, titulo, lineas)
        vibrar(if (ok) 120 else 600)
    }

    fun encolar(items: List<ItemCola>) {
        viewModelScope.launch { cola.agregar(items) }
        vibrar(80)
    }

    private suspend fun alEvento(e: JSONObject) {
        when (e.optString("tipo")) {
            "movimiento" -> {
                val id = e.txt("id") ?: return
                cola.actualizar(id, e.optString("estado"), e.txt("mensaje"))
                // cartel de aprobado / no aprobado (reemplaza el aviso suelto de error)
                cola.items.value.firstOrNull { it.id == id }?.let { revisarGrupo(it.grupo) }
            }
            "autorizacion" -> {
                val grupo = e.txt("grupo") ?: return
                val est = e.optString("estado")
                val nuevo = when (est) { "aprobada" -> "pendiente_wms"; "rechazada" -> "rechazado"; "cancelada" -> "anulado"; else -> return }
                cola.items.value.filter { it.grupo == grupo }.forEach { cola.actualizar(it.id, nuevo, if (est == "aprobada") "Autorizado por ${e.txt("resuelto_por") ?: "el analista"}" else "Rechazado por ${e.txt("resuelto_por") ?: "el analista"}") }
                if (cola.items.value.any { it.grupo == grupo }) { avisar(if (est == "aprobada") "✔ Autorización aprobada" else "✕ Autorización rechazada", est != "aprobada"); vibrar(300) }
            }
            "conteo" -> if (e.optString("estado") == "abierta" && rol == "conteo") avisar("Nueva tarea de conteo")
            "app_nueva" -> revisarActualizacion()
            "produccion" -> {
                val accion = e.optString("accion")
                if (accion in setOf("asignada", "reasignada") && e.optString("picker").equals(ajustes.usuario, ignoreCase = true)) {
                    alertaOp = e
                    alertar()
                }
            }
            "aviso" -> e.txt("texto")?.let { avisar(it); vibrar(200) }
            "mensaje" -> {
                val conv = e.optString("conversacion")
                val mio = conv == ajustes.usuario.uppercase() || conv == "*"
                if (mio && e.optBoolean("desde_panel")) {
                    if (pantalla != Pantalla.Mensajes) {
                        mensajesSinLeer++
                        avisar("💬 ${if (conv == "*") "Aviso a todos" else "Panel"}: ${e.optString("texto")}")
                    }
                    vibrar(400)
                }
            }
        }
    }

    // ------------------------------------------------------------------ imágenes (vía servidor, con caché)
    suspend fun imagen(url: String, maxPx: Int): Bitmap? {
        val clave = "$url@$maxPx"
        imagenes.get(clave)?.let { return it }
        val b = api.bytes(url) ?: return null
        return withContext(Dispatchers.Default) {
            try {
                val o = BitmapFactory.Options().apply { inJustDecodeBounds = true }
                BitmapFactory.decodeByteArray(b, 0, b.size, o)
                var muestra = 1
                while (o.outWidth / (muestra * 2) >= maxPx && o.outHeight / (muestra * 2) >= maxPx) muestra *= 2
                BitmapFactory.decodeByteArray(b, 0, b.size, BitmapFactory.Options().apply { inSampleSize = muestra })?.also { imagenes.put(clave, it) }
            } catch (_: Exception) { null }
        }
    }
}
