package ar.com.sona.deposito.ui

import android.app.Activity
import android.content.Intent
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.AlertDialog
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.platform.LocalContext
import ar.com.sona.deposito.ar.MedirActivity
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

/**
 * Control de bulto: se busca la solicitud en LES, se cargan los bultos nuevos con sus medidas y "Generar etiquetas" los guarda
 * en LES (como bultos.bultos-ejemplo.com.ar). Si la solicitud no está controlada, no deja.
 */
@Composable
fun ControlBultoScreen(vm: AppViewModel) {
    var nro by remember { mutableStateOf("") }
    var pedido by remember { mutableStateOf<JSONObject?>(null) }
    var origenes by remember { mutableStateOf<List<JSONObject>>(emptyList()) }
    var origen by remember { mutableStateOf("PUESTO1") }
    var cargando by remember { mutableStateOf(false) }
    val nuevos = remember { mutableStateListOf<Triple<Double, Double, Double>>() }
    // cambios sobre los bultos ya cargados en LES: medidas nuevas por número de bulto, y los que se borran
    val cambios = remember { mutableStateMapOf<Long, Triple<Double, Double, Double>>() }
    val borrar = remember { mutableStateListOf<Long>() }
    var editando by remember { mutableStateOf<JSONObject?>(null) }
    // impresión de las etiquetas en la impresora del puesto (se configura en el panel: ZIMP)
    var impresion by remember { mutableStateOf<JSONObject?>(null) }
    var imprimir by remember { mutableStateOf(true) }
    androidx.compose.runtime.LaunchedEffect(Unit) {
        try { val c = vm.api.obj("/api/bultos/impresion"); impresion = c; imprimir = c.optBoolean("imprimirAlGuardar", true) } catch (_: Exception) {}
    }
    fun impresoraDe(o: String) = impresion?.optJSONArray("origenes")?.objetos()?.firstOrNull { it.optString("codigo") == o }?.txt("impresora")
    fun avisarImpresion(im: JSONObject?) {
        if (im == null) return
        if (im.optBoolean("ok")) { if (im.optInt("cantidad") > 0) vm.avisar("🖨 ${im.optInt("cantidad")} etiqueta(s) enviada(s) a ${im.optString("impresora")}") }
        else vm.avisar("No se pudo imprimir en ${im.optString("impresora")}: ${im.optString("error")}", true)
    }
    fun reimprimir(nroSol: String, bultos: List<Long>) {
        vm.lanzar(alTerminar = { r: JSONObject -> avisarImpresion(r) }) {
            vm.api.post("/api/bultos/${Api.cod(nroSol)}/imprimir", JSONObject().put("origen", origen).put("bultos", JSONArray(bultos)))
        }
    }
    var eAlto by remember { mutableStateOf("") }
    var eAncho by remember { mutableStateOf("") }
    var eProf by remember { mutableStateOf("") }
    var alto by remember { mutableStateOf("") }
    var ancho by remember { mutableStateOf("") }
    var prof by remember { mutableStateOf("") }
    var confirmar by remember { mutableStateOf(false) }
    val fAlto = remember { FocusRequester() }
    val fAncho = remember { FocusRequester() }
    val fProf = remember { FocusRequester() }
    val ctx = LocalContext.current
    // medir con realidad aumentada (opcional): solo si el celular soporta ARCore
    val conAr = remember { MedirActivity.disponible(ctx) }
    val medirAr = rememberLauncherForActivityResult(ActivityResultContracts.StartActivityForResult()) { r ->
        val d = r.data
        if (r.resultCode == Activity.RESULT_OK && d != null) {
            fun cm(k: String) = Math.round(d.getDoubleExtra(k, 0.0)).toString()
            alto = cm(MedirActivity.ALTO); ancho = cm(MedirActivity.ANCHO); prof = cm(MedirActivity.PROF)
            vm.avisar("Medidas de la cámara cargadas: revisalas y tocá Agregar bulto")
        } else d?.getStringExtra("error")?.let { vm.avisar(it, true) }
    }

    fun buscar(n: String) {
        if (n.isBlank()) return
        cargando = true; pedido = null; nuevos.clear(); cambios.clear(); borrar.clear()
        vm.lanzar(alTerminar = { r: JSONObject ->
            cargando = false; pedido = r.optJSONObject("pedido"); origenes = r.optJSONArray("origenes")?.objetos() ?: emptyList()
            pedido?.optJSONArray("bultos")?.objetos()?.firstOrNull()?.txt("origen")?.let { origen = it }
        }, alFallar = { cargando = false }) { vm.api.obj("/api/bultos/${Api.cod(n)}") }
    }
    fun agregar() {
        val a = alto.toDoubleOrNull(); val b = ancho.toDoubleOrNull(); val p = prof.toDoubleOrNull()
        if (a == null || b == null || p == null || listOf(a, b, p).any { it <= 0 || it > 1000 }) { vm.avisar("Medidas en cm, mayores a 0 y hasta 1000", true); return }
        nuevos.add(Triple(a, b, p)); alto = ""; ancho = ""; prof = ""
        vm.vibrar(50)
        vm.avisar("✔ Bulto ${nuevos.size} agregado: ${a.lindo()} × ${b.lindo()} × ${p.lindo()} cm")
        try { fAlto.requestFocus() } catch (_: Exception) {}
    }
    fun guardar() {
        val ped = pedido ?: return
        cargando = true
        val cuerpo = JSONObject().put("origen", origen)
            .put("nuevos", JSONArray(nuevos.map { (a, b, p) -> JSONObject().put("alto", a).put("ancho", b).put("profundidad", p) }))
            .put("cambios", JSONArray(cambios.map { (n, m) -> JSONObject().put("nroContenedor", n).put("alto", m.first).put("ancho", m.second).put("profundidad", m.third) }))
            .put("quitar", JSONArray(borrar.toList())).put("imprimir", imprimir)
        vm.lanzar(alTerminar = { r: JSONObject ->
            cargando = false; pedido = r.optJSONObject("pedido"); nuevos.clear(); cambios.clear(); borrar.clear()
            val partes = listOfNotNull(r.optInt("nuevos").takeIf { it > 0 }?.let { "$it nuevo(s)" }, r.optInt("modificados").takeIf { it > 0 }?.let { "$it modificado(s)" },
                r.optInt("borrados").takeIf { it > 0 }?.let { "$it borrado(s)" })
            vm.avisar("✔ Guardado en LES: ${partes.joinToString(", ")}.")
            avisarImpresion(r.optJSONObject("impresion"))
        }, alFallar = { cargando = false }) { vm.api.post("/api/bultos/${Api.cod(ped.optString("nroSolicitud"))}", cuerpo) }
    }

    Escena("Control de bulto", vm, subtitulo = "Medidas de los bultos de la solicitud") {
        CampoEscaneo(vm, nro, { nro = it }, "N° de solicitud / pedido", alEnviar = { nro = it; buscar(it) })
        if (cargando) Cargando("Consultando LES…")
        pedido?.let { p ->
            Tarjeta {
                Text("Solicitud ${p.optString("nroSolicitud")}", fontWeight = FontWeight.Bold, fontSize = 18.sp, color = Colores.Marino)
                Text(listOfNotNull(p.txt("cliente"), p.txt("proyecto")).joinToString(" · "), fontSize = 13.sp)
                if (p.optBoolean("controlado")) Aviso("✔ Controlada" + (p.txt("usuarioControl")?.let { " por $it" } ?: ""), TipoAviso.Ok)
                else Aviso("El pedido debe ser controlado. No es posible generar etiquetas de medida hasta que esté controlado.", TipoAviso.Error)
            }
            val bultos = p.optJSONArray("bultos")?.objetos() ?: emptyList()
            Text("Bultos (${bultos.size + nuevos.size})", fontWeight = FontWeight.Bold)
            val controlado = p.optBoolean("controlado")
            bultos.forEach { b ->
                val nroB = b.optLong("nroContenedor")
                val cambio = cambios[nroB]
                val borrado = nroB in borrar
                val (ma, mb, mc) = cambio ?: Triple(b.num("alto") ?: 0.0, b.num("ancho") ?: 0.0, b.num("profundidad") ?: 0.0)
                Tarjeta(borde = when { borrado -> Colores.Error; cambio != null -> Colores.Naranja; else -> Colores.Linea }) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Column(Modifier.weight(1f)) {
                            Text(b.optString("nroContenedor"), fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold,
                                textDecoration = if (borrado) TextDecoration.LineThrough else null)
                            Text("${ma.lindo()} × ${mb.lindo()} × ${mc.lindo()} cm", color = if (cambio != null) Colores.Naranja else Colores.Tenue, fontSize = 13.sp,
                                fontWeight = if (cambio != null) FontWeight.SemiBold else FontWeight.Normal, textDecoration = if (borrado) TextDecoration.LineThrough else null)
                            if (cambio != null) Text("antes: ${b.num("alto")?.lindo()} × ${b.num("ancho")?.lindo()} × ${b.num("profundidad")?.lindo()} cm", color = Colores.Tenue, fontSize = 11.sp)
                            if (borrado) Text("Se borra al guardar", color = Colores.Error, fontSize = 12.sp, fontWeight = FontWeight.SemiBold)
                        }
                        Text("%.3f m³".format(ma * mb * mc / 1_000_000), fontWeight = FontWeight.SemiBold)
                    }
                    if (controlado) Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        if (borrado) TextButton(onClick = { borrar.remove(nroB) }) { Text("Deshacer borrado") }
                        else {
                            TextButton(onClick = { editando = b; eAlto = ma.lindo(); eAncho = mb.lindo(); eProf = mc.lindo() }) { Text("✎ Modificar medidas") }
                            if (cambio != null) TextButton(onClick = { cambios.remove(nroB) }) { Text("Deshacer") }
                            TextButton(onClick = { cambios.remove(nroB); borrar.add(nroB) }) { Text("🗑 Borrar", color = Colores.Error) }
                            if (cambio == null) TextButton(onClick = { reimprimir(p.optString("nroSolicitud"), listOf(nroB)) }) { Text("🖨") }
                        }
                    }
                }
            }
            nuevos.forEachIndexed { i, (a, b, c) ->
                Tarjeta(borde = Colores.Naranja) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Column(Modifier.weight(1f)) {
                            Text("Nuevo bulto ${i + 1}", fontWeight = FontWeight.Bold, color = Colores.Naranja)
                            Text("${a.lindo()} × ${b.lindo()} × ${c.lindo()} cm · %.3f m³".format(a * b * c / 1_000_000), fontSize = 13.sp)
                        }
                        TextButton(onClick = { nuevos.removeAt(i) }) { Text("Quitar", color = Colores.Error) }
                    }
                }
            }
            if (p.optBoolean("controlado")) {
                Text("Nuevo bulto (cm)", fontWeight = FontWeight.Bold)
                // cada medida tiene su ✓ para confirmar (pasa a la siguiente); el ✓ de la profundidad agrega el bulto
                CampoNumero(alto, { alto = it }, "Alto (cm)", Modifier.fillMaxWidth(), fAlto, decimales = true) { try { fAncho.requestFocus() } catch (_: Exception) {} }
                CampoNumero(ancho, { ancho = it }, "Ancho (cm)", Modifier.fillMaxWidth(), fAncho, decimales = true) { try { fProf.requestFocus() } catch (_: Exception) {} }
                CampoNumero(prof, { prof = it }, "Profundidad (cm) · ✓ agrega el bulto", Modifier.fillMaxWidth(), fProf, decimales = true) {
                    if (alto.isNotBlank() && ancho.isNotBlank() && prof.isNotBlank()) agregar() else vm.avisar("Completá alto, ancho y profundidad", true)
                }
                if (conAr) BotonSecundario("📐 Medir con la cámara (realidad aumentada)", Modifier.fillMaxWidth()) { medirAr.launch(Intent(ctx, MedirActivity::class.java)) }
                BotonPrincipal("✓ Confirmar medida y agregar bulto", Modifier.fillMaxWidth(), habilitado = alto.isNotBlank() && ancho.isNotBlank() && prof.isNotBlank(), color = Colores.Marino) { agregar() }
                Text("Origen (puesto)", fontWeight = FontWeight.Bold)
                (origenes.ifEmpty { listOf(JSONObject().put("codigo", "PUESTO1").put("nombre", "PUESTO 1: CD Sona")) }).chunked(2).forEach { fila ->
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        fila.forEach { o -> Opcion(o.optString("nombre"), origen == o.optString("codigo"), Colores.Marino, Modifier.weight(1f)) { origen = o.optString("codigo") } }
                    }
                }
                Row(verticalAlignment = Alignment.CenterVertically) {
                    androidx.compose.material3.Checkbox(checked = imprimir, onCheckedChange = { imprimir = it })
                    Column(Modifier.weight(1f)) {
                        Text("Imprimir las etiquetas al guardar", fontWeight = FontWeight.SemiBold)
                        Text("🖨 " + (impresoraDe(origen) ?: "impresora predeterminada del servidor"), color = Colores.Tenue, fontSize = 12.sp)
                    }
                }
                if (bultos.isNotEmpty() && nuevos.isEmpty() && cambios.isEmpty() && borrar.isEmpty())
                    BotonSecundario("🖨 Reimprimir todas las etiquetas (${bultos.size})", Modifier.fillMaxWidth()) { reimprimir(p.optString("nroSolicitud"), bultos.map { it.optLong("nroContenedor") }) }
                val hayCambios = nuevos.isNotEmpty() || cambios.isNotEmpty() || borrar.isNotEmpty()
                val resumen = listOfNotNull(nuevos.size.takeIf { it > 0 }?.let { "$it nuevo/s" }, cambios.size.takeIf { it > 0 }?.let { "$it modificado/s" },
                    borrar.size.takeIf { it > 0 }?.let { "$it a borrar" })
                BotonPrincipal(if (hayCambios) "Guardar y generar etiquetas (${resumen.joinToString(", ")})" else "Generar etiquetas", habilitado = !cargando && hayCambios) { confirmar = true }
            }
        }
    }
    if (confirmar) AlertDialog(
        onDismissRequest = { confirmar = false },
        title = { Text("¿Guardar y generar etiquetas?") },
        text = {
            Text("Solicitud ${pedido?.optString("nroSolicitud")}, origen $origen:\n" + listOfNotNull(
                nuevos.size.takeIf { it > 0 }?.let { "• $it bulto(s) nuevo(s)" }, cambios.size.takeIf { it > 0 }?.let { "• $it bulto(s) con medidas modificadas" },
                borrar.size.takeIf { it > 0 }?.let { "• $it bulto(s) borrado(s): ${borrar.joinToString(", ")}" },
                if (imprimir) "🖨 Se imprimen en ${impresoraDe(origen) ?: "la impresora predeterminada"}" else "Sin imprimir").joinToString("\n"))
        },
        confirmButton = { TextButton(onClick = { confirmar = false; guardar() }) { Text("Confirmar", fontWeight = FontWeight.Bold) } },
        dismissButton = { TextButton(onClick = { confirmar = false }) { Text("Cancelar") } },
    )
    editando?.let { b ->
        val nroB = b.optLong("nroContenedor")
        AlertDialog(
            onDismissRequest = { editando = null },
            title = { Text("Medidas del bulto $nroB") },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text("Antes: ${b.num("alto")?.lindo()} × ${b.num("ancho")?.lindo()} × ${b.num("profundidad")?.lindo()} cm", color = Colores.Tenue, fontSize = 13.sp)
                    CampoNumero(eAlto, { eAlto = it }, "Alto (cm)", Modifier.fillMaxWidth(), decimales = true, conBoton = false)
                    CampoNumero(eAncho, { eAncho = it }, "Ancho (cm)", Modifier.fillMaxWidth(), decimales = true, conBoton = false)
                    CampoNumero(eProf, { eProf = it }, "Profundidad (cm)", Modifier.fillMaxWidth(), decimales = true, conBoton = false)
                }
            },
            confirmButton = {
                TextButton(onClick = {
                    val a = eAlto.replace(',', '.').toDoubleOrNull(); val c = eAncho.replace(',', '.').toDoubleOrNull(); val d = eProf.replace(',', '.').toDoubleOrNull()
                    if (a == null || c == null || d == null || listOf(a, c, d).any { it <= 0 || it > 1000 }) vm.avisar("Medidas en cm, mayores a 0 y hasta 1000", true)
                    else {
                        val igual = a == b.num("alto") && c == b.num("ancho") && d == b.num("profundidad")
                        if (igual) cambios.remove(nroB) else cambios[nroB] = Triple(a, c, d)
                        editando = null
                        if (!igual) vm.avisar("Medidas del bulto $nroB cambiadas: tocá Guardar para mandarlas a LES")
                    }
                }) { Text("Aceptar", fontWeight = FontWeight.Bold) }
            },
            dismissButton = { TextButton(onClick = { editando = null }) { Text("Cancelar") } },
        )
    }
}
