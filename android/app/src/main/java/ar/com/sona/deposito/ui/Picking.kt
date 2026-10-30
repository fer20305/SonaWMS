package ar.com.sona.deposito.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.width
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.ui.graphics.Color
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import ar.com.sona.deposito.data.lindo
import ar.com.sona.deposito.data.num
import ar.com.sona.deposito.data.objetos
import ar.com.sona.deposito.data.txt
import org.json.JSONArray
import org.json.JSONObject

/**
 * Picking (7-PREPARACIÓN del WMS): consolidado (opción 9) o por unidades (opción 5).
 * Cada pantalla del WMS se muestra como una tarjeta clara con la foto del artículo. SONA controla la posición, el nivel,
 * el artículo y la cantidad ANTES de mandarlos al WMS; si algo no coincide, avisa y no lo manda.
 */
@Composable
fun PickingScreen(vm: AppViewModel) {
    var resp by remember { mutableStateOf<JSONObject?>(null) }
    var cargando by remember { mutableStateOf(true) }
    var error by remember { mutableStateOf<String?>(null) }
    var confirmar by remember { mutableStateOf<Pair<String, String>?>(null) }   // acción, texto
    var aviso by remember { mutableStateOf<String?>(null) }
    var faltante by remember { mutableStateOf(false) }
    var faltanteNota by remember { mutableStateOf("") }

    fun recibir(r: JSONObject) {
        resp = r; cargando = false
        error = r.txt("error")
        aviso = r.txt("aviso")
        if (error != null) { vm.vibrar(400); vm.avisar(error!!, true) } else vm.vibrar(40)
    }
    fun accionJ(cuerpo: JSONObject) {
        cargando = true
        vm.lanzar(alTerminar = { r: JSONObject -> recibir(r) }, alFallar = { cargando = false }) { vm.api.post("/api/picking/accion", cuerpo) }
    }
    fun accion(nombre: String) = accionJ(JSONObject().put("accion", nombre))
    fun iniciar(modo: String) {
        cargando = true
        vm.lanzar(alTerminar = { r: JSONObject -> recibir(r) }, alFallar = { cargando = false }) {
            vm.api.post("/api/picking/iniciar", JSONObject().put("modo", modo))
        }
    }
    fun elegir(op: JSONObject, modo: String) {
        cargando = true
        vm.lanzar(alTerminar = { r: JSONObject -> recibir(r) }, alFallar = { cargando = false }) {
            vm.api.post("/api/picking/elegir", JSONObject().put("empresa", op.optInt("empresa")).put("tipo", op.optString("tipo")).put("op", op.optString("op")).put("modo", modo))
        }
    }
    LaunchedEffect(Unit) {
        // si había un picking en curso, se retoma donde quedó
        vm.lanzar(alTerminar = { r: JSONObject -> if (r.has("paso")) recibir(r) else cargando = false }, alFallar = { cargando = false }) {
            vm.api.obj("/api/picking/actual")
        }
    }

    val paso = resp?.optJSONObject("paso")
    val pantalla = paso?.optString("pantalla")
    val p = paso ?: JSONObject()
    val titulo = when (pantalla) { "ir" -> "Ir a la posición"; "tomar" -> "Tomar"; "etiquetar" -> "Isla de control"; "fin" -> "OP terminada"; "lista" -> "Orden de preparación"; "abandonar" -> "¿Abandonar el pedido?"; else -> "Picking" }
    val enOp = pantalla in setOf("lista", "ir", "tomar", "etiquetar", "abandonar", "otra")
    val sub = listOfNotNull(paso?.txt("opDescripcion"), paso?.txt("op")?.let { "N° $it" }, resp?.txt("empresaNombre")).joinToString(" · ").ifBlank { "Preparación de pedidos" }

    Escena(titulo, vm, subtitulo = sub, acciones = {
        if (enOp) {
            // consultar sin perder la OP: al volver se retoma donde quedó
            IconButton(onClick = { vm.ir(Pantalla.Consultar()) }) { Icon(Icons.Filled.Search, "Consultar", tint = Color.White) }
        }
    }) {
        error?.let { Aviso("⛔ $it", TipoAviso.Error) }
        if (cargando) Cargando("Hablando con el WMS…")
        aviso?.let { Aviso("⏭ $it", TipoAviso.Info) }
        when (pantalla) {
            null, "menu", "menu_rf" -> PickInicio(vm, paso, resp?.optJSONArray("postergadas")?.objetos() ?: emptyList(), cargando, ::iniciar, ::elegir) { accion("siguiente") }
            "lista" -> PickLista(p, resp?.optJSONObject("avance"), cargando, { n -> accionJ(JSONObject().put("accion", "continuar").put("item", n)) }) { a, t -> confirmar = a to t }
            "ir" -> {
                PickIr(vm, p, cargando, { u, n, a, c -> accionJ(JSONObject().put("accion", "leer").put("ubicacion", u).put("nivel", n).put("articulo", a).put("contenedor", c)) }) { a ->
                    if (a == "cancelar") confirmar = a to "¿Interrumpir este artículo en el WMS?" else accion(a)
                }
                if (!p.optBoolean("puedeContinuar")) BotonSecundario("⚠ Faltante: el artículo no está", Modifier.fillMaxWidth(), habilitado = !cargando) { faltanteNota = ""; faltante = true }
            }
            "tomar" -> {
                PickTomar(vm, p, cargando, { cant, cont -> accionJ(JSONObject().put("accion", "tomar").put("cantidad", cant).put("contenedor", cont)) }) { accion(it) }
                BotonSecundario("⚠ Faltante: no hay la cantidad", Modifier.fillMaxWidth(), habilitado = !cargando) { faltanteNota = ""; faltante = true }
            }
            "etiquetar" -> {
                Tarjeta(borde = Colores.Naranja) {
                    Text("Pegá la etiqueta en el contenedor", fontWeight = FontWeight.Bold, fontSize = 18.sp)
                    Text("Dejá el contenedor", color = Colores.Tenue)
                    Text(p.txt("contenedorPicking") ?: "-", fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Black, fontSize = 26.sp, color = Colores.Marino)
                    Text("en la ISLA DE CONTROL", color = Colores.Tenue)
                    Text(p.txt("isla") ?: "-", fontWeight = FontWeight.Black, fontSize = 30.sp, color = Colores.Naranja)
                    Text(listOfNotNull(p.num("totalUnidades")?.let { "${it.lindo()} unidades" }, p.txt("totalM3")?.let { "$it m³" }, p.txt("totalKg")?.let { "$it kg" },
                        p.txt("entrega")?.let { "entrega $it" }).joinToString(" · "), color = Colores.Tenue)
                }
                BotonPrincipal("Listo, lo dejé en la isla", habilitado = !cargando) { accion("aceptar") }
            }
            "fin" -> {
                Aviso("✔ Orden de preparación concluida" + (p.num("totalUnidades")?.let { " · ${it.lindo()} unidades" } ?: "") + (p.txt("isla")?.let { " · isla $it" } ?: ""), TipoAviso.Ok)
                BotonPrincipal("Aceptar", habilitado = !cargando) { accion("aceptar") }
            }
            "abandonar" -> PickAbandonar(p, cargando, { accion("seguir") }) { boton -> accionJ(JSONObject().put("accion", "enviar").put("valores", JSONObject()).put("boton", boton)) }
            else -> PickGenerica(paso, cargando, { accion("seguir") }) { valores, boton -> accionJ(JSONObject().put("accion", "enviar").put("valores", valores).also { j -> boton?.let { j.put("boton", it) } }) }
        }
    }
    if (faltante) AlertDialog(
        onDismissRequest = { faltante = false },
        title = { Text("¿Informar faltante?") },
        text = {
            Column {
                Text("${p.txt("articulo") ?: ""} ${p.txt("descripcion") ?: ""}\nSe avisa al panel de inventario (y a producción) y la OP queda postergada (en stand by) hasta que repongan. Pasás a la siguiente orden.")
                OutlinedTextField(faltanteNota, { faltanteNota = it }, label = { Text("Comentario (opcional)") }, modifier = Modifier.fillMaxWidth())
            }
        },
        confirmButton = { TextButton(onClick = { faltante = false; accionJ(JSONObject().put("accion", "faltante").put("comentario", faltanteNota.trim())) }) { Text("Informar faltante", fontWeight = FontWeight.Bold, color = Colores.Error) } },
        dismissButton = { TextButton(onClick = { faltante = false }) { Text("Cancelar") } },
    )
    confirmar?.let { (a, t) ->
        AlertDialog(
            onDismissRequest = { confirmar = null },
            title = { Text("¿Seguro?") }, text = { Text(t) },
            confirmButton = { TextButton(onClick = { confirmar = null; accion(a) }) { Text("Sí", fontWeight = FontWeight.Bold) } },
            dismissButton = { TextButton(onClick = { confirmar = null }) { Text("No") } },
        )
    }
}

