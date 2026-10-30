package ar.com.sona.deposito.ar

import android.Manifest
import android.app.Activity
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Typeface
import android.opengl.GLES11Ext
import android.opengl.GLES20
import android.opengl.GLSurfaceView
import android.opengl.Matrix
import android.os.Bundle
import android.view.Gravity
import android.view.Surface
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView
import com.google.ar.core.Anchor
import com.google.ar.core.ArCoreApk
import com.google.ar.core.Config
import com.google.ar.core.Coordinates2d
import com.google.ar.core.DepthPoint
import com.google.ar.core.Frame
import com.google.ar.core.Plane
import com.google.ar.core.Point
import com.google.ar.core.Pose
import com.google.ar.core.Session
import com.google.ar.core.TrackingState
import com.google.ar.core.exceptions.CameraNotAvailableException
import com.google.ar.core.exceptions.UnavailableException
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.nio.FloatBuffer
import javax.microedition.khronos.egl.EGLConfig
import javax.microedition.khronos.opengles.GL10
import kotlin.math.abs
import kotlin.math.roundToInt
import kotlin.math.sqrt

/**
 * Medir un bulto con realidad aumentada (ARCore). Es opcional: si el celular no tiene ARCore se cargan las medidas a mano.
 * Se apunta la mira al borde del bulto y se toca "Marcar punto" dos veces por medida: ancho, profundidad y alto
 * (para el alto: primer punto en el piso junto al bulto, segundo arriba del bulto). Devuelve las tres medidas en cm.
 */
class MedirActivity : Activity(), GLSurfaceView.Renderer {

    companion object {
        const val ALTO = "alto"; const val ANCHO = "ancho"; const val PROF = "prof"

        /** true si vale la pena mostrar el botón (el celular soporta ARCore, aunque falte instalarlo). */
        fun disponible(ctx: Context): Boolean = try {
            val a = ArCoreApk.getInstance().checkAvailability(ctx)
            a.isSupported || a.isTransient
        } catch (_: Exception) { false }
    }

    private val pasos = listOf("Ancho", "Profundidad", "Alto")
    private val ayudas = listOf(
        "Apuntá la mira a una esquina de arriba del bulto y marcá; después la otra esquina del mismo lado (ancho).",
        "Ahora el otro lado: una esquina de arriba y la siguiente (profundidad).",
        "Alto: primer punto en el piso pegado al bulto, segundo punto arriba del bulto, justo encima.",
    )

    private lateinit var vistaGl: GLSurfaceView
    private lateinit var capa: Capa
    private lateinit var titulo: TextView
    private lateinit var ayuda: TextView
    private lateinit var botonMarcar: Button
    private lateinit var botonDeshacer: Button
    private lateinit var botonListo: Button

    private var sesion: Session? = null
    private var pedirInstalacion = true
    private val fondo = FondoCamara()
    private var ancho = 1
    private var alto = 1

    // estado compartido entre el hilo de OpenGL y la pantalla
    private val anclas = mutableListOf<Anchor>()          // de a pares: [0,1]=ancho, [2,3]=prof, [4,5]=alto
    @Volatile private var marcarPedido = false
    @Volatile private var deshacerPedido = false
    @Volatile private var hayApunte = false
    private val medidas = DoubleArray(3) { -1.0 }        // cm

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val raiz = FrameLayout(this)
        vistaGl = GLSurfaceView(this).apply {
            preserveEGLContextOnPause = true
            setEGLContextClientVersion(2)
            setEGLConfigChooser(8, 8, 8, 8, 16, 0)
            setRenderer(this@MedirActivity)
            renderMode = GLSurfaceView.RENDERMODE_CONTINUOUSLY
        }
        raiz.addView(vistaGl, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
        capa = Capa(this)
        raiz.addView(capa, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))

