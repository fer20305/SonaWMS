package ar.com.sona.deposito.ui

import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.width
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.FileProvider
import ar.com.sona.deposito.data.Api
import ar.com.sona.deposito.data.ItemCola
import ar.com.sona.deposito.data.lindo
import ar.com.sona.deposito.data.num
import ar.com.sona.deposito.data.objetos
import ar.com.sona.deposito.data.txt
import org.json.JSONArray
import org.json.JSONObject
import java.io.File

/** Motivos de ajuste del WMS (Chequeos y ajustes › 5). */
val MOTIVOS_AJUSTE = listOf("INVENTARIO", "ERROR_INGRESO", "CON_FALTANTES_AJU", "ROTURA_AJU", "SCRAP_AJU", "SALIDA", "MUDANZA", "MIGRACION", "PROMO_DISC_AJU")

// ======================================================================= eliminar contenedor (solo admin)

/** Eliminar un contenedor = ajustarlo a 0 en el WMS (resta todo lo que tiene). Solo administradores, con motivo y doble confirmación. */
@Composable
fun EliminarContenedorScreen(vm: AppViewModel, inicial: String?) {
    var codigo by remember { mutableStateOf(inicial ?: "") }
    var info by remember { mutableStateOf<JSONObject?>(null) }
    var motivo by remember { mutableStateOf<String?>(null) }
    var comentario by remember { mutableStateOf("") }
    var confirmar by remember { mutableStateOf(false) }
    fun buscar(c: String) {
        info = null; motivo = null
        vm.lanzar(alTerminar = { r: JSONObject -> if (r.optBoolean("encontrado")) info = r else vm.avisar("No se encontró $c", true) }) { vm.api.obj("/api/contenedor/${Api.cod(c)}") }
    }
    LaunchedEffect(inicial) { if (!inicial.isNullOrBlank()) buscar(inicial) }
    Escena("Eliminar contenedor", vm, subtitulo = "Ajuste a 0 en el WMS · solo administradores") {
        if (!vm.puede("eliminar")) { Aviso("Tu usuario no tiene permiso para eliminar contenedores (ZUSR).", TipoAviso.Error); return@Escena }
        CampoEscaneo(vm, codigo, { codigo = it }, "Contenedor", alEnviar = { buscar(it) })
        info?.let { c ->
            FichaContenedor(vm, c)
            Aviso("Se resta todo lo que tiene (${c.num("cantidad")?.lindo() ?: "?"} unidades) y queda en 0 en el WMS. No se puede deshacer desde la app. " +
                "El WMS no deja ajustar el mismo contenedor dos veces en menos de 5 minutos.", TipoAviso.Aviso)
            Text("Motivo del ajuste", fontWeight = FontWeight.Bold)
            MOTIVOS_AJUSTE.chunked(2).forEach { fila ->
                Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    fila.forEach { m -> Opcion(m.replace('_', ' ').lowercase().replaceFirstChar { it.uppercase() }, motivo == m, Colores.Marino, Modifier.weight(1f)) { motivo = m } }
                    if (fila.size == 1) Spacer(Modifier.weight(1f))
                }
            }
            OutlinedTextField(comentario, { comentario = it }, label = { Text("Comentario (opcional)") }, modifier = Modifier.fillMaxWidth())
            BotonPrincipal("Eliminar contenedor", habilitado = motivo != null, color = Colores.Error) { confirmar = true }
            if (confirmar) AlertDialog(
                onDismissRequest = { confirmar = false },
                title = { Text("¿Eliminar ${c.optString("contenedor")}?") },
                text = { Text("Se ajusta a 0 en el WMS (${c.num("cantidad")?.lindo() ?: "?"} unidades de ${c.txt("articulo") ?: "?"}), motivo $motivo.") },
                confirmButton = {
                    TextButton(onClick = {
                        confirmar = false
                        val item = ItemCola.nuevo("ajuste", c.optString("contenedor"), c.txt("descripcion") ?: "", motivo = motivo, comentario = comentario.ifBlank { null })
                        vm.encolar(listOf(item))
                        vm.avisar("✔ Eliminación de ${c.optString("contenedor")} registrada. Se carga en el WMS.")
                        info = null; codigo = ""; motivo = null; comentario = ""
                    }) { Text("Sí, eliminar", color = Colores.Error, fontWeight = FontWeight.Bold) }
                },
                dismissButton = { TextButton(onClick = { confirmar = false }) { Text("Cancelar") } },
            )
        }
    }
}

