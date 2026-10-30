package ar.com.sona.deposito.data

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.MultipartBody
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.asRequestBody
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.Response
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.IOException
import java.net.URLEncoder
import java.util.concurrent.TimeUnit

class ErrorApi(mensaje: String, val codigo: Int = 0) : Exception(mensaje)

/**
 * Cliente del servidor SONA Depósito. Prueba el último servidor que anduvo y, si no responde, el otro
 * (red local / IP externa). Si la sesión venció, vuelve a ingresar solo con el usuario guardado.
 */
class Api(private val ajustes: Ajustes, private val version: String) {
    val http: OkHttpClient = OkHttpClient.Builder()
        .connectTimeout(6, TimeUnit.SECONDS)
        .readTimeout(90, TimeUnit.SECONDS)
        .writeTimeout(60, TimeUnit.SECONDS)
        .pingInterval(25, TimeUnit.SECONDS)
        .build()
    private val json = "application/json; charset=utf-8".toMediaType()
    private val reconexion = Mutex()

    val base: String get() = ajustes.servidores.firstOrNull() ?: ""

    /** Ingresa con usuario y clave del WMS. Devuelve el rol. */
    suspend fun login(usuario: String, clave: String): JSONObject {
        val cuerpo = JSONObject()
            .put("usuario", usuario.trim().uppercase()).put("clave", clave)
            .put("dispositivo", ajustes.dispositivo).put("modelo", "${android.os.Build.MANUFACTURER} ${android.os.Build.MODEL}")
            .put("version", version)
        val r = enviar("POST", "/api/login", cuerpo, conToken = false, reintentar = false)
        val j = JSONObject(r)
        ajustes.usuario = usuario
        ajustes.clave = clave
        ajustes.token = j.getString("token")
        val u = j.getJSONObject("usuario")
        ajustes.rol = u.optString("rol")
        ajustes.permisos = (j.optJSONArray("permisos") ?: u.optJSONArray("permisos"))?.let { a -> (0 until a.length()).joinToString(",") { a.getString(it) } } ?: ""
        ajustes.nombre = u.optString("nombre").takeIf { it != "null" } ?: ""
        return j
    }

    private suspend fun reconectar(): Boolean = reconexion.withLock {
        val u = ajustes.usuario
        val c = ajustes.clave
        if (u.isBlank() || c.isBlank()) return false
        return try { login(u, c); true } catch (e: Exception) { false }
    }

    suspend fun obj(ruta: String): JSONObject = JSONObject(enviar("GET", ruta, null))
    suspend fun lista(ruta: String): JSONArray = JSONArray(enviar("GET", ruta, null))
    suspend fun post(ruta: String, cuerpo: JSONObject): JSONObject = JSONObject(enviar("POST", ruta, cuerpo))
    /** Texto tal cual (ej. la etiqueta en HTML de la impresora virtual). */
    suspend fun texto(ruta: String): String = enviar("GET", ruta, null)

    suspend fun subirFoto(archivo: File): String {
        val parte = MultipartBody.Builder().setType(MultipartBody.FORM)
            .addFormDataPart("foto", archivo.name, archivo.asRequestBody("image/jpeg".toMediaType()))
            .build()
        val r = ejecutar { b -> Request.Builder().url(b + "/api/fotos").post(parte) }
        return JSONObject(r).getString("id")
    }

    /** Sube una foto a una ruta que contesta JSON (ej. reconocimiento por foto). */
    suspend fun subirFotoA(ruta: String, archivo: File): JSONObject {
        val parte = MultipartBody.Builder().setType(MultipartBody.FORM)
            .addFormDataPart("foto", archivo.name, archivo.asRequestBody("image/jpeg".toMediaType()))
            .build()
        return JSONObject(ejecutar { b -> Request.Builder().url(b + ruta).post(parte) })
    }

