package ar.com.sona.deposito.ui

import android.Manifest
import android.content.pm.PackageManager
import android.graphics.Bitmap
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTransformGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.FlashlightOff
import androidx.compose.material.icons.filled.FlashlightOn
import androidx.compose.material.icons.filled.Image
import androidx.compose.material.icons.filled.QrCodeScanner
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.key
import androidx.compose.ui.input.key.onPreviewKeyEvent
import androidx.compose.ui.input.key.type
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import androidx.core.content.ContextCompat
import ar.com.sona.deposito.data.lindo
import ar.com.sona.deposito.data.num
import ar.com.sona.deposito.data.txt
import org.json.JSONObject

/**
 * Pantalla estándar: barra azul con título y volver, contenido desplazable.
 * El contenido nunca queda debajo de la barra de navegación del celular (botones atrás/inicio) ni del teclado:
 * el último elemento siempre se puede subir hasta verlo entero.
 */
@Composable
fun Escena(
    titulo: String,
    vm: AppViewModel,
    subtitulo: String? = null,
    volver: Boolean = true,
    acciones: @Composable () -> Unit = {},
    desplazable: Boolean = true,
    pie: (@Composable () -> Unit)? = null,
    contenido: @Composable ColumnScope.() -> Unit,
) {
    Column(Modifier.fillMaxSize().background(Colores.Fondo).imePadding()) {
        Row(
            Modifier.fillMaxWidth().background(Colores.Marino).statusBarsPadding().padding(horizontal = 4.dp, vertical = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            if (volver) IconButton(onClick = { vm.atras() }) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "Volver", tint = Color.White) }
            else Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f)) {
                Text(titulo, color = Color.White, fontSize = 20.sp, fontWeight = FontWeight.Bold)
                if (subtitulo != null) Text(subtitulo, color = Color.White.copy(alpha = 0.75f), fontSize = 12.sp)
            }
            acciones()
        }
        Box(Modifier.height(3.dp).fillMaxWidth().background(Colores.Naranja))
        val mod = Modifier.weight(1f).fillMaxWidth().clipToBounds().let { if (desplazable) it.verticalScroll(rememberScrollState()) else it }
            .let { if (pie == null) it.navigationBarsPadding() else it }
        Column(mod.padding(start = 14.dp, end = 14.dp, top = 14.dp, bottom = 14.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            contenido()
            // aire al final: que el último ítem no quede tapado por los avisos que aparecen abajo
            if (desplazable) Spacer(Modifier.height(if (pie == null) 72.dp else 24.dp))
        }
        if (pie != null) {
            Box(Modifier.fillMaxWidth().background(Colores.Panel).border(BorderStroke(1.dp, Colores.Linea)).navigationBarsPadding().padding(12.dp)) { pie() }
        }
    }
}

@Composable
fun Tarjeta(modifier: Modifier = Modifier, borde: Color = Colores.Linea, fondo: Color = Colores.Panel, onClick: (() -> Unit)? = null, contenido: @Composable ColumnScope.() -> Unit) {
    val forma = RoundedCornerShape(12.dp)
    Column(
        modifier.fillMaxWidth().clip(forma).background(fondo).border(BorderStroke(1.dp, borde), forma)
            .let { if (onClick != null) it.clickable(onClick = onClick) else it }.padding(14.dp),
        verticalArrangement = Arrangement.spacedBy(6.dp), content = contenido,
    )
}

enum class TipoAviso { Ok, Aviso, Error, Info }

@Composable
fun Aviso(texto: String, tipo: TipoAviso = TipoAviso.Info) {
    val (f, c) = when (tipo) {
        TipoAviso.Ok -> Colores.OkClaro to Colores.Ok
        TipoAviso.Aviso -> Colores.AvisoClaro to Colores.Aviso
        TipoAviso.Error -> Colores.ErrorClaro to Colores.Error
        TipoAviso.Info -> Colores.InfoClaro to Colores.Info
    }
    Text(texto, color = c, fontWeight = FontWeight.SemiBold, modifier = Modifier.fillMaxWidth().clip(RoundedCornerShape(10.dp)).background(f).padding(12.dp))
}

