package ar.com.sona.deposito.ui

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Image
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
import androidx.compose.material.icons.filled.Close
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.FileProvider
import ar.com.sona.deposito.data.Api
import ar.com.sona.deposito.data.ItemCola
import ar.com.sona.deposito.data.lindo
import ar.com.sona.deposito.data.objetos
import ar.com.sona.deposito.data.txt
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.FileOutputStream
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter

// ======================================================================= guardado

/** Contenedores recibidos pendientes de guardar: el sistema da la ubicación por empresa (la del WMS es solo sugerencia). */
@Composable
fun GuardarScreen(vm: AppViewModel) {
    var pendientes by remember { mutableStateOf<List<JSONObject>?>(null) }
    var elegido by remember { mutableStateOf<JSONObject?>(null) }
    var codigo by remember { mutableStateOf("") }
    fun cargar() {
        pendientes = null
        vm.lanzar(alTerminar = { a: JSONArray -> pendientes = a.objetos() }, alFallar = { pendientes = emptyList() }) { vm.api.lista("/api/guardado/pendientes") }
    }
    LaunchedEffect(Unit) { cargar() }
    val e = elegido
    Escena("Guardar", vm, subtitulo = if (e == null) "Recepciones pendientes" else "Ubicación para ${e.optString("contenedor")}") {
        if (e == null) {
            CampoEscaneo(vm, codigo, { codigo = it }, "Escaneá el contenedor recibido", alEnviar = { c ->
                val p = pendientes?.firstOrNull { it.optString("contenedor") == c }
                if (p != null) elegido = p
                else vm.lanzar(alTerminar = { r: JSONObject -> if (r.optBoolean("encontrado")) elegido = r else vm.avisar("No se encontró $c", true) }) { vm.api.obj("/api/contenedor/${Api.cod(c)}") }
            })
            when {
                pendientes == null -> Cargando("Leyendo pendientes de guardado en el WMS…")
                pendientes!!.isEmpty() -> Aviso("No hay contenedores pendientes de guardar.", TipoAviso.Ok)
                else -> {
                    Text("${pendientes!!.count { it.has("contenedor") }} pendientes", fontWeight = FontWeight.Bold)
                    pendientes!!.forEach { p ->
                        if (p.has("error")) Aviso(p.optString("error"), TipoAviso.Error)
                        else Tarjeta(onClick = { elegido = p }) {
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                FotoRemota(vm, p.txt("foto"), 56.dp)
                                Spacer(Modifier.width(10.dp))
                                Column(Modifier.weight(1f)) {
                                    Text(p.optString("contenedor"), fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold, color = Colores.Marino)
                                    Text(listOfNotNull(p.txt("articulo"), p.txt("descripcion")).joinToString(" · "), fontSize = 13.sp)
                                    Text("${p.txt("empresaNombre") ?: ""} · Recepción ${p.txt("recepcion") ?: ""}", color = Colores.Tenue, fontSize = 12.sp)
                                }
                            }
                        }
                    }
                }
            }
            BotonSecundario("Actualizar") { cargar() }
        } else {
            FichaContenedor(vm, e)
            val cont = e.optString("contenedor")
            SelectorDestino(vm, listOf(cont), null, "Guardar") { destino, nivel, aut ->
                val item = ItemCola.nuevo("guardado", cont, e.txt("descripcion") ?: "", destino = destino, nivel = nivel)
                vm.encolar(listOf(item))
                if (aut) vm.ir(Pantalla.Esperando(item.grupo, "$cont → $destino"))
                else vm.avisar("✔ $cont guardado en $destino. Se carga en el WMS.")
                pendientes = pendientes?.filter { it.optString("contenedor") != cont }
                elegido = null; codigo = ""
            }
            BotonSecundario("Elegir otro contenedor") { elegido = null }
        }
    }
}

// ======================================================================= cambiar estado (con foto)

