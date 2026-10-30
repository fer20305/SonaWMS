package ar.com.sona.deposito.ui

import androidx.compose.material.icons.filled.Refresh
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.width
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import ar.com.sona.deposito.data.Api
import ar.com.sona.deposito.data.lindo
import ar.com.sona.deposito.data.num
import ar.com.sona.deposito.data.objetos
import ar.com.sona.deposito.data.txt
import org.json.JSONArray
import org.json.JSONObject

fun nombreModo(m: String?) = when (m) { "confirmacion" -> "Confirmación"; "articulo" -> "Por artículo"; else -> "Ciego" }

/** Tareas de conteo activas asignadas (las crea el analista desde el panel). Se actualiza sola cuando llega una nueva. */
@Composable
fun ConteoTareasScreen(vm: AppViewModel) {
    var tareas by remember { mutableStateOf<List<JSONObject>?>(null) }
    fun cargar() { vm.lanzar(alTerminar = { a: JSONArray -> tareas = a.objetos() }, alFallar = { tareas = emptyList() }) { vm.api.lista("/api/conteo/tareas?abiertas=1") } }
    LaunchedEffect(Unit) { cargar() }
    Escena("Conteo", vm, subtitulo = "Tareas de conteo activas", acciones = {
        androidx.compose.material3.IconButton(onClick = { tareas = null; cargar() }) {
            androidx.compose.material3.Icon(androidx.compose.material.icons.Icons.Filled.Refresh, "Actualizar", tint = androidx.compose.ui.graphics.Color.White)
        }
    }) {
        when {
            tareas == null -> Cargando()
            tareas!!.isEmpty() -> Aviso("No tenés tareas de conteo activas. Las tareas las crea el analista de inventario desde el panel.")
            else -> tareas!!.forEach { t ->
                Tarjeta(onClick = { vm.ir(Pantalla.ConteoTarea(t.optInt("id"), t.optString("nombre"))) }) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Column(Modifier.weight(1f)) {
                            Text(t.optString("nombre"), fontWeight = FontWeight.Bold, fontSize = 17.sp)
                            Text("${nombreModo(t.txt("modo"))}${t.txt("articulo")?.let { " · $it" } ?: ""} · ${t.optString("contadas")}/${t.optString("total")} posiciones contadas",
                                color = Colores.Tenue, fontSize = 13.sp)
                        }
                        Text(if (t.optString("estado") == "abierta") "Nueva" else "En curso", color = Colores.Naranja, fontWeight = FontWeight.Bold)
                    }
                }
            }
        }
    }
}

@Composable
fun ConteoTareaScreen(vm: AppViewModel, id: Int, nombre: String) {
    var tarea by remember { mutableStateOf<JSONObject?>(null) }
    LaunchedEffect(id) { vm.lanzar(alTerminar = { r: JSONObject -> tarea = r }) { vm.api.obj("/api/conteo/tareas/$id") } }
    val detalle = tarea?.optJSONArray("detalle")?.objetos() ?: emptyList()
    val t = tarea
    val cerrada = t?.optString("estado") == "cerrada"
    Escena(nombre, vm, subtitulo = "${nombreModo(t?.txt("modo"))} · ${detalle.count { it.optBoolean("contada") }}/${detalle.size} contadas") {
        if (t == null) Cargando()
        if (cerrada) Aviso("Esta tarea está cerrada: ya no se puede contar.", TipoAviso.Aviso)
        t?.txt("articulo")?.let { a ->
            Aviso("Conteo del artículo $a: en cada posición se cuentan solo sus contenedores." +
                (t?.num("total_contado")?.let { c -> " Contado hasta ahora: ${c.lindo()}" } ?: ""))
        }
        detalle.forEach { p ->
            val contada = p.optBoolean("contada")
            Tarjeta(fondo = if (contada) Colores.OkClaro else Colores.Panel, onClick = {
                if (!cerrada) vm.ir(Pantalla.ConteoPosicion(id, p.optString("posicion"), t?.txt("modo") ?: "ciego"))
            }) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(p.optString("posicion"), fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold, fontSize = 18.sp, color = Colores.Marino, modifier = Modifier.weight(1f))
                    Text(if (contada) "✔ Contada" else "Pendiente", color = if (contada) Colores.Ok else Colores.Tenue, fontWeight = FontWeight.SemiBold)
                }
            }
        }
    }
}

/**
 * Contar una posición.
 *  - Ciego / por artículo: se ve contenedor, artículo y foto, sin la cantidad; se escribe lo contado o "No está".
 *  - Confirmación: se ve lo que dice el sistema; "✔ Coincide" o se corrige la cantidad.
 *  - Si el sistema la tiene vacía, se confirma "vacía" o se escanea lo que haya. Siempre queda contada.
 */