@Composable
private fun PickInicio(vm: AppViewModel, paso: JSONObject?, postergadas: List<JSONObject>, cargando: Boolean, iniciar: (String) -> Unit,
                       elegir: (JSONObject, String) -> Unit, siguiente: () -> Unit) {
    // OPs que el WMS le tiene asignadas (lo asigna el panel de producción), con el avance: el picker elige con cuál seguir
    var misOps by remember { mutableStateOf<JSONObject?>(null) }
    var leyendo by remember { mutableStateOf(false) }
    var abierta by remember { mutableStateOf<String?>(null) }
    var confirmarOp by remember { mutableStateOf<Pair<JSONObject, String>?>(null) }
    fun leerMisOps() {
        leyendo = true
        vm.lanzar(alTerminar = { r: JSONObject -> misOps = r; leyendo = false }, alFallar = { leyendo = false }) { vm.api.obj("/api/picking/mis-ops") }
    }
    LaunchedEffect(Unit) { leerMisOps() }
    val ops = misOps?.optJSONArray("ops")?.objetos() ?: emptyList()
    val empezada = ops.firstOrNull { it.optBoolean("comenzada") }
    fun pedir(o: JSONObject, modo: String) {
        // si tiene otra OP empezada, el WMS le va a dar esa primero: se avisa antes
        if (empezada != null && empezada.optString("op") != o.optString("op")) confirmarOp = o to modo else elegir(o, modo)
    }

    Row(verticalAlignment = Alignment.CenterVertically) {
        Text(if (ops.isEmpty()) "Tus OPs pendientes" else "Elegí la OP a preparar (${ops.size})", fontWeight = FontWeight.Bold, fontSize = 17.sp, modifier = Modifier.weight(1f))
        TextButton(onClick = { leerMisOps() }, enabled = !leyendo) { Text(if (leyendo) "Leyendo…" else "⟳ Actualizar") }
    }
    if (leyendo && misOps == null) Cargando("Buscando tus OPs en el WMS…")
    misOps?.txt("error")?.let { Text(it, color = Colores.Tenue, fontSize = 12.sp) }
    if (misOps != null && ops.isEmpty() && misOps?.txt("error") == null) Aviso("El WMS no te tiene OPs asignadas pendientes. Avisale a producción o tomá la siguiente.")
    ops.forEach { o ->
        val pct = o.optInt("avance").coerceIn(0, 100)
        val clave = o.optString("empresa") + "|" + o.optString("op")
        val nivel = o.optString("prioridadNivel", "normal")
        val colPrio = when (nivel) { "urgente" -> Colores.Error; "alta" -> Colores.Naranja; "baja" -> Colores.Marino2; else -> Colores.Tenue }
        Tarjeta(borde = if (abierta == clave) Colores.Naranja else if (o.optBoolean("standby")) Colores.Error else if (o.optBoolean("comenzada")) Colores.Naranja else Colores.Linea,
            onClick = { if (!cargando) abierta = if (abierta == clave) null else clave }) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text("OP ${o.optString("op")}" + (o.txt("solicitud")?.let { " · $it" } ?: ""), fontWeight = FontWeight.Bold, color = Colores.Marino)
                        if (nivel != "normal") Text("  ${nivel.uppercase()}", color = colPrio, fontWeight = FontWeight.Black, fontSize = 12.sp)
                    }
                    Text(listOfNotNull(o.txt("empresaNombre"), o.txt("lugar")).joinToString(" · "), fontSize = 13.sp)
                    Text(listOfNotNull("${o.optInt("articulos")} art.", o.num("unidades")?.let { "${it.lindo()} u." }, o.txt("viaje")?.let { "viaje $it" }).joinToString(" · "),
                        color = Colores.Tenue, fontSize = 12.sp)
                    if (o.optBoolean("comenzada")) Text("▶ Empezada", color = Colores.Naranja, fontSize = 12.sp, fontWeight = FontWeight.SemiBold)
                    if (o.optBoolean("standby")) Text("⏸ En stand by por faltante", color = Colores.Error, fontSize = 12.sp, fontWeight = FontWeight.SemiBold)
                }
                Text("$pct%", fontWeight = FontWeight.Black, fontSize = 22.sp, color = if (o.optBoolean("comenzada")) Colores.Naranja else Colores.Tenue)
            }
            androidx.compose.material3.LinearProgressIndicator(progress = { pct / 100f }, modifier = Modifier.fillMaxWidth(), color = Colores.Naranja, trackColor = Colores.Linea)
            if (abierta == clave) {
                Text("¿Cómo la preparás?", fontWeight = FontWeight.SemiBold)
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    BotonSecundario("Consolidado", Modifier.weight(1f), habilitado = !cargando) { pedir(o, "consolidado") }
                    BotonSecundario("Por unidades", Modifier.weight(1f), habilitado = !cargando) { pedir(o, "unidades") }
                }
            } else Text("Tocá para elegirla", color = Colores.Tenue, fontSize = 11.sp)
        }
    }
    HorizontalDivider(color = Colores.Linea)
    if (paso?.optString("pantalla") == "menu") {
        Aviso("No hay una OP en curso. Podés tomar la siguiente que te asigne el WMS.")
        BotonPrincipal("Tomar la siguiente OP", habilitado = !cargando) { siguiente() }
    }
    Text("O tomá la siguiente según el orden del WMS", fontWeight = FontWeight.Bold)
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        BotonSecundario("Consolidado", Modifier.weight(1f), habilitado = !cargando) { iniciar("consolidado") }
        BotonSecundario("Por unidades", Modifier.weight(1f), habilitado = !cargando) { iniciar("unidades") }
    }
    if (postergadas.isNotEmpty()) {
        HorizontalDivider(color = Colores.Linea)
        Text("Tus OPs postergadas", fontWeight = FontWeight.Bold)
        postergadas.forEach { TarjetaAvance(it, "OP ${it.optString("op")}") }
        Text("El WMS te las vuelve a asignar cuando terminás las demás.", color = Colores.Tenue, fontSize = 12.sp)
    }
    Text("Al elegir una OP, SONA la pone primera en el WMS y la arranca. Te muestra cada artículo con su foto y controla lo que escaneás antes de mandarlo.",
        color = Colores.Tenue, fontSize = 12.sp)
    confirmarOp?.let { (o, modo) ->
        AlertDialog(
            onDismissRequest = { confirmarOp = null },
            title = { Text("Tenés otra OP empezada") },
            text = { Text("La OP ${empezada?.optString("op")} ya está empezada (${empezada?.optInt("avance")}%). El WMS suele darte primero la empezada. ¿Querés intentar con la OP ${o.optString("op")} igual?") },
            confirmButton = { TextButton(onClick = { confirmarOp = null; elegir(o, modo) }) { Text("Sí, la ${o.optString("op")}", fontWeight = FontWeight.Bold) } },
            dismissButton = { TextButton(onClick = { confirmarOp = null; empezada?.let { elegir(it, modo) } }) { Text("Seguir la empezada") } },
        )
    }
}

