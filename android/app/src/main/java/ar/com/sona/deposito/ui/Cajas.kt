package ar.com.sona.deposito.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import ar.com.sona.deposito.data.Api
import ar.com.sona.deposito.data.ItemCola
import ar.com.sona.deposito.data.lindo
import ar.com.sona.deposito.data.num
import ar.com.sona.deposito.data.objetos
import ar.com.sona.deposito.data.txt
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.width
import androidx.compose.ui.text.font.FontFamily
import org.json.JSONObject

/**
 * Reubicar cajas (como la opción 13 de "Chequeos y ajustes" del handheld): se pasan cajas/unidades de un contenedor a otro.
 * 1) contenedor origen  2) cantidad  3) contenedor destino  4) confirmar. Se registra en SONA y se carga en el WMS.
 */
@Composable
fun ReubicarCajasScreen(vm: AppViewModel, inicial: String?) {
    var codOrigen by remember { mutableStateOf(inicial ?: "") }
    var origen by remember { mutableStateOf<JSONObject?>(null) }
    var cantidad by remember { mutableStateOf("") }
    var codDestino by remember { mutableStateOf("") }
    var destino by remember { mutableStateOf<JSONObject?>(null) }
    var cargando by remember { mutableStateOf(false) }
    var ubicDest by remember { mutableStateOf("") }
    var nivelDest by remember { mutableStateOf("") }
    val focoUbicDest = remember { FocusRequester() }
    val focoNivelDest = remember { FocusRequester() }
    val focoCantidad = remember { FocusRequester() }
    val focoDestino = remember { FocusRequester() }
    // contenedores a los que se pueden pasar las cajas (mismo artículo; primero los de la misma posición): se eligen tocando
    var opciones by remember { mutableStateOf<List<JSONObject>?>(null) }

    fun traer(c: String, alTener: (JSONObject) -> Unit) {
        cargando = true
        vm.lanzar(alTerminar = { r: JSONObject ->
            cargando = false
            if (r.optBoolean("encontrado")) alTener(r) else vm.avisar("No se encontró el contenedor $c", true)
        }, alFallar = { cargando = false }) { vm.api.obj("/api/contenedor/${Api.cod(c)}") }
    }
    fun elegirOrigen(c: String) {
        origen = null; destino = null; codDestino = ""; cantidad = ""; opciones = null
        traer(c) { r ->
            origen = r; try { focoCantidad.requestFocus() } catch (_: Exception) {}
            val k = r.optString("contenedor")
            vm.lanzar(alTerminar = { d: JSONObject -> opciones = d.optJSONArray("destinos")?.objetos() ?: emptyList() }, alFallar = { opciones = emptyList() }) {
                try { vm.api.obj("/api/cajas/destinos/${Api.cod(k)}") }
                catch (e: Exception) {
                    // sin la ruta nueva en el servidor: se arma con todos los contenedores del artículo (buscador)
                    val art = r.txt("articulo") ?: throw e
                    val a = vm.api.obj("/api/articulo/${Api.cod(art)}")
                    val lista = (a.optJSONArray("contenedores")?.objetos() ?: emptyList()).filter { !it.optString("contenedor").equals(k, true) && it.optString("contenedor").isNotBlank() }
                        .map { JSONObject().put("contenedor", it.optString("contenedor").uppercase()).put("posicion", it.txt("posicion")).put("cantidad", it.num("cantidad")) }
                    JSONObject().put("destinos", org.json.JSONArray(lista))
                }
            }
        }
    }
    fun elegirDestino(c: String) {
        val o = origen ?: return
        if (c.equals(o.optString("contenedor"), ignoreCase = true)) { vm.avisar("El destino tiene que ser otro contenedor", true); codDestino = ""; return }
        destino = null; ubicDest = ""; nivelDest = ""
        traer(c) { r ->
            destino = r
            // nivel de la posición que informa el sistema (ej. DG01203 → 3); la etiqueta (Ubic.Ctrl) la escanea el operario
            r.txt("posicion")?.filter { it.isDigit() }?.takeIf { it.length >= 4 }?.takeLast(2)?.toIntOrNull()?.let { nivelDest = it.toString() }
            try { focoUbicDest.requestFocus() } catch (_: Exception) {}
        }
    }
    LaunchedEffect(inicial) { if (!inicial.isNullOrBlank()) elegirOrigen(inicial) }

    val o = origen
    val d = destino
    val disponible = o?.num("cantidad")
    val cant = cantidad.toDoubleOrNull()
    val problemas = buildList {
        if (cant != null && cant <= 0) add("La cantidad tiene que ser mayor que cero.")
        if (cant != null && disponible != null && cant > disponible) add("El contenedor origen tiene ${disponible.lindo()}.")
        if (o != null && d != null) {
            val eo = o.txt("empresa"); val ed = d.txt("empresa")
            if (eo != null && ed != null && eo != ed) add("El destino es de ${d.txt("empresaNombre") ?: "otra empresa"}: no se pueden pasar cajas entre empresas.")
        }
    }
    val avisos = buildList {
        if (o != null && d != null && o.txt("articulo") != null && d.txt("articulo") != null && o.txt("articulo") != d.txt("articulo"))
            add("Ojo: el destino tiene otro artículo (${d.txt("articulo")}). El WMS puede rechazarlo.")
        if (o != null && o.has("disponible") && !o.isNull("disponible") && !o.optBoolean("disponible")) add("El contenedor origen está NO disponible.")
    }
    val listo = o != null && d != null && cant != null && problemas.isEmpty() && ubicDest.isNotBlank() && (nivelDest.isNotBlank() || !esUbicCtrl.matches(ubicDest.trim()))

    Escena("Reubicar cajas", vm, subtitulo = "Pasar cajas de un contenedor a otro", pie = {
        BotonPrincipal(
            if (listo) "Pasar ${cant!!.lindo()} de ${o!!.optString("contenedor")} a ${d!!.optString("contenedor")}" else "Completá origen, cantidad, destino y su posición",
            habilitado = listo,
        ) {
            val item = ItemCola.nuevo(
                "reubicar_cajas", o!!.optString("contenedor"), o.txt("descripcion") ?: "",
                destino = d!!.optString("contenedor"), cantidad = cant, nivel = nivelDest.toIntOrNull(), posicionDestino = ubicDest.trim(),
            )
            vm.encolar(listOf(item))
            vm.avisar("✔ Reubicación registrada (${cant!!.lindo()} → ${d.optString("contenedor")}). Se carga en el WMS.")
            // queda lista para la siguiente
            origen = null; destino = null; codOrigen = ""; codDestino = ""; cantidad = ""; opciones = null; ubicDest = ""; nivelDest = ""
        }
    }) {
        Text("1. Contenedor origen", fontWeight = FontWeight.Bold, color = Colores.Marino)
        CampoEscaneo(vm, codOrigen, { codOrigen = it }, "Escaneá el contenedor de donde salen las cajas", alEnviar = { elegirOrigen(it) })
        if (cargando && o == null) Cargando()
        if (o != null) {
            FichaContenedor(vm, o)
            Text("2. Cantidad a pasar", fontWeight = FontWeight.Bold, color = Colores.Marino)
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                CampoNumero(cantidad, { cantidad = it }, "Cantidad" + (disponible?.let { " (tiene ${it.lindo()})" } ?: ""), Modifier.weight(1f), focoCantidad, decimales = true) {
                    try { focoDestino.requestFocus() } catch (_: Exception) {}
                }
                if (disponible != null) BotonChico("Todo") { cantidad = disponible.lindo() }
            }
            Text("3. Contenedor destino", fontWeight = FontWeight.Bold, color = Colores.Marino)
            when {
                opciones == null -> Cargando("Buscando contenedores del mismo artículo…")
                opciones!!.isEmpty() -> Text("No hay otros contenedores de ${o.txt("articulo") ?: "este artículo"} en stock: escaneá el destino.", color = Colores.Tenue, fontSize = 13.sp)
                else -> {
                    Text("Tocá el contenedor que recibe (${opciones!!.size} del mismo artículo) o escaneá otro", color = Colores.Tenue, fontSize = 13.sp)
                    opciones!!.forEach { op ->
                        val k = op.optString("contenedor")
                        val elegido = d?.optString("contenedor") == k
                        Tarjeta(borde = if (elegido) Colores.Naranja else Colores.Linea, fondo = if (elegido) Colores.NaranjaClaro else Colores.Panel,
                            onClick = { codDestino = k; elegirDestino(k) }) {
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Column(Modifier.weight(1f)) {
                                    Text(k, fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold, fontSize = 17.sp, color = Colores.Marino)
                                    Text(listOfNotNull(op.txt("posicion")?.let { "en $it" }, if (op.optBoolean("mismaPosicion")) "misma posición" else null).joinToString(" · "),
                                        color = if (op.optBoolean("mismaPosicion")) Colores.Ok else Colores.Tenue, fontSize = 12.sp)
                                }
                                Spacer(Modifier.width(8.dp))
                                Text(op.num("cantidad")?.lindo() ?: "", fontWeight = FontWeight.Bold, color = Colores.Naranja)
                            }
                        }
                    }
                }
            }
            CampoEscaneo(vm, codDestino, { codDestino = it; destino = null }, "…o escaneá el contenedor que recibe", alEnviar = { elegirDestino(it) }, foco = focoDestino)
            if (cargando && d == null) Cargando()
            if (d != null) {
                FichaContenedor(vm, d)
                Text("4. Posición del contenedor que recibe", fontWeight = FontWeight.Bold, color = Colores.Marino)
                d.txt("posicion")?.let { Text("Según el sistema está en $it", color = Colores.Tenue, fontSize = 13.sp) }
                CampoEscaneo(vm, ubicDest, { ubicDest = it }, "Escaneá la posición (Ubic.Ctrl)", alEnviar = { ubicDest = it; try { focoNivelDest.requestFocus() } catch (_: Exception) {} }, foco = focoUbicDest)
                CampoNumero(nivelDest, { nivelDest = it.take(2) }, "Nivel", Modifier.fillMaxWidth(), focoNivelDest)
            }
            problemas.forEach { Aviso("⛔ $it", TipoAviso.Error) }
            avisos.forEach { Aviso(it, TipoAviso.Aviso) }
            if (listo) Column(Modifier.fillMaxWidth()) {
                Text("Revisá y confirmá abajo.", color = Colores.Tenue, fontSize = 13.sp)
            }
        }
    }
}