@Composable
fun BotonPrincipal(texto: String, modifier: Modifier = Modifier, habilitado: Boolean = true, color: Color = Colores.Naranja, cargando: Boolean = false, onClick: () -> Unit) {
    Button(
        onClick = onClick, enabled = habilitado && !cargando,
        modifier = modifier.fillMaxWidth().height(58.dp),
        shape = RoundedCornerShape(12.dp),
        colors = ButtonDefaults.buttonColors(containerColor = color, contentColor = Color.White),
    ) {
        if (cargando) CircularProgressIndicator(Modifier.size(22.dp), color = Color.White, strokeWidth = 2.dp)
        else Text(texto, fontSize = 18.sp, fontWeight = FontWeight.Bold)
    }
}

@Composable
fun BotonSecundario(texto: String, modifier: Modifier = Modifier, habilitado: Boolean = true, onClick: () -> Unit) {
    Button(
        onClick = onClick, enabled = habilitado, modifier = modifier.fillMaxWidth().height(50.dp), shape = RoundedCornerShape(12.dp),
        colors = ButtonDefaults.buttonColors(containerColor = Colores.Panel, contentColor = Colores.Marino),
        border = BorderStroke(1.dp, Colores.Linea),
    ) { Text(texto, fontSize = 16.sp, fontWeight = FontWeight.SemiBold) }
}

@Composable
fun Cargando(texto: String = "Consultando…") {
    Row(Modifier.fillMaxWidth().padding(20.dp), horizontalArrangement = Arrangement.Center, verticalAlignment = Alignment.CenterVertically) {
        CircularProgressIndicator(Modifier.size(26.dp), color = Colores.Naranja, strokeWidth = 3.dp)
        Spacer(Modifier.width(12.dp))
        Text(texto, color = Colores.Tenue)
    }
}

/** Botón redondo de "aceptar" (✓) que va al costado de los campos de texto. */
@Composable
fun BotonAceptarCampo(habilitado: Boolean, onClick: () -> Unit) {
    Box(
        Modifier.padding(end = 2.dp).size(42.dp).clip(RoundedCornerShape(50))
            .background(if (habilitado) Colores.Naranja else Colores.Linea)
            .clickable(enabled = habilitado, onClick = onClick),
        contentAlignment = Alignment.Center,
    ) { Icon(Icons.Filled.Check, "Aceptar", tint = Color.White, modifier = Modifier.size(26.dp)) }
}

/** Enter de un teclado físico o de la pistola lectora (modo teclado) también confirma. */
fun Modifier.alApretarEnter(accion: () -> Unit): Modifier = onPreviewKeyEvent { e ->
    if ((e.key == Key.Enter || e.key == Key.NumPadEnter) && e.type == KeyEventType.KeyUp) { accion(); true }
    else e.key == Key.Enter || e.key == Key.NumPadEnter
}

/**
 * Campo para escanear o tipear (contenedor, Ubic.Ctrl, posición). El botón QR abre la cámara y el ✓ confirma lo escrito
 * (igual que Enter en el teclado). Al leer/confirmar llama a [alEnviar].
 */
@Composable
fun CampoEscaneo(
    vm: AppViewModel,
    valor: String,
    alCambiar: (String) -> Unit,
    etiqueta: String,
    alEnviar: (String) -> Unit,
    modifier: Modifier = Modifier,
    foco: FocusRequester? = null,
    numerico: Boolean = false,
) {
    var camara by remember { mutableStateOf(false) }
    val teclado = LocalSoftwareKeyboardController.current
    fun confirmar() { if (valor.isNotBlank()) { teclado?.hide(); alEnviar(valor.trim().uppercase()) } }
    OutlinedTextField(
        value = valor,
        onValueChange = { t -> alCambiar(t.uppercase().replace("\n", "").replace("\r", "")) },
        label = { Text(etiqueta) },
        singleLine = true,
        textStyle = androidx.compose.ui.text.TextStyle(fontSize = 20.sp, fontWeight = FontWeight.SemiBold, fontFamily = FontFamily.Monospace),
        keyboardOptions = KeyboardOptions(
            capitalization = KeyboardCapitalization.Characters, imeAction = ImeAction.Done,
            keyboardType = if (numerico) KeyboardType.Number else KeyboardType.Ascii, autoCorrectEnabled = false,
        ),
        keyboardActions = KeyboardActions(onDone = { confirmar() }, onGo = { confirmar() }, onSearch = { confirmar() }),
        trailingIcon = {
            Row(verticalAlignment = Alignment.CenterVertically) {
                IconButton(onClick = { camara = true }) { Icon(Icons.Filled.QrCodeScanner, "Escanear", tint = Colores.Naranja, modifier = Modifier.size(30.dp)) }
                BotonAceptarCampo(valor.isNotBlank()) { confirmar() }
            }
        },
        colors = OutlinedTextFieldDefaults.colors(focusedBorderColor = Colores.Naranja, focusedLabelColor = Colores.Naranja, unfocusedContainerColor = Colores.Panel, focusedContainerColor = Colores.Panel),
        modifier = modifier.fillMaxWidth().alApretarEnter { confirmar() }.let { if (foco != null) it.focusRequester(foco) else it },
    )
    if (camara) DialogoEscaner(vm, cerrar = { camara = false }) { leido ->
        camara = false
        alCambiar(leido.uppercase())
        alEnviar(leido.trim().uppercase())
    }
}

