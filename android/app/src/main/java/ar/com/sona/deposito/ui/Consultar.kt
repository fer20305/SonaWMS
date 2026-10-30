package ar.com.sona.deposito.ui

import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
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
import ar.com.sona.deposito.data.Api
import ar.com.sona.deposito.data.ent
import ar.com.sona.deposito.data.lindo
import ar.com.sona.deposito.data.num
import ar.com.sona.deposito.data.objetos
import ar.com.sona.deposito.data.txt
import org.json.JSONObject

val esUbicCtrl = Regex("^\\d{3,8}$")

/** Consultar: escanear Ubic.Ctrl (y nivel, vacío = todos), posición, contenedor o artículo. Como la opción 8 del handheld. */
@Composable
fun ConsultarScreen(vm: AppViewModel, inicial: String?) {
    var codigo by remember { mutableStateOf(inicial ?: "") }
    var nivel by remember { mutableStateOf("") }
    var resultado by remember { mutableStateOf<JSONObject?>(null) }
    var cargando by remember { mutableStateOf(false) }
    val focoNivel = remember { FocusRequester() }
    // buscador "a medida que escribís" (como el filtro por descripción del LES)
    var sugerencias by remember { mutableStateOf<List<JSONObject>>(emptyList()) }
    var errorSug by remember { mutableStateOf<String?>(null) }
    var buscandoSug by remember { mutableStateOf(false) }
    var elegido by remember { mutableStateOf(false) }

    fun buscar(c: String = codigo) {
        if (c.isBlank()) return
        sugerencias = emptyList(); errorSug = null; elegido = true
        cargando = true; resultado = null
        vm.lanzar(alTerminar = { r: JSONObject -> resultado = r; cargando = false }, alFallar = { cargando = false }) {
            vm.api.obj("/api/consulta/${Api.cod(c)}" + (if (nivel.isNotBlank()) "?nivel=$nivel" else ""))
        }
    }
    LaunchedEffect(inicial) { if (!inicial.isNullOrBlank()) buscar(inicial) }
    LaunchedEffect(codigo) {
        val q = codigo.trim()
        // no para Ubic.Ctrl (solo números) ni pedidos (10395-421)
        if (elegido || q.length < 2 || esUbicCtrl.matches(q) || Regex("^\\d+-\\d+$").matches(q)) { sugerencias = emptyList(); errorSug = null; buscandoSug = false; return@LaunchedEffect }
        kotlinx.coroutines.delay(350)   // espera que deje de tipear
        if (elegido) return@LaunchedEffect   // ya buscó (Enter o escáner)
        buscandoSug = true
        try {
            val r = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) { vm.api.obj("/api/articulos/sugerir?q=" + Api.cod(q)) }
            if (!elegido) { sugerencias = r.optJSONArray("items")?.objetos() ?: emptyList(); errorSug = r.txt("errorLes") }
        } catch (_: Exception) {}
        buscandoSug = false
    }

    Escena("Consultar", vm, subtitulo = "Ubic.Ctrl, posición, contenedor, artículo, descripción o pedido") {
        CampoEscaneo(vm, codigo, { codigo = it; elegido = false }, "Escaneá o escribí (también descripción)", alEnviar = { v ->
            // Ubic.Ctrl: el foco pasa al nivel (vacío = recorre todos los niveles)
            if (esUbicCtrl.matches(v)) { try { focoNivel.requestFocus() } catch (_: Exception) {} } else buscar(v)
        })
        Row(verticalAlignment = Alignment.CenterVertically) {
            CampoNumero(nivel, { nivel = it.take(2) }, "Nivel (vacío = todos)", Modifier.weight(1f), focoNivel) { buscar() }
            Spacer(Modifier.width(10.dp))
            BotonPrincipal("Buscar", Modifier.width(130.dp), habilitado = codigo.isNotBlank(), cargando = cargando) { buscar() }
        }
        if (sugerencias.isNotEmpty() || buscandoSug || errorSug != null) {
            Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Text(if (buscandoSug && sugerencias.isEmpty()) "Buscando…" else "${sugerencias.size} artículo(s)" + (if (buscandoSug) " · buscando más…" else ""), color = Colores.Tenue, fontSize = 12.sp)
                sugerencias.take(25).forEach { a ->
                    Tarjeta(onClick = { codigo = a.optString("articulo"); buscar(a.optString("articulo")) }) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Column(Modifier.weight(1f)) {
                                Text(a.optString("articulo"), fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold, color = Colores.Marino)
                                Text(a.txt("descripcion") ?: "", fontSize = 13.sp)
                                Text(listOfNotNull(a.txt("empresaNombre"), if (a.optBoolean("habilitado", true)) null else "deshabilitado en LES").joinToString(" · "), color = Colores.Tenue, fontSize = 12.sp)
                            }
                            Column(horizontalAlignment = Alignment.End) {
                                a.num("stock")?.let { Text(it.lindo(), fontWeight = FontWeight.Black, color = Colores.Naranja) }
                                a.ent("contenedores")?.takeIf { it > 0 }?.let { Text("$it cont.", color = Colores.Tenue, fontSize = 11.sp) }
                            }
                        }
                    }
                }
                errorSug?.let { Text(it, color = Colores.Aviso, fontSize = 12.sp) }
            }
        }
        if (cargando) Cargando(if (esUbicCtrl.matches(codigo) && nivel.isBlank()) "Recorriendo todos los niveles en el WMS…" else "Consultando…")
        resultado?.let { r ->
            when (r.optString("tipo")) {
                "posicion" -> ResultadoPosicion(vm, r)
                "contenedor" -> {
                    r.txt("aviso")?.let { Aviso("⚠ $it", TipoAviso.Aviso) }
                    ResultadoContenedor(vm, r.getJSONObject("contenedor"), r.optJSONArray("movimientos")?.objetos() ?: emptyList(), r.optBoolean("enStock", true))
                }
                "articulo" -> ResultadoArticulo(vm, r.getJSONObject("articulo"))
                "pedido" -> ResultadoPedido(vm, r)
                "descripcion" -> {
                    val arts = r.optJSONArray("articulos")?.objetos() ?: emptyList()
                    Text("${arts.size} artículo(s) con \"${r.optString("texto")}\" en la descripción (LES) · tocá para ver el stock", color = Colores.Tenue, fontSize = 13.sp)
                    arts.forEach { a ->
                        Tarjeta(onClick = { codigo = a.optString("articulo"); buscar(a.optString("articulo")) }) {
                            Text(a.optString("articulo"), fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold, fontSize = 17.sp, color = Colores.Marino)
                            Text(a.txt("descripcion") ?: "", fontSize = 14.sp)
                            Text(listOfNotNull(a.txt("empresaNombre"), a.num("stock")?.let { "stock ${it.lindo()}" },
                                if (a.has("habilitado") && !a.isNull("habilitado") && !a.optBoolean("habilitado")) "deshabilitado" else null).joinToString(" · "),
                                color = Colores.Tenue, fontSize = 12.sp)
                        }
                    }
                }
                else -> Aviso(r.txt("mensaje") ?: "Sin resultados", TipoAviso.Aviso)
            }
        }
    }
}

