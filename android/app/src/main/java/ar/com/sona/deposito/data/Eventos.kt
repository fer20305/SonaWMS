package ar.com.sona.deposito.data

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import org.json.JSONObject

/** Avisos en vivo del servidor (movimiento aplicado/error, autorización resuelta, tarea de conteo nueva). */
class Eventos(private val api: Api, private val ajustes: Ajustes, private val alcance: CoroutineScope) {
    private val _eventos = MutableSharedFlow<JSONObject>(extraBufferCapacity = 64)
    val eventos: SharedFlow<JSONObject> = _eventos
    private val _enVivo = MutableStateFlow(false)
    val enVivo: StateFlow<Boolean> = _enVivo
    private var ws: WebSocket? = null
    private var activo = false

    fun conectar() {
        activo = true
        if (ws != null || ajustes.token.isBlank()) return
        val base = api.base.replaceFirst("http", "ws")
        val req = Request.Builder().url("$base/api/eventos?token=${Api.cod(ajustes.token)}").build()
        ws = api.http.newWebSocket(req, object : WebSocketListener() {
            override fun onOpen(webSocket: WebSocket, response: Response) { _enVivo.value = true }
            override fun onMessage(webSocket: WebSocket, text: String) {
                try { _eventos.tryEmit(JSONObject(text)) } catch (_: Exception) {}
            }
            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) = caida()
            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) = caida()
        })
    }

    private fun caida() {
        ws = null
        _enVivo.value = false
        if (!activo) return
        alcance.launch(Dispatchers.IO) {
            delay(5000)
            // por si venció el token: una consulta liviana fuerza la reconexión
            try { api.obj("/api/yo") } catch (_: Exception) {}
            conectar()
        }
    }

    fun cerrar() {
        activo = false
        ws?.close(1000, "salir")
        ws = null
    }
}