        val arriba = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(Color.argb(170, 11, 42, 66)); setPadding(32, 48, 32, 24)
        }
        titulo = TextView(this).apply { setTextColor(Color.WHITE); textSize = 20f; typeface = Typeface.DEFAULT_BOLD }
        ayuda = TextView(this).apply { setTextColor(Color.WHITE); textSize = 14f }
        arriba.addView(titulo); arriba.addView(ayuda)
        raiz.addView(arriba, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.TOP))

        val abajo = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER
            setBackgroundColor(Color.argb(170, 11, 42, 66)); setPadding(16, 16, 16, 40)
        }
        fun boton(t: String, color: Int, accion: () -> Unit) = Button(this).apply {
            text = t; setTextColor(Color.WHITE); setBackgroundColor(color); textSize = 16f; setAllCaps(false)
            setOnClickListener { accion() }
        }
        botonDeshacer = boton("Deshacer", Color.rgb(93, 107, 121)) { deshacerPedido = true }
        botonMarcar = boton("Marcar punto", Color.rgb(244, 121, 32)) { marcarPedido = true }
        botonListo = boton("Usar medidas", Color.rgb(30, 142, 62)) { devolver() }
        val lp = LinearLayout.LayoutParams(0, 150, 1f).apply { setMargins(10, 0, 10, 0) }
        abajo.addView(botonDeshacer, lp); abajo.addView(botonMarcar, LinearLayout.LayoutParams(0, 150, 1.4f).apply { setMargins(10, 0, 10, 0) })
        abajo.addView(botonListo, lp)
        raiz.addView(abajo, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.BOTTOM))
        setContentView(raiz)
        actualizarTextos()
    }

    override fun onResume() {
        super.onResume()
        if (checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(arrayOf(Manifest.permission.CAMERA), 1); return
        }
        if (sesion == null) {
            try {
                when (ArCoreApk.getInstance().requestInstall(this, pedirInstalacion)) {
                    ArCoreApk.InstallStatus.INSTALL_REQUESTED -> { pedirInstalacion = false; return }
                    else -> {}
                }
                val s = Session(this)
                val cfg = Config(s).apply {
                    focusMode = Config.FocusMode.AUTO
                    planeFindingMode = Config.PlaneFindingMode.HORIZONTAL_AND_VERTICAL
                    updateMode = Config.UpdateMode.LATEST_CAMERA_IMAGE
                    if (s.isDepthModeSupported(Config.DepthMode.AUTOMATIC)) depthMode = Config.DepthMode.AUTOMATIC
                }
                s.configure(cfg)
                sesion = s
            } catch (e: UnavailableException) { terminarConError("Este celular no tiene realidad aumentada (ARCore): cargá las medidas a mano."); return }
            catch (e: Exception) { terminarConError("No se pudo abrir la realidad aumentada: ${e.message}"); return }
        }
        try { sesion?.resume() } catch (e: CameraNotAvailableException) { terminarConError("La cámara está ocupada. Cerrá otras apps y probá de nuevo."); return }
        vistaGl.onResume()
    }

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (grantResults.firstOrNull() != PackageManager.PERMISSION_GRANTED) terminarConError("Hace falta permiso de cámara para medir.")
    }

    override fun onPause() {
        super.onPause()
        if (sesion != null) { vistaGl.onPause(); sesion?.pause() }
    }

    override fun onDestroy() {
        sesion?.close(); sesion = null
        super.onDestroy()
    }

    private fun terminarConError(msj: String) {
        setResult(RESULT_CANCELED, Intent().putExtra("error", msj)); finish()
    }

    private fun devolver() {
        if (medidas.any { it <= 0 }) return
        setResult(RESULT_OK, Intent().putExtra(ANCHO, medidas[0]).putExtra(PROF, medidas[1]).putExtra(ALTO, medidas[2]))
        finish()
    }

    private fun paso() = minOf(anclas.size / 2, 2)

    private fun actualizarTextos() = runOnUiThread {
        val completas = medidas.all { it > 0 }
        val resumen = pasos.indices.joinToString("   ") { i -> "${pasos[i]}: " + if (medidas[i] > 0) "${medidas[i].roundToInt()} cm" else "—" }
        if (completas) {
            titulo.text = "Listo · $resumen"
            ayuda.text = "Tocá \"Usar medidas\" para cargarlas en el bulto, o \"Deshacer\" para repetir la última."
        } else {
            val p = paso()
            titulo.text = "${pasos[p]} · punto ${anclas.size % 2 + 1} de 2"
            ayuda.text = ayudas[p] + "\n" + resumen
        }
        botonListo.isEnabled = completas; botonListo.alpha = if (completas) 1f else 0.4f
        botonMarcar.isEnabled = !completas; botonMarcar.alpha = if (completas) 0.4f else 1f
    }

    // ------------------------------------------------------------------ OpenGL
    override fun onSurfaceCreated(gl: GL10?, config: EGLConfig?) {
        GLES20.glClearColor(0f, 0f, 0f, 1f)
        fondo.crear()
    }

    override fun onSurfaceChanged(gl: GL10?, width: Int, height: Int) {
        ancho = width; alto = height
        GLES20.glViewport(0, 0, width, height)
        sesion?.setDisplayGeometry(Surface.ROTATION_0, width, height)
    }

    override fun onDrawFrame(gl: GL10?) {
        GLES20.glClear(GLES20.GL_COLOR_BUFFER_BIT or GLES20.GL_DEPTH_BUFFER_BIT)
        val s = sesion ?: return
        try {
            s.setCameraTextureName(fondo.textura)
            s.setDisplayGeometry(Surface.ROTATION_0, ancho, alto)
            val frame = s.update()
            fondo.dibujar(frame)
            val cam = frame.camera
            if (cam.trackingState != TrackingState.TRACKING) {
                capa.actualizar(emptyList(), null, false, "Mové el celular despacio para que reconozca el lugar…"); return
            }
            if (deshacerPedido) {
                deshacerPedido = false
                anclas.removeLastOrNull()?.detach()
                recalcular(); actualizarTextos()
            }
            val apunte = apuntar(frame)
            hayApunte = apunte != null
            if (marcarPedido) {
                marcarPedido = false
                if (apunte != null && anclas.size < 6) {
                    anclas.add(s.createAnchor(apunte))
                    recalcular(); actualizarTextos()
                }
            }
            // proyección de los puntos a la pantalla para dibujarlos
            val proj = FloatArray(16); val vista = FloatArray(16); val mvp = FloatArray(16)
            cam.getProjectionMatrix(proj, 0, 0.05f, 100f)
            cam.getViewMatrix(vista, 0)
            Matrix.multiplyMM(mvp, 0, proj, 0, vista, 0)
            fun pantalla(p: Pose): FloatArray? {
                val v = floatArrayOf(p.tx(), p.ty(), p.tz(), 1f); val r = FloatArray(4)
                Matrix.multiplyMV(r, 0, mvp, 0, v, 0)
                if (r[3] <= 0f) return null
                return floatArrayOf((r[0] / r[3] + 1f) / 2f * ancho, (1f - r[1] / r[3]) / 2f * alto)
            }
            val puntos = anclas.map { pantalla(it.pose) }
            // medida en vivo: del primer punto del par al lugar donde apunta la mira
            var vivo: String? = null
            if (anclas.size % 2 == 1 && apunte != null && anclas.size < 6) {
                val a = anclas.last().pose
                val cm = distanciaCm(a, apunte, paso())
                vivo = "${cm.roundToInt()} cm"
            }
            capa.actualizar(puntos, vivo, apunte != null, null)
        } catch (e: Exception) {
            // un cuadro perdido no corta la medición
        }
    }

    /** Lo que hay en el centro de la pantalla (mira): plano detectado, punto de profundidad o punto con orientación. */
    private fun apuntar(frame: Frame): Pose? {
        for (h in frame.hitTest(ancho / 2f, alto / 2f)) {
            val t = h.trackable
            val ok = (t is Plane && t.isPoseInPolygon(h.hitPose)) || t is DepthPoint ||
                (t is Point && t.orientationMode == Point.OrientationMode.ESTIMATED_SURFACE_NORMAL)
            if (ok) return h.hitPose
        }
        return null
    }

    /** Ancho y profundidad: distancia entre los dos puntos. Alto: diferencia vertical (el eje Y de ARCore sigue la gravedad). */
    private fun distanciaCm(a: Pose, b: Pose, paso: Int): Double {
        val dx = (a.tx() - b.tx()).toDouble(); val dy = (a.ty() - b.ty()).toDouble(); val dz = (a.tz() - b.tz()).toDouble()
        return if (paso == 2) abs(dy) * 100 else sqrt(dx * dx + dy * dy + dz * dz) * 100
    }

    private fun recalcular() {
        for (i in 0 until 3) {
            medidas[i] = if (anclas.size >= i * 2 + 2) distanciaCm(anclas[i * 2].pose, anclas[i * 2 + 1].pose, i) else -1.0
        }
    }

    // ------------------------------------------------------------------ capa con la mira, los puntos y la medida
    private class Capa(ctx: Context) : View(ctx) {
        private val pMira = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE; strokeWidth = 5f }
        private val pPunto = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.rgb(244, 121, 32) }
        private val pLinea = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.WHITE; strokeWidth = 6f }
        private val pTexto = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.WHITE; textSize = 56f; typeface = Typeface.DEFAULT_BOLD; setShadowLayer(6f, 0f, 0f, Color.BLACK) }
        @Volatile private var puntos: List<FloatArray?> = emptyList()
        @Volatile private var vivo: String? = null
        @Volatile private var apunta = false
        @Volatile private var aviso: String? = null

        fun actualizar(p: List<FloatArray?>, v: String?, a: Boolean, av: String?) { puntos = p; vivo = v; apunta = a; aviso = av; postInvalidate() }

        override fun onDraw(c: Canvas) {
            super.onDraw(c)
            val cx = width / 2f; val cy = height / 2f
            pMira.color = if (apunta) Color.rgb(91, 209, 126) else Color.WHITE
            c.drawCircle(cx, cy, 28f, pMira)
            c.drawLine(cx - 50f, cy, cx - 14f, cy, pMira); c.drawLine(cx + 14f, cy, cx + 50f, cy, pMira)
            c.drawLine(cx, cy - 50f, cx, cy - 14f, pMira); c.drawLine(cx, cy + 14f, cx, cy + 50f, pMira)
            val ps = puntos
            for (i in ps.indices) {
                val p = ps[i] ?: continue
                c.drawCircle(p[0], p[1], 16f, pPunto)
                if (i % 2 == 1) ps[i - 1]?.let { q -> c.drawLine(q[0], q[1], p[0], p[1], pLinea) }
            }
            if (ps.size % 2 == 1) ps.last()?.let { q -> c.drawLine(q[0], q[1], cx, cy, pLinea) }
            vivo?.let { c.drawText(it, cx + 40f, cy - 40f, pTexto) }
            aviso?.let { c.drawText(it, 30f, cy + 120f, pTexto.apply { textSize = 40f }) ; pTexto.textSize = 56f }
        }
    }
}

