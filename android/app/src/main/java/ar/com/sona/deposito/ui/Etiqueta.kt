package ar.com.sona.deposito.ui

import android.content.Context
import android.print.PrintAttributes
import android.print.PrintManager
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView

/**
 * Impresora virtual: muestra la etiqueta del contenedor (10 x 8 cm, con código de barras) que arma el servidor.
 * "Imprimir" abre la impresión de Android: sirve con cualquier impresora configurada en el celular o, si no hay ninguna enchufada,
 * con "Guardar como PDF" (queda el PDF para imprimirlo después o mandarlo por WhatsApp).
 */
@Composable
fun EtiquetaVirtual(vm: AppViewModel, ruta: String, contenedor: String) {
    val ctx = LocalContext.current
    var html by remember(ruta) { mutableStateOf<String?>(null) }
    var vista by remember { mutableStateOf<WebView?>(null) }
    LaunchedEffect(ruta) {
        vm.lanzar(alTerminar = { t: String -> html = t }) { vm.api.texto(ruta) }
    }
    Tarjeta {
        Text("🖨 Etiqueta (impresora virtual)", fontWeight = FontWeight.Bold, color = Colores.Marino)
        Text("No hace falta impresora enchufada: la podés imprimir o guardar en PDF.", color = Colores.Tenue, fontSize = 12.sp)
        val h = html
        if (h == null) Cargando("Armando la etiqueta…")
        else AndroidView(
            factory = { c -> WebView(c).apply { webViewClient = WebViewClient(); settings.loadWithOverviewMode = true; settings.useWideViewPort = true; vista = this } },
            update = { w -> w.loadDataWithBaseURL(vm.api.base + "/", h, "text/html", "utf-8", null) },
            modifier = Modifier.fillMaxWidth().height(300.dp),
        )
        BotonPrincipal("Imprimir / guardar PDF", habilitado = vista != null && html != null, color = Colores.Marino) {
            vista?.let { imprimir(ctx, it, contenedor) }
        }
    }
}

fun imprimir(ctx: Context, w: WebView, contenedor: String) {
    val pm = ctx.getSystemService(Context.PRINT_SERVICE) as PrintManager
    val atributos = PrintAttributes.Builder()
        .setMediaSize(PrintAttributes.MediaSize("SONA_10x8", "Etiqueta 10 x 8 cm", 3937, 3150).asLandscape())
        .setMinMargins(PrintAttributes.Margins.NO_MARGINS)
        .build()
    pm.print("Etiqueta $contenedor", w.createPrintDocumentAdapter("Etiqueta_$contenedor"), atributos)
}

@Composable
fun EtiquetaColumna(vm: AppViewModel, ruta: String?, contenedor: String?) {
    if (ruta == null || contenedor == null) return
    Column { EtiquetaVirtual(vm, ruta, contenedor) }
}
