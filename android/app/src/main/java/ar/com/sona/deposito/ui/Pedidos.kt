package ar.com.sona.deposito.ui

import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.AddAPhoto
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Checkbox
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.FileProvider
import ar.com.sona.deposito.data.Api
import ar.com.sona.deposito.data.lindo
import ar.com.sona.deposito.data.num
import ar.com.sona.deposito.data.objetos
import ar.com.sona.deposito.data.txt
import org.json.JSONArray
import org.json.JSONObject
import java.io.File

/** Consultar pedido / solicitud (ej. 10395-421): datos del pedido, bultos, controles y cada artículo con sus fotos. */
@Composable
fun ConsultarPedidoScreen(vm: AppViewModel, inicial: String?) {
    var nro by remember { mutableStateOf(inicial ?: "") }
    var r by remember { mutableStateOf<JSONObject?>(null) }
    var cargando by remember { mutableStateOf(false) }
    fun buscar(n: String) {
        if (n.isBlank()) return
        cargando = true; r = null
        vm.lanzar(alTerminar = { j: JSONObject -> r = j; cargando = false }, alFallar = { cargando = false }) { vm.api.obj("/api/pedido/${Api.cod(n)}") }
    }
    LaunchedEffect(inicial) { if (!inicial.isNullOrBlank()) buscar(inicial) }
    Escena("Consultar pedido", vm, subtitulo = "N° de OP, solicitud o etiqueta de bulto") {
        CampoEscaneo(vm, nro, { nro = it }, "N° de OP (ej. 2055) o solicitud (10395-421)", alEnviar = { nro = it; buscar(it) })
        if (cargando) Cargando("Buscando el pedido…")
        r?.let { if (it.optString("tipo") == "op") ResultadoOp(vm, it) else ResultadoPedido(vm, it) }
    }
}

/** Pedido por número de OP: los artículos que SONA vio en la orden de preparación y lo que se pickeó de cada uno, con fotos. */
@Composable
fun ResultadoOp(vm: AppViewModel, p: JSONObject) {
    Tarjeta {
        Text("OP ${p.optString("op")}", fontWeight = FontWeight.Black, fontSize = 20.sp, color = Colores.Marino)
        Text(listOfNotNull(p.txt("empresaNombre"), p.txt("modo")?.let { if (it == "unidades") "por unidades" else "consolidado" }).joinToString(" · "), fontWeight = FontWeight.SemiBold)
        p.txt("destino")?.let { Text("Destino: $it", fontSize = 14.sp) }
        val pk = p.optJSONArray("pickers")?.let { a -> (0 until a.length()).map { a.optString(it) } } ?: emptyList()
        Text(listOfNotNull("${p.optInt("lineas")} línea(s) · ${p.num("unidades")?.lindo() ?: "0"} unidades",
            if (pk.isNotEmpty()) "pickeó ${pk.joinToString(", ")}" else null,
            p.txt("fin")?.let { "terminada ${it.take(16).replace('T', ' ')}" }).joinToString(" · "), color = Colores.Tenue, fontSize = 13.sp)
        p.txt("isla")?.let { Text("Isla de control: $it", color = Colores.Naranja, fontWeight = FontWeight.Bold) }
    }
    val arts = p.optJSONArray("articulos")?.objetos() ?: emptyList()
    Text("Artículos (${arts.size})", fontWeight = FontWeight.Bold, fontSize = 17.sp, color = Colores.Marino)
    arts.forEach { a ->
        val fotos = a.optJSONArray("fotos")?.let { f -> (0 until f.length()).map { f.optString(it) } }?.filter { it.isNotBlank() } ?: emptyList()
        Tarjeta(borde = if (a.optBoolean("completo")) Colores.Ok else Colores.Linea, onClick = { vm.ir(Pantalla.Consultar(a.optString("articulo"))) }) {
            Row(verticalAlignment = Alignment.Top) {
                FotoRemota(vm, fotos.firstOrNull(), 96.dp, fotos)
                Spacer(Modifier.width(12.dp))
                Column(Modifier.weight(1f)) {
                    Text(a.optString("articulo"), fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold, fontSize = 18.sp, color = Colores.Marino)
                    Text(a.txt("descripcion") ?: "", fontWeight = FontWeight.SemiBold, fontSize = 14.sp)
                    Text(listOfNotNull(a.num("cantidad")?.let { "Pide ${it.lindo()}" }, "Pickeado ${a.num("pickeada")?.lindo() ?: "0"}").joinToString(" · "),
                        color = if (a.optBoolean("completo")) Colores.Ok else Colores.Naranja, fontWeight = FontWeight.Bold)
                    a.txt("ubicacion")?.let { Text("En $it", color = Colores.Tenue, fontSize = 12.sp) }
                    val conts = a.optJSONArray("contenedores")?.let { c -> (0 until c.length()).map { c.optString(it) } } ?: emptyList()
                    if (conts.isNotEmpty()) Text("Cont. picking: ${conts.joinToString(", ")}", color = Colores.Tenue, fontSize = 12.sp)
                }
            }
            if (fotos.size > 1) TiraFotos(vm, fotos, 56.dp)
        }
    }
}