/** Dibuja la imagen de la cámara de ARCore de fondo (textura externa OES), como el ejemplo oficial de ARCore. */
private class FondoCamara {
    var textura = -1; private set
    private var programa = 0
    private var aPos = 0
    private var aTex = 0
    private val quad: FloatBuffer = buffer(floatArrayOf(-1f, -1f, 1f, -1f, -1f, 1f, 1f, 1f))
    private val coordTex: FloatBuffer = buffer(FloatArray(8))

    private fun buffer(v: FloatArray): FloatBuffer =
        ByteBuffer.allocateDirect(v.size * 4).order(ByteOrder.nativeOrder()).asFloatBuffer().apply { put(v); position(0) }

    fun crear() {
        val t = IntArray(1)
        GLES20.glGenTextures(1, t, 0); textura = t[0]
        val destino = GLES11Ext.GL_TEXTURE_EXTERNAL_OES
        GLES20.glBindTexture(destino, textura)
        GLES20.glTexParameteri(destino, GLES20.GL_TEXTURE_WRAP_S, GLES20.GL_CLAMP_TO_EDGE)
        GLES20.glTexParameteri(destino, GLES20.GL_TEXTURE_WRAP_T, GLES20.GL_CLAMP_TO_EDGE)
        GLES20.glTexParameteri(destino, GLES20.GL_TEXTURE_MIN_FILTER, GLES20.GL_LINEAR)
        GLES20.glTexParameteri(destino, GLES20.GL_TEXTURE_MAG_FILTER, GLES20.GL_LINEAR)
        val vs = """
            attribute vec4 a_Position;
            attribute vec2 a_TexCoord;
            varying vec2 v_TexCoord;
            void main() { gl_Position = a_Position; v_TexCoord = a_TexCoord; }
        """.trimIndent()
        val fs = """
            #extension GL_OES_EGL_image_external : require
            precision mediump float;
            varying vec2 v_TexCoord;
            uniform samplerExternalOES sTexture;
            void main() { gl_FragColor = texture2D(sTexture, v_TexCoord); }
        """.trimIndent()
        programa = GLES20.glCreateProgram()
        GLES20.glAttachShader(programa, sombreador(GLES20.GL_VERTEX_SHADER, vs))
        GLES20.glAttachShader(programa, sombreador(GLES20.GL_FRAGMENT_SHADER, fs))
        GLES20.glLinkProgram(programa)
        aPos = GLES20.glGetAttribLocation(programa, "a_Position")
        aTex = GLES20.glGetAttribLocation(programa, "a_TexCoord")
    }

