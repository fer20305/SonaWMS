package ar.com.sona.deposito.ui

import android.annotation.SuppressLint
import android.media.AudioManager
import android.media.ToneGenerator
import android.util.Log
import android.util.Size
import android.view.MotionEvent
import android.view.ScaleGestureDetector
import androidx.camera.core.Camera
import androidx.camera.core.CameraSelector
import androidx.camera.core.FocusMeteringAction
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.Preview
import androidx.camera.core.SurfaceOrientedMeteringPointFactory
import androidx.camera.core.resolutionselector.ResolutionSelector
import androidx.camera.core.resolutionselector.ResolutionStrategy
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.google.mlkit.vision.barcode.BarcodeScanner
import com.google.mlkit.vision.barcode.BarcodeScannerOptions
import com.google.mlkit.vision.barcode.BarcodeScanning
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.common.InputImage
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong
import kotlin.math.abs
import kotlin.math.hypot
import kotlin.math.max

/** Formatos que se usan en el depósito (QR de posiciones, códigos de barra de contenedores y artículos). */
private val FORMATOS = intArrayOf(
    Barcode.FORMAT_CODE_128, Barcode.FORMAT_CODE_39, Barcode.FORMAT_CODE_93, Barcode.FORMAT_EAN_13, Barcode.FORMAT_EAN_8,
    Barcode.FORMAT_UPC_A, Barcode.FORMAT_UPC_E, Barcode.FORMAT_ITF, Barcode.FORMAT_CODABAR, Barcode.FORMAT_DATA_MATRIX,
)

/**
 * Vista de cámara que lee QR y códigos de barra con más precisión que la lectura simple:
 *  - Solo acepta el código que está DENTRO del recuadro de mira ([ventanaPx], centrado). Así no se leen las etiquetas vecinas.
 *  - Si hay varios códigos dentro, toma el más cercano al centro.
 *  - Los códigos de barra (1D) tienen que leerse igual en dos cuadros seguidos antes de aceptarse (evita lecturas erróneas).
 *    El QR y el DataMatrix tienen corrección de errores y se aceptan a la primera.
 *  - Imagen de análisis en alta resolución, enfoque al centro, tocar para enfocar y zoom (pellizcar o [zoom]).
 * Después de leer un código espera 2,5 s antes de aceptar otro igual.
 */
