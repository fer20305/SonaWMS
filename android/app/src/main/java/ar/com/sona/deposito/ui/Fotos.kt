package ar.com.sona.deposito.ui

import android.graphics.Bitmap
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.calculatePan
import androidx.compose.foundation.gestures.calculateZoom
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Image
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.input.pointer.positionChanged
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import kotlinx.coroutines.launch
import org.json.JSONObject

/** Todas las fotos que trae un resultado del servidor ("fotos"), o la única "foto". */
fun JSONObject.fotos(): List<String> {
    val a = optJSONArray("fotos")
    val l = if (a != null) (0 until a.length()).mapNotNull { i -> a.optString(i).takeIf { it.isNotBlank() && it != "null" } } else emptyList()
    if (l.isNotEmpty()) return l
    val f = optString("foto")
    return if (f.isNotBlank() && f != "null") listOf(f) else emptyList()
}

/**
 * Foto traída por el servidor (artículo o movimiento). Tocando se abre en grande; si el artículo tiene varias fotos
 * se pasan deslizando (como en SONA Escáner). Si hay más de una, muestra cuántas.
 */
@Composable
fun FotoRemota(vm: AppViewModel, url: String?, tamano: Dp = 72.dp, galeria: List<String> = emptyList(), indice: Int = 0) {
    if (url.isNullOrBlank()) {
        Box(Modifier.size(tamano).clip(RoundedCornerShape(10.dp)).background(Color(0xFFE4E9EE)), contentAlignment = Alignment.Center) {
            Icon(Icons.Filled.Image, null, tint = Colores.Linea)
        }
        return
    }
    val todas = if (galeria.isEmpty()) listOf(url) else galeria
    val bmp by produceState<Bitmap?>(initialValue = null, url) { value = vm.imagen(url, (tamano.value * 3).toInt()) }
    var grande by remember { mutableStateOf(false) }
    Box(
        Modifier.size(tamano).clip(RoundedCornerShape(10.dp)).background(Color(0xFFE4E9EE)).border(BorderStroke(1.dp, Colores.Linea), RoundedCornerShape(10.dp)).clickable { grande = true },
        contentAlignment = Alignment.Center,
    ) {
        val b = bmp
        if (b != null) Image(b.asImageBitmap(), null, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize())
        else CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp, color = Colores.Tenue)
        if (todas.size > 1) Text(
            "📷 ${todas.size}", color = Color.White, fontSize = 11.sp, fontWeight = FontWeight.Bold,
            modifier = Modifier.align(Alignment.BottomEnd).padding(3.dp).clip(RoundedCornerShape(6.dp)).background(Color.Black.copy(alpha = 0.6f)).padding(horizontal = 5.dp, vertical = 1.dp),
        )
    }
    if (grande) VisorFotos(vm, todas, todas.indexOf(url).coerceAtLeast(indice.coerceIn(0, todas.lastIndex))) { grande = false }
}

/** Tira de miniaturas: cada una abre la galería completa en esa foto. */
@Composable
fun TiraFotos(vm: AppViewModel, fotos: List<String>, tamano: Dp = 64.dp) {
    if (fotos.isEmpty()) return
    Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        fotos.forEachIndexed { i, f -> FotoRemota(vm, f, tamano, fotos, i) }
    }
}

@Composable
fun VisorFoto(vm: AppViewModel, url: String, cerrar: () -> Unit) = VisorFotos(vm, listOf(url), 0, cerrar)

