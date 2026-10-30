package ar.com.sona.deposito.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Inventory2
import androidx.compose.material.icons.filled.Layers
import androidx.compose.material.icons.filled.AllInbox
import androidx.compose.material.icons.filled.Inventory
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material3.Checkbox
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import kotlinx.coroutines.withContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.Dispatchers
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import ar.com.sona.deposito.data.Api
import ar.com.sona.deposito.data.ItemCola
import ar.com.sona.deposito.data.ent
import ar.com.sona.deposito.data.lindo
import ar.com.sona.deposito.data.num
import ar.com.sona.deposito.data.objetos
import ar.com.sona.deposito.data.textos
import ar.com.sona.deposito.data.txt
import org.json.JSONArray
import org.json.JSONObject
import java.util.UUID

@Composable
fun TransferirScreen(vm: AppViewModel) {
    Escena("Transferir", vm, subtitulo = "¿Qué vas a mover?") {
        BotonMenu(Icons.Filled.Inventory2, "Un contenedor", "Escaneás el contenedor, ves la foto y elegís el destino", Modifier.fillMaxWidth(), Colores.Naranja) { vm.ir(Pantalla.TransfContenedor()) }
        BotonMenu(Icons.Filled.Layers, "Posición completa", "Escaneás la posición y se mueven todos sus contenedores (sin escanear uno por uno)", Modifier.fillMaxWidth(), Colores.Marino) { vm.ir(Pantalla.TransfPosicion()) }
        BotonMenu(Icons.Filled.AllInbox, "Multicontenedor", "Escaneás varios contenedores (de cualquier empresa) y van todos a la misma posición", Modifier.fillMaxWidth(), Colores.Naranja) { vm.ir(Pantalla.TransfMulti) }
        BotonMenu(Icons.Filled.Inventory, "Reubicar cajas", "Pasar cajas de un contenedor a otro (como la opción 13 del handheld)", Modifier.fillMaxWidth(), Colores.Marino2) { vm.ir(Pantalla.ReubicarCajas()) }
    }
}

// ======================================================================= destino (compartido)

/**
 * Elegir destino: muestra las sugerencias del sistema (Layout Objetivo), permite escanear la etiqueta (Ubic.Ctrl)
 * y después pide el nivel. Valida en el servidor: lleno/inhabilitado/mezcla bloquean; zona de otro cliente pide autorización.
 */