@Composable
private fun ResultadoPosicion(vm: AppViewModel, r: JSONObject) {
    val p = r.getJSONObject("posicion")
    val niveles = r.getJSONArray("niveles").objetos()
    Text("Posición ${p.txt("legible")}" + (p.ent("ubicCtrl")?.let { " · Ubic.Ctrl $it" } ?: ""), fontWeight = FontWeight.Bold, fontSize = 18.sp)
    if (niveles.isEmpty()) Aviso("El WMS no devolvió niveles para esa posición.", TipoAviso.Aviso)
    val total = niveles.sumOf { it.optInt("ocupados") }
    if (niveles.size > 1) Text("$total contenedores en ${niveles.size} niveles", color = Colores.Tenue)
    niveles.forEach { n ->
        Tarjeta(fondo = Colores.Fondo) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(n.optString("codigo"), fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold, fontSize = 18.sp, color = Colores.Marino, modifier = Modifier.weight(1f))
                val libres = n.optInt("libres")
                Text("${n.optInt("ocupados")}/${n.optInt("capacidad")} · $libres libres", color = if (libres == 0) Colores.Error else Colores.Ok, fontWeight = FontWeight.SemiBold)
            }
            n.optJSONObject("ficha")?.let { f ->
                Text(listOfNotNull(f.txt("tipo"), f.txt("rol_zona"), f.txt("mezcla"), if (!f.optBoolean("habilitada", true)) "INHABILITADA" else null).joinToString(" · "), color = Colores.Tenue, fontSize = 12.sp)
            }
            val detalle = n.getJSONArray("detalle").objetos()
            if (detalle.isEmpty()) Text("Vacía", color = Colores.Tenue)
            detalle.forEach { c ->
                FichaContenedor(vm, c) {
                    // el picker (y quien no transfiere) solo consulta: sin botones de transferencia ni cambio de estado
                    Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        if (puedeTransferir(vm)) BotonChico("Transferir") { vm.ir(Pantalla.TransfContenedor(c.optString("contenedor"))) }
                        if (puedeCambiarEstado(vm)) BotonChico("Estado") { vm.ir(Pantalla.CambiarEstado(c.optString("contenedor"))) }
                        if (puedeNn(vm)) BotonChico("Pasar a NN") { vm.ir(Pantalla.EnviarNn(listOf(c.optString("contenedor")))) }
                        BotonChico("Detalle") { vm.ir(Pantalla.Consultar(c.optString("contenedor"))) }
                    }
                }
            }
            if (detalle.size > 1 && puedeTransferir(vm)) BotonSecundario("Transferir toda la posición") { vm.ir(Pantalla.TransfPosicion(n.optString("codigo"))) }
            if (detalle.isNotEmpty() && puedeNn(vm)) BotonSecundario("Pasar ${if (detalle.size > 1) "toda la posición (${detalle.size})" else "el contenedor"} a NN") {
                vm.ir(Pantalla.EnviarNn(detalle.map { it.optString("contenedor") }))
            }
        }
    }
}