val MOTIVOS = listOf("ROTURA", "NO CONFORME", "INCOMPLETO", "CUARENTENA", "OBSOLETO", "SCRAP", "DEPOSITO EXTERNO")
val MOTIVOS_CON_FOTO = setOf("ROTURA", "NO CONFORME")

@Composable
fun CambiarEstadoScreen(vm: AppViewModel, inicial: String?) {
    val ctx = LocalContext.current
    var codigo by remember { mutableStateOf(inicial ?: "") }
    var info by remember { mutableStateOf<JSONObject?>(null) }
    var disponible by remember { mutableStateOf(false) }
    var motivo by remember { mutableStateOf<String?>(null) }
    var comentario by remember { mutableStateOf("") }
    val fotos = remember { mutableStateListOf<String>() }
    var pendienteFoto by remember { mutableStateOf<File?>(null) }
    val camara = rememberLauncherForActivityResult(ActivityResultContracts.TakePicture()) { ok ->
        val f = pendienteFoto
        if (ok && f != null && f.exists()) { achicar(f); fotos.add(f.absolutePath) } else f?.delete()
        pendienteFoto = null
    }
    fun sacarFoto() {
        val dir = File(ctx.filesDir, "fotos").apply { mkdirs() }
        val f = File(dir, "foto_${System.currentTimeMillis()}.jpg")
        pendienteFoto = f
        val uri: Uri = FileProvider.getUriForFile(ctx, ctx.packageName + ".archivos", f)
        camara.launch(uri)
    }
    fun buscar(c: String) {
        info = null
        vm.lanzar(alTerminar = { r: JSONObject -> if (r.optBoolean("encontrado")) info = r else vm.avisar("No se encontró $c", true) }) { vm.api.obj("/api/contenedor/${Api.cod(c)}") }
    }
    LaunchedEffect(inicial) { if (!inicial.isNullOrBlank()) buscar(inicial) }
    val fotoObligatoria = !disponible && motivo in MOTIVOS_CON_FOTO
    Escena("Cambiar estado", vm, subtitulo = "Contenedor dañado o no disponible") {
        CampoEscaneo(vm, codigo, { codigo = it }, "Contenedor", alEnviar = { buscar(it) })
        info?.let { c ->
            FichaContenedor(vm, c)
            Text("Nuevo estado", fontWeight = FontWeight.Bold)
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Opcion("No disponible", !disponible, Colores.Error, Modifier.weight(1f)) { disponible = false }
                Opcion("Disponible", disponible, Colores.Ok, Modifier.weight(1f)) { disponible = true; motivo = null }
            }
            if (!disponible) {
                Text("Motivo", fontWeight = FontWeight.Bold)
                MOTIVOS.chunked(2).forEach { fila ->
                    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        fila.forEach { m -> Opcion(m.lowercase().replaceFirstChar { it.uppercase() } + if (m in MOTIVOS_CON_FOTO) " 📷" else "", motivo == m, Colores.Marino, Modifier.weight(1f)) { motivo = m } }
                        if (fila.size == 1) Spacer(Modifier.weight(1f))
                    }
                }
            }
            Text("Fotos" + if (fotoObligatoria) " (obligatoria)" else " (opcional)", fontWeight = FontWeight.Bold, color = if (fotoObligatoria && fotos.isEmpty()) Colores.Error else Colores.Texto)
            Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                fotos.forEach { p -> MiniaturaLocal(p) { fotos.remove(p); File(p).delete() } }
                Box(Modifier.size(90.dp).clip(RoundedCornerShape(10.dp)).background(Colores.NaranjaClaro).border(androidx.compose.foundation.BorderStroke(1.dp, Colores.Naranja), RoundedCornerShape(10.dp)).clickable { sacarFoto() },
                    contentAlignment = Alignment.Center) { Icon(Icons.Filled.AddAPhoto, "Sacar foto", tint = Colores.Naranja, modifier = Modifier.size(34.dp)) }
            }
            OutlinedTextField(comentario, { comentario = it }, label = { Text("Comentario (opcional)") }, modifier = Modifier.fillMaxWidth())
            BotonPrincipal("Registrar cambio de estado", habilitado = (disponible || motivo != null) && (!fotoObligatoria || fotos.isNotEmpty()), color = Colores.Error) {
                val item = ItemCola.nuevo("cambio_estado", c.optString("contenedor"), c.txt("descripcion") ?: "", disponible = disponible, motivo = motivo,
                    comentario = comentario.ifBlank { null }, fotos = fotos.toList())
                vm.encolar(listOf(item))
                vm.avisar("✔ Cambio de estado registrado. Se carga en el WMS.")
                vm.atras()
            }
        }
    }
}