@Composable
private fun PickLista(paso: JSONObject, avance: JSONObject?, cargando: Boolean, continuar: (Int) -> Unit, confirmar: (String, String) -> Unit) {
    val items = paso.optJSONArray("items")?.objetos() ?: emptyList()
    avance?.let { TarjetaAvance(it, "Avance de la OP ${paso.txt("op") ?: ""}") }
    Tarjeta {
        Row { Text("Contenedor de picking: ", color = Colores.Tenue); Text(paso.txt("contenedorPicking") ?: "— (lo escaneás al tomar)", fontWeight = FontWeight.Bold, fontFamily = FontFamily.Monospace) }
        paso.txt("destino")?.let { Row { Text("Destino: ", color = Colores.Tenue); Text(it, fontWeight = FontWeight.SemiBold) } }
        Text("${items.size} artículo(s) a pickear", fontWeight = FontWeight.Bold)
    }
    paso.txt("mensaje")?.let { Aviso(it, TipoAviso.Aviso) }
    items.forEachIndexed { i, it ->
        Tarjeta(borde = if (i == 0) Colores.Naranja else Colores.Linea, onClick = { if (!cargando && it.optBoolean("conStock", true)) continuar(it.optInt("orden")) }) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text(it.optString("articulo"), fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold, fontSize = 17.sp, color = Colores.Marino)
                    Text(it.txt("descripcion") ?: "", fontSize = 13.sp)
                    Text(listOfNotNull(it.txt("ubicacion"), it.txt("contenedor")).joinToString(" · "), color = if (it.optBoolean("conStock", true)) Colores.Tenue else Colores.Error, fontSize = 13.sp)
                }
                Text(it.num("cantidad")?.lindo() ?: "-", fontWeight = FontWeight.Black, fontSize = 24.sp, color = Colores.Naranja)
            }
        }
    }
    BotonPrincipal(if (items.isEmpty()) "Continuar" else "Empezar con ${items.firstOrNull()?.optString("articulo") ?: ""}", habilitado = !cargando) {
        continuar(items.firstOrNull()?.optInt("orden") ?: 1)
    }
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        BotonSecundario("Ver pendiente", Modifier.weight(1f), habilitado = !cargando) { confirmar("pendiente", "¿Ver lo pendiente de la OP?") }
        BotonSecundario("Cerrar contenedor", Modifier.weight(1f), habilitado = !cargando) { confirmar("cerrar", "¿Cerrar el contenedor de picking ${paso.txt("contenedorPicking") ?: ""}?") }
    }
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        BotonSecundario("Postergar OP", Modifier.weight(1f), habilitado = !cargando) {
            confirmar("postergar", "¿Postergar la OP ${paso.txt("op") ?: ""}" + (avance?.let { " (va ${it.optInt("porcentaje")}%)" } ?: "") + "? Queda para después y pasás a la siguiente orden.")
        }
    }
}

