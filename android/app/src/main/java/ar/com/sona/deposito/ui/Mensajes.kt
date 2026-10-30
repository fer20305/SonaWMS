package ar.com.sona.deposito.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Send
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import ar.com.sona.deposito.data.objetos
import ar.com.sona.deposito.data.txt
import org.json.JSONArray
import org.json.JSONObject
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter

private val horaChat = DateTimeFormatter.ofPattern("dd/MM HH:mm").withZone(ZoneId.systemDefault())

/** Chat con el panel: lo que escribe el operario lo ven todos los analistas; también llegan los avisos "a todos". */
@Composable
fun MensajesScreen(vm: AppViewModel) {
    var lista by remember { mutableStateOf<List<JSONObject>?>(null) }
    var texto by remember { mutableStateOf("") }
    var enviando by remember { mutableStateOf(false) }
    val estadoLista = rememberLazyListState()

    fun cargar() {
        vm.lanzar(alTerminar = { a: JSONArray ->
            lista = a.objetos()
            vm.mensajesSinLeer = 0
            vm.lanzar(alTerminar = { _: JSONObject -> }) { vm.api.post("/api/mensajes/leido", JSONObject()) }
        }, alFallar = { if (lista == null) lista = emptyList() }) { vm.api.lista("/api/mensajes") }
    }
    fun enviar() {
        val t = texto.trim()
        if (t.isEmpty() || enviando) return
        enviando = true
        vm.lanzar(alTerminar = { m: JSONObject -> enviando = false; texto = ""; lista = (lista ?: emptyList()) + m },
            alFallar = { enviando = false }) { vm.api.post("/api/mensajes", JSONObject().put("para", "PANEL").put("texto", t)) }
    }
    LaunchedEffect(Unit) { cargar() }
    LaunchedEffect(Unit) {
        vm.eventos.eventos.collect { e ->
            if (e.optString("tipo") == "mensaje") {
                val conv = e.optString("conversacion")
                if (conv == vm.ajustes.usuario.uppercase() || conv == "*") cargar()
            }
        }
    }
    LaunchedEffect(lista?.size) { val n = lista?.size ?: 0; if (n > 0) estadoLista.animateScrollToItem(n - 1) }

    Escena("Mensajes", vm, subtitulo = "Chat con el panel", desplazable = false, pie = {
        Row(verticalAlignment = Alignment.CenterVertically) {
            OutlinedTextField(
                texto, { texto = it }, placeholder = { Text("Escribí un mensaje…") }, maxLines = 4,
                keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Sentences, imeAction = ImeAction.Send),
                keyboardActions = KeyboardActions(onSend = { enviar() }),
                colors = OutlinedTextFieldDefaults.colors(focusedBorderColor = Colores.Naranja, unfocusedContainerColor = Colores.Panel, focusedContainerColor = Colores.Panel),
                modifier = Modifier.weight(1f).alApretarEnter { enviar() },
            )
            Spacer(Modifier.width(8.dp))
            IconButton(
                onClick = { enviar() }, enabled = texto.isNotBlank() && !enviando,
                modifier = Modifier.size(52.dp).clip(RoundedCornerShape(50)).background(if (texto.isNotBlank()) Colores.Naranja else Colores.Linea),
            ) { Icon(Icons.AutoMirrored.Filled.Send, "Enviar", tint = Color.White) }
        }
    }) {
        val l = lista
        when {
            l == null -> Cargando()
            l.isEmpty() -> Aviso("Todavía no hay mensajes. Escribí abajo: lo ven los analistas en el panel.")
            else -> LazyColumn(Modifier.weight(1f).fillMaxWidth(), state = estadoLista, verticalArrangement = Arrangement.spacedBy(8.dp)) {
                items(l, key = { it.optLong("id") }) { m -> Burbuja(m) }
            }
        }
    }
}

@Composable
private fun Burbuja(m: JSONObject) {
    val delPanel = m.optBoolean("desde_panel")
    val aTodos = m.optString("conversacion") == "*"
    val quien = when {
        !delPanel -> "Yo"
        aTodos -> "📢 Aviso a todos · ${m.txt("nombre") ?: m.optString("de")}"
        else -> "Panel · ${m.txt("nombre") ?: m.optString("de")}"
    }
    val hora = try { horaChat.format(Instant.parse(m.optString("hora"))) } catch (_: Exception) { "" }
    Box(Modifier.fillMaxWidth(), contentAlignment = if (delPanel) Alignment.CenterStart else Alignment.CenterEnd) {
        Column(
            Modifier.widthIn(max = 300.dp).clip(RoundedCornerShape(14.dp))
                .background(if (!delPanel) Colores.Naranja else if (aTodos) Colores.AvisoClaro else Colores.Panel)
                .padding(horizontal = 12.dp, vertical = 8.dp),
        ) {
            Text("$quien · $hora", fontSize = 11.sp, color = if (!delPanel) Color.White.copy(alpha = 0.85f) else Colores.Tenue, fontWeight = FontWeight.SemiBold)
            Text(m.optString("texto"), fontSize = 16.sp, color = if (!delPanel) Color.White else Colores.Texto)
        }
    }
}