@Composable
fun CampoNumero(
    valor: String, alCambiar: (String) -> Unit, etiqueta: String, modifier: Modifier = Modifier, foco: FocusRequester? = null,
    decimales: Boolean = false, conBoton: Boolean = true, alEnviar: () -> Unit = {},
) {
    val teclado = LocalSoftwareKeyboardController.current
    fun confirmar() { teclado?.hide(); alEnviar() }
    val icono: (@Composable () -> Unit)? = if (conBoton) { { BotonAceptarCampo(true) { confirmar() } } } else null
    OutlinedTextField(
        value = valor,
        onValueChange = { t -> alCambiar(t.filter { it.isDigit() || (decimales && (it == '.' || it == ',')) }.replace(',', '.')) },
        label = { Text(etiqueta) },
        singleLine = true,
        textStyle = androidx.compose.ui.text.TextStyle(fontSize = 20.sp, fontWeight = FontWeight.SemiBold),
        keyboardOptions = KeyboardOptions(keyboardType = if (decimales) KeyboardType.Decimal else KeyboardType.Number, imeAction = ImeAction.Done),
        keyboardActions = KeyboardActions(onDone = { confirmar() }),
        trailingIcon = icono,
        colors = OutlinedTextFieldDefaults.colors(focusedBorderColor = Colores.Naranja, focusedLabelColor = Colores.Naranja, unfocusedContainerColor = Colores.Panel, focusedContainerColor = Colores.Panel),
        modifier = modifier.alApretarEnter { confirmar() }.let { if (foco != null) it.focusRequester(foco) else it },
    )
}

/**
 * Cámara a pantalla completa para leer QR / código de barras.
 * Solo se lee lo que queda dentro del recuadro; si hay un código afuera se avisa. Zoom con los botones o pellizcando.
 */