@Composable
fun ConteoPosicionScreen(vm: AppViewModel, tareaId: Int?, inicial: String?, modoInicial: String = "ciego") {
    var posicion by remember { mutableStateOf(inicial ?: "") }
    var datos by remember { mutableStateOf<JSONObject?>(null) }
    var cargando by remember { mutableStateOf(false) }
    val encontrados = remember { mutableStateMapOf<String, Boolean>() }
    val cantidades = remember { mutableStateMapOf<String, String>() }
    val extras = remember { mutableStateListOf<JSONObject>() }
    var nuevo by remember { mutableStateOf("") }
    var resultado by remember { mutableStateOf<JSONObject?>(null) }
    var enviando by remember { mutableStateOf(false) }
    var errorEnvio by remember { mutableStateOf<String?>(null) }

    fun cargar(p: String) {
        cargando = true; datos = null; resultado = null; errorEnvio = null; encontrados.clear(); cantidades.clear(); extras.clear()
        val q = if (tareaId != null) "?tarea=$tareaId" else "?modo=$modoInicial"
        vm.lanzar(alTerminar = { r: JSONObject -> cargando = false; datos = r; posicion = r.optString("posicion") }, alFallar = { cargando = false }) {
            vm.api.obj("/api/conteo/posicion/${Api.cod(p)}$q")
        }
    }
    LaunchedEffect(inicial) { if (!inicial.isNullOrBlank()) cargar(inicial) }
    val modo = datos?.txt("modo") ?: modoInicial
    val confirmacion = modo == "confirmacion"
    val esperados = datos?.optJSONArray("contenedores")?.objetos() ?: emptyList()
    val todos = esperados + extras
    val sinMarcar = esperados.filter { encontrados[it.optString("contenedor")] == null }.map { it.optString("contenedor") }
    val sinCantidad = todos.filter { encontrados[it.optString("contenedor")] == true && cantidades[it.optString("contenedor")]?.toDoubleOrNull() == null }.map { it.optString("contenedor") }

    fun enviar() {
        if (sinMarcar.isNotEmpty()) { vm.avisar("Falta contar o marcar \"No está\": ${sinMarcar.joinToString(", ")}", true); return }
        if (sinCantidad.isNotEmpty()) { vm.avisar("Falta la cantidad de: ${sinCantidad.joinToString(", ")}", true); return }
        enviando = true; errorEnvio = null
        val lineas = JSONArray()
        todos.forEach { c ->
            val k = c.optString("contenedor")
            val est = encontrados[k] ?: return@forEach
            val l = JSONObject().put("contenedor", k).put("encontrado", est)
            c.txt("articulo")?.let { l.put("articulo", it) }
            l.put("cantidad", if (est) cantidades[k]?.toDoubleOrNull() ?: JSONObject.NULL else JSONObject.NULL)
            lineas.put(l)
        }
        val cuerpo = JSONObject().put("posicion", posicion).put("lineas", lineas)
            .put("esperados", JSONArray(esperados.map { it.optString("contenedor") }))
        if (tareaId != null) cuerpo.put("tarea_id", tareaId)
        vm.lanzar(
            alTerminar = { r: JSONObject -> enviando = false; resultado = r; vm.vibrar(120) },
            alFallar = { m -> enviando = false; errorEnvio = m },
        ) { vm.api.post("/api/conteo/lineas", cuerpo) }
    }

    val enCurso = datos != null && resultado == null
    val vacia = esperados.isEmpty() && extras.isEmpty()
    val pie: (@Composable () -> Unit)? = if (!enCurso) null else {
        {
            Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                val faltan = sinMarcar.size + sinCantidad.size
                Text(
                    when {
                        vacia -> "El sistema la tiene vacía. Si hay algo, escanealo arriba."
                        faltan == 0 -> "Listo para enviar: ${todos.count { encontrados[it.optString("contenedor")] != null }} contenedor(es)"
                        else -> "Faltan $faltan: ${if (confirmacion) "confirmá o corregí" else "contá"} cada contenedor o marcá \"No está\""
                    },
                    color = if (faltan == 0) Colores.Ok else Colores.Aviso, fontWeight = FontWeight.SemiBold, fontSize = 13.sp,
                )
                BotonPrincipal(if (vacia) "Confirmar posición vacía" else "Enviar conteo", cargando = enviando,
                    color = if (faltan == 0) Colores.Naranja else Colores.Marino2) { enviar() }
            }
        }
    }
    Escena("Contar posición", vm, subtitulo = (if (tareaId != null) "Tarea $tareaId · " else "Suelto · ") + nombreModo(modo), pie = pie) {
        if (inicial == null) CampoEscaneo(vm, posicion, { posicion = it }, "Posición con nivel (ej. FI1502)", alEnviar = { cargar(it) })
        if (cargando) Cargando("Leyendo la posición…")
        resultado?.let { r ->
            if (r.optBoolean("revisar")) Aviso("✔ Conteo guardado (${r.optInt("lineas")} línea/s). Hay diferencias: el analista lo revisa en el panel.", TipoAviso.Aviso)
            else Aviso("✔ Conteo guardado. ${if (r.optInt("lineas") == 0) "Posición vacía confirmada." else "Todo coincide."}", TipoAviso.Ok)
            BotonPrincipal(if (inicial == null) "Contar otra posición" else "Volver") {
                if (inicial == null) { resultado = null; datos = null; posicion = "" } else vm.atras()
            }
            return@Escena
        }
        errorEnvio?.let { Aviso("✕ No se pudo enviar el conteo: $it\nNo se perdió nada: tocá \"Enviar conteo\" de nuevo.", TipoAviso.Error) }
        datos?.let { d ->
            Text("Posición ${d.optString("legible")}", fontWeight = FontWeight.Bold, fontSize = 18.sp)
            d.txt("articulo")?.let { Aviso("Conteo del artículo $it: solo se cuentan sus contenedores.") }
            Aviso(
                if (confirmacion) "Confirmación: fijate si lo que dice el sistema coincide con lo que ves. Si no, escribí la cantidad real o tocá \"No está\"."
                else "Conteo ciego: escribí la cantidad que ves en cada contenedor, o tocá \"No está\". El sistema compara después."
            )
            if (esperados.isEmpty()) Aviso("El sistema tiene esta posición vacía. Si está vacía, confirmalo abajo; si hay algo, escanealo.", TipoAviso.Aviso)
            esperados.forEach { c ->
                val k = c.optString("contenedor")
                val marca = encontrados[k]
                val sistema = c.num("cantidad")
                FichaContenedor(vm, c, ciego = true) {
                    if (confirmacion && sistema != null) Text("Sistema: ${sistema.lindo()}", fontWeight = FontWeight.Bold, color = Colores.Marino, fontSize = 16.sp)
                    if (marca == false) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Text("✕ No está", color = Colores.Error, fontWeight = FontWeight.Bold, modifier = Modifier.weight(1f))
                            BotonChico("Deshacer") { encontrados.remove(k) }
                        }
                    } else {
                        if (confirmacion && sistema != null) {
                            val coincide = marca == true && cantidades[k]?.toDoubleOrNull() == sistema
                            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                Opcion(if (coincide) "✔ Coincide" else "Coincide", coincide, Colores.Ok, Modifier.weight(1f)) { encontrados[k] = true; cantidades[k] = sistema.lindo() }
                                Opcion("No está", false, Colores.Error, Modifier.weight(1f)) { encontrados[k] = false; cantidades.remove(k) }
                            }
                            if (!coincide) CampoNumero(cantidades[k] ?: "", { v -> cantidades[k] = v; if (v.isNotBlank()) encontrados[k] = true else encontrados.remove(k) },
                                "Cantidad real (si no coincide)", Modifier.fillMaxWidth(), decimales = true)
                        } else {
                            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                CampoNumero(cantidades[k] ?: "", { v -> cantidades[k] = v; if (v.isNotBlank()) encontrados[k] = true else encontrados.remove(k) },
                                    "Cantidad contada", Modifier.weight(1f), decimales = true)
                                Opcion("No está", false, Colores.Error, Modifier.width(96.dp)) { encontrados[k] = false; cantidades.remove(k) }
                            }
                            if (marca == true && cantidades[k]?.toDoubleOrNull() != null) Text("✔ Contado", color = Colores.Ok, fontWeight = FontWeight.SemiBold, fontSize = 13.sp)
                        }
                    }
                }
            }
            Text("¿Encontraste algo que no está en la lista?", fontWeight = FontWeight.Bold)
            CampoEscaneo(vm, nuevo, { nuevo = it }, "Escaneá el contenedor encontrado", alEnviar = { c ->
                if (esperados.any { it.optString("contenedor") == c }) { vm.avisar("$c ya está en la lista"); nuevo = ""; return@CampoEscaneo }
                if (extras.any { it.optString("contenedor") == c }) { nuevo = ""; return@CampoEscaneo }
                vm.lanzar(alTerminar = { r: JSONObject -> extras.add(if (r.optBoolean("encontrado")) r else JSONObject().put("contenedor", c)); encontrados[c] = true; nuevo = "" }) {
                    vm.api.obj("/api/contenedor/${Api.cod(c)}")
                }
            })
            extras.forEach { c ->
                val k = c.optString("contenedor")
                FichaContenedor(vm, c, ciego = true) {
                    Text("No estaba en la lista", color = Colores.Aviso, fontWeight = FontWeight.SemiBold)
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        CampoNumero(cantidades[k] ?: "", { cantidades[k] = it }, "Cantidad contada", Modifier.weight(1f), decimales = true)
                        BotonChico("Quitar") { extras.remove(c); encontrados.remove(k); cantidades.remove(k) }
                    }
                }
            }
        }
    }
}
