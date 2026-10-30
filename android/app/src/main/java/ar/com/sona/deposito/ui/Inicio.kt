package ar.com.sona.deposito.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Chat
import androidx.compose.material.icons.automirrored.filled.List
import androidx.compose.material.icons.filled.MoveDown
import androidx.compose.material.icons.filled.PanTool
import androidx.compose.material.icons.filled.ReportProblem
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.ShoppingCart
import androidx.compose.material.icons.filled.SwapHoriz
import androidx.compose.material.icons.filled.Checklist
import androidx.compose.material.icons.filled.DeleteForever
import androidx.compose.material.icons.filled.Inventory
import androidx.compose.material.icons.filled.Inventory2
import androidx.compose.material.icons.filled.PhotoCamera
import androidx.compose.material.icons.filled.Exposure
import androidx.compose.material.icons.automirrored.filled.ReceiptLong
import androidx.compose.material.icons.filled.TaskAlt
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.AddBox
import androidx.compose.material.icons.filled.LocalShipping
import androidx.compose.ui.platform.LocalUriHandler
import androidx.compose.material.icons.filled.Report
import androidx.compose.material3.Checkbox
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import ar.com.sona.deposito.data.txt

@Composable
fun LogoSona(tamano: Int = 64) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(tamano.dp).clip(RoundedCornerShape((tamano / 4.5f).dp)).background(Colores.Marino), contentAlignment = Alignment.Center) {
            Text("S", color = Colores.Naranja, fontSize = (tamano * 0.62f).sp, fontWeight = FontWeight.Black)
        }
        Spacer(Modifier.width(12.dp))
        Column {
            Text("SONA", fontSize = (tamano * 0.42f).sp, fontWeight = FontWeight.Black, color = Colores.Marino, letterSpacing = 2.sp)
            Text("Depósito", fontSize = (tamano * 0.22f).sp, color = Colores.Tenue)
        }
    }
}

@Composable
fun LoginScreen(vm: AppViewModel) {
    var usuario by remember { mutableStateOf(vm.ajustes.usuario) }
    var clave by remember { mutableStateOf("") }
    Column(
        Modifier.fillMaxSize().background(Colores.Fondo).statusBarsPadding().imePadding().navigationBarsPadding().verticalScroll(rememberScrollState()).padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        Spacer(Modifier.height(30.dp))
        LogoSona(72)
        Spacer(Modifier.height(10.dp))
        Text("Ingresá con tu usuario y clave del WMS", color = Colores.Tenue)
        if (vm.reconectando) Cargando("Reconectando con el usuario guardado…")
        val colores = OutlinedTextFieldDefaults.colors(focusedBorderColor = Colores.Naranja, focusedLabelColor = Colores.Naranja, unfocusedContainerColor = Colores.Panel, focusedContainerColor = Colores.Panel)
        val focoClave = remember { FocusRequester() }
        fun entrar() { if (usuario.isNotBlank() && clave.isNotBlank() && !vm.ingresando) vm.ingresar(usuario, clave) }
        OutlinedTextField(usuario, { usuario = it.uppercase().trim() }, label = { Text("Usuario") }, singleLine = true, colors = colores,
            keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Characters, imeAction = ImeAction.Next),
            keyboardActions = KeyboardActions(onNext = { try { focoClave.requestFocus() } catch (_: Exception) {} }),
            modifier = Modifier.fillMaxWidth().alApretarEnter { try { focoClave.requestFocus() } catch (_: Exception) {} })
        OutlinedTextField(clave, { clave = it }, label = { Text("Clave") }, singleLine = true, colors = colores, visualTransformation = PasswordVisualTransformation(),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password, imeAction = ImeAction.Go),
            keyboardActions = KeyboardActions(onGo = { entrar() }, onDone = { entrar() }),
            trailingIcon = { BotonAceptarCampo(usuario.isNotBlank() && clave.isNotBlank()) { entrar() } },
            modifier = Modifier.fillMaxWidth().focusRequester(focoClave).alApretarEnter { entrar() })
        vm.errorLogin?.let { Aviso(it, TipoAviso.Error) }
        BotonPrincipal("Ingresar", habilitado = usuario.isNotBlank() && clave.isNotBlank(), cargando = vm.ingresando) { vm.ingresar(usuario, clave) }
        Text("El celular queda conectado: si se corta la sesión, vuelve a entrar solo con este usuario.", color = Colores.Tenue, fontSize = 12.sp)
        TextButton(onClick = { vm.ir(Pantalla.Ajustes) }) { Text("Configurar servidor", color = Colores.Marino2) }
    }
}