@Composable
fun ResultadoPedido(vm: AppViewModel, p: JSONObject) {
    val nro = p.optString("nroSolicitud")
    Tarjeta {
        Text("Solicitud $nro", fontWeight = FontWeight.Black, fontSize = 20.sp, color = Colores.Marino)
        Text(listOfNotNull(p.txt("cliente"), p.txt("proyecto")).joinToString(" · "), fontWeight = FontWeight.SemiBold)
        p.txt("pdv")?.let { Text("PDV: $it", fontSize = 14.sp) }
        Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            p.txt("estado")?.let { Chip(it, Colores.InfoClaro, Colores.Info) }
            when (p.txt("preparado")) { "SI" -> Chip("Preparado", Colores.OkClaro, Colores.Ok); "NO" -> Chip("Sin preparar", Colores.AvisoClaro, Colores.Aviso) }
            when (p.txt("despachado")) { "SI" -> Chip("Despachado", Colores.OkClaro, Colores.Ok); "NO" -> Chip("Sin despachar", Colores.Fondo, Colores.Tenue) }
            if (p.has("controlado") && !p.isNull("controlado"))
                if (p.optBoolean("controlado")) Chip("✔ Controlado" + (p.txt("usuarioControl")?.let { " por $it" } ?: ""), Colores.OkClaro, Colores.Ok)
                else Chip("Sin controlar", Colores.ErrorClaro, Colores.Error)
        }
        val fechas = listOfNotNull(p.txt("viaje")?.let { "Viaje $it" }, p.txt("fechaRealizacion")?.let { "Realización ${it.take(10)}" }, p.txt("fechaPreparacion")?.let { "Preparado ${it.take(16)}" })
        if (fechas.isNotEmpty()) Text(fechas.joinToString(" · "), color = Colores.Tenue, fontSize = 12.sp)
    }
    val bultos = p.optJSONArray("bultos")?.objetos() ?: emptyList()
    if (bultos.isNotEmpty()) Tarjeta {
        Text("Bultos (${bultos.size}) · ${p.num("m3")?.let { "%.3f".format(it) } ?: "-"} m³", fontWeight = FontWeight.Bold)
        bultos.forEach { b ->
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(b.optString("nombre"), fontWeight = FontWeight.Bold, color = Colores.Naranja, modifier = Modifier.width(40.dp))
                Text(b.optString("numero"), fontFamily = FontFamily.Monospace, modifier = Modifier.weight(1f))
                Text(listOfNotNull(b.num("alto"), b.num("ancho"), b.num("profundidad")).joinToString(" × ") { it.lindo() } + " cm", color = Colores.Tenue, fontSize = 13.sp)
            }
        }
    }
    val arts = p.optJSONArray("articulos")?.objetos() ?: emptyList()
    Text("Artículos (${arts.size})", fontWeight = FontWeight.Bold, fontSize = 17.sp, color = Colores.Marino)
    p.txt("avisoArticulos")?.let { Aviso(it, TipoAviso.Info) }
    arts.forEach { a ->
        val fotos = a.optJSONArray("fotos")?.let { f -> (0 until f.length()).map { f.optString(it) } }?.filter { it.isNotBlank() } ?: emptyList()
        Tarjeta(onClick = { vm.ir(Pantalla.Consultar(a.optString("articulo"))) }) {
            Row(verticalAlignment = Alignment.Top) {
                FotoRemota(vm, fotos.firstOrNull(), 96.dp, fotos)
                Spacer(Modifier.width(12.dp))
                Column(Modifier.weight(1f)) {
                    Text(a.optString("articulo"), fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold, fontSize = 18.sp, color = Colores.Marino)
                    Text(a.txt("descripcion") ?: "", fontWeight = FontWeight.SemiBold, fontSize = 14.sp)
                    a.num("cantidad")?.let { Text("Cantidad: ${it.lindo()}", color = Colores.Naranja, fontWeight = FontWeight.Bold) }
                    if (fotos.isEmpty()) Text("Sin foto en tradebox", color = Colores.Tenue, fontSize = 12.sp)
                }
            }
            if (fotos.size > 1) TiraFotos(vm, fotos, 56.dp)
        }
    }
    val controles = p.optJSONArray("controles")?.objetos() ?: emptyList()
    if (controles.isNotEmpty()) Tarjeta {
        Text("Controles hechos en SONA (${controles.size})", fontWeight = FontWeight.Bold)
        controles.forEach { c ->
            Text("${c.optString("hora").take(16).replace('T', ' ')} · ${c.optString("usuario")} · " +
                (if (c.optBoolean("completo")) "completo" else "obs.: ${c.txt("observacion") ?: ""}") + (c.num("bultos")?.let { " · ${it.lindo()} bultos" } ?: ""), fontSize = 13.sp)
            HorizontalDivider(color = Colores.Linea)
        }
    }
    val datos = p.optJSONArray("datos")?.objetos() ?: emptyList()
    if (datos.isNotEmpty()) Tarjeta {
        Text("Datos del buscador", fontWeight = FontWeight.Bold)
        datos.forEach { d -> Row { Text(d.optString("campo"), color = Colores.Tenue, fontSize = 13.sp, modifier = Modifier.width(140.dp)); Text(d.optString("valor"), fontSize = 13.sp) } }
    }
    if (vm.puede("control_pedido") || vm.puede("control_bultos")) Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        BotonPrincipal("Controlar pedido", Modifier.weight(1f), color = Colores.Ok) { vm.ir(Pantalla.ControlPedido(nro)) }
        BotonPrincipal("Medir bultos", Modifier.weight(1f), color = Colores.Marino) { vm.ir(Pantalla.ControlBulto) }
    }
}