@Composable
private fun ResultadoContenedor(vm: AppViewModel, c: JSONObject, movs: List<JSONObject>, enStock: Boolean = true) {
    FichaContenedor(vm, c) {
        val fs = c.fotos()
        if (fs.size > 1) { Text("Fotos del artículo (${fs.size}) · tocá para ver en grande", color = Colores.Tenue, fontSize = 12.sp); TiraFotos(vm, fs, 56.dp) }
    }
    if (enStock && (puedeTransferir(vm) || puedeCambiarEstado(vm))) Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
        if (puedeTransferir(vm)) BotonPrincipal("Transferir", Modifier.weight(1f)) { vm.ir(Pantalla.TransfContenedor(c.optString("contenedor"))) }
        if (puedeCambiarEstado(vm)) BotonPrincipal("Cambiar estado", Modifier.weight(1f), color = Colores.Marino) { vm.ir(Pantalla.CambiarEstado(c.optString("contenedor"))) }
    }
    if (enStock && puedeNn(vm)) BotonSecundario("Pasar a NN") { vm.ir(Pantalla.EnviarNn(listOf(c.optString("contenedor")))) }
    if (enStock && vm.puede("eliminar")) BotonSecundario("🗑 Eliminar contenedor (ajuste a 0)") { vm.ir(Pantalla.EliminarContenedor(c.optString("contenedor"))) }
    if (enStock) ReimprimirEtiqueta(vm, c.optString("contenedor"))
    Historias(vm, c.txt("contenedor"), c.txt("articulo"), c.txt("empresa"))
    val campos = c.optJSONObject("campos")
    if (campos != null && campos.length() > 0) Tarjeta {
        Text("Datos del buscador", fontWeight = FontWeight.Bold)
        campos.keys().forEach { k -> Row { Text(k, color = Colores.Tenue, fontSize = 13.sp, modifier = Modifier.width(150.dp)); Text(campos.optString(k), fontSize = 13.sp) } }
    }
    if (movs.isNotEmpty()) Tarjeta {
        Text("Movimientos en SONA (${movs.size})", fontWeight = FontWeight.Bold)
        movs.take(30).forEach { m ->
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text("${m.optString("tipo")} ${m.txt("origen") ?: ""} → ${m.txt("destino") ?: ""}", fontSize = 14.sp)
                    Text("${m.optString("usuario")} · ${m.optString("hora_servidor").take(16).replace('T', ' ')}", color = Colores.Tenue, fontSize = 12.sp)
                }
                EstadoChip(m.optString("estado"))
            }
            HorizontalDivider(color = Colores.Linea)
        }
    }
}