// ======================================================================= reconocer artículo por foto (solo admin)

/** Una opción sugerida por la IA, con botón explícito "Es este". */
@Composable
private fun CandidatoIa(vm: AppViewModel, c: JSONObject, destacado: Boolean, elegir: () -> Unit) {
    val pct = ((c.num("similitud") ?: 0.0) * 100).toInt()
    Tarjeta(borde = if (destacado) Colores.Naranja else Colores.Linea) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            FotoRemota(vm, c.txt("foto"), 72.dp)
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f)) {
                Text(c.optString("articulo"), fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold, color = Colores.Marino, fontSize = 17.sp)
                Text(c.txt("descripcion") ?: "", fontSize = 13.sp)
                Text(c.txt("empresa") ?: "", color = Colores.Tenue, fontSize = 12.sp)
            }
            Text("$pct%", fontWeight = FontWeight.Black, fontSize = 18.sp,
                color = if (pct >= 75) Colores.Ok else if (pct >= 55) Colores.Aviso else Colores.Tenue)
        }
        BotonSecundario("✓ Es este") { elegir() }
    }
}

/**
 * Sacás una foto del producto y el servidor (IA propia, sin costo por consulta) muestra los artículos más parecidos.
 * "Es este" le enseña a la IA; "Ninguno es" la deja en "Fotos sin identificar" para asignarla cuando se sepa.
 */