@Composable
fun SelectorDestino(
    vm: AppViewModel,
    contenedores: List<String>,
    origen: String?,
    textoBoton: String,
    multi: Boolean = false,
    alConfirmar: (destino: String, nivel: Int, requiereAutorizacion: Boolean) -> Unit,
) {
    var sugerencias by remember(contenedores) { mutableStateOf<List<JSONObject>?>(null) }
    var carga by remember(contenedores) { mutableStateOf<JSONObject?>(null) }
    var destino by remember { mutableStateOf("") }
    var nivel by remember { mutableStateOf("") }
    var validacion by remember { mutableStateOf<JSONObject?>(null) }
    var validando by remember { mutableStateOf(false) }
    var preguntarForzar by remember { mutableStateOf(false) }
    val focoNivel = remember { FocusRequester() }

    LaunchedEffect(contenedores) {
        if (contenedores.isEmpty()) return@LaunchedEffect
        if (multi) { sugerencias = emptyList(); return@LaunchedEffect }   // multicontenedor: el destino lo elige el operario
        try {
            val r = vm.api.post("/api/sugerencia", JSONObject().put("contenedores", JSONArray(contenedores)).put("origen", origen))
            carga = r.optJSONObject("carga")
            sugerencias = r.getJSONArray("sugerencias").objetos()
        } catch (e: Exception) { sugerencias = emptyList(); vm.avisar("No se pudieron calcular sugerencias: ${e.message}", true) }
    }

    fun validar(d: String = destino, n: String = nivel, forzar: Boolean = false) {
        val niv = n.toIntOrNull()
        if (d.isBlank()) return
        // Ubic.Ctrl sin nivel: falta el nivel. Código con nivel (FI1502) se valida directo.
        if (niv == null && esUbicCtrl.matches(d)) { try { focoNivel.requestFocus() } catch (_: Exception) {}; return }
        validando = true; validacion = null
        vm.lanzar(alTerminar = { v: JSONObject ->
            validacion = v; validando = false
            if (v.optBoolean("ok")) vm.vibrar(60) else vm.vibrar(250)
            // "Transferir igual": si con eso queda bien (o solo pide autorización) se sigue directo
            if (forzar && (v.optJSONArray("bloqueos")?.length() ?: 0) == 0 && (v.optJSONArray("forzables")?.length() ?: 0) == 0) {
                val cod = v.optJSONObject("destino")?.txt("codigo") ?: d
                val nv = v.optJSONObject("destino")?.ent("nivel") ?: niv ?: 1
                alConfirmar(cod, nv, v.txt("autorizacion") != null)
            }
        }, alFallar = { validando = false }) {
            val cuerpo = JSONObject().put("contenedores", JSONArray(contenedores)).put("destino", d).put("origen", origen)
            if (niv != null) cuerpo.put("nivel", niv)
            if (multi) cuerpo.put("multi", true)
            if (forzar) cuerpo.put("forzar", true)
            vm.api.post("/api/validar", cuerpo)
        }
    }

    Text("Destino", fontWeight = FontWeight.Bold, fontSize = 18.sp, color = Colores.Marino)
    carga?.let { c ->
        val partes = listOfNotNull(c.num("m3")?.let { "${it.lindo()} m³" }, if (c.optBoolean("poca")) "poca cantidad" else null)
        if (partes.isNotEmpty()) Text(partes.joinToString(" · "), color = Colores.Tenue, fontSize = 13.sp)
    }
    when {
        sugerencias == null -> Cargando("Buscando la mejor posición…")
        sugerencias!!.isEmpty() -> if (multi) Aviso("Escaneá la posición donde va el pallet armado y tipeá el nivel.") else Aviso("Sin sugerencias para este material: escaneá el destino.", TipoAviso.Aviso)
        else -> {
            Text("Sugeridas (tocá para elegir)", color = Colores.Tenue, fontSize = 13.sp)
            sugerencias!!.forEachIndexed { i, s ->
                val elegida = destino == s.optString("codigo")
                Tarjeta(borde = if (elegida) Colores.Naranja else Colores.Linea, fondo = if (elegida) Colores.NaranjaClaro else Colores.Panel, onClick = {
                    destino = s.optString("codigo"); nivel = s.optInt("nivel").toString(); validar(destino, nivel)
                }) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(if (i == 0) "★" else "${i + 1}", color = Colores.Naranja, fontWeight = FontWeight.Black, fontSize = 20.sp, modifier = Modifier.width(28.dp))
                        Column(Modifier.weight(1f)) {
                            Text(s.optString("legible"), fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold, fontSize = 20.sp, color = Colores.Marino)
                            Text(s.optString("motivo"), color = Colores.Tenue, fontSize = 12.sp)
                        }
                        Column(horizontalAlignment = Alignment.End) {
                            s.ent("ubicCtrl")?.let { Text("Ubic.Ctrl $it", fontSize = 12.sp, color = Colores.Tenue) }
                            s.ent("libres")?.let { Text("$it libres", color = Colores.Ok, fontWeight = FontWeight.SemiBold, fontSize = 13.sp) }
                        }
                    }
                }
            }
        }
    }
    CampoEscaneo(vm, destino, { destino = it; validacion = null }, "Escaneá la etiqueta (Ubic.Ctrl) o posición", alEnviar = { v ->
        destino = v
        if (esUbicCtrl.matches(v)) { nivel = ""; try { focoNivel.requestFocus() } catch (_: Exception) {} } else validar(v, nivel)
    })
    CampoNumero(nivel, { nivel = it.take(2); validacion = null }, "Nivel", Modifier.fillMaxWidth(), focoNivel) { validar() }
    if (validando) Cargando("Controlando el destino en el WMS…")
    validacion?.let { v ->
        val d = v.optJSONObject("destino")
        val o = v.optJSONObject("ocupacion")
        if (d != null) Text("Destino ${d.txt("codigo") ?: ""}" + (o?.let { " · ${it.optInt("ocupados")}/${it.optInt("capacidad")} ocupados" } ?: ""), fontWeight = FontWeight.SemiBold)
        v.optJSONArray("bloqueos")?.textos()?.forEach { Aviso("⛔ $it", TipoAviso.Error) }
        v.optJSONArray("forzables")?.textos()?.forEach { Aviso("⚠ $it", TipoAviso.Aviso) }
        v.txt("autorizacion")?.let { Aviso("✋ $it\nHay que pedir autorización al analista.", TipoAviso.Aviso) }
        v.optJSONArray("avisos")?.textos()?.forEach { Aviso(it, TipoAviso.Info) }
        if (v.optBoolean("ok")) Aviso("✔ Destino correcto", TipoAviso.Ok)
    }
    val v = validacion
    val bloqueado = v == null || (v.optJSONArray("bloqueos")?.length() ?: 0) > 0
    val forzable = !bloqueado && (v?.optJSONArray("forzables")?.length() ?: 0) > 0
    val pideAut = v?.txt("autorizacion") != null
    Spacer(Modifier.height(4.dp))
    if (preguntarForzar) AlertDialog(
        onDismissRequest = { preguntarForzar = false },
        title = { Text("¿Transferir igual?") },
        text = { Text("Según SONA no hay huecos libres, pero el WMS puede aceptarlo (por ejemplo si la posición tiene más profundidad). Si el WMS lo rechaza, el movimiento vuelve con el error.") },
        confirmButton = { TextButton(onClick = { preguntarForzar = false; validar(forzar = true) }) { Text("Sí, transferir igual", fontWeight = FontWeight.Bold) } },
        dismissButton = { TextButton(onClick = { preguntarForzar = false }) { Text("Cancelar") } },
    )
    if (forzable) {
        BotonPrincipal("Transferir igual (que decida el WMS)", habilitado = !validando, color = Colores.Aviso) { preguntarForzar = true }
    } else if (v == null) {
        BotonPrincipal("Controlar destino", habilitado = destino.isNotBlank() && !validando, color = Colores.Marino) { validar() }
    } else {
        BotonPrincipal(if (pideAut) "Pedir autorización y mover" else textoBoton, habilitado = !bloqueado, color = if (pideAut) Colores.Aviso else Colores.Naranja) {
            val cod = v.optJSONObject("destino")?.txt("codigo") ?: destino
            val niv = v.optJSONObject("destino")?.ent("nivel") ?: nivel.toIntOrNull() ?: 1
            alConfirmar(cod, niv, pideAut)
        }
    }
}

