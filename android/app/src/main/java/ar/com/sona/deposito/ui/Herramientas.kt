package ar.com.sona.deposito.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material3.AlertDialog
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
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import ar.com.sona.deposito.data.Api
import ar.com.sona.deposito.data.ItemCola
import ar.com.sona.deposito.data.lindo
import ar.com.sona.deposito.data.num
import ar.com.sona.deposito.data.txt
import org.json.JSONObject
import java.util.UUID

// ======================================================================= ajustes (analistas y administradores)

/**
 * Ajuste de contenedor (Chequeos y ajustes › 5 del handheld): sumar o restar unidades con motivo.
 * Es lo mismo que "Eliminar" pero con la cantidad que se elige. El WMS no deja ajustar el mismo contenedor dos veces en 5 minutos.
 */
@Composable
fun AjusteContenedorScreen(vm: AppViewModel, inicial: String?) {
    var codigo by remember { mutableStateOf(inicial ?: "") }
    var info by remember { mutableStateOf<JSONObject?>(null) }
    var sumar by remember { mutableStateOf<Boolean?>(null) }
    var cantidad by remember { mutableStateOf("") }
    var motivo by remember { mutableStateOf<String?>(null) }
    var comentario by remember { mutableStateOf("") }
    var confirmar by remember { mutableStateOf(false) }
    var cargando by remember { mutableStateOf(false) }
    fun limpiar() { info = null; sumar = null; cantidad = ""; motivo = null; comentario = "" }
    fun buscar(c: String) {
        limpiar(); cargando = true
        vm.lanzar(alTerminar = { r: JSONObject -> cargando = false; if (r.optBoolean("encontrado")) info = r else vm.avisar("No se encontró $c", true) },
            alFallar = { cargando = false }) { vm.api.obj("/api/contenedor/${Api.cod(c)}") }
    }
    LaunchedEffect(inicial) { if (!inicial.isNullOrBlank()) buscar(inicial) }
    Escena("Ajustar contenedor", vm, subtitulo = "Sumar o restar unidades en el WMS") {
        if (!vm.puede("ajustar")) { Aviso("Tu usuario no tiene permiso para ajustar (ZUSR).", TipoAviso.Error); return@Escena }
        CampoEscaneo(vm, codigo, { codigo = it }, "Contenedor", alEnviar = { codigo = it; buscar(it) })
        if (cargando) Cargando()
        info?.let { c ->
            FichaContenedor(vm, c)
            val tiene = c.num("cantidad")
            Text("¿Qué hacés?", fontWeight = FontWeight.Bold)
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Opcion("＋ Sumar", sumar == true, Colores.Ok, Modifier.weight(1f)) { sumar = true }
                Opcion("－ Restar", sumar == false, Colores.Error, Modifier.weight(1f)) { sumar = false }
            }
            CampoNumero(cantidad, { cantidad = it }, "Cantidad de unidades", Modifier.fillMaxWidth(), decimales = true)
            val cant = cantidad.replace(',', '.').toDoubleOrNull()
            val excede = sumar == false && cant != null && tiene != null && cant > tiene + 1e-6
            if (excede) Aviso("El contenedor tiene ${tiene!!.lindo()}: no podés restar ${cant!!.lindo()}.", TipoAviso.Error)
            if (cant != null && tiene != null && sumar != null && !excede)
                Text("Queda en ${(if (sumar == true) tiene + cant else tiene - cant).lindo()} unidades", color = Colores.Tenue)
            Text("Motivo del ajuste", fontWeight = FontWeight.Bold)
            MOTIVOS_AJUSTE.chunked(2).forEach { fila ->
                Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    fila.forEach { m -> Opcion(m.replace('_', ' ').lowercase().replaceFirstChar { it.uppercase() }, motivo == m, Colores.Marino, Modifier.weight(1f)) { motivo = m } }
                    if (fila.size == 1) Spacer(Modifier.weight(1f))
                }
            }
            OutlinedTextField(comentario, { comentario = it }, label = { Text("Comentario (opcional)") }, modifier = Modifier.fillMaxWidth())
            Aviso("El WMS no deja ajustar el mismo contenedor dos veces en menos de 5 minutos.", TipoAviso.Info)
            BotonPrincipal("Ajustar", habilitado = sumar != null && cant != null && cant > 0 && motivo != null && !excede,
                color = if (sumar == false) Colores.Error else Colores.Naranja) { confirmar = true }
            if (confirmar) AlertDialog(
                onDismissRequest = { confirmar = false },
                title = { Text("¿Ajustar ${c.optString("contenedor")}?") },
                text = { Text("${if (sumar == true) "Sumar" else "Restar"} ${cant?.lindo()} unidades de ${c.txt("articulo") ?: "?"} · motivo $motivo.") },
                confirmButton = {
                    TextButton(onClick = {
                        confirmar = false
                        val item = ItemCola.nuevo("ajuste", c.optString("contenedor"), c.txt("descripcion") ?: "", motivo = motivo,
                            comentario = comentario.ifBlank { null }, cantidad = cant, sumar = sumar == true)
                        vm.encolar(listOf(item))
                        vm.avisar("✔ Ajuste de ${c.optString("contenedor")} registrado. Se carga en el WMS.")
                        limpiar(); codigo = ""
                    }) { Text("Sí, ajustar", fontWeight = FontWeight.Bold) }
                },
                dismissButton = { TextButton(onClick = { confirmar = false }) { Text("Cancelar") } },
            )
        }
    }
}