@SuppressLint("UnsafeOptInUsageError", "ClickableViewAccessibility")
@androidx.annotation.OptIn(androidx.camera.core.ExperimentalGetImage::class)
@Composable
fun VistaCamara(
    modifier: Modifier = Modifier,
    linterna: Boolean,
    activa: Boolean,
    ventanaPx: Pair<Int, Int>? = null,
    zoom: Float = 1f,
    alCambiarZoom: (Float) -> Unit = {},
    alVerCodigoFuera: () -> Unit = {},
    alLeer: (String) -> Unit,
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val callback = rememberUpdatedState(alLeer)
    val fueraCb = rememberUpdatedState(alVerCodigoFuera)
    val zoomCb = rememberUpdatedState(alCambiarZoom)
    val activaState = rememberUpdatedState(activa)
    val ventanaState = rememberUpdatedState(ventanaPx)

    val previewView = remember {
        PreviewView(context).apply {
            scaleType = PreviewView.ScaleType.FILL_CENTER
            implementationMode = PreviewView.ImplementationMode.COMPATIBLE
        }
    }
    val ejecutor = remember { Executors.newSingleThreadExecutor() }
    val lector: BarcodeScanner = remember {
        BarcodeScanning.getClient(BarcodeScannerOptions.Builder().setBarcodeFormats(Barcode.FORMAT_QR_CODE, *FORMATOS).build())
    }
    val pitido = remember { try { ToneGenerator(AudioManager.STREAM_NOTIFICATION, 80) } catch (_: Exception) { null } }
    var camaraInstancia by remember { mutableStateOf<Camera?>(null) }
    val ultimo = remember { arrayOf("") }
    val ultimoT = remember { AtomicLong(0) }
    val candidato = remember { arrayOf("") }
    val candidatoT = remember { AtomicLong(0) }
    val ocupado = remember { AtomicBoolean(false) }
    val avisoFueraT = remember { AtomicLong(0) }

    DisposableEffect(lifecycleOwner) {
        val futuro = ProcessCameraProvider.getInstance(context)
        var proveedor: ProcessCameraProvider? = null
        futuro.addListener({
            try {
                val p = futuro.get()
                proveedor = p
                val preview = Preview.Builder().build().also { it.setSurfaceProvider(previewView.surfaceProvider) }
                val resolucion = ResolutionSelector.Builder()
                    .setResolutionStrategy(ResolutionStrategy(Size(1920, 1080), ResolutionStrategy.FALLBACK_RULE_CLOSEST_LOWER_THEN_HIGHER))
                    .build()
                val analisis = ImageAnalysis.Builder()
                    .setResolutionSelector(resolucion)
                    .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                    .build()
                analisis.setAnalyzer(ejecutor) { proxy ->
                    val media = proxy.image
                    if (media == null || !activaState.value || ocupado.get()) {
                        proxy.close(); return@setAnalyzer
                    }
                    ocupado.set(true)
                    val rot = proxy.imageInfo.rotationDegrees
                    // tamaño de la imagen "derecha" (las coordenadas de ML Kit vienen ya rotadas)
                    val anchoImg = if (rot == 90 || rot == 270) proxy.height else proxy.width
                    val altoImg = if (rot == 90 || rot == 270) proxy.width else proxy.height
                    val img = InputImage.fromMediaImage(media, rot)
                    lector.process(img)
                        .addOnSuccessListener { codigos ->
                            val elegido = elegir(codigos, anchoImg, altoImg, previewView.width, previewView.height, ventanaState.value)
                            if (elegido == null) {
                                if (codigos.any { !it.rawValue.isNullOrBlank() }) {
                                    val ahora = System.currentTimeMillis()
                                    if (ahora - avisoFueraT.get() > 1500) { avisoFueraT.set(ahora); fueraCb.value() }
                                }
                                return@addOnSuccessListener
                            }
                            val valor = elegido.rawValue!!.trim()
                            val ahora = System.currentTimeMillis()
                            val es2D = elegido.format == Barcode.FORMAT_QR_CODE || elegido.format == Barcode.FORMAT_DATA_MATRIX
                            if (!es2D) {
                                // 1D: confirmar con un segundo cuadro igual (dentro de 1,2 s)
                                if (valor.length < 3) return@addOnSuccessListener
                                if (candidato[0] != valor || ahora - candidatoT.get() > 1200) {
                                    candidato[0] = valor; candidatoT.set(ahora); return@addOnSuccessListener
                                }
                            }
                            if (valor != ultimo[0] || ahora - ultimoT.get() > 2500) {
                                ultimo[0] = valor
                                ultimoT.set(ahora)
                                candidato[0] = ""
                                try { pitido?.startTone(ToneGenerator.TONE_PROP_BEEP, 120) } catch (_: Exception) {}
                                ContextCompat.getMainExecutor(context).execute { callback.value(valor) }
                            }
                        }
                        .addOnCompleteListener {
                            ocupado.set(false)
                            proxy.close()
                        }
                }
                p.unbindAll()
                val cam = p.bindToLifecycle(lifecycleOwner, CameraSelector.DEFAULT_BACK_CAMERA, preview, analisis)
                camaraInstancia = cam
                enfocarCentro(cam)
            } catch (e: Exception) {
                Log.e("VistaCamara", "No se pudo abrir la cámara", e)
            }
        }, ContextCompat.getMainExecutor(context))

        onDispose {
            try { proveedor?.unbindAll() } catch (_: Exception) {}
        }
    }

    DisposableEffect(Unit) {
        onDispose {
            lector.close()
            ejecutor.shutdown()
            try { pitido?.release() } catch (_: Exception) {}
        }
    }

    // Tocar para enfocar ahí; pellizcar para hacer zoom
    LaunchedEffect(camaraInstancia) {
        val cam = camaraInstancia ?: return@LaunchedEffect
        val escala = ScaleGestureDetector(context, object : ScaleGestureDetector.SimpleOnScaleGestureListener() {
            override fun onScale(d: ScaleGestureDetector): Boolean {
                val actual = cam.cameraInfo.zoomState.value?.zoomRatio ?: 1f
                val max = cam.cameraInfo.zoomState.value?.maxZoomRatio ?: 1f
                val nuevo = (actual * d.scaleFactor).coerceIn(1f, max)
                zoomCb.value(nuevo)
                return true
            }
        })
        previewView.setOnTouchListener { v, ev ->
            escala.onTouchEvent(ev)
            if (ev.action == MotionEvent.ACTION_UP && !escala.isInProgress) {
                val punto = previewView.meteringPointFactory.createPoint(ev.x, ev.y)
                try {
                    cam.cameraControl.startFocusAndMetering(
                        FocusMeteringAction.Builder(punto, FocusMeteringAction.FLAG_AF or FocusMeteringAction.FLAG_AE).setAutoCancelDuration(4, TimeUnit.SECONDS).build()
                    )
                } catch (_: Exception) {}
                v.performClick()
            }
            true
        }
    }

    LaunchedEffect(linterna, camaraInstancia) {
        camaraInstancia?.let { c -> if (c.cameraInfo.hasFlashUnit()) c.cameraControl.enableTorch(linterna) }
    }
    LaunchedEffect(zoom, camaraInstancia) {
        camaraInstancia?.let { c ->
            val max = c.cameraInfo.zoomState.value?.maxZoomRatio ?: 1f
            try { c.cameraControl.setZoomRatio(zoom.coerceIn(1f, max)) } catch (_: Exception) {}
        }
    }

    AndroidView(factory = { previewView }, modifier = modifier)
}