/** Avance de una OP: porcentaje, líneas hechas y unidades pickeadas (lo que registró SONA). */
@Composable
private fun TarjetaAvance(a: JSONObject, titulo: String) {
    val pct = a.optInt("porcentaje").coerceIn(0, 100)
    Tarjeta {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text(titulo, fontWeight = FontWeight.Bold, color = Colores.Marino)
                Text("${a.optInt("hechas")} de ${a.optInt("lineas")} líneas" + (a.num("unidades")?.takeIf { it > 0 }?.let { " · ${(a.num("pickeadas") ?: 0.0).lindo()} de ${it.lindo()} u." } ?: ""),
                    color = Colores.Tenue, fontSize = 13.sp)
            }
            Text("$pct%", fontWeight = FontWeight.Black, fontSize = 24.sp, color = if (pct >= 100) Colores.Ok else Colores.Naranja)
        }
        val falt = a.optJSONArray("faltantes")?.let { arr -> (0 until arr.length()).map { arr.optString(it) } } ?: emptyList()
        if (falt.isNotEmpty()) Text("⏸ En stand by por faltante: ${falt.joinToString(", ")}", color = Colores.Error, fontSize = 13.sp, fontWeight = FontWeight.SemiBold)
        androidx.compose.material3.LinearProgressIndicator(progress = { pct / 100f }, modifier = Modifier.fillMaxWidth(), color = Colores.Naranja, trackColor = Colores.Linea)
    }
}

