package ar.com.sona.deposito

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.viewModels
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Snackbar
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.graphics.Color
import androidx.compose.foundation.layout.fillMaxWidth
import ar.com.sona.deposito.ui.AjustesScreen
import ar.com.sona.deposito.ui.AppViewModel
import ar.com.sona.deposito.ui.AutorizacionesScreen
import ar.com.sona.deposito.ui.CambiarEstadoScreen
import ar.com.sona.deposito.ui.Colores
import ar.com.sona.deposito.ui.ConsultarScreen
import ar.com.sona.deposito.ui.ConteoPosicionScreen
import ar.com.sona.deposito.ui.ConteoTareaScreen
import ar.com.sona.deposito.ui.ConteoTareasScreen
import ar.com.sona.deposito.ui.EsperandoScreen
import ar.com.sona.deposito.ui.GuardarScreen
import ar.com.sona.deposito.ui.LoginScreen
import ar.com.sona.deposito.ui.EliminarContenedorScreen
import ar.com.sona.deposito.ui.AjusteContenedorScreen
import ar.com.sona.deposito.ui.CrearContenedorScreen
import ar.com.sona.deposito.ui.EnviarNnScreen
import ar.com.sona.deposito.ui.MensajesScreen
import ar.com.sona.deposito.ui.ReconocerScreen
import ar.com.sona.deposito.ui.IaPendientesScreen
import ar.com.sona.deposito.ui.PickingScreen
import ar.com.sona.deposito.ui.ControlBultoScreen
import ar.com.sona.deposito.ui.ConsultarPedidoScreen
import ar.com.sona.deposito.ui.ControlPedidoScreen
import ar.com.sona.deposito.ui.TransfMultiScreen
import ar.com.sona.deposito.ui.MenuScreen
import ar.com.sona.deposito.ui.MisMovimientosScreen
import ar.com.sona.deposito.ui.Pantalla
import ar.com.sona.deposito.ui.ReubicarCajasScreen
import ar.com.sona.deposito.ui.TemaSona
import ar.com.sona.deposito.ui.TransfContenedorScreen
import ar.com.sona.deposito.ui.TransfPosicionScreen
import ar.com.sona.deposito.ui.TransfVariosScreen
import ar.com.sona.deposito.ui.TransferirScreen
import kotlinx.coroutines.delay

class MainActivity : ComponentActivity() {
    private val vm: AppViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent { TemaSona { App(vm) } }
    }

    override fun onResume() {
        super.onResume()
        vm.cola.empujar()      // al volver a la app, reintenta lo que quedó en cola
        vm.revisarActualizacion()   // ¿hay una versión nueva publicada en el servidor?
        vm.refrescarPermisos()      // por si un administrador le cambió los permisos (ZUSR)
    }
}