@Composable
fun MenuScreen(vm: AppViewModel) {
    val items by vm.cola.items.collectAsState()
    val enVivo by vm.eventos.enVivo.collectAsState()
    val conectado by vm.cola.conectado.collectAsState()
    val pendientes = items.count { !it.enviado }
    val abrirUrl = LocalUriHandler.current
    val esperando = items.count { it.estado == "esperando_autorizacion" }
    var autorizaciones by remember { mutableIntStateOf(0) }
    val ops = vm.opciones()
    LaunchedEffect(Unit) { vm.leerSinLeer() }
    LaunchedEffect(vm.rol, vm.permisos) {
        if ("autorizaciones" in ops) try { autorizaciones = vm.api.lista("/api/autorizaciones?estado=pendiente").length() } catch (_: Exception) {}
    }
    Column(Modifier.fillMaxSize().background(Colores.Fondo)) {
        Column(Modifier.fillMaxWidth().background(Colores.Marino).statusBarsPadding().padding(start = 18.dp, end = 6.dp, top = 10.dp, bottom = 14.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text("S", color = Colores.Naranja, fontSize = 30.sp, fontWeight = FontWeight.Black)
                Spacer(Modifier.width(8.dp))
                Column(Modifier.weight(1f)) {
                    Text("SONA Depósito", color = Color.White, fontSize = 20.sp, fontWeight = FontWeight.Bold)
                    Text("${vm.ajustes.nombre.ifBlank { vm.ajustes.usuario }} · ${nombreRol(vm.rol)} · v$VERSION", color = Color.White.copy(alpha = 0.8f), fontSize = 13.sp)
                }
                IconButton(onClick = { vm.ir(Pantalla.Ajustes) }) { Icon(Icons.Filled.Settings, "Ajustes", tint = Color.White) }
            }
            Spacer(Modifier.height(8.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(14.dp)) {
                Indicador(if (conectado) "Servidor OK" else "Sin señal", conectado)
                Indicador(if (enVivo) "En vivo" else "Reconectando", enVivo)
                if (pendientes > 0) Text("$pendientes en cola", color = Colores.Naranja, fontSize = 12.sp, fontWeight = FontWeight.Bold)
            }
        }
        Box(Modifier.height(3.dp).fillMaxWidth().background(Colores.Naranja))
        Column(Modifier.weight(1f).verticalScroll(rememberScrollState()).navigationBarsPadding().padding(14.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            vm.actualizacion?.takeIf { !it.optBoolean("obligatoria") }?.let { ar.com.sona.deposito.AvisoActualizacion(vm, it, obligatoria = false) }
            if (esperando > 0) Aviso("$esperando movimiento(s) esperando autorización del analista", TipoAviso.Aviso)
            val botones = ops.mapNotNull { o ->
                when (o) {
                    "consultar" -> Triple(Icons.Filled.Search, "Consultar", "Posición, contenedor o artículo") to { vm.ir(Pantalla.Consultar()) }
                    "transferir" -> Triple(Icons.Filled.SwapHoriz, "Transferir", "Contenedor, posición completa o reubicar cajas") to { vm.ir(Pantalla.Transferir) }
                    "cajas" -> Triple(Icons.Filled.Inventory, "Reubicar cajas", "Pasar cajas de un contenedor a otro") to { vm.ir(Pantalla.ReubicarCajas()) }
                    "guardar" -> Triple(Icons.Filled.MoveDown, "Guardar", "Recepciones pendientes con ubicación sugerida") to { vm.ir(Pantalla.Guardar) }
                    "conteo" -> Triple(Icons.Filled.Checklist, "Conteo", "Conteo ciego por posición") to { vm.ir(Pantalla.ConteoTareas) }
                    "estado" -> Triple(Icons.Filled.ReportProblem, "Cambiar estado", "Dañado / no disponible con foto") to { vm.ir(Pantalla.CambiarEstado()) }
                    "autorizaciones" -> Triple(Icons.Filled.PanTool, "Autorizaciones", "Aprobar destinos en zona de otro cliente") to { vm.ir(Pantalla.Autorizaciones) }
                    "reconocer" -> Triple(Icons.Filled.PhotoCamera, "Reconocer por foto", "Sacá una foto y te dice qué artículo es (admin)") to { vm.ir(Pantalla.Reconocer) }
                    "ajustes" -> Triple(Icons.Filled.Exposure, "Ajustar", "Sumar o restar unidades de un contenedor") to { vm.ir(Pantalla.AjusteContenedor()) }
                    "ingreso" -> Triple(Icons.Filled.LocalShipping, "Ingreso", "Recepción: LES, fotos, contenedores y WMS") to {
                        val srv = vm.ajustes.servidorActivo.ifBlank { vm.ajustes.servidorLocal }
                        runCatching { abrirUrl.openUri("$srv/ingresos#t=" + android.net.Uri.encode(vm.ajustes.token)) }; Unit
                    }
                    "crear" -> Triple(Icons.Filled.AddBox, "Crear contenedor", "Número de LES + carga inicial en el WMS") to { vm.ir(Pantalla.CrearContenedor) }
                    "nn" -> Triple(Icons.Filled.Report, "Enviar a NN", "Transferir contenedores a la posición NN") to { vm.ir(Pantalla.EnviarNn()) }
                    "eliminar" -> Triple(Icons.Filled.DeleteForever, "Eliminar contenedor", "Ajusta el contenedor a 0 en el WMS (admin)") to { vm.ir(Pantalla.EliminarContenedor()) }
                    "mensajes" -> Triple(Icons.AutoMirrored.Filled.Chat, "Mensajes", "Chat con el panel / analistas") to { vm.ir(Pantalla.Mensajes) }
                    "movs" -> Triple(Icons.AutoMirrored.Filled.List, "Mis movimientos", "Lo que hice hoy y lo que está en cola") to { vm.ir(Pantalla.MisMovimientos) }
                    "bultos" -> Triple(Icons.Filled.Inventory2, "Control de bulto", "Medidas de los bultos de la solicitud (LES)") to { vm.ir(Pantalla.ControlBulto) }
                    "picking" -> Triple(Icons.Filled.ShoppingCart, "Picking", "Preparación de pedidos (consolidado / unidades)") to { vm.ir(Pantalla.Picking) }
                    "pedido" -> Triple(Icons.AutoMirrored.Filled.ReceiptLong, "Consultar pedido", "Datos, bultos y fotos de los artículos del pedido") to { vm.ir(Pantalla.ConsultarPedido()) }
                    "control" -> Triple(Icons.Filled.TaskAlt, "Control de pedido", "Controlar la solicitud: cantidad, bultos y foto") to { vm.ir(Pantalla.ControlPedido()) }
                    else -> null
                }
            }
            botones.chunked(2).forEach { fila ->
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    fila.forEach { (t, accion) ->
                        val (ico, tit, det) = t
                        val color = when (tit) { "Transferir", "Guardar", "Picking", "Crear contenedor", "Ingreso" -> Colores.Naranja; "Ajustar", "Consultar pedido" -> Colores.Marino2; "Control de pedido" -> Colores.Ok; "Enviar a NN" -> Colores.Aviso; "Reubicar cajas", "Mensajes" -> Colores.Marino2; "Cambiar estado", "Eliminar contenedor" -> Colores.Error; else -> Colores.Marino }
                        val ins = when (tit) { "Mis movimientos" -> pendientes; "Autorizaciones" -> autorizaciones; "Mensajes" -> vm.mensajesSinLeer; else -> 0 }
                        BotonMenu(ico, tit, det, Modifier.weight(1f).height(150.dp), color, ins, accion)
                    }
                    if (fila.size == 1) Spacer(Modifier.weight(1f))
                }
            }
        }
    }
}

@Composable
private fun Indicador(t: String, ok: Boolean) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(8.dp).clip(RoundedCornerShape(50)).background(if (ok) Color(0xFF5BD17E) else Color(0xFFFF8A80)))
        Spacer(Modifier.width(5.dp))
        Text(t, color = Color.White.copy(alpha = 0.85f), fontSize = 12.sp)
    }
}