    private fun sombreador(tipo: Int, codigo: String): Int {
        val s = GLES20.glCreateShader(tipo)
        GLES20.glShaderSource(s, codigo)
        GLES20.glCompileShader(s)
        return s
    }

    fun dibujar(frame: Frame) {
        if (frame.hasDisplayGeometryChanged()) {
            quad.position(0); coordTex.position(0)
            frame.transformCoordinates2d(Coordinates2d.OPENGL_NORMALIZED_DEVICE_COORDINATES, quad, Coordinates2d.TEXTURE_NORMALIZED, coordTex)
        }
        if (frame.timestamp == 0L) return
        quad.position(0); coordTex.position(0)
        GLES20.glDisable(GLES20.GL_DEPTH_TEST)
        GLES20.glDepthMask(false)
        GLES20.glActiveTexture(GLES20.GL_TEXTURE0)
        GLES20.glBindTexture(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, textura)
        GLES20.glUseProgram(programa)
        GLES20.glVertexAttribPointer(aPos, 2, GLES20.GL_FLOAT, false, 0, quad)
        GLES20.glVertexAttribPointer(aTex, 2, GLES20.GL_FLOAT, false, 0, coordTex)
        GLES20.glEnableVertexAttribArray(aPos)
        GLES20.glEnableVertexAttribArray(aTex)
        GLES20.glDrawArrays(GLES20.GL_TRIANGLE_STRIP, 0, 4)
        GLES20.glDisableVertexAttribArray(aPos)
        GLES20.glDisableVertexAttribArray(aTex)
        GLES20.glDepthMask(true)
        GLES20.glEnable(GLES20.GL_DEPTH_TEST)
    }
}