/** Historia del WMS oficial (picking, egresos…) y Ingresos/Egresos del e-TMS, a pedido. */
@Composable
private fun Historias(vm: AppViewModel, contenedor: String?, articulo: String?, empresa: String?) {
    var wms by remember(contenedor, articulo) { mutableStateOf<JSONObject?>(null) }
    var etms by remember(articulo) { mutableStateOf<JSONObject?>(null) }
    var cargando by remember { mutableStateOf<String?>(null) }
    fun traerWms(q: String) {
        cargando = "wms"; wms = null
        vm.lanzar(alTerminar = { r: JSONObject -> wms = r; cargando = null }, alFallar = { cargando = null }) { vm.api.obj("/api/historia?$q") }
    }
    Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        if (!contenedor.isNullOrBlank()) BotonChico("📜 Historia en el WMS") { traerWms("contenedor=${Api.cod(contenedor)}") }
        if (!articulo.isNullOrBlank()) {
            if (contenedor.isNullOrBlank()) BotonChico("📜 Historia en el WMS") { traerWms("articulo=${Api.cod(articulo)}") }
            BotonChico("📦 Ingresos / Egresos") {
                cargando = "etms"; etms = null
                vm.lanzar(alTerminar = { r: JSONObject -> etms = r; cargando = null }, alFallar = { cargando = null }) {
                    vm.api.obj("/api/ingresos-egresos/${Api.cod(articulo)}" + (if (!empresa.isNullOrBlank()) "?empresa=$empresa" else ""))
                }
            }
        }
    }
    when (cargando) {
        "wms" -> Cargando("Pidiendo la historia al WMS…")
        "etms" -> Cargando("Consultando el e-TMS…")
    }
    wms?.let { r ->
        val cols = r.optJSONArray("columnas")?.let { a -> (0 until a.length()).map { a.optString(it) } } ?: emptyList()
        val filas = r.optJSONArray("filas")?.objetos() ?: emptyList()
        Tarjeta {
            Text("Historia en el WMS (último año) · ${filas.size}", fontWeight = FontWeight.Bold)
            if (filas.isEmpty()) Text("El WMS no informa movimientos.", color = Colores.Tenue)
            fun v(f: JSONObject, vararg n: String) = n.firstNotNullOfOrNull { k -> cols.firstOrNull { it.startsWith(k, true) }?.let { f.optString(it).takeIf { s -> s.isNotBlank() } } }
            filas.take(60).forEach { f ->
                Column {
                    Text(listOfNotNull(v(f, "Tipo", "Movimiento"), v(f, "Cantidad")?.let { "x$it" }, v(f, "Ubicaci")).joinToString(" · "), fontWeight = FontWeight.SemiBold, fontSize = 14.sp)
                    Text(listOfNotNull(v(f, "Fecha"), v(f, "Hora"), v(f, "Usuario"), v(f, "Doc"), v(f, "Contenedor")?.takeIf { contenedor.isNullOrBlank() }).joinToString(" · "), color = Colores.Tenue, fontSize = 12.sp)
                }
                HorizontalDivider(color = Colores.Linea)
            }
        }
    }
    etms?.let { r ->
        val movs = r.optJSONArray("movimientos")?.objetos() ?: emptyList()
        Tarjeta {
            Text("Ingresos / Egresos (e-TMS)", fontWeight = FontWeight.Bold)
            Text("Ingresado ${r.num("ingresado")?.lindo()} · Egresado ${r.num("egresado")?.lindo()} · Saldo ${r.num("saldo")?.lindo()}", color = Colores.Marino, fontWeight = FontWeight.SemiBold)
            movs.take(80).forEach { m ->
                val egreso = m.optString("tipo") == "EGRESO"
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Column(Modifier.weight(1f)) {
                        Text(if (egreso) "→ ${m.txt("pdv") ?: m.txt("codigoPdv") ?: "PDV"}" else "← ${m.txt("referencia") ?: "Ingreso"}", fontWeight = FontWeight.SemiBold, fontSize = 14.sp)
                        Text(listOfNotNull(m.txt("fecha"), m.txt("remito")?.let { "Rem $it" }, m.txt("solicitud")?.let { "Sol $it" }, m.txt("estado")).joinToString(" · "), color = Colores.Tenue, fontSize = 12.sp)
                    }
                    Text((if (egreso) "-" + (m.num("salida")?.lindo() ?: "0") else "+" + (m.num("entrada")?.lindo() ?: "0")),
                        color = if (egreso) Colores.Error else Colores.Ok, fontWeight = FontWeight.Bold)
                }
                HorizontalDivider(color = Colores.Linea)
            }
        }
    }
}