/** Galería a pantalla completa: se desliza entre fotos, pellizcar o doble toque para hacer zoom. */
@Composable
fun VisorFotos(vm: AppViewModel, urls: List<String>, inicio: Int, cerrar: () -> Unit) {
    val pager = rememberPagerState(initialPage = inicio.coerceIn(0, (urls.size - 1).coerceAtLeast(0))) { urls.size }
    val alcance = rememberCoroutineScope()
    var escala by remember { mutableFloatStateOf(1f) }
    var dx by remember { mutableFloatStateOf(0f) }
    var dy by remember { mutableFloatStateOf(0f) }
    LaunchedEffect(pager.currentPage) { escala = 1f; dx = 0f; dy = 0f }
    Dialog(onDismissRequest = cerrar, properties = DialogProperties(usePlatformDefaultWidth = false)) {
        Box(Modifier.fillMaxSize().background(Color.Black)) {
            HorizontalPager(state = pager, userScrollEnabled = escala <= 1f, modifier = Modifier.fillMaxSize()) { pagina ->
                val url = urls[pagina]
                val bmp by produceState<Bitmap?>(initialValue = null, url) { value = vm.imagen(url, 1600) }
                val actual = pagina == pager.currentPage
                Box(
                    Modifier.fillMaxSize()
                        .pointerInput(pagina) {
                            // con un dedo y sin zoom no se consume nada: así el deslizamiento pasa al pager
                            awaitEachGesture {
                                awaitFirstDown(requireUnconsumed = false)
                                do {
                                    val ev = awaitPointerEvent()
                                    val dedos = ev.changes.count { it.pressed }
                                    if (dedos >= 2 || escala > 1f) {
                                        escala = (escala * ev.calculateZoom()).coerceIn(1f, 6f)
                                        if (escala > 1f) { val p = ev.calculatePan(); dx += p.x; dy += p.y } else { dx = 0f; dy = 0f }
                                        ev.changes.forEach { if (it.positionChanged()) it.consume() }
                                    }
                                } while (ev.changes.any { it.pressed })
                            }
                        }
                        .pointerInput(pagina) { detectTapGestures(onDoubleTap = { if (escala > 1f) { escala = 1f; dx = 0f; dy = 0f } else escala = 2.5f }) },
                    contentAlignment = Alignment.Center,
                ) {
                    val b = bmp
                    if (b != null) Image(b.asImageBitmap(), null, contentScale = ContentScale.Fit,
                        modifier = Modifier.fillMaxSize().graphicsLayer {
                            if (actual) { scaleX = escala; scaleY = escala; translationX = dx; translationY = dy }
                        })
                    else CircularProgressIndicator(color = Colores.Naranja)
                }
            }
            IconButton(onClick = cerrar, modifier = Modifier.align(Alignment.TopEnd).statusBarsPadding()) { Icon(Icons.Filled.Close, "Cerrar", tint = Color.White) }
            if (urls.size > 1) {
                Text("${pager.currentPage + 1} / ${urls.size}", color = Color.White, fontWeight = FontWeight.Bold,
                    modifier = Modifier.align(Alignment.TopStart).statusBarsPadding().padding(16.dp))
                Row(
                    Modifier.align(Alignment.BottomCenter).navigationBarsPadding().padding(bottom = 16.dp).horizontalScroll(rememberScrollState()),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    urls.forEachIndexed { i, u ->
                        val mini by produceState<Bitmap?>(initialValue = null, u) { value = vm.imagen(u, 150) }
                        Box(
                            Modifier.size(52.dp).clip(RoundedCornerShape(8.dp))
                                .border(BorderStroke(if (i == pager.currentPage) 3.dp else 1.dp, if (i == pager.currentPage) Colores.Naranja else Color.White.copy(alpha = 0.4f)), RoundedCornerShape(8.dp))
                                .background(Color.DarkGray).clickable { alcance.launch { pager.animateScrollToPage(i) } },
                        ) {
                            val m = mini
                            if (m != null) Image(m.asImageBitmap(), null, contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize())
                        }
                    }
                }
            }
        }
    }
}

/** Foto grande del artículo (picking). Si no hay, lo dice: IMAGEN NO DISPONIBLE. Tocando se abre la galería. */
@Composable
fun FotoGrande(vm: AppViewModel, fotos: List<String>, alto: Dp = 230.dp) {
    val url = fotos.firstOrNull()
    var grande by remember { mutableStateOf(false) }
    Box(
        Modifier.fillMaxWidth().height(alto).clip(RoundedCornerShape(14.dp)).background(Color(0xFFE4E9EE))
            .border(BorderStroke(1.dp, Colores.Linea), RoundedCornerShape(14.dp)).clickable(enabled = url != null) { grande = true },
        contentAlignment = Alignment.Center,
    ) {
        if (url == null) {
            Text("IMAGEN NO DISPONIBLE", color = Colores.Tenue, fontWeight = FontWeight.Bold, fontSize = 16.sp)
        } else {
            val bmp by produceState<Bitmap?>(initialValue = null, url) { value = vm.imagen(url, 900) }
            val b = bmp
            if (b != null) Image(b.asImageBitmap(), null, contentScale = ContentScale.Fit, modifier = Modifier.fillMaxSize().padding(6.dp))
            else CircularProgressIndicator(color = Colores.Naranja)
            if (fotos.size > 1) Text("📷 ${fotos.size}", color = Color.White, fontSize = 12.sp, fontWeight = FontWeight.Bold,
                modifier = Modifier.align(Alignment.BottomEnd).padding(8.dp).clip(RoundedCornerShape(6.dp)).background(Color.Black.copy(alpha = 0.6f)).padding(horizontal = 6.dp, vertical = 2.dp))
        }
    }
    if (grande && url != null) VisorFotos(vm, fotos, 0) { grande = false }
}