/** Encola los movimientos y, si hace falta autorización, abre la espera. */
fun confirmarMovimientos(vm: AppViewModel, items: List<ItemCola>, requiereAutorizacion: Boolean, titulo: String) {
    vm.encolar(items)
    if (requiereAutorizacion) vm.reemplazar(Pantalla.Esperando(items.first().grupo, titulo))
    else { vm.avisar("✔ ${items.size} movimiento(s) registrado(s). Se cargan en el WMS."); vm.atras() }
}

// ======================================================================= un contenedor

@Composable
fun TransfContenedorScreen(vm: AppViewModel, inicial: String?) {
    var codigo by remember { mutableStateOf(inicial ?: "") }
    var info by remember { mutableStateOf<JSONObject?>(null) }
    var cargando by remember { mutableStateOf(false) }
    fun buscar(c: String) {
        cargando = true; info = null
        vm.lanzar(alTerminar = { r: JSONObject -> cargando = false; if (r.optBoolean("encontrado")) info = r else vm.avisar("No se encontró el contenedor $c", true) }, alFallar = { cargando = false }) {
            vm.api.obj("/api/contenedor/${Api.cod(c)}")
        }
    }
    LaunchedEffect(inicial) { if (!inicial.isNullOrBlank()) buscar(inicial) }
    Escena("Transferir contenedor", vm) {
        CampoEscaneo(vm, codigo, { codigo = it }, "Contenedor", alEnviar = { buscar(it) })
        if (cargando) Cargando()
        info?.let { c ->
            FichaContenedor(vm, c)
            val cont = c.optString("contenedor")
            SelectorDestino(vm, listOf(cont), null, "Transferir") { destino, nivel, aut ->
                val item = ItemCola.nuevo("transferencia", cont, c.txt("descripcion") ?: "", destino = destino, nivel = nivel)
                confirmarMovimientos(vm, listOf(item), aut, "$cont → $destino")
            }
        }
    }
}