/** Datos grandes del artículo a pickear: foto, código, descripción, posición y cantidad. */
@Composable
private fun TarjetaArticulo(vm: AppViewModel, paso: JSONObject, conPosicion: Boolean) {
    val fotos = (paso.optJSONArray("fotos")?.let { a -> (0 until a.length()).map { a.optString(it) } } ?: emptyList()).filter { it.isNotBlank() }
    FotoGrande(vm, fotos)
    Tarjeta {
        Text(paso.optString("articulo"), fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Black, fontSize = 24.sp, color = Colores.Marino)
        Text(paso.txt("descripcion") ?: "", fontWeight = FontWeight.SemiBold, fontSize = 15.sp)
        Row(verticalAlignment = Alignment.Bottom) {
            if (conPosicion) Column(Modifier.weight(1f)) {
                Text("POSICIÓN", color = Colores.Tenue, fontSize = 12.sp, fontWeight = FontWeight.Bold)
                Text(paso.txt("posicion") ?: "-", fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Black, fontSize = 30.sp, color = Colores.Naranja)
                Text(listOfNotNull(paso.txt("ubicCtrl")?.let { "Ubic.Ctrl $it" }, paso.txt("profundidad")?.let { "hueco $it" }).joinToString(" · "), color = Colores.Tenue, fontSize = 12.sp)
            } else Spacer(Modifier.weight(1f))
            Column(horizontalAlignment = Alignment.End) {
                Text("CANTIDAD", color = Colores.Tenue, fontSize = 12.sp, fontWeight = FontWeight.Bold)
                Text(paso.num("cantidad")?.lindo() ?: "-", fontWeight = FontWeight.Black, fontSize = 36.sp, color = Colores.Marino)
                Text(paso.txt("unidad") ?: "unidades", color = Colores.Tenue, fontSize = 12.sp)
            }
        }
        paso.txt("contenedorOrigen")?.let { Row { Text("Del contenedor: ", color = Colores.Tenue); Text(it, fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold) } }
    }
}