@Composable
fun DialogoEscaner(vm: AppViewModel, cerrar: () -> Unit, alLeer: (String) -> Unit) {
    val ctx = LocalContext.current
    val densidad = LocalDensity.current
    var permiso by remember { mutableStateOf(ContextCompat.checkSelfPermission(ctx, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) }
    val pedir = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { ok -> permiso = ok; if (!ok) cerrar() }
    var linterna by remember { mutableStateOf(vm.ajustes.linterna) }
    var zoom by remember { mutableStateOf(1f) }
    var fuera by remember { mutableStateOf(false) }
    var leido by remember { mutableStateOf(false) }
    if (!permiso) { androidx.compose.runtime.LaunchedEffect(Unit) { pedir.launch(Manifest.permission.CAMERA) }; return }
    androidx.compose.runtime.LaunchedEffect(fuera) { if (fuera) { kotlinx.coroutines.delay(1400); fuera = false } }
    val anchoMira = 290.dp
    val altoMira = 190.dp
    val miraPx = with(densidad) { Pair(anchoMira.roundToPx(), altoMira.roundToPx()) }
    Dialog(onDismissRequest = cerrar, properties = DialogProperties(usePlatformDefaultWidth = false, decorFitsSystemWindows = false)) {
        Box(Modifier.fillMaxSize().background(Color.Black)) {
            VistaCamara(
                Modifier.fillMaxSize(), linterna = linterna, activa = !leido, ventanaPx = miraPx, zoom = zoom,
                alCambiarZoom = { zoom = it }, alVerCodigoFuera = { fuera = true },
            ) { v ->
                leido = true
                vm.vibrar(70)
                alLeer(v)
            }
            // oscurece todo menos la mira
            Box(Modifier.fillMaxSize().background(Color.Black.copy(alpha = 0.25f)))
            Box(
                Modifier.align(Alignment.Center).size(anchoMira, altoMira)
                    .border(BorderStroke(3.dp, if (leido) Colores.Ok else if (fuera) Colores.Aviso else Colores.Naranja), RoundedCornerShape(16.dp))
            ) {
                Box(Modifier.align(Alignment.Center).fillMaxWidth(0.9f).height(2.dp).background(Colores.Naranja.copy(alpha = 0.7f)))
            }
            Row(Modifier.fillMaxWidth().align(Alignment.TopCenter).background(Color.Black.copy(alpha = 0.5f)).statusBarsPadding().padding(8.dp), verticalAlignment = Alignment.CenterVertically) {
                Text("Apuntá al código", color = Color.White, fontSize = 18.sp, fontWeight = FontWeight.Bold, modifier = Modifier.weight(1f).padding(start = 8.dp))
                IconButton(onClick = { linterna = !linterna; vm.ajustes.linterna = linterna }) {
                    Icon(if (linterna) Icons.Filled.FlashlightOn else Icons.Filled.FlashlightOff, "Linterna", tint = Color.White)
                }
                IconButton(onClick = cerrar) { Icon(Icons.Filled.Close, "Cerrar", tint = Color.White) }
            }
            Column(
                Modifier.fillMaxWidth().align(Alignment.BottomCenter).background(Color.Black.copy(alpha = 0.5f)).navigationBarsPadding().padding(12.dp),
                horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                Text(
                    if (fuera) "Hay un código fuera del recuadro: centralo" else "Solo se lee el código que está dentro del recuadro. Tocá la imagen para enfocar.",
                    color = if (fuera) Colores.NaranjaClaro else Color.White, fontSize = 13.sp,
                )
                Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    listOf(1f, 2f, 3f, 4f).forEach { z ->
                        val elegido = kotlin.math.abs(zoom - z) < 0.25f
                        Text(
                            "${z.toInt()}x", color = if (elegido) Color.White else Color.White.copy(alpha = 0.8f), fontWeight = FontWeight.Bold,
                            modifier = Modifier.clip(RoundedCornerShape(50)).background(if (elegido) Colores.Naranja else Color.White.copy(alpha = 0.15f))
                                .clickable { zoom = z }.padding(horizontal = 16.dp, vertical = 8.dp),
                        )
                    }
                }
            }
        }
    }
}

/** Ficha de un contenedor con foto. [ciego] oculta la cantidad (conteo). */
@Composable
fun FichaContenedor(vm: AppViewModel, c: JSONObject, ciego: Boolean = false, extra: @Composable ColumnScope.() -> Unit = {}) {
    Tarjeta {
        Row(verticalAlignment = Alignment.Top) {
            FotoRemota(vm, c.txt("foto"), 84.dp, c.fotos())
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(c.txt("contenedor") ?: "", fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold, fontSize = 19.sp, color = Colores.Marino)
                Text(listOfNotNull(c.txt("articulo"), c.txt("descripcion")).joinToString(" · "), fontWeight = FontWeight.SemiBold)
                (c.txt("empresaNombre") ?: c.txt("empresa") ?: c.txt("cliente"))?.let { Text(it, color = Colores.Tenue, fontSize = 13.sp) }
                if (!ciego) {
                    val partes = listOfNotNull(
                        c.num("cantidad")?.let { "Cant. ${it.lindo()}" },
                        c.num("m3")?.let { "${it.lindo()} m³" },
                        c.txt("posicion")?.let { "en $it" },
                    )
                    if (partes.isNotEmpty()) Text(partes.joinToString(" · "), color = Colores.Tenue, fontSize = 13.sp)
                    Spacer(Modifier.height(2.dp))
                    Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        EstadoContenedor(c)
                        if (c.has("articuloHabilitado") && !c.isNull("articuloHabilitado")) Habilitado(c.optBoolean("articuloHabilitado"))
                    }
                }
            }
        }
        extra()
    }
}