// ======================================================================= posición completa

@Composable
fun TransfPosicionScreen(vm: AppViewModel, inicial: String?, nivelInicial: Int?) {
    var codigo by remember { mutableStateOf(inicial ?: "") }
    var nivel by remember { mutableStateOf(nivelInicial?.toString() ?: "") }
    var datos by remember { mutableStateOf<JSONObject?>(null) }
    var cargando by remember { mutableStateOf(false) }
    val elegidos = remember { mutableStateListOf<String>() }
    var paso by remember { mutableStateOf(1) }
    val focoNivel = remember { FocusRequester() }

    fun buscar() {
        if (codigo.isBlank()) return
        if (esUbicCtrl.matches(codigo) && nivel.isBlank()) { try { focoNivel.requestFocus() } catch (_: Exception) {}; return }
        cargando = true; datos = null; elegidos.clear(); paso = 1
        vm.lanzar(alTerminar = { r: JSONObject ->
            cargando = false; datos = r
            r.getJSONArray("niveles").objetos().forEach { n -> n.getJSONArray("detalle").objetos().forEach { elegidos.add(it.optString("contenedor")) } }
        }, alFallar = { cargando = false }) {
            vm.api.obj("/api/posicion/${Api.cod(codigo)}" + (if (nivel.isNotBlank()) "?nivel=$nivel" else ""))
        }
    }
    LaunchedEffect(inicial) { if (!inicial.isNullOrBlank()) buscar() }

    val niveles = datos?.optJSONArray("niveles")?.objetos() ?: emptyList()
    val todos = niveles.flatMap { it.getJSONArray("detalle").objetos() }
    val origen = niveles.singleOrNull()?.optString("codigo")
    Escena("Transferir posición", vm, subtitulo = if (paso == 1) "Paso 1: qué se mueve" else "Paso 2: a dónde") {
        if (paso == 1) {
            CampoEscaneo(vm, codigo, { codigo = it }, "Posición (Ubic.Ctrl o FI1502)", alEnviar = { codigo = it; buscar() })
            CampoNumero(nivel, { nivel = it.take(2) }, "Nivel", Modifier.fillMaxWidth(), focoNivel) { buscar() }
            if (cargando) Cargando("Leyendo la posición en el WMS…")
            if (datos != null && todos.isEmpty()) Aviso("La posición está vacía.", TipoAviso.Aviso)
            if (todos.isNotEmpty()) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text("${todos.size} contenedores · ${elegidos.size} elegidos", fontWeight = FontWeight.Bold, modifier = Modifier.weight(1f))
                    BotonChico(if (elegidos.size == todos.size) "Ninguno" else "Todos") {
                        if (elegidos.size == todos.size) elegidos.clear() else { elegidos.clear(); elegidos.addAll(todos.map { it.optString("contenedor") }) }
                    }
                }
                todos.forEach { c ->
                    val k = c.optString("contenedor")
                    FichaContenedor(vm, c) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Checkbox(k in elegidos, { if (it) elegidos.add(k) else elegidos.remove(k) })
                            Text(if (k in elegidos) "Se mueve" else "Se queda", color = if (k in elegidos) Colores.Ok else Colores.Tenue)
                        }
                    }
                }
                BotonPrincipal("Elegir destino para ${elegidos.size}", habilitado = elegidos.isNotEmpty()) { paso = 2 }
            }
        } else {
            Tarjeta(fondo = Colores.Fondo) {
                Text("Se mueven ${elegidos.size} contenedores" + (origen?.let { " desde $it" } ?: ""), fontWeight = FontWeight.Bold)
                Text(elegidos.joinToString("  "), fontFamily = FontFamily.Monospace, fontSize = 13.sp, color = Colores.Tenue)
                BotonChico("Cambiar selección") { paso = 1 }
            }
            SelectorDestino(vm, elegidos.toList(), origen, "Confirmar ${elegidos.size} movimientos") { destino, niv, aut ->
                val grupo = UUID.randomUUID().toString()
                val desc = todos.associate { it.optString("contenedor") to (it.txt("descripcion") ?: "") }
                // si se mueven todos los que se leyeron, es la posición completa: al terminar SONA mira el origen y mueve los que hayan quedado
                val completa = elegidos.size == todos.size
                val items = elegidos.map { ItemCola.nuevo("transferencia", it, desc[it] ?: "", grupo = grupo, destino = destino, nivel = niv, completa = completa, origen = origen) }
                confirmarMovimientos(vm, items, aut, "${items.size} contenedores → $destino")
            }
        }
    }
}

