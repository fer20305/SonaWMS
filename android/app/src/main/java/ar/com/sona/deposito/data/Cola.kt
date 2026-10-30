package ar.com.sona.deposito.data

import android.content.Context
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.time.Instant
import java.util.UUID

/**
 * Movimientos hechos en el celular. Se guardan primero en el teléfono (archivo) y se mandan al servidor;
 * si no hay señal quedan en cola y se reenvían solos. El id lo genera el celular: reenviar nunca duplica.
 */
data class ItemCola(
    val id: String,
    val grupo: String,
    val tipo: String,
    val contenedor: String,
    val descripcion: String,
    val destino: String?,
    val nivel: Int?,
    val disponible: Boolean?,
    val motivo: String?,
    val comentario: String?,
    val fotosLocales: List<String>,
    val hora: String,
    val estado: String,          // local | enviado(estado del servidor)
    val mensaje: String?,
    val autorizacion: Int?,
    val cantidad: Double? = null,   // reubicar cajas: cuánto se pasa al contenedor destino
    val multi: Boolean = false,     // multicontenedor: distintas empresas a la misma posición a propósito
    val sumar: Boolean? = null,     // ajuste: true = suma, false = resta (null = eliminar todo)
    val completa: Boolean = false,  // posición completa: al terminar, el servidor mira el origen y mueve los que hayan quedado
    val origen: String? = null,     // posición de donde sale (si se conoce)
    val posicionDestino: String? = null,   // reubicar cajas: Ubic.Ctrl / posición del contenedor que recibe
) {
    val enviado get() = estado != "local"
    fun json() = JSONObject().put("id", id).put("grupo", grupo).put("tipo", tipo).put("contenedor", contenedor)
        .put("descripcion", descripcion).put("destino", destino).put("nivel", nivel).put("disponible", disponible)
        .put("motivo", motivo).put("comentario", comentario).put("fotosLocales", JSONArray(fotosLocales)).put("hora", hora)
        .put("estado", estado).put("mensaje", mensaje).put("autorizacion", autorizacion).put("cantidad", cantidad).put("multi", multi).put("sumar", sumar).put("completa", completa).put("origen", origen).put("posicionDestino", posicionDestino)

    companion object {
        fun de(j: JSONObject) = ItemCola(
            j.getString("id"), j.getString("grupo"), j.getString("tipo"), j.getString("contenedor"), j.optString("descripcion"),
            j.txt("destino"), j.ent("nivel"), if (j.isNull("disponible") || !j.has("disponible")) null else j.optBoolean("disponible"),
            j.txt("motivo"), j.txt("comentario"), j.optJSONArray("fotosLocales")?.textos() ?: emptyList(), j.getString("hora"),
            j.getString("estado"), j.txt("mensaje"), j.ent("autorizacion"), j.num("cantidad"), j.optBoolean("multi"),
            if (j.isNull("sumar") || !j.has("sumar")) null else j.optBoolean("sumar"), j.optBoolean("completa"), j.txt("origen"), j.txt("posicionDestino"),
        )

        fun nuevo(tipo: String, contenedor: String, descripcion: String = "", grupo: String = UUID.randomUUID().toString(), destino: String? = null,
                  nivel: Int? = null, disponible: Boolean? = null, motivo: String? = null, comentario: String? = null, fotos: List<String> = emptyList(),
                  cantidad: Double? = null, multi: Boolean = false, sumar: Boolean? = null, completa: Boolean = false, origen: String? = null, posicionDestino: String? = null) =
            ItemCola(UUID.randomUUID().toString(), grupo, tipo, contenedor.trim().uppercase(), descripcion, destino, nivel, disponible, motivo, comentario,
                fotos, Instant.now().toString(), "local", null, null, cantidad, multi, sumar, completa, origen, posicionDestino)
    }
}

class Cola(context: Context, private val api: Api) {
    private val archivo = File(context.filesDir, "cola.json")
    private val candado = Mutex()
    private val _items = MutableStateFlow(leer())
    val items: StateFlow<List<ItemCola>> = _items
    private val _conectado = MutableStateFlow(true)
    val conectado: StateFlow<Boolean> = _conectado
    private var trabajo: Job? = null
    private val despertador = MutableStateFlow(0)