@Composable
fun Opcion(texto: String, elegida: Boolean, color: Color, modifier: Modifier = Modifier, onClick: () -> Unit) {
    Box(
        modifier.clip(RoundedCornerShape(10.dp)).background(if (elegida) color else Colores.Panel)
            .border(androidx.compose.foundation.BorderStroke(1.dp, if (elegida) color else Colores.Linea), RoundedCornerShape(10.dp))
            .clickable(onClick = onClick).padding(vertical = 14.dp, horizontal = 8.dp),
        contentAlignment = Alignment.Center,
    ) { Text(texto, color = if (elegida) Color.White else Colores.Texto, fontWeight = FontWeight.SemiBold) }
}

@Composable
fun MiniaturaLocal(ruta: String, quitar: () -> Unit) {
    val bmp = remember(ruta) { BitmapFactory.decodeFile(ruta, BitmapFactory.Options().apply { inSampleSize = 8 }) }
    Box(Modifier.size(90.dp)) {
        if (bmp != null) Image(bmp.asImageBitmap(), null, contentScale = ContentScale.Crop, modifier = Modifier.size(90.dp).clip(RoundedCornerShape(10.dp)))
        IconButton(onClick = quitar, modifier = Modifier.align(Alignment.TopEnd).size(28.dp).clip(RoundedCornerShape(50)).background(Color.Black.copy(alpha = 0.5f))) {
            Icon(Icons.Filled.Close, "Quitar", tint = Color.White, modifier = Modifier.size(16.dp))
        }
    }
}

/** Reduce la foto a 1600 px para que suba rápido por datos móviles. */
fun achicar(f: File) {
    try {
        val o = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeFile(f.absolutePath, o)
        var m = 1
        while (o.outWidth / (m * 2) >= 1600 || o.outHeight / (m * 2) >= 1600) m *= 2
        var b = BitmapFactory.decodeFile(f.absolutePath, BitmapFactory.Options().apply { inSampleSize = m }) ?: return
        // la cámara guarda la foto "acostada" con una marca EXIF: se endereza antes de mandarla
        val grados = when (android.media.ExifInterface(f.absolutePath).getAttributeInt(android.media.ExifInterface.TAG_ORIENTATION, android.media.ExifInterface.ORIENTATION_NORMAL)) {
            android.media.ExifInterface.ORIENTATION_ROTATE_90 -> 90f
            android.media.ExifInterface.ORIENTATION_ROTATE_180 -> 180f
            android.media.ExifInterface.ORIENTATION_ROTATE_270 -> 270f
            else -> 0f
        }
        if (grados != 0f) b = Bitmap.createBitmap(b, 0, 0, b.width, b.height, android.graphics.Matrix().apply { postRotate(grados) }, true)
        FileOutputStream(f).use { b.compress(Bitmap.CompressFormat.JPEG, 85, it) }
    } catch (_: Exception) {}
}

// ======================================================================= mis movimientos

private val formatoHora = DateTimeFormatter.ofPattern("dd/MM HH:mm").withZone(ZoneId.systemDefault())