// ======================================================================= varios contenedores

@Composable
fun TransfVariosScreen(vm: AppViewModel) {
    var codigo by remember { mutableStateOf("") }
    val lista = remember { mutableStateListOf<JSONObject>() }
    var paso by remember { mutableStateOf(1) }
    var cargando by remember { mutableStateOf(false) }
    fun agregar(c: String) {
        if (lista.any { it.optString("contenedor") == c }) { vm.avisar("$c ya está en la lista", true); codigo = ""; return }
        cargando = true
        vm.lanzar(alTerminar = { r: JSONObject ->
            cargando = false; codigo = ""
            if (r.optBoolean("encontrado")) { lista.add(r); vm.vibrar() } else vm.avisar("No se encontró $c", true)
        }, alFallar = { cargando = false }) { vm.api.obj("/api/contenedor/${Api.cod(c)}") }
    }
    val porArticulo = lista.groupBy { it.txt("articulo") ?: "?" }
    Escena("Varios contenedores", vm, subtitulo = if (paso == 1) "Escaneá todos los que vas a mover" else "Elegí el destino") {
        if (paso == 1) {
            CampoEscaneo(vm, codigo, { codigo = it }, "Contenedor", alEnviar = { agregar(it) })
            if (cargando) Cargando()
            porArticulo.forEach { (art, cs) ->
                Tarjeta {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        FotoRemota(vm, cs.first().txt("foto"), 56.dp)
                        Spacer(Modifier.width(10.dp))
                        Column(Modifier.weight(1f)) {
                            Text(art, fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold, color = Colores.Marino)
                            Text(cs.first().txt("descripcion") ?: "", fontSize = 13.sp)
                            Text("${cs.size} contenedores · total ${cs.sumOf { it.num("cantidad") ?: 0.0 }.lindo()}", color = Colores.Tenue, fontSize = 12.sp)
                        }
                    }
                    cs.forEach { c ->
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Text(c.optString("contenedor"), fontFamily = FontFamily.Monospace, modifier = Modifier.weight(1f))
                            Text(c.txt("posicion") ?: "", color = Colores.Tenue, fontSize = 12.sp)
                            IconButton(onClick = { lista.remove(c) }) { Icon(Icons.Filled.Delete, "Quitar", tint = Colores.Error) }
                        }
                    }
                }
            }
            if (porArticulo.size > 1) Aviso("Hay ${porArticulo.size} artículos distintos: el destino tiene que admitir mezcla.", TipoAviso.Aviso)
            BotonPrincipal("Elegir destino para ${lista.size}", habilitado = lista.isNotEmpty()) { paso = 2 }
        } else {
            Tarjeta(fondo = Colores.Fondo) {
                Text("${lista.size} contenedores de ${porArticulo.size} artículo(s)", fontWeight = FontWeight.Bold)
                BotonChico("Volver a la lista") { paso = 1 }
            }
            SelectorDestino(vm, lista.map { it.optString("contenedor") }, null, "Confirmar ${lista.size} movimientos") { destino, niv, aut ->
                val grupo = UUID.randomUUID().toString()
                val items = lista.map { ItemCola.nuevo("transferencia", it.optString("contenedor"), it.txt("descripcion") ?: "", grupo = grupo, destino = destino, nivel = niv) }
                confirmarMovimientos(vm, items, aut, "${items.size} contenedores → $destino")
            }
        }
    }
}

// ======================================================================= multicontenedor