fun nombreRol(r: String) = when (r) {
    "maquinista" -> "Maquinista"; "conteo" -> "Asistente de conteo"; "picker" -> "Picker"; "supervisor" -> "Supervisor"; "analista" -> "Analista"; "admin" -> "Administrador"; else -> r
}

@Composable
fun AjustesScreen(vm: AppViewModel) {
    var local by remember { mutableStateOf(vm.ajustes.servidorLocal) }
    var externo by remember { mutableStateOf(vm.ajustes.servidorExterno) }
    var olvidar by remember { mutableStateOf(false) }
    var prueba by remember { mutableStateOf<String?>(null) }
    val logueado = vm.ajustes.token.isNotBlank()
    Escena("Ajustes", vm) {
        Tarjeta {
            Text("Servidor SONA Depósito", fontWeight = FontWeight.Bold)
            val colores = OutlinedTextFieldDefaults.colors(focusedBorderColor = Colores.Naranja, focusedLabelColor = Colores.Naranja)
            OutlinedTextField(local, { local = it.trim() }, label = { Text("Red local (Wi-Fi del depósito)") }, singleLine = true, colors = colores,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri, imeAction = ImeAction.Next), modifier = Modifier.fillMaxWidth())
            OutlinedTextField(externo, { externo = it.trim() }, label = { Text("IP externa (datos móviles)") }, singleLine = true, colors = colores,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri, imeAction = ImeAction.Done), modifier = Modifier.fillMaxWidth())
            Text("Se prueba primero la que anduvo la última vez; si no responde, la otra.", color = Colores.Tenue, fontSize = 12.sp)
            BotonSecundario("Guardar y probar conexión") {
                vm.ajustes.servidorLocal = local; vm.ajustes.servidorExterno = externo; vm.ajustes.servidorActivo = ""
                prueba = "Probando…"
                vm.lanzar(alTerminar = { j: org.json.JSONObject -> prueba = "✔ Conectado a ${vm.api.base} · servidor v${j.optString("version")} · ${j.optInt("pendientesWms")} pendientes WMS" },
                    alFallar = { prueba = "✕ $it" }) { vm.api.obj("/api/estado") }
            }
            prueba?.let { Text(it, color = if (it.startsWith("✔")) Colores.Ok else if (it.startsWith("✕")) Colores.Error else Colores.Tenue) }
        }
        if (logueado) Tarjeta {
            Text("Sesión", fontWeight = FontWeight.Bold)
            Text("${vm.ajustes.usuario} · ${nombreRol(vm.rol)}")
            Row(verticalAlignment = Alignment.CenterVertically) {
                Checkbox(olvidar, { olvidar = it })
                Text("Olvidar la clave en este celular (no reconecta solo)")
            }
            BotonSecundario("Cerrar sesión") { vm.salir(olvidar) }
        }
        Text("SONA Depósito $VERSION · dispositivo ${vm.ajustes.dispositivo.take(8)}", color = Colores.Tenue, fontSize = 12.sp, textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth())
    }
}