@Composable
private fun ResultadoArticulo(vm: AppViewModel, a: JSONObject) {
    val fotos = (a.optJSONArray("fotos")?.objetos() ?: emptyList()).mapNotNull { it.txt("url") }
    Tarjeta {
        Row(verticalAlignment = Alignment.Top) {
            FotoRemota(vm, fotos.firstOrNull(), 90.dp, fotos)
            Spacer(Modifier.width(12.dp))
            Column {
                Text(a.optString("articulo"), fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold, fontSize = 18.sp, color = Colores.Marino)
                Text(a.txt("descripcion") ?: "", fontWeight = FontWeight.SemiBold)
                Text(a.txt("empresaNombre") ?: a.txt("cliente") ?: "", color = Colores.Tenue)
                Text("Total: ${a.num("total")?.lindo() ?: "-"}", color = Colores.Tenue)
                a.optJSONObject("les")?.let { l ->
                    if (l.has("habilitado") && !l.isNull("habilitado")) Habilitado(l.optBoolean("habilitado"))
                }
            }
        }
        a.optJSONObject("les")?.let { l ->
            val partes = listOfNotNull(
                l.num("stockFisico")?.let { "Físico ${it.lindo()}" }, l.num("stockDisponible")?.let { "Disp. ${it.lindo()}" },
                l.num("stockNoDisponible")?.takeIf { it > 0 }?.let { "No disp. ${it.lindo()}" }, l.num("stockReservado")?.takeIf { it > 0 }?.let { "Reserv. ${it.lindo()}" },
            )
            if (partes.isNotEmpty()) Text("Stock en LES: " + partes.joinToString(" · "), color = Colores.Marino, fontSize = 13.sp, fontWeight = FontWeight.SemiBold)
            listOfNotNull(l.txt("marca")?.takeIf { it != "No aplica" }, l.txt("proyecto")?.takeIf { !it.startsWith("(") && it != "no aplica" })
                .takeIf { it.isNotEmpty() }?.let { Text(it.joinToString(" · "), color = Colores.Tenue, fontSize = 12.sp) }
        }
        if (fotos.size > 1) TiraFotos(vm, fotos)
        if (fotos.isEmpty()) Text("Este artículo no tiene fotos en tradebox.", color = Colores.Tenue, fontSize = 12.sp)
    }
    Historias(vm, null, a.txt("articulo"), a.txt("empresa"))
    AgregarFotoCatalogo(vm, a.optString("articulo"))
    val conts = a.getJSONArray("contenedores").objetos()
    if (conts.isEmpty()) Aviso("No tiene stock en el depósito según el buscador SONA (está en LES).", TipoAviso.Aviso)
    else {
        val noDisp = conts.count { it.has("disponible") && !it.isNull("disponible") && !it.optBoolean("disponible") }
        Text("${conts.size} contenedores" + if (noDisp > 0) " · $noDisp no disponible(s)" else "", fontWeight = FontWeight.Bold, color = if (noDisp > 0) Colores.Error else Colores.Texto)
    }
    conts.forEach { c ->
        Tarjeta(onClick = { c.txt("contenedor")?.let { vm.ir(Pantalla.Consultar(it)) } }) {
            Row {
                Text(c.txt("contenedor") ?: "-", fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold, modifier = Modifier.weight(1f))
                Text(c.txt("posicion") ?: "", fontFamily = FontFamily.Monospace, color = Colores.Marino2)
                Spacer(Modifier.width(12.dp))
                Text(c.num("cantidad")?.lindo() ?: "", fontWeight = FontWeight.SemiBold)
            }
            // estado según el WMS (Ver un hueco › Datos de Ubicaciones)
            val disp = if (c.has("disponible") && !c.isNull("disponible")) c.optBoolean("disponible") else null
            when (disp) {
                true -> Text("● Disponible", color = Colores.Ok, fontSize = 12.sp, fontWeight = FontWeight.SemiBold)
                false -> Text("● NO DISPONIBLE · ${c.txt("estadoTexto") ?: "BLOQUEADO"}", color = Colores.Error, fontSize = 12.sp, fontWeight = FontWeight.Bold)
                null -> c.txt("estadoTexto")?.let { Text("Estado: $it", color = Colores.Tenue, fontSize = 12.sp) }
            }
        }
    }
}