/**
 * Multicontenedor: se juntan varios contenedores (pueden ser de distintas empresas) en un pallet y van todos a la misma posición.
 * Después de cada lectura pregunta si seguís escaneando. El servidor controla que haya huecos suficientes y los carga en el WMS
 * cada uno con su empresa.
 */
@Composable
fun TransfMultiScreen(vm: AppViewModel) {
    var codigo by remember { mutableStateOf("") }
    val lista = remember { mutableStateListOf<JSONObject>() }
    var paso by remember { mutableStateOf(1) }
    var cargando by remember { mutableStateOf(false) }
    var preguntar by remember { mutableStateOf<String?>(null) }
    var camara by remember { mutableStateOf(false) }
    fun agregar(c: String) {
        if (lista.any { it.optString("contenedor").equals(c, true) }) { vm.avisar("$c ya está en la lista", true); codigo = ""; return }
        cargando = true
        vm.lanzar(alTerminar = { r: JSONObject ->
            cargando = false; codigo = ""
            if (r.optBoolean("encontrado")) { lista.add(r); vm.vibrar(); preguntar = r.optString("contenedor") } else vm.avisar("No se encontró $c", true)
        }, alFallar = { cargando = false }) { vm.api.obj("/api/contenedor/${Api.cod(c)}") }
    }
    val empresas = lista.mapNotNull { it.txt("empresaNombre") ?: it.txt("cliente") }.distinct()
    Escena("Multicontenedor", vm, subtitulo = if (paso == 1) "Escaneá los contenedores del pallet" else "Elegí la posición") {
        if (paso == 1) {
            CampoEscaneo(vm, codigo, { codigo = it }, "Contenedor", alEnviar = { agregar(it) })
            if (cargando) Cargando()
            if (lista.isNotEmpty()) {
                Text("${lista.size} contenedor(es) · ${empresas.size} empresa(s)", fontWeight = FontWeight.Bold)
                if (empresas.size > 1) Aviso("Hay ${empresas.size} empresas (${empresas.joinToString(", ")}): se cargan en el WMS cada uno con su empresa. La posición tiene que admitir mezcla.", TipoAviso.Aviso)
            }
            lista.forEach { c ->
                Tarjeta {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        FotoRemota(vm, c.txt("foto"), 48.dp)
                        Spacer(Modifier.width(10.dp))
                        Column(Modifier.weight(1f)) {
                            Text(c.optString("contenedor"), fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold, color = Colores.Marino)
                            Text(listOfNotNull(c.txt("articulo"), c.txt("descripcion")).joinToString(" · "), fontSize = 12.sp)
                            Text(listOfNotNull(c.txt("empresaNombre"), c.txt("posicion")?.let { "en $it" }).joinToString(" · "), color = Colores.Tenue, fontSize = 12.sp)
                        }
                        IconButton(onClick = { lista.remove(c) }) { Icon(Icons.Filled.Delete, "Quitar", tint = Colores.Error) }
                    }
                }
            }
            BotonPrincipal("Terminé: elegir posición para ${lista.size}", habilitado = lista.isNotEmpty()) { paso = 2 }
        } else {
            Tarjeta(fondo = Colores.Fondo) {
                Text("${lista.size} contenedores de ${empresas.size} empresa(s)", fontWeight = FontWeight.Bold)
                Text(lista.joinToString("  ") { it.optString("contenedor") }, fontFamily = FontFamily.Monospace, fontSize = 13.sp, color = Colores.Tenue)
                BotonChico("Volver a la lista") { paso = 1 }
            }
            Aviso("Cada contenedor ocupa un hueco en el WMS: la posición tiene que tener ${lista.size} libres. Si faltan, ampliá la profundidad en el WMS y volvé a controlar.")
            SelectorDestino(vm, lista.map { it.optString("contenedor") }, null, "Guardar ${lista.size} en la posición", multi = true) { destino, niv, _ ->
                val grupo = UUID.randomUUID().toString()
                val items = lista.map { ItemCola.nuevo("transferencia", it.optString("contenedor"), it.txt("descripcion") ?: "", grupo = grupo, destino = destino, nivel = niv, multi = true) }
                confirmarMovimientos(vm, items, false, "${items.size} contenedores → $destino")
            }
        }
    }
    preguntar?.let { c ->
        androidx.compose.material3.AlertDialog(
            onDismissRequest = { preguntar = null },
            title = { Text("✔ $c agregado") },
            text = { Text("Llevás ${lista.size} contenedor(es). ¿Seguís escaneando?") },
            confirmButton = { androidx.compose.material3.TextButton(onClick = { preguntar = null; camara = true }) { Text("Sí, escanear otro", fontWeight = FontWeight.Bold) } },
            dismissButton = { androidx.compose.material3.TextButton(onClick = { preguntar = null; paso = 2 }) { Text("No, elegir posición") } },
        )
    }
    if (camara) DialogoEscaner(vm, cerrar = { camara = false }) { leido -> camara = false; agregar(leido.trim().uppercase()) }
}