/** Disponible / No disponible (con motivo) de un contenedor. Si ninguna fuente lo informa, lo dice. */
@Composable
fun EstadoContenedor(c: JSONObject) {
    val disp: Boolean? = if (c.has("disponible") && !c.isNull("disponible")) c.optBoolean("disponible") else null
    val texto = c.txt("estadoTexto")
    val (t, f, col) = when (disp) {
        true -> Triple("● Disponible", Colores.OkClaro, Colores.Ok)
        false -> Triple("● No disponible" + (texto?.takeIf { !it.startsWith("NO DISP") }?.let { " · $it" } ?: ""), Colores.ErrorClaro, Colores.Error)
        null -> Triple(texto?.let { "Estado: $it" } ?: "Estado: sin dato", Color(0xFFEEF0F2), Colores.Tenue)
    }
    Text(t, color = col, fontSize = 12.sp, fontWeight = FontWeight.Bold, modifier = Modifier.clip(RoundedCornerShape(50)).background(f).padding(horizontal = 10.dp, vertical = 3.dp))
}

/** Artículo habilitado / deshabilitado en LES. */
@Composable
fun Habilitado(hab: Boolean) {
    val (t, f, col) = if (hab) Triple("✔ Habilitado", Colores.OkClaro, Colores.Ok) else Triple("✖ Deshabilitado en LES", Colores.ErrorClaro, Colores.Error)
    Text(t, color = col, fontSize = 12.sp, fontWeight = FontWeight.Bold, modifier = Modifier.clip(RoundedCornerShape(50)).background(f).padding(horizontal = 10.dp, vertical = 3.dp))
}

/** Icono + texto en grande, para el menú. */
@Composable
fun BotonMenu(icono: ImageVector, titulo: String, detalle: String, modifier: Modifier = Modifier, color: Color = Colores.Marino, insignia: Int = 0, onClick: () -> Unit) {
    val forma = RoundedCornerShape(16.dp)
    Column(
        modifier.clip(forma).background(Colores.Panel).border(BorderStroke(1.dp, Colores.Linea), forma).clickable(onClick = onClick).padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Box(Modifier.size(48.dp).clip(RoundedCornerShape(12.dp)).background(color), contentAlignment = Alignment.Center) {
                Icon(icono, null, tint = Color.White, modifier = Modifier.size(28.dp))
            }
            Spacer(Modifier.weight(1f))
            if (insignia > 0) Text("$insignia", color = Color.White, fontWeight = FontWeight.Bold, modifier = Modifier.clip(RoundedCornerShape(50)).background(Colores.Naranja).padding(horizontal = 9.dp, vertical = 2.dp))
        }
        Text(titulo, fontSize = 18.sp, fontWeight = FontWeight.Bold, color = Colores.Texto)
        Text(detalle, fontSize = 12.sp, color = Colores.Tenue, lineHeight = 15.sp)
    }
}

/** Etiqueta de estado de un movimiento. */
@Composable
fun EstadoChip(estado: String) {
    val (t, f, c) = when (estado) {
        "local" -> Triple("En el celular", Colores.InfoClaro, Colores.Info)
        "pendiente_wms" -> Triple("Cargando en WMS", Colores.InfoClaro, Colores.Info)
        "aplicado" -> Triple("En WMS ✔", Colores.OkClaro, Colores.Ok)
        "esperando_autorizacion" -> Triple("Esperando autorización", Colores.AvisoClaro, Colores.Aviso)
        "error" -> Triple("Error WMS", Colores.ErrorClaro, Colores.Error)
        "rechazado" -> Triple("Rechazado", Colores.ErrorClaro, Colores.Error)
        "anulado" -> Triple("Anulado", Color(0xFFEEF0F2), Colores.Tenue)
        "registrado" -> Triple("Registrado", Colores.OkClaro, Colores.Ok)
        else -> Triple(estado, Color(0xFFEEF0F2), Colores.Tenue)
    }
    Text(t, color = c, fontSize = 12.sp, fontWeight = FontWeight.Bold, modifier = Modifier.clip(RoundedCornerShape(50)).background(f).padding(horizontal = 10.dp, vertical = 3.dp))
}