@Composable
private fun PickIr(vm: AppViewModel, paso: JSONObject, cargando: Boolean, leer: (String, String, String, String) -> Unit, accion: (String) -> Unit) {
    var ubic by remember(paso.optString("articulo"), paso.optString("posicion")) { mutableStateOf("") }
    var nivel by remember(paso.optString("articulo"), paso.optString("posicion")) { mutableStateOf("") }
    var esArt by remember(paso.optString("articulo"), paso.optString("posicion")) { mutableStateOf(false) }
    val articulo = paso.optString("articulo")
    val fNivel = remember { FocusRequester() }
    // ya no se escanea el artículo ni el contenedor: el picker confirma que el artículo es el que pide la OP
    fun mandar() { if (ubic.isNotBlank() && nivel.isNotBlank() && esArt) leer(ubic, nivel, articulo, "") }
    TarjetaArticulo(vm, paso, true)
    if (paso.optBoolean("puedeContinuar")) {
        // aviso del WMS que deja seguir (lote / vencimiento / FIFO-FEFO): se muestra el cartel y se puede continuar
        LaunchedEffect(paso.optString("mensaje")) { vm.vibrar(300) }
        Tarjeta(borde = Colores.Aviso, fondo = Colores.AvisoClaro) {
            Text("⚠ Advertencia del WMS", fontWeight = FontWeight.Bold, fontSize = 17.sp, color = Colores.Aviso)
            Text(paso.txt("mensaje") ?: "El WMS avisa por lote o vencimiento.", fontWeight = FontWeight.SemiBold, color = Colores.Texto)
            Text("Revisá el lote / vencimiento. Si está bien, podés continuar con el picking.", color = Colores.Tenue, fontSize = 13.sp)
        }
        BotonPrincipal("Continuar igual", habilitado = !cargando, color = Colores.Aviso) { accion("continuar_aviso") }
        BotonSecundario("Cancelar", Modifier.fillMaxWidth(), habilitado = !cargando) { accion("cancelar") }
        return
    }
    paso.txt("mensaje")?.let { Aviso(it, TipoAviso.Aviso) }
    CampoEscaneo(vm, ubic, { ubic = it }, "1 · Escaneá la posición (Ubic.Ctrl)", alEnviar = { ubic = it; try { fNivel.requestFocus() } catch (_: Exception) {} })
    CampoNumero(nivel, { nivel = it.take(2) }, "2 · Tipeá el nivel", Modifier.fillMaxWidth(), fNivel) { }
    Tarjeta(borde = if (esArt) Colores.Ok else Colores.Naranja, onClick = { esArt = !esArt }) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text("3 · ¿Es este artículo?", color = Colores.Tenue, fontSize = 12.sp, fontWeight = FontWeight.Bold)
                Text(articulo, fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Black, fontSize = 20.sp, color = Colores.Marino)
                Text(if (esArt) "Confirmado: es el que pide la orden" else "Tocá para confirmar que es el que pide la orden", fontSize = 13.sp,
                    color = if (esArt) Colores.Ok else Colores.Tenue)
            }
            Text(if (esArt) "✔" else "○", fontSize = 30.sp, fontWeight = FontWeight.Black, color = if (esArt) Colores.Ok else Colores.Tenue)
        }
    }
    BotonPrincipal("Confirmar", habilitado = !cargando && ubic.isNotBlank() && nivel.isNotBlank() && esArt) { mandar() }
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        BotonSecundario("Volver a la lista", Modifier.weight(1f), habilitado = !cargando) { accion("volver") }
        BotonSecundario("Interrumpir", Modifier.weight(1f), habilitado = !cargando) { accion("cancelar") }
    }
}