// ======================================================================= espera de autorización

@Composable
fun EsperandoScreen(vm: AppViewModel, grupo: String, titulo: String) {
    val items by vm.cola.items.collectAsState()
    val mios = items.filter { it.grupo == grupo }
    val estados = mios.map { it.estado }.toSet()
    val aprobado = estados.isNotEmpty() && estados.all { it in setOf("pendiente_wms", "aplicado") }
    val rechazado = estados.any { it in setOf("rechazado", "anulado") }
    // No depende solo del aviso en vivo (si el celular perdió la conexión un rato, el aviso se pierde):
    // cada 4 segundos le pregunta al servidor cómo están los movimientos del pedido.
    LaunchedEffect(grupo) {
        while (true) {
            val actuales = vm.cola.items.value.filter { it.grupo == grupo }
            if (actuales.isNotEmpty() && actuales.none { it.estado in setOf("local", "esperando_autorizacion") }) break
            vm.cola.empujar()
            for (it in actuales.filter { x -> x.enviado && x.estado == "esperando_autorizacion" }) {
                try {
                    val m = withContext(Dispatchers.IO) { vm.api.obj("/api/movimientos/${it.id}") }
                    val est = m.optString("estado")
                    if (est.isNotBlank() && est != it.estado) vm.cola.actualizar(it.id, est, m.optString("comentario").takeIf { c -> c.isNotBlank() && c != "null" })
                } catch (_: Exception) {}
            }
            delay(4000)
        }
    }
    // aviso fuerte cuando responde (sonido + vibración), aunque tenga el celular en el bolsillo
    var avisado by remember(grupo) { mutableStateOf(false) }
    LaunchedEffect(aprobado, rechazado) {
        if ((aprobado || rechazado) && !avisado) { avisado = true; vm.alertar(); vm.avisar(if (aprobado) "✔ Autorización aprobada" else "✕ Autorización rechazada", rechazado) }
    }
    Escena("Autorización", vm) {
        Text(titulo, fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold, fontSize = 18.sp)
        when {
            rechazado -> {
                Aviso("✕ El analista rechazó el movimiento. ${mios.firstOrNull()?.mensaje ?: ""}\nDejá el material donde estaba o elegí otro destino.", TipoAviso.Error)
                BotonPrincipal("Volver", color = Colores.Marino) { vm.atras() }
            }
            aprobado -> {
                Aviso("✔ Autorizado. Ya podés dejar el material en el destino. ${mios.firstOrNull()?.mensaje ?: ""}", TipoAviso.Ok)
                BotonPrincipal("Listo") { vm.atras() }
            }
            else -> {
                Column(Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).background(Colores.AvisoClaro).padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text("✋ Esperando que el analista autorice en el panel…", color = Colores.Aviso, fontWeight = FontWeight.Bold, fontSize = 17.sp)
                    Text(if (estados.contains("local")) "Enviando el pedido al servidor…" else "Te avisamos acá apenas responda. No dejes el material todavía.", color = Colores.Aviso)
                }
                Cargando("Esperando respuesta")
                BotonSecundario("Cancelar pedido") {
                    val aut = mios.firstNotNullOfOrNull { it.autorizacion }
                    if (aut != null) vm.lanzar(alTerminar = { _: JSONObject -> vm.avisar("Pedido cancelado"); vm.atras() }) { vm.api.post("/api/autorizaciones/$aut/cancelar", JSONObject()) }
                    else vm.atras()
                }
            }
        }
    }
}