@Composable
private fun Chip(t: String, fondo: androidx.compose.ui.graphics.Color, color: androidx.compose.ui.graphics.Color) {
    Text(t, color = color, fontSize = 12.sp, fontWeight = FontWeight.Bold,
        modifier = Modifier.clip(RoundedCornerShape(50)).background(fondo).padding(horizontal = 10.dp, vertical = 3.dp))
}

/**
 * Control de pedido: el picker controla la solicitud (cantidad controlada, bultos, "pedido completo" u observación, foto del bulto).
 * Queda grabado en SONA (productividad del control) y, si está configurado, se avisa al grupo de Telegram con la foto.
 */
@Composable
fun ControlPedidoScreen(vm: AppViewModel, inicial: String?) {
    val ctx = LocalContext.current
    var nro by remember { mutableStateOf(inicial ?: "") }
    var pedido by remember { mutableStateOf<JSONObject?>(null) }
    var cargando by remember { mutableStateOf(false) }
    var cantidad by remember { mutableStateOf("") }
    var bultos by remember { mutableStateOf("") }
    var completo by remember { mutableStateOf(false) }
    var observacion by remember { mutableStateOf("") }
    val fotos = remember { mutableStateListOf<String>() }
    val articulos = remember { mutableStateListOf<Pair<String, String>>() }
    var codArt by remember { mutableStateOf("") }
    var confirmar by remember { mutableStateOf(false) }
    var enviando by remember { mutableStateOf(false) }
    var pendienteFoto by remember { mutableStateOf<File?>(null) }
    val camara = rememberLauncherForActivityResult(ActivityResultContracts.TakePicture()) { ok ->
        val f = pendienteFoto
        if (ok && f != null && f.exists()) { achicar(f); fotos.add(f.absolutePath) } else f?.delete()
        pendienteFoto = null
    }
    fun sacarFoto() {
        val dir = File(ctx.filesDir, "fotos").apply { mkdirs() }
        val f = File(dir, "control_${System.currentTimeMillis()}.jpg")
        pendienteFoto = f
        val uri: Uri = FileProvider.getUriForFile(ctx, ctx.packageName + ".archivos", f)
        camara.launch(uri)
    }
    fun limpiar() { pedido = null; cantidad = ""; bultos = ""; completo = false; observacion = ""; fotos.clear(); articulos.clear() }
    fun buscar(n: String) {
        if (n.isBlank()) return
        limpiar(); cargando = true
        vm.lanzar(alTerminar = { j: JSONObject ->
            pedido = j; cargando = false
            j.optJSONArray("bultos")?.length()?.takeIf { it > 0 }?.let { bultos = it.toString() }
        }, alFallar = { cargando = false }) { vm.api.obj("/api/pedido/${Api.cod(n)}") }
    }
    fun enviar() {
        val p = pedido ?: return
        enviando = true
        vm.lanzar(alTerminar = { r: JSONObject ->
            enviando = false
            vm.avisar("✔ Control de ${p.optString("nroSolicitud")} registrado" + when (r.txt("telegram")) { "enviado" -> " y enviado al grupo"; else -> "" })
            fotos.forEach { File(it).delete() }
            limpiar(); nro = ""
        }, alFallar = { enviando = false }) {
            val ids = fotos.map { vm.api.subirFoto(File(it)) }
            val cuerpo = JSONObject().put("completo", completo).put("fotos", JSONArray(ids))
            cantidad.toDoubleOrNull()?.let { cuerpo.put("cantidad", it) }
            bultos.toIntOrNull()?.let { cuerpo.put("bultos", it) }
            if (observacion.isNotBlank()) cuerpo.put("observacion", observacion.trim())
            cuerpo.put("articulos", JSONArray(articulos.map { (a, c) -> JSONObject().put("articulo", a).also { j -> c.toDoubleOrNull()?.let { j.put("cantidad", it) } } }))
            vm.api.post("/api/pedido/${Api.cod(p.optString("nroSolicitud"))}/control", cuerpo)
        }
    }
    LaunchedEffect(inicial) { if (!inicial.isNullOrBlank()) buscar(inicial) }

    Escena("Control de pedido", vm, subtitulo = "Cantidad controlada, bultos, observación y foto") {
        CampoEscaneo(vm, nro, { nro = it }, "N° de solicitud / pedido", alEnviar = { nro = it; buscar(it) })
        if (cargando) Cargando("Buscando el pedido…")
        pedido?.let { p ->
            Tarjeta {
                Text("Solicitud ${p.optString("nroSolicitud")}", fontWeight = FontWeight.Bold, fontSize = 18.sp, color = Colores.Marino)
                Text(listOfNotNull(p.txt("cliente"), p.txt("proyecto"), p.txt("pdv")).joinToString(" · "), fontSize = 13.sp)
                BotonChico("Ver el pedido con fotos") { vm.ir(Pantalla.ConsultarPedido(p.optString("nroSolicitud"))) }
            }
            CampoNumero(cantidad, { cantidad = it }, "Cantidad controlada (unidades)", Modifier.fillMaxWidth(), decimales = true)
            CampoNumero(bultos, { bultos = it }, "Bultos", Modifier.fillMaxWidth())
            Tarjeta(borde = if (completo) Colores.Ok else Colores.Linea, onClick = { completo = !completo }) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Checkbox(completo, { completo = it })
                    Text("Pedido completo", fontWeight = FontWeight.Bold, color = if (completo) Colores.Ok else Colores.Texto)
                }
            }
            OutlinedTextField(observacion, { observacion = it }, label = { Text(if (completo) "Observación (opcional)" else "Observación general (obligatoria si no está completo)") },
                modifier = Modifier.fillMaxWidth(), minLines = 2)
            Text("Artículos controlados (opcional)", fontWeight = FontWeight.Bold)
            CampoEscaneo(vm, codArt, { codArt = it }, "Escaneá el artículo", alEnviar = { a ->
                if (articulos.none { it.first == a }) articulos.add(a to ""); codArt = ""
            })
            articulos.forEachIndexed { i, (a, c) ->
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(a, fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold, modifier = Modifier.weight(1f))
                    CampoNumero(c, { articulos[i] = a to it }, "Cant.", Modifier.width(130.dp), decimales = true, conBoton = false)
                    IconButton(onClick = { articulos.removeAt(i) }) { Icon(Icons.Filled.Delete, "Quitar", tint = Colores.Error) }
                }
            }
            Text("Foto del bulto", fontWeight = FontWeight.Bold)
            Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                fotos.forEach { f -> MiniaturaLocal(f) { fotos.remove(f); File(f).delete() } }
                Box(Modifier.size(90.dp).clip(RoundedCornerShape(10.dp)).background(Colores.NaranjaClaro)
                    .border(androidx.compose.foundation.BorderStroke(1.dp, Colores.Naranja), RoundedCornerShape(10.dp)).clickable { sacarFoto() },
                    contentAlignment = Alignment.Center) { Icon(Icons.Filled.AddAPhoto, "Sacar foto", tint = Colores.Naranja, modifier = Modifier.size(34.dp)) }
            }
            val listo = completo || observacion.isNotBlank()
            if (!listo) Aviso("Tildá \"Pedido completo\" o escribí la observación.", TipoAviso.Info)
            BotonPrincipal("Confirmar control", habilitado = listo && !enviando, cargando = enviando, color = Colores.Ok) { confirmar = true }
        }
    }
    if (confirmar) AlertDialog(
        onDismissRequest = { confirmar = false },
        title = { Text("¿Confirmar el control?") },
        text = { Text("Solicitud ${pedido?.optString("nroSolicitud")} · ${if (completo) "pedido completo" else "con observación"}" +
            (cantidad.toDoubleOrNull()?.let { " · ${it.lindo()} unidades" } ?: "") + (bultos.toIntOrNull()?.let { " · $it bultos" } ?: "") +
            (if (fotos.isNotEmpty()) " · ${fotos.size} foto(s)" else "")) },
        confirmButton = { TextButton(onClick = { confirmar = false; enviar() }) { Text("Confirmar", fontWeight = FontWeight.Bold) } },
        dismissButton = { TextButton(onClick = { confirmar = false }) { Text("Cancelar") } },
    )
}