@Composable
private fun PickTomar(vm: AppViewModel, paso: JSONObject, cargando: Boolean, tomar: (Double, String) -> Unit, accion: (String) -> Unit) {
    val sugerida = paso.num("cantidad")
    var cant by remember(paso.optString("articulo"), sugerida) { mutableStateOf(sugerida?.lindo() ?: "") }
    var cont by remember(paso.optString("articulo"), sugerida) { mutableStateOf(paso.txt("contenedorPicking") ?: "") }
    TarjetaArticulo(vm, paso, false)
    paso.num("cantPorBulto")?.takeIf { it > 0 }?.let { Text("Unidades por embalaje: ${it.lindo()}", color = Colores.Tenue) }
    paso.txt("mensaje")?.let { Aviso(it, TipoAviso.Aviso) }
    CampoNumero(cant, { cant = it }, "Cantidad que tomaste", Modifier.fillMaxWidth(), decimales = true)
    CampoEscaneo(vm, cont, { cont = it }, "Escaneá la etiqueta del contenedor de picking", alEnviar = { cont = it; cant.toDoubleOrNull()?.let { c -> tomar(c, it) } })
    BotonPrincipal("Confirmar ${cant.ifBlank { "0" }} ${paso.txt("unidad") ?: "unidades"}", habilitado = !cargando && (cant.toDoubleOrNull() ?: 0.0) > 0 && cont.isNotBlank()) {
        tomar(cant.toDouble(), cont)
    }
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        BotonSecundario("Reiniciar", Modifier.weight(1f), habilitado = !cargando) { accion("reiniciar") }
        BotonSecundario("Volver", Modifier.weight(1f), habilitado = !cargando) { accion("volver") }
    }
}

/**
 * El WMS pregunta "¿Abandona el Pedido?" (aparece al salir de una OP). Lo seguro es seguir con el pedido: SONA vuelve al último paso.
 * Los botones del WMS quedan disponibles, con su nombre, por si de verdad se quiere abandonar.
 */