@Composable
fun ReconocerScreen(vm: AppViewModel) {
    val ctx = LocalContext.current
    var resultado by remember { mutableStateOf<JSONObject?>(null) }
    var buscando by remember { mutableStateOf(false) }
    var manual by remember { mutableStateOf("") }
    var respuesta by remember { mutableStateOf<String?>(null) }      // artículo confirmado o "" = ninguno
    var sinIdentificar by remember { mutableStateOf<Int?>(null) }
    fun contarPendientes() { vm.lanzar(alTerminar = { a: JSONArray -> sinIdentificar = a.length() }) { vm.api.lista("/api/ia/historial?estado=pendientes") } }
    LaunchedEffect(Unit) { contarPendientes() }
    fun confirmar(articulo: String) {
        val id = resultado?.txt("fotoId") ?: return
        vm.lanzar(alTerminar = { _: JSONObject ->
            respuesta = articulo.uppercase(); manual = ""
            vm.avisar("✔ Aprendido: esta foto es ${articulo.uppercase()}")
        }) { vm.api.post("/api/ia/confirmar", JSONObject().put("fotoId", id).put("articulo", articulo)) }
    }
    fun ninguno() {
        val id = resultado?.txt("fotoId") ?: return
        vm.lanzar(alTerminar = { _: JSONObject -> respuesta = ""; contarPendientes() }) { vm.api.post("/api/ia/ninguno", JSONObject().put("fotoId", id)) }
    }
    fun procesar(f: File) {
        achicar(f)
        buscando = true; resultado = null; respuesta = null; manual = ""
        vm.lanzar(alTerminar = { r: JSONObject -> buscando = false; resultado = r; f.delete(); contarPendientes() }, alFallar = { buscando = false; f.delete() }) {
            vm.api.subirFotoA("/api/ia/reconocer", f)
        }
    }
    /** Toma la foto pendiente (guardada en Ajustes: sobrevive a que Android cierre la app con la cámara abierta). */
    fun tomarPendiente(ok: Boolean) {
        val ruta = vm.ajustes.fotoIaPendiente
        if (ruta.isBlank()) return
        vm.ajustes.fotoIaPendiente = ""
        val f = File(ruta)
        if (ok && f.exists() && f.length() > 0) procesar(f) else f.delete()
    }
    val camara = rememberLauncherForActivityResult(ActivityResultContracts.TakePicture()) { ok -> tomarPendiente(ok) }
    // si la app se reabrió después de la cámara (Android la cerró por memoria), la foto quedó guardada: se procesa al volver
    LaunchedEffect(Unit) {
        kotlinx.coroutines.delay(700)
        if (vm.ajustes.fotoIaPendiente.isNotBlank()) {
            val f = File(vm.ajustes.fotoIaPendiente)
            val hay = f.exists() && f.length() > 0
            tomarPendiente(hay)
            if (hay) vm.avisar("Se retomó la foto que sacaste")
        }
    }
    fun sacarFoto() {
        val dir = File(ctx.filesDir, "fotos").apply { mkdirs() }
        val f = File(dir, "ia_${System.currentTimeMillis()}.jpg")
        vm.ajustes.fotoIaPendiente = f.absolutePath
        val uri: Uri = FileProvider.getUriForFile(ctx, ctx.packageName + ".archivos", f)
        try { camara.launch(uri) } catch (e: Exception) { vm.ajustes.fotoIaPendiente = ""; vm.avisar("No se pudo abrir la cámara: ${e.message}", true) }
    }
    Escena("Reconocer por foto", vm, subtitulo = "IA propia · solo administradores") {
        if (!vm.puede("reconocer_foto")) { Aviso("Tu usuario no tiene permiso para reconocer por foto (ZUSR).", TipoAviso.Error); return@Escena }
        sinIdentificar?.takeIf { it > 0 }?.let { n -> BotonSecundario("🗂 Fotos sin identificar ($n)") { vm.ir(Pantalla.IaPendientes) } }
        if (resultado == null) Aviso("Sacá la foto del producto de frente, bien iluminado y que ocupe casi toda la imagen. " +
            "Después tocá \"Es este\" en el correcto: así la IA aprende y la próxima vez lo reconoce mejor.")
        BotonPrincipal(if (resultado == null) "📷 Sacar foto" else "📷 Sacar otra foto", cargando = buscando) { sacarFoto() }
        if (buscando) Cargando("Buscando artículos parecidos… (la primera vez puede tardar más)")
        resultado?.let { r ->
            when (val resp = respuesta) {
                null -> {
                    r.txt("mensaje")?.let { Aviso(it, TipoAviso.Aviso) }
                    val cands = r.optJSONArray("candidatos")?.objetos() ?: emptyList()
                    if (cands.isNotEmpty()) Text("¿Cuál es? Tocá \"Es este\" en el correcto", fontWeight = FontWeight.Bold)
                    cands.forEachIndexed { i, c -> CandidatoIa(vm, c, i == 0) { confirmar(c.optString("articulo")) } }
                    Text("¿Sabés cuál es y no está en la lista? Escribilo o escanealo (también aprende):", color = Colores.Tenue, fontSize = 13.sp)
                    CampoEscaneo(vm, manual, { manual = it }, "Código de artículo", alEnviar = { confirmar(it) })
                    BotonSecundario("✗ Ninguno es — lo identifico después") { ninguno() }
                }
                "" -> {
                    Aviso("Quedó guardada en \"Fotos sin identificar\". Cuando sepas qué artículo es, asignalo desde ahí o desde el panel (ZIA) y la IA aprende igual.", TipoAviso.Info)
                    BotonSecundario("🗂 Ver fotos sin identificar") { vm.ir(Pantalla.IaPendientes) }
                }
                else -> {
                    Aviso("✔ Guardado: la IA aprendió que esta foto es $resp. Cuantas más fotos confirmadas, mejor reconoce.", TipoAviso.Ok)
                    BotonSecundario("Ver el artículo $resp") { vm.ir(Pantalla.Consultar(resp)) }
                    TextButton(onClick = { respuesta = null }) { Text("Me equivoqué, corregir") }
                }
            }
        }
    }
}