/** Enfoque y exposición al centro (donde está la mira); se renueva solo. */
private fun enfocarCentro(cam: Camera) {
    try {
        val punto = SurfaceOrientedMeteringPointFactory(1f, 1f).createPoint(0.5f, 0.5f)
        cam.cameraControl.startFocusAndMetering(
            FocusMeteringAction.Builder(punto, FocusMeteringAction.FLAG_AF or FocusMeteringAction.FLAG_AE).setAutoCancelDuration(3, TimeUnit.SECONDS).build()
        )
    } catch (_: Exception) {}
}

/**
 * De los códigos detectados, el que está dentro de la mira y más cerca del centro.
 * La vista previa llena la pantalla recortando los bordes (FILL_CENTER), así que se pasa cada centro a coordenadas de pantalla.
 */
private fun elegir(codigos: List<Barcode>, anchoImg: Int, altoImg: Int, anchoVista: Int, altoVista: Int, ventana: Pair<Int, Int>?): Barcode? {
    val validos = codigos.filter { !it.rawValue.isNullOrBlank() && it.boundingBox != null }
    if (validos.isEmpty() || anchoImg <= 0 || altoImg <= 0) return null
    if (anchoVista <= 0 || altoVista <= 0) return validos.first()
    val escala = max(anchoVista.toFloat() / anchoImg, altoVista.toFloat() / altoImg)
    val dx = (anchoVista - anchoImg * escala) / 2f
    val dy = (altoVista - altoImg * escala) / 2f
    val cx = anchoVista / 2f
    val cy = altoVista / 2f
    // mira con 15 % de tolerancia; sin mira se acepta el tercio central
    val (vw, vh) = ventana ?: Pair(anchoVista * 2 / 3, altoVista / 3)
    val mitadAncho = vw * 0.575f
    val mitadAlto = vh * 0.575f
    return validos
        .map { b ->
            val r = b.boundingBox!!
            val x = r.exactCenterX() * escala + dx
            val y = r.exactCenterY() * escala + dy
            b to Pair(x, y)
        }
        .filter { (_, p) -> abs(p.first - cx) <= mitadAncho && abs(p.second - cy) <= mitadAlto }
        .minByOrNull { (_, p) -> hypot(p.first - cx, p.second - cy) }
        ?.first
}