@Composable
private fun PickAbandonar(paso: JSONObject, cargando: Boolean, seguir: () -> Unit, boton: (String) -> Unit) {
    var confirmarBoton by remember { mutableStateOf<Pair<String, String>?>(null) }
    Tarjeta(borde = Colores.Aviso, fondo = Colores.AvisoClaro) {
        Text("El WMS pregunta si abandonás el pedido", fontWeight = FontWeight.Bold, fontSize = 17.sp, color = Colores.Aviso)
        Text(listOfNotNull(paso.txt("op")?.let { "OP $it" }, paso.txt("entrega")?.let { "entrega $it" }).joinToString(" · ").ifBlank { paso.txt("texto") ?: "" }, color = Colores.Texto)
        Text("Si querías hacer otra cosa (una consulta), seguí con el pedido y usá la lupa de arriba: la OP queda como está.", color = Colores.Tenue, fontSize = 13.sp)
    }
    BotonPrincipal("No, seguir con el pedido", habilitado = !cargando) { seguir() }
    val botones = paso.optJSONArray("botones")?.let { a -> (0 until a.length()).map { a.optString(it) } } ?: emptyList()
    val etiquetas = paso.optJSONArray("etiquetas")?.let { a -> (0 until a.length()).map { a.optString(it) } } ?: emptyList()
    botones.forEachIndexed { i, b ->
        val et = etiquetas.getOrNull(i) ?: b
        val texto = if (b.contains("ACEPTAR", true)) "Sí, abandonar el pedido ($et)" else "$et (opción del WMS)"
        BotonSecundario(texto, Modifier.fillMaxWidth(), habilitado = !cargando) { confirmarBoton = b to texto }
    }
    confirmarBoton?.let { (b, t) ->
        AlertDialog(
            onDismissRequest = { confirmarBoton = null },
            title = { Text("¿Seguro?") }, text = { Text("Se le contesta al WMS: \"$t\". Esto sí cambia la OP en el WMS.") },
            confirmButton = { TextButton(onClick = { confirmarBoton = null; boton(b) }) { Text("Sí", fontWeight = FontWeight.Bold) } },
            dismissButton = { TextButton(onClick = { confirmarBoton = null }) { Text("No") } },
        )
    }
}

/** Pantalla del WMS que SONA todavía no conoce: se muestran sus campos y botones tal cual (y queda guardada para incorporarla). */
@Composable
private fun PickGenerica(paso: JSONObject?, cargando: Boolean, volverAlPedido: () -> Unit, enviar: (JSONObject, String?) -> Unit) {
    if (paso == null) return
    Aviso("Pantalla del WMS que SONA todavía no conoce. Se muestra tal cual.", TipoAviso.Aviso)
    paso.txt("texto")?.let { Tarjeta { Text(it, fontSize = 13.sp) } }
    val valores = remember(paso.toString()) { mutableStateMapOf<String, String>() }
    val campos = paso.optJSONArray("campos")?.let { a -> (0 until a.length()).map { a.optString(it) } } ?: emptyList()
    campos.forEach { c -> OutlinedTextField(valores[c] ?: "", { valores[c] = it }, label = { Text(c) }, modifier = Modifier.fillMaxWidth()) }
    val botones = paso.optJSONArray("botones")?.let { a -> (0 until a.length()).map { a.optString(it) } } ?: emptyList()
    val etiquetas = paso.optJSONArray("etiquetas")?.let { a -> (0 until a.length()).map { a.optString(it) } } ?: emptyList()
    BotonPrincipal("Volver al pedido", habilitado = !cargando, color = Colores.Marino) { volverAlPedido() }
    if (botones.isEmpty()) BotonSecundario("Aceptar", habilitado = !cargando) { enviar(JSONObject(valores.toMap()), null) }
    botones.forEachIndexed { i, b -> BotonSecundario(etiquetas.getOrNull(i) ?: b, Modifier.fillMaxWidth(), habilitado = !cargando) { enviar(JSONObject(valores.toMap()), b) } }
    Spacer(Modifier.width(1.dp))
}