    private fun leer(): List<ItemCola> = try {
        if (archivo.exists()) JSONArray(archivo.readText()).objetos().map { ItemCola.de(it) } else emptyList()
    } catch (e: Exception) { emptyList() }

    private fun guardar(l: List<ItemCola>) {
        // Se guardan los pendientes y los últimos 150 enviados (para "Mis movimientos")
        val pendientes = l.filter { !it.enviado }
        val enviados = l.filter { it.enviado }.takeLast(150)
        val lista = (pendientes + enviados).sortedBy { it.hora }
        archivo.writeText(JSONArray(lista.map { it.json() }).toString())
        _items.value = lista
    }

    suspend fun agregar(nuevos: List<ItemCola>) {
        candado.withLock { guardar(_items.value + nuevos) }
        despertador.value++
    }

    /** Actualiza el estado de un ítem (por eventos del servidor: aplicado, error, autorizado…). */
    suspend fun actualizar(id: String, estado: String, mensaje: String?) = candado.withLock {
        if (_items.value.none { it.id == id }) return@withLock
        guardar(_items.value.map { if (it.id == id) it.copy(estado = estado, mensaje = mensaje ?: it.mensaje) else it })
    }

    fun iniciar(alcance: CoroutineScope) {
        if (trabajo?.isActive == true) return
        trabajo = alcance.launch(Dispatchers.IO) {
            while (isActive) {
                val hubo = try { enviarPendientes() } catch (e: Exception) { false }
                val espera = if (_items.value.any { !it.enviado }) (if (hubo) 1000L else 8000L) else 30000L
                val antes = despertador.value
                var t = 0L
                while (t < espera && despertador.value == antes) { delay(250); t += 250 }
            }
        }
    }

    fun empujar() { despertador.value++ }

    private suspend fun enviarPendientes(): Boolean {
        val pendientes = _items.value.filter { !it.enviado }.take(100)
        if (pendientes.isEmpty()) return false
        val lote = JSONArray()
        val fotosSubidas = mutableMapOf<String, List<String>>()
        for (p in pendientes) {
            val ids = p.fotosLocales.map { api.subirFoto(File(it)) }
            fotosSubidas[p.id] = ids
            val j = JSONObject().put("id", p.id).put("grupo", p.grupo).put("tipo", p.tipo).put("contenedor", p.contenedor)
                .put("hora_celular", p.hora).put("fotos", JSONArray(ids))
            p.destino?.let { j.put("destino", it) }
            p.nivel?.let { j.put("nivel", it) }
            p.disponible?.let { j.put("disponible", it) }
            p.motivo?.let { j.put("motivo", it) }
            p.comentario?.let { j.put("comentario", it) }
            p.cantidad?.let { j.put("cantidad", it) }
            if (p.multi) j.put("multi", true)
            p.sumar?.let { j.put("sumar", it) }
            if (p.completa) j.put("completa", true)
            p.origen?.let { j.put("origen", it) }
            p.posicionDestino?.let { j.put("posicion_destino", it) }
            lote.put(j)
        }
        val r = try {
            api.post("/api/movimientos", JSONObject().put("movimientos", lote)).also { _conectado.value = true }
        } catch (e: ErrorApi) {
            if (e.codigo == -1) _conectado.value = false
            throw e
        }
        val resultados = r.getJSONArray("resultados").objetos().associateBy { it.getString("id") }
        candado.withLock {
            guardar(_items.value.map { it ->
                val res = resultados[it.id] ?: return@map it
                fotosSubidas[it.id]?.let { _ -> it.fotosLocales.forEach { f -> File(f).delete() } }
                it.copy(estado = res.getString("estado"), mensaje = res.txt("mensaje"), autorizacion = res.ent("autorizacion"), fotosLocales = emptyList())
            })
        }
        return true
    }

    val pendientes: Int get() = _items.value.count { !it.enviado }
}