@Composable
fun MisMovimientosScreen(vm: AppViewModel) {
    val items by vm.cola.items.collectAsState()
    val conectado by vm.cola.conectado.collectAsState()
    Escena("Mis movimientos", vm, subtitulo = "${items.count { !it.enviado }} en cola · ${items.size} en el celular") {
        if (!conectado) Aviso("Sin conexión con el servidor: los movimientos quedan guardados en el celular y se envían solos.", TipoAviso.Aviso)
        if (items.any { !it.enviado }) BotonSecundario("Reintentar envío ahora") { vm.cola.empujar() }
        if (items.isEmpty()) Aviso("Todavía no hiciste movimientos en este celular.")
        items.reversed().forEach { m ->
            Tarjeta {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Column(Modifier.weight(1f)) {
                        Text(m.contenedor, fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold, color = Colores.Marino)
                        Text(when (m.tipo) {
                            "transferencia" -> "Transferencia → ${m.destino}"
                            "guardado" -> "Guardado en ${m.destino}"
                            "cambio_estado" -> if (m.disponible == true) "Pasó a disponible" else "No disponible · ${m.motivo ?: ""}"
                            "reubicar_cajas" -> "Reubicar ${m.cantidad?.lindo() ?: ""} cajas → ${m.destino}"
                            "ajuste" -> "Eliminado (ajuste a 0) · ${m.motivo ?: ""}"
                            else -> m.tipo
                        }, fontSize = 14.sp)
                        Text(listOfNotNull(try { formatoHora.format(Instant.parse(m.hora)) } catch (_: Exception) { null }, m.descripcion.ifBlank { null }).joinToString(" · "), color = Colores.Tenue, fontSize = 12.sp)
                    }
                    EstadoChip(m.estado)
                }
                m.mensaje?.let { Text(it, color = if (m.estado in setOf("error", "rechazado")) Colores.Error else Colores.Tenue, fontSize = 12.sp) }
            }
        }
    }
}

// ======================================================================= autorizaciones (analista desde el celular)

@Composable
fun AutorizacionesScreen(vm: AppViewModel) {
    var lista by remember { mutableStateOf<List<JSONObject>?>(null) }
    fun cargar() { vm.lanzar(alTerminar = { a: JSONArray -> lista = a.objetos() }) { vm.api.lista("/api/autorizaciones?estado=pendiente") } }
    LaunchedEffect(Unit) { cargar() }
    LaunchedEffect(Unit) { vm.eventos.eventos.collect { if (it.optString("tipo") == "autorizacion") cargar() } }
    Escena("Autorizaciones", vm) {
        when {
            lista == null -> Cargando()
            lista!!.isEmpty() -> Aviso("No hay pedidos pendientes 👍", TipoAviso.Ok)
            else -> lista!!.forEach { a ->
                Tarjeta(borde = Colores.Aviso, fondo = Colores.AvisoClaro) {
                    Text(a.optString("solicitado_por"), fontWeight = FontWeight.Bold)
                    Text(a.optString("motivo"))
                    a.optJSONArray("movimientos")?.objetos()?.forEach { m ->
                        Text("${m.optString("contenedor")} ${m.txt("origen") ?: ""} → ${m.optString("destino")}", fontFamily = FontFamily.Monospace, fontSize = 12.sp, color = Colores.Tenue)
                    }
                    HorizontalDivider(color = Colores.Linea)
                    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        BotonPrincipal("Sí", Modifier.weight(1f), color = Colores.Ok) {
                            vm.lanzar(alTerminar = { _: JSONObject -> vm.avisar("Autorizado"); cargar() }) { vm.api.post("/api/autorizaciones/${a.optInt("id")}", JSONObject().put("aprobar", true)) }
                        }
                        BotonPrincipal("No", Modifier.weight(1f), color = Colores.Error) {
                            vm.lanzar(alTerminar = { _: JSONObject -> vm.avisar("Rechazado"); cargar() }) { vm.api.post("/api/autorizaciones/${a.optInt("id")}", JSONObject().put("aprobar", false)) }
                        }
                    }
                }
            }
        }
    }
}