@Composable
fun BotonChico(texto: String, onClick: () -> Unit) {
    androidx.compose.material3.OutlinedButton(onClick = onClick, contentPadding = androidx.compose.foundation.layout.PaddingValues(horizontal = 14.dp, vertical = 4.dp)) {
        Text(texto, color = Colores.Marino, fontSize = 13.sp)
    }
}


/** Solo los roles que tienen Transferir / Cambiar estado en el menú ven esos botones en Consultar (el picker no). */
fun puedeTransferir(vm: AppViewModel) = vm.puede("transferir")
fun puedeCambiarEstado(vm: AppViewModel) = vm.puede("cambiar_estado")
fun puedeNn(vm: AppViewModel) = vm.puede("enviar_nn")

/** Reimprimir la etiqueta del contenedor (con QR) en la impresora del servidor: Zebra o A4 grande (se configuran en el panel, ZIMP). */
@Composable
private fun ReimprimirEtiqueta(vm: AppViewModel, contenedor: String) {
    var elegir by androidx.compose.runtime.remember { androidx.compose.runtime.mutableStateOf(false) }
    var enviando by androidx.compose.runtime.remember { androidx.compose.runtime.mutableStateOf(false) }
    fun imprimir(formato: String) {
        elegir = false; enviando = true
        vm.lanzar(alTerminar = { r: JSONObject ->
            enviando = false
            if (r.optBoolean("ok")) vm.avisar("🖨 Etiqueta de $contenedor enviada a ${r.optString("impresora")}")
            else vm.avisar("No se pudo imprimir en ${r.optString("impresora")}: ${r.optString("error")}", true)
        }, alFallar = { enviando = false }) { vm.api.post("/api/contenedores/${ar.com.sona.deposito.data.Api.cod(contenedor)}/imprimir", JSONObject().put("formato", formato)) }
    }
    BotonSecundario(if (enviando) "Enviando a la impresora…" else "🖨 Reimprimir etiqueta", habilitado = !enviando) { elegir = true }
    if (elegir) androidx.compose.material3.AlertDialog(
        onDismissRequest = { elegir = false },
        title = { Text("Reimprimir etiqueta de $contenedor") },
        text = { Text("Con código QR. ¿En qué formato?") },
        confirmButton = { androidx.compose.material3.TextButton(onClick = { imprimir("zebra") }) { Text("Zebra (etiqueta)", fontWeight = FontWeight.Bold) } },
        dismissButton = { androidx.compose.material3.TextButton(onClick = { imprimir("a4") }) { Text("Hoja A4 grande") } },
    )
}