// ======================================================================= crear contenedor (analistas y administradores)

/**
 * Crear contenedor: LES genera el número (como "Nueva etiqueta" de Ingresos pendientes) y SONA lo da de alta en el WMS
 * con la Carga inicial (Chequeos y ajustes › 7) en la posición, nivel y cantidad que se indican. No depende de ninguna impresora.
 */
@Composable
fun CrearContenedorScreen(vm: AppViewModel) {
    var articulo by remember { mutableStateOf("") }
    var ficha by remember { mutableStateOf<JSONObject?>(null) }
    var cantidad by remember { mutableStateOf("") }
    var tipo by remember { mutableStateOf("PP") }
    var posicion by remember { mutableStateOf("") }
    var nivel by remember { mutableStateOf("") }
    var cargando by remember { mutableStateOf(false) }
    var resultado by remember { mutableStateOf<JSONObject?>(null) }
    var confirmar by remember { mutableStateOf(false) }
    val focoCant = remember { FocusRequester() }
    val focoNivel = remember { FocusRequester() }

    fun buscarArticulo(a: String) {
        ficha = null; resultado = null
        if (a.isBlank()) return
        vm.lanzar(alTerminar = { r: JSONObject -> ficha = r; try { focoCant.requestFocus() } catch (_: Exception) {} }, alFallar = { }) {
            vm.api.obj("/api/articulo/${Api.cod(a)}")
        }
    }
    fun crear(reintentoContenedor: String? = null) {
        cargando = true
        vm.lanzar(alTerminar = { r: JSONObject ->
            cargando = false; resultado = r
            if (r.optBoolean("ok")) { vm.vibrar(80); vm.avisar("✔ ${r.optString("contenedor")} creado y cargado") } else vm.vibrar(400)
        }, alFallar = { cargando = false }) {
            val cuerpo = JSONObject().put("articulo", articulo.trim().uppercase()).put("cantidad", cantidad.replace(',', '.').toDouble())
                .put("posicion", posicion.trim()).put("prefijo", tipo)
            nivel.toIntOrNull()?.let { cuerpo.put("nivel", it) }
            reintentoContenedor?.let { cuerpo.put("contenedor", it) }
            vm.api.post("/api/contenedores/crear", cuerpo)
        }
    }

    Escena("Crear contenedor", vm, subtitulo = "Número de LES + carga inicial en el WMS") {
        if (!vm.puede("crear_contenedor")) { Aviso("Tu usuario no tiene permiso para crear contenedores (ZUSR).", TipoAviso.Error); return@Escena }
        resultado?.let { r ->
            if (r.optBoolean("ok")) {
                Tarjeta(borde = Colores.Ok) {
                    Text("Contenedor creado", color = Colores.Ok, fontWeight = FontWeight.Bold)
                    Text(r.optString("contenedor"), fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Black, fontSize = 30.sp, color = Colores.Marino)
                    Text(r.optString("mensaje"), color = Colores.Tenue)
                }
                BotonPrincipal("Crear otro") { resultado = null; articulo = ""; ficha = null; cantidad = ""; posicion = ""; nivel = "" }
            } else {
                Aviso("⛔ ${r.optString("mensaje")}", TipoAviso.Error)
                r.txt("contenedor")?.let { cont ->
                    BotonPrincipal("Reintentar la carga inicial de $cont", habilitado = !cargando) { crear(cont) }
                }
            }
            if (r.optBoolean("ok")) return@Escena
        }
        CampoEscaneo(vm, articulo, { articulo = it.uppercase(); ficha = null }, "1 · Artículo", alEnviar = { articulo = it.uppercase(); buscarArticulo(it) })
        ficha?.let { f ->
            Tarjeta {
                Text(f.optString("articulo"), fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold, color = Colores.Marino, fontSize = 18.sp)
                Text(f.txt("descripcion") ?: "", fontSize = 14.sp)
                Text(f.txt("empresaNombre") ?: "⚠ Sin empresa: SONA no sabe de qué cliente es", color = if (f.txt("empresaNombre") == null) Colores.Error else Colores.Tenue, fontSize = 13.sp)
            }
        }
        CampoNumero(cantidad, { cantidad = it }, "2 · Cantidad (unidades)", Modifier.fillMaxWidth(), focoCant, decimales = true)
        Text("Tipo de contenedor", fontWeight = FontWeight.Bold)
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            listOf("PP", "PC", "Bulto").forEach { t -> Opcion(t, tipo == t, Colores.Marino, Modifier.weight(1f)) { tipo = t } }
        }
        CampoEscaneo(vm, posicion, { posicion = it }, "3 · Posición (Ubic.Ctrl o FI1502)", alEnviar = { posicion = it; if (esUbicCtrl.matches(it)) try { focoNivel.requestFocus() } catch (_: Exception) {} })
        CampoNumero(nivel, { nivel = it.take(2) }, "4 · Nivel", Modifier.fillMaxWidth(), focoNivel)
        if (cargando) Cargando("Creando el contenedor en LES y cargándolo en el WMS…")
        val cant = cantidad.replace(',', '.').toDoubleOrNull()
        val listo = articulo.isNotBlank() && cant != null && cant > 0 && posicion.isNotBlank() && (nivel.isNotBlank() || !esUbicCtrl.matches(posicion.trim()))
        BotonPrincipal("Crear contenedor", habilitado = listo && !cargando) { confirmar = true }
        if (confirmar) AlertDialog(
            onDismissRequest = { confirmar = false },
            title = { Text("¿Crear contenedor?") },
            text = { Text("$tipo de ${articulo.uppercase()} con ${cant?.lindo()} unidades en ${posicion.trim()}${if (nivel.isNotBlank()) " nivel $nivel" else ""}. Se crea en LES y se carga en el WMS.") },
            confirmButton = { TextButton(onClick = { confirmar = false; crear() }) { Text("Sí, crear", fontWeight = FontWeight.Bold) } },
            dismissButton = { TextButton(onClick = { confirmar = false }) { Text("Cancelar") } },
        )
    }
}