@Composable
fun App(vm: AppViewModel) {
    BackHandler(enabled = vm.pantalla != Pantalla.Menu && vm.pantalla != Pantalla.Login) { vm.atras() }
    Box(Modifier.fillMaxSize()) {
        when (val p = vm.pantalla) {
            Pantalla.Login -> LoginScreen(vm)
            Pantalla.Menu -> MenuScreen(vm)
            is Pantalla.Consultar -> ConsultarScreen(vm, p.codigo)
            Pantalla.Transferir -> TransferirScreen(vm)
            is Pantalla.TransfContenedor -> TransfContenedorScreen(vm, p.contenedor)
            is Pantalla.TransfPosicion -> TransfPosicionScreen(vm, p.posicion, p.nivel)
            Pantalla.TransfVarios -> TransfVariosScreen(vm)
            Pantalla.Guardar -> GuardarScreen(vm)
            is Pantalla.CambiarEstado -> CambiarEstadoScreen(vm, p.contenedor)
            is Pantalla.ReubicarCajas -> ReubicarCajasScreen(vm, p.origen)
            Pantalla.Mensajes -> MensajesScreen(vm)
            Pantalla.TransfMulti -> TransfMultiScreen(vm)
            is Pantalla.EliminarContenedor -> EliminarContenedorScreen(vm, p.contenedor)
            is Pantalla.AjusteContenedor -> AjusteContenedorScreen(vm, p.contenedor)
            is Pantalla.CrearContenedor -> CrearContenedorScreen(vm)
            is Pantalla.EnviarNn -> EnviarNnScreen(vm, p.contenedores)
            Pantalla.Reconocer -> ReconocerScreen(vm)
            Pantalla.IaPendientes -> IaPendientesScreen(vm)
            Pantalla.Picking -> PickingScreen(vm)
            Pantalla.ControlBulto -> ControlBultoScreen(vm)
            is Pantalla.ConsultarPedido -> ConsultarPedidoScreen(vm, p.nro)
            is Pantalla.ControlPedido -> ControlPedidoScreen(vm, p.nro)
            Pantalla.ConteoTareas -> ConteoTareasScreen(vm)
            is Pantalla.ConteoTarea -> ConteoTareaScreen(vm, p.id, p.nombre)
            is Pantalla.ConteoPosicion -> ConteoPosicionScreen(vm, p.tareaId, p.posicion, p.modo)
            Pantalla.MisMovimientos -> MisMovimientosScreen(vm)
            Pantalla.Autorizaciones -> AutorizacionesScreen(vm)
            Pantalla.Ajustes -> AjustesScreen(vm)
            is Pantalla.Esperando -> EsperandoScreen(vm, p.grupo, p.titulo)
        }
        vm.resultadoWms?.let { r ->
            androidx.compose.material3.AlertDialog(
                onDismissRequest = { vm.resultadoWms = null },
                title = { Text(r.titulo, color = if (r.ok) Colores.Ok else Colores.Error, fontWeight = androidx.compose.ui.text.font.FontWeight.Bold) },
                text = {
                    androidx.compose.foundation.layout.Column(Modifier.heightIn(max = 360.dp).verticalScroll(androidx.compose.foundation.rememberScrollState())) {
                        r.lineas.forEach { (ok, t) -> Text((if (ok) "✔ " else "✕ ") + t, color = if (ok) Colores.Texto else Colores.Error, modifier = Modifier.padding(vertical = 4.dp)) }
                    }
                },
                confirmButton = { androidx.compose.material3.TextButton(onClick = { vm.resultadoWms = null }) { Text("Aceptar") } },
            )
        }
        vm.alertaOp?.let { a -> AlertaOpAsignada(vm, a) }
        // actualización obligatoria: no deja seguir hasta actualizar
        vm.actualizacion?.takeIf { it.optBoolean("obligatoria") && vm.pantalla != Pantalla.Login }?.let { AvisoActualizacion(vm, it, obligatoria = true) }
        vm.mensaje?.let { (texto, error) ->
            LaunchedEffect(texto) { delay(if (error) 6000 else 3500); vm.mensaje = null }
            Snackbar(
                modifier = Modifier.align(Alignment.BottomCenter).navigationBarsPadding().padding(12.dp),
                containerColor = if (error) Colores.Error else Colores.Marino,
            ) { Text(texto) }
        }
    }
}