/** Fotos que la IA no pudo identificar (o que se marcaron "ninguno es"): se asigna el artículo cuando se sabe. */
@Composable
fun IaPendientesScreen(vm: AppViewModel) {
    var lista by remember { mutableStateOf<List<JSONObject>?>(null) }
    var abierta by remember { mutableStateOf<String?>(null) }
    var codigo by remember { mutableStateOf("") }
    fun cargar() { vm.lanzar(alTerminar = { a: JSONArray -> lista = a.objetos() }, alFallar = { lista = emptyList() }) { vm.api.lista("/api/ia/historial?estado=pendientes") } }
    LaunchedEffect(Unit) { cargar() }
    fun resolver(ruta: String, foto: String, articulo: String?) {
        val cuerpo = JSONObject().put("fotoId", foto)
        if (articulo != null) cuerpo.put("articulo", articulo)
        vm.lanzar(alTerminar = { _: JSONObject ->
            vm.avisar(if (articulo != null) "✔ Aprendido: era ${articulo.uppercase()}" else "Foto descartada")
            abierta = null; codigo = ""; cargar()
        }) { vm.api.post(ruta, cuerpo) }
    }
    Escena("Fotos sin identificar", vm, subtitulo = "Asigná el artículo y la IA aprende") {
        val l = lista
        when {
            l == null -> Cargando()
            l.isEmpty() -> Aviso("No hay fotos pendientes de identificar.", TipoAviso.Ok)
            else -> l.forEach { f ->
                val foto = f.optString("foto")
                val abiertaEsta = abierta == foto
                Tarjeta(borde = if (abiertaEsta) Colores.Naranja else Colores.Linea, onClick = { abierta = if (abiertaEsta) null else foto; codigo = "" }) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        FotoRemota(vm, f.txt("url"), 84.dp)
                        Spacer(Modifier.width(12.dp))
                        Column(Modifier.weight(1f)) {
                            Text(f.optString("creado").take(16).replace('T', ' ') + " · " + f.optString("usuario"), color = Colores.Tenue, fontSize = 12.sp)
                            f.txt("sugerido")?.let { s -> Text("La IA sugirió: $s (${((f.num("similitud") ?: 0.0) * 100).toInt()}%)", fontSize = 13.sp) }
                            Text(if (f.optString("estado") == "ninguno") "Ninguno de los sugeridos" else "Sin respuesta", color = Colores.Aviso, fontSize = 12.sp, fontWeight = FontWeight.SemiBold)
                        }
                    }
                    if (abiertaEsta) {
                        val cands = f.optJSONArray("candidatos")?.objetos() ?: emptyList()
                        if (cands.isNotEmpty()) Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            cands.take(5).forEach { c -> BotonChico(c.optString("articulo")) { codigo = c.optString("articulo") } }
                        }
                        CampoEscaneo(vm, codigo, { codigo = it }, "Artículo correcto", alEnviar = { resolver("/api/ia/confirmar", foto, it) })
                        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                            BotonPrincipal("Asignar", Modifier.weight(1f), habilitado = codigo.isNotBlank()) { resolver("/api/ia/confirmar", foto, codigo.trim()) }
                            BotonPrincipal("Descartar", Modifier.weight(1f), color = Colores.Tenue) { resolver("/api/ia/descartar", foto, null) }
                        }
                    }
                }
            }
        }
    }
}

/** Botón para sacar una foto y agregarla al catálogo de SONA para ese artículo (por su código). */
@Composable
fun AgregarFotoCatalogo(vm: AppViewModel, articulo: String, alSubir: () -> Unit = {}) {
    if (vm.rol !in listOf("admin", "analista", "picker")) return
    val ctx = LocalContext.current
    var pendiente by remember { mutableStateOf<File?>(null) }
    var subiendo by remember { mutableStateOf(false) }
    val camara = rememberLauncherForActivityResult(ActivityResultContracts.TakePicture()) { ok ->
        val f = pendiente
        pendiente = null
        if (!ok || f == null || !f.exists()) { f?.delete(); return@rememberLauncherForActivityResult }
        achicar(f)
        subiendo = true
        vm.lanzar(alTerminar = { _: JSONObject -> subiendo = false; f.delete(); vm.avisar("✔ Foto agregada al catálogo de $articulo"); alSubir() },
            alFallar = { subiendo = false; f.delete() }) { vm.api.subirFotoA("/api/catalogo/${Api.cod(articulo)}/fotos", f) }
    }
    BotonSecundario(if (subiendo) "Subiendo foto…" else "📷 Agregar foto al catálogo", habilitado = !subiendo) {
        val dir = File(ctx.filesDir, "fotos").apply { mkdirs() }
        val f = File(dir, "cat_${System.currentTimeMillis()}.jpg")
        pendiente = f
        camara.launch(FileProvider.getUriForFile(ctx, ctx.packageName + ".archivos", f))
    }
}