// ======================================================================= enviar a NN (analistas y administradores)

/** Enviar a NN: escaneás uno o varios contenedores y se transfieren a la posición NN precargada (Ubic.Ctrl 5422 nivel 1 por defecto). */
@Composable
fun EnviarNnScreen(vm: AppViewModel, iniciales: List<String> = emptyList()) {
    var codigo by remember { mutableStateOf("") }
    val lista = remember { mutableStateListOf<JSONObject>() }
    var nn by remember { mutableStateOf<JSONObject?>(null) }
    var confirmar by remember { mutableStateOf(false) }
    var comentario by remember { mutableStateOf("") }
    LaunchedEffect(Unit) {
        vm.lanzar(alTerminar = { r: JSONObject -> nn = r }, alFallar = { nn = JSONObject().put("destino", "5422").put("nivel", 1) }) { vm.api.obj("/api/nn") }
    }
    fun agregar(c: String) {
        val k = c.trim().uppercase()
        if (k.isBlank()) return
        if (lista.any { it.optString("contenedor") == k }) { vm.avisar("$k ya está en la lista", true); codigo = ""; return }
        vm.lanzar(alTerminar = { r: JSONObject ->
            if (r.optBoolean("encontrado")) { lista.add(0, r); vm.vibrar(40) } else vm.avisar("No se encontró el contenedor $k", true)
            codigo = ""
        }) { vm.api.obj("/api/contenedor/${Api.cod(k)}") }
    }
    // desde Consultar: llegan ya cargados el contenedor o los de toda la posición
    LaunchedEffect(iniciales) { iniciales.forEach { agregar(it) } }
    val destino = nn?.optString("destino") ?: "5422"
    val nivel = nn?.optInt("nivel", 1) ?: 1
    Escena("Enviar a NN", vm, subtitulo = "Material perdido → NN (Ubic.Ctrl $destino nivel $nivel)") {
        if (!vm.puede("enviar_nn")) { Aviso("Tu usuario no tiene permiso para enviar a NN (ZUSR).", TipoAviso.Error); return@Escena }
        CampoEscaneo(vm, codigo, { codigo = it }, "Escaneá el contenedor", alEnviar = { agregar(it) })
        if (lista.isEmpty()) Aviso("Escaneá uno o varios contenedores: se transfieren todos a NN y queda grabado quién y cuándo.")
        lista.toList().forEach { c ->
            Tarjeta {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Column(Modifier.weight(1f)) {
                        Text(c.optString("contenedor"), fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold, color = Colores.Marino)
                        Text(listOfNotNull(c.txt("articulo"), c.txt("descripcion")).joinToString(" · "), fontSize = 13.sp)
                        Text(listOfNotNull(c.txt("posicion")?.let { "en $it" }, c.num("cantidad")?.let { "${it.lindo()} u." }, c.txt("empresaNombre")).joinToString(" · "),
                            color = Colores.Tenue, fontSize = 12.sp)
                    }
                    IconButton(onClick = { lista.remove(c) }) { Icon(Icons.Filled.Delete, "Quitar", tint = Colores.Error) }
                }
            }
        }
        if (lista.isNotEmpty()) {
            OutlinedTextField(comentario, { comentario = it }, label = { Text("Comentario (opcional)") }, modifier = Modifier.fillMaxWidth())
            BotonPrincipal("Enviar ${lista.size} a NN") { confirmar = true }
        }
        if (confirmar) AlertDialog(
            onDismissRequest = { confirmar = false },
            title = { Text("¿Enviar ${lista.size} contenedor(es) a NN?") },
            text = { Text("Se transfieren a Ubic.Ctrl $destino nivel $nivel en el WMS.") },
            confirmButton = {
                TextButton(onClick = {
                    confirmar = false
                    val grupo = UUID.randomUUID().toString()
                    val items = lista.map {
                        ItemCola.nuevo("transferencia", it.optString("contenedor"), it.txt("descripcion") ?: "", grupo = grupo, destino = destino, nivel = nivel,
                            multi = true, comentario = "Enviado a NN" + (if (comentario.isNotBlank()) ": $comentario" else ""))
                    }
                    vm.encolar(items)
                    vm.avisar("✔ ${items.size} contenedor(es) enviados a NN. Se cargan en el WMS.")
                    lista.clear(); comentario = ""
                }) { Text("Sí, enviar", fontWeight = FontWeight.Bold) }
            },
            dismissButton = { TextButton(onClick = { confirmar = false }) { Text("Cancelar") } },
        )
    }
}