/** Cartel de OP nueva: lo dispara el panel de producción al asignar (o reasignar) una OP a este picker. */
@Composable
fun AlertaOpAsignada(vm: AppViewModel, a: org.json.JSONObject) {
    val nivel = a.optString("prioridadNivel").ifBlank { "normal" }
    val colPrio = when (nivel) { "urgente" -> Colores.Error; "alta" -> Colores.Naranja; "baja" -> Colores.Marino2; else -> Colores.Tenue }
    androidx.compose.ui.window.Dialog(onDismissRequest = { vm.alertaOp = null }) {
        androidx.compose.material3.Card(
            shape = androidx.compose.foundation.shape.RoundedCornerShape(18.dp),
            colors = androidx.compose.material3.CardDefaults.cardColors(containerColor = Color.White),
        ) {
            androidx.compose.foundation.layout.Column(Modifier.padding(16.dp), verticalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(8.dp)) {
                androidx.compose.foundation.Image(
                    painter = androidx.compose.ui.res.painterResource(ar.com.sona.deposito.R.drawable.alerta_op),
                    contentDescription = "Nueva OP asignada",
                    modifier = Modifier.fillMaxWidth().heightIn(max = 220.dp),
                    contentScale = androidx.compose.ui.layout.ContentScale.Fit,
                )
                Text(if (a.optString("accion") == "reasignada") "¡Te pasaron una OP!" else "¡Nueva OP asignada!",
                    fontWeight = androidx.compose.ui.text.font.FontWeight.Black, fontSize = 22.sp, color = Colores.Marino)
                Text("OP ${a.optString("op")}" + (a.optString("solicitud").takeIf { it.isNotBlank() && it != "null" }?.let { " · $it" } ?: ""),
                    fontWeight = androidx.compose.ui.text.font.FontWeight.Bold, fontSize = 18.sp)
                listOf(a.optString("empresaNombre"), a.optString("lugar")).filter { it.isNotBlank() && it != "null" }.joinToString(" · ").takeIf { it.isNotBlank() }?.let { Text(it) }
                val det = listOfNotNull(a.optInt("articulos").takeIf { it > 0 }?.let { "$it art." }, a.optDouble("unidades").takeIf { !it.isNaN() && it > 0 }?.let { "${it.toLong()} u." },
                    a.optString("viaje").takeIf { it.isNotBlank() && it != "null" }?.let { "viaje $it" }).joinToString(" · ")
                if (det.isNotBlank()) Text(det, color = Colores.Tenue)
                if (nivel != "normal") Text("Prioridad ${nivel.uppercase()}", color = colPrio, fontWeight = androidx.compose.ui.text.font.FontWeight.Black, fontSize = 18.sp)
                a.optString("por").takeIf { it.isNotBlank() && it != "null" }?.let { Text("Asignó: $it", color = Colores.Tenue, fontSize = 12.sp) }
                androidx.compose.foundation.layout.Row(horizontalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(8.dp)) {
                    androidx.compose.material3.TextButton(onClick = { vm.alertaOp = null }, modifier = Modifier.weight(1f)) { Text("Después") }
                    androidx.compose.material3.Button(
                        onClick = { vm.alertaOp = null; if (vm.pantalla != Pantalla.Picking) vm.ir(Pantalla.Picking) },
                        modifier = Modifier.weight(1f),
                        colors = androidx.compose.material3.ButtonDefaults.buttonColors(containerColor = Colores.Naranja),
                    ) { Text("Ir a Picking", fontWeight = androidx.compose.ui.text.font.FontWeight.Bold) }
                }
            }
        }
    }
}

/** Cartel de versión nueva de la app (en el menú, o bloqueante si es obligatoria). */
@Composable
fun AvisoActualizacion(vm: AppViewModel, v: org.json.JSONObject, obligatoria: Boolean) {
    val ctx = androidx.compose.ui.platform.LocalContext.current
    val contenido: @Composable () -> Unit = {
        androidx.compose.foundation.layout.Column(verticalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(6.dp)) {
            Text("Nueva versión ${v.optString("version")} de SONA", fontWeight = androidx.compose.ui.text.font.FontWeight.Bold, fontSize = 16.sp, color = Colores.Marino)
            Text("Tenés la ${ar.com.sona.deposito.ui.VERSION}." + (if (obligatoria) " Hay que actualizar para seguir." else ""), fontSize = 13.sp, color = Colores.Tenue)
            v.optString("notas").takeIf { it.isNotBlank() && it != "null" }?.let { Text(it, fontSize = 13.sp) }
            val p = vm.bajandoApp
            if (p != null) {
                androidx.compose.material3.LinearProgressIndicator(progress = { p }, modifier = Modifier.fillMaxWidth(), color = Colores.Naranja, trackColor = Colores.Linea)
                Text("Bajando… ${(p * 100).toInt()}%", fontSize = 12.sp, color = Colores.Tenue)
            } else androidx.compose.material3.Button(
                onClick = { vm.instalarActualizacion(ctx) }, modifier = Modifier.fillMaxWidth(),
                colors = androidx.compose.material3.ButtonDefaults.buttonColors(containerColor = Colores.Naranja),
            ) { Text("Actualizar ahora", fontWeight = androidx.compose.ui.text.font.FontWeight.Bold) }
        }
    }
    if (obligatoria) androidx.compose.ui.window.Dialog(onDismissRequest = {}, properties = androidx.compose.ui.window.DialogProperties(dismissOnBackPress = false, dismissOnClickOutside = false)) {
        androidx.compose.material3.Card(colors = androidx.compose.material3.CardDefaults.cardColors(containerColor = Color.White)) { androidx.compose.foundation.layout.Box(Modifier.padding(18.dp)) { contenido() } }
    } else androidx.compose.material3.Card(
        modifier = Modifier.fillMaxWidth(),
        colors = androidx.compose.material3.CardDefaults.cardColors(containerColor = Colores.NaranjaClaro),
    ) { androidx.compose.foundation.layout.Box(Modifier.padding(14.dp)) { contenido() } }
}