    /** Baja un archivo grande del servidor (la app nueva) a disco, con avance. */
    suspend fun descargar(ruta: String, destino: File, avance: (Float) -> Unit): File = withContext(Dispatchers.IO) {
        var ultimo: Exception? = null
        for (b in ajustes.servidores) {
            try {
                val sep = if (ruta.contains("?")) "&" else "?"
                http.newCall(Request.Builder().url(b + ruta + sep + "token=" + cod(ajustes.token)).build()).execute().use { r ->
                    if (!r.isSuccessful) throw ErrorApi("El servidor respondió ${r.code} al bajar la app.", r.code)
                    val cuerpo = r.body ?: throw ErrorApi("El servidor no mandó el archivo.")
                    val total = cuerpo.contentLength()
                    destino.parentFile?.mkdirs()
                    val tmp = File(destino.path + ".parcial")
                    cuerpo.byteStream().use { ent ->
                        tmp.outputStream().use { sal ->
                            val buf = ByteArray(64 * 1024); var leido = 0L
                            while (true) {
                                val n = ent.read(buf); if (n < 0) break
                                sal.write(buf, 0, n); leido += n
                                if (total > 0) avance(leido.toFloat() / total)
                            }
                        }
                    }
                    if (destino.exists()) destino.delete()
                    tmp.renameTo(destino)
                    return@withContext destino
                }
            } catch (e: IOException) { ultimo = e }
        }
        throw ErrorApi("No se pudo bajar la app: " + (ultimo?.message ?: "sin conexión"), -1)
    }

    /** Imagen (foto de artículo o de movimiento) vía servidor. */
    suspend fun bytes(rutaOUrl: String): ByteArray? = withContext(Dispatchers.IO) {
        for (b in ajustes.servidores) {
            try {
                val url = if (rutaOUrl.startsWith("http")) rutaOUrl else b + rutaOUrl
                http.newCall(Request.Builder().url(url).build()).execute().use { r ->
                    if (r.isSuccessful) return@withContext r.body?.bytes()
                    return@withContext null
                }
            } catch (e: IOException) { /* probar el otro servidor */ }
        }
        null
    }

    private suspend fun enviar(metodo: String, ruta: String, cuerpo: JSONObject?, conToken: Boolean = true, reintentar: Boolean = true): String {
        try {
            return ejecutar(conToken) { b ->
                val rb = Request.Builder().url(b + ruta)
                when (metodo) {
                    "GET" -> rb.get()
                    else -> rb.method(metodo, (cuerpo ?: JSONObject()).toString().toRequestBody(json))
                }
            }
        } catch (e: ErrorApi) {
            if (e.codigo == 401 && reintentar && conToken && reconectar()) return enviar(metodo, ruta, cuerpo, conToken, false)
            throw e
        }
    }

    private suspend fun ejecutar(conToken: Boolean = true, armar: (String) -> Request.Builder): String = withContext(Dispatchers.IO) {
        val servidores = ajustes.servidores
        if (servidores.isEmpty()) throw ErrorApi("Configurá la dirección del servidor en Ajustes.")
        var ultimo: Exception? = null
        for (b in servidores) {
            try {
                val rb = armar(b)
                if (conToken && ajustes.token.isNotBlank()) rb.header("Authorization", "Bearer " + ajustes.token)
                http.newCall(rb.build()).execute().use { r ->
                    if (ajustes.servidorActivo != b) ajustes.servidorActivo = b
                    return@withContext leer(r)
                }
            } catch (e: IOException) {
                ultimo = e
            }
        }
        throw ErrorApi("Sin conexión con el servidor (${ultimo?.message ?: "no responde"}).", -1)
    }

    private fun leer(r: Response): String {
        val t = r.body?.string() ?: ""
        if (!r.isSuccessful) {
            val msg = try { JSONObject(t).optString("error") } catch (e: Exception) { "" }
            throw ErrorApi(msg.ifBlank { "Error ${r.code}" }, r.code)
        }
        return t
    }

    companion object {
        fun cod(s: String): String = URLEncoder.encode(s.trim(), "UTF-8").replace("+", "%20")
    }
}

// ------------------------------------------------------------------ ayudas JSON
fun JSONObject.txt(k: String): String? = if (isNull(k)) null else optString(k).takeIf { it.isNotEmpty() && it != "null" }
fun JSONObject.num(k: String): Double? = if (isNull(k) || !has(k)) null else optString(k).toDoubleOrNull()
fun JSONObject.ent(k: String): Int? = num(k)?.toInt()
fun JSONArray.objetos(): List<JSONObject> = (0 until length()).mapNotNull { optJSONObject(it) }
fun JSONArray.textos(): List<String> = (0 until length()).map { optString(it) }
fun Double.lindo(): String = if (this % 1.0 == 0.0) toLong().toString() else "%.2f".format(this).trimEnd('0').trimEnd(',', '.')
