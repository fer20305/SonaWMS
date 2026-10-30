// Prueba de punta a punta del servidor C# contra el WMS de prueba (páginas reales capturadas del handheld).
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Sona;
using Sona.Pruebas;

Environment.SetEnvironmentVariable("SONA_PRUEBAS", "1");
var carpeta = Path.Combine(Path.GetTempPath(), "sona_prueba_" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(carpeta);
File.WriteAllText(Path.Combine(carpeta, "config.json"), """
{ "puerto": 18080,
  "wms": { "local": "http://127.0.0.1:18090/sglwms_prod/", "externo": "http://127.0.0.1:18091/sglwms_prod/", "timeoutSegundos": 10 },
  "les": { "buscador": "http://127.0.0.1:18090/Api/Buscador", "url": "http://127.0.0.1:18090/les/", "account": "http://127.0.0.1:18090/les/", "usuario": "SLEDESM6", "clave": "pw" },
  "etms": { "url": "http://127.0.0.1:18090/etms/" },
  "admins": ["SLEDESM6"], "sincronizarLayoutHoras": 0 }
""");
var fake = new FakeWms();
var appFake = fake.Crear(18090);
await appFake.StartAsync();
var cfg = Configuracion.Cargar(carpeta);
var app = Motor.Crear(cfg);
await app.StartAsync();

var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:18080"), Timeout = TimeSpan.FromSeconds(120) };
var fallas = 0;
void Paso(string n) => Console.WriteLine("✔ " + n);
void Falla(string n, object? det = null) { fallas++; Console.WriteLine("✘ " + n + (det != null ? "  →  " + det : "")); }
void Ver(bool cond, string n, object? det = null) { if (cond) Paso(n); else Falla(n, det); }

async Task<(int Estado, JsonNode? J)> Api(string metodo, string ruta, object? cuerpo = null)
{
    var m = new HttpRequestMessage(new HttpMethod(metodo), ruta);
    if (cuerpo != null) m.Content = new StringContent(cuerpo is JsonNode n ? n.ToJsonString() : System.Text.Json.JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json");
    var r = await http.SendAsync(m);
    var t = await r.Content.ReadAsStringAsync();
    JsonNode? j = null; try { j = JsonNode.Parse(t); } catch { }
    return ((int)r.StatusCode, j);
}
async Task<JsonNode?> Esperar(string id, params string[] estados)
{
    for (var i = 0; i < 40; i++)
    {
        var (_, j) = await Api("GET", "/api/movimientos/" + id);
        if (estados.Contains(j?["estado"]?.ToString())) return j;
        await Task.Delay(500);
    }
    return null;
}
string S(JsonNode? n) => n?.ToString() ?? "";

try
{
    var (e, j) = await Api("POST", "/api/login", new { usuario = "SLEDESM6", clave = "mal" });
    Ver(e == 401, "clave incorrecta rechazada", e);
    (e, j) = await Api("POST", "/api/login", new { usuario = "SLEDESM6", clave = "pw", dispositivo = "TEST1", modelo = "Prueba", panel = true });
    Ver(e == 200 && S(j?["usuario"]?["rol"]) == "admin", "login admin", j);
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", S(j?["token"]));

    (e, j) = await Api("POST", "/api/layout/sincronizar", new { caras = new[] { "DC", "DD", "IG", "FI" } });
    JsonNode? lay = null;
    for (var i = 0; i < 30; i++) { (_, lay) = await Api("GET", "/api/layout/estado"); if (lay?["fin"] != null) break; await Task.Delay(500); }
    Ver((lay?["posiciones"]?.GetValue<int>() ?? 0) >= 16, $"layout sincronizado: {S(lay?["posiciones"])} posiciones", lay);

    (e, j) = await Api("GET", "/api/consulta/96456");
    var n2 = j?["niveles"]?.AsArray().FirstOrDefault(n => n!["nivel"]!.GetValue<int>() == 2);
    Ver(S(j?["tipo"]) == "posicion" && S(n2?["contenedores"]?[0]) == "PC-199417" && S(n2?["detalle"]?[0]?["articulo"]) == "BIM-123", "consulta Ubic.Ctrl recorre niveles + buscador", j);
    (e, j) = await Api("GET", "/api/consulta/DC0402");
    Ver(S(j?["tipo"]) == "posicion" && j?["niveles"]?.AsArray().Count == 1, "consulta por código FI1502", j);
    (e, j) = await Api("GET", "/api/consulta/PC-199417");
    Ver(S(j?["tipo"]) == "contenedor" && S(j?["contenedor"]?["empresa"]) == "38", "empresa sale del cliente (Mburucuyá=38)", j);
    (e, j) = await Api("GET", "/api/consulta/I-10");
    Ver(S(j?["contenedor"]?["empresa"]) == "6", "Notro Bazar → empresa 6", j);

    (e, j) = await Api("POST", "/api/validar", new { contenedores = new[] { "I-11" }, destino = "50001", nivel = 1 });
    Ver(j?["ok"]?.GetValue<bool>() == false && j?["bloqueos"]?.AsArray().Any(b => S(b).Contains("llena") && S(b).Contains("profundidad 4")) == true, "destino lleno según el WMS rechaza con aviso", j);
    (e, j) = await Api("POST", "/api/validar", new { contenedores = new[] { "I-11" }, destino = "IG0101" });
    Ver(j?["bloqueos"]?.AsArray().Count == 0 && S(j?["ocupacion"]?["capacidad"]) == "20" && S(j?["ocupacion"]?["libres"]) == "16", "profundidad real del WMS web (20) aunque el handheld muestre 4 huecos", j);
    (e, j) = await Api("POST", "/api/validar", new { contenedores = new[] { "PC-199417" }, destino = "DD0203" });
    Ver(S(j?["autorizacion"]).Length > 0, "zona de otro cliente pide autorización", j);
    (e, j) = await Api("POST", "/api/validar", new { contenedores = new[] { "I-11" }, destino = "DD0203" });
    Ver(j?["ok"]?.GetValue<bool>() == true, "destino válido", j);

    (e, j) = await Api("POST", "/api/sugerencia", new { contenedores = new[] { "I-11" } });
    var sug = j?["sugerencias"]?.AsArray();
    Ver(S(sug?.FirstOrDefault()?["codigo"]) == "DD0201" && S(sug?.FirstOrDefault()?["regla"]) == "1", "sugiere junto al mismo artículo", j);
    Ver(sug?.All(s => S(s!["codigo"]) != "DD0101") == true, "nunca sugiere posición llena");

    var id1 = Guid.NewGuid().ToString();
    (e, j) = await Api("POST", "/api/movimientos", new { movimientos = new[] { new { id = id1, tipo = "transferencia", contenedor = "I-11", destino = "DD0201" } } });
    Ver(S(j?["resultados"]?[0]?["estado"]) == "pendiente_wms", "transferencia registrada", j);
    var m = await Esperar(id1, "aplicado", "error");
    Ver(S(m?["estado"]) == "aplicado" && S(m?["respuesta_wms"]).Contains("REUBICADO"), "transferencia cargada en el WMS: " + S(m?["respuesta_wms"]), m);
    (e, j) = await Api("POST", "/api/movimientos", new { movimientos = new[] { new { id = id1, tipo = "transferencia", contenedor = "I-11", destino = "DD0201" } } });
    Ver(S(j?["resultados"]?[0]?["estado"]) == "aplicado", "reenvío idempotente", j);

    (e, j) = await Api("POST", "/api/validar", new { contenedores = new[] { "I-13" }, destino = "DD0201" });
    Ver(j?["bloqueos"]?.AsArray().Count == 0, "posición multiproducto en el WMS (MEZCLA=S) acepta otro artículo", j);
    (e, j) = await Api("GET", "/api/consulta/50002");
    Ver(S(j?["tipo"]) == "posicion" && (j?["niveles"]?.AsArray().Count ?? 0) >= 4, $"consulta sin nivel muestra todos los niveles ({j?["niveles"]?.AsArray().Count})", j);

    var grupo = Guid.NewGuid().ToString(); var id2 = Guid.NewGuid().ToString();
    (e, j) = await Api("POST", "/api/movimientos", new { movimientos = new[] { new { id = id2, grupo, tipo = "transferencia", contenedor = "PC-199417", destino = "DD0203" } } });
    var aut = S(j?["resultados"]?[0]?["autorizacion"]);
    Ver(S(j?["resultados"]?[0]?["estado"]) == "esperando_autorizacion" && aut.Length > 0, "queda esperando autorización", j);
    (e, j) = await Api("POST", $"/api/autorizaciones/{aut}", new { aprobar = true });
    m = await Esperar(id2, "aplicado", "error");
    Ver(S(m?["estado"]) == "aplicado", "analista aprueba → se carga en el WMS", m);

    var id3 = Guid.NewGuid().ToString();
    (e, j) = await Api("POST", "/api/movimientos", new { movimientos = new[] { new { id = id3, tipo = "cambio_estado", contenedor = "I-10", disponible = false, motivo = "ROTURA" } } });
    Ver(S(j?["resultados"]?[0]?["estado"]) == "rechazado", "rotura sin foto rechazada", j);
    var form = new MultipartFormDataContent { { new ByteArrayContent(new byte[] { 0xff, 0xd8, 0xff, 0xd9 }) { Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") } }, "foto", "f.jpg" } };
    var rf = await http.PostAsync("/api/fotos", form);
    var foto = S(JsonNode.Parse(await rf.Content.ReadAsStringAsync())?["id"]);
    Ver(foto.Length > 0, "foto subida");
    var id4 = Guid.NewGuid().ToString();
    (e, j) = await Api("POST", "/api/movimientos", new { movimientos = new[] { new { id = id4, tipo = "cambio_estado", contenedor = "I-10", disponible = false, motivo = "ROTURA", fotos = new[] { foto } } } });
    m = await Esperar(id4, "aplicado", "error");
    Ver(S(m?["estado"]) == "aplicado", "cambio de estado en el WMS: " + S(m?["respuesta_wms"]), m);

    var id4b = Guid.NewGuid().ToString();
    (e, j) = await Api("POST", "/api/movimientos", new { movimientos = new[] { new { id = id4b, tipo = "cambio_estado", contenedor = "I-10", disponible = true } } });
    m = await Esperar(id4b, "aplicado", "error");
    Ver(S(m?["estado"]) == "aplicado" && fake.Log.Count(l => l.Contains("hcambcal") && l.Contains("EENTER.") && l.Contains("I-10")) == 2, "pasar a disponible con un solo ENTER: " + S(m?["respuesta_wms"]), m);

    var idc = Guid.NewGuid().ToString();
    (e, j) = await Api("POST", "/api/movimientos", new { movimientos = new[] { new { id = idc, tipo = "reubicar_cajas", contenedor = "I-10", destino = "I-11", cantidad = 5 } } });
    Ver(S(j?["resultados"]?[0]?["estado"]) == "pendiente_wms", "reubicar cajas registrado", j);
    m = await Esperar(idc, "aplicado", "error");
    Ver(S(m?["estado"]) == "aplicado" && S(m?["respuesta_wms"]).Contains("CAJAS REUBICADAS EN: I-11"), "reubicar cajas cargado en el WMS: " + S(m?["respuesta_wms"]), m);
    // 2.13: la posición del contenedor que recibe la escanea el operario (Ubic.Ctrl + nivel) como en el handheld
    var dI11 = fake.Log.LastOrDefault(l => l.StartsWith("reubicar cajas destino I-11"));
    var idcM = Guid.NewGuid().ToString();
    (e, j) = await Api("POST", "/api/movimientos", new { movimientos = new[] { new { id = idcM, tipo = "reubicar_cajas", contenedor = "I-10", destino = "I-11", cantidad = 1.0, posicion_destino = "50004", nivel = 1 } } });
    m = await Esperar(idcM, "aplicado", "error");
    Ver(S(m?["estado"]) == "error" && S(m?["respuesta_wms"]).Contains("UBICACIÓN DE DESTINO ES INCORRECTA"), "reubicar cajas: posición de destino equivocada la rechaza el WMS: " + S(m?["respuesta_wms"]), new { m, dI11 });
    var ubicOk = System.Text.RegularExpressions.Regex.Match(dI11 ?? "", @"ubic=(\d+) niv=(\d+)");
    var idcB = Guid.NewGuid().ToString();
    (e, j) = await Api("POST", "/api/movimientos", new { movimientos = new[] { new { id = idcB, tipo = "reubicar_cajas", contenedor = "I-10", destino = "I-11", cantidad = 1.0, posicion_destino = ubicOk.Groups[1].Value, nivel = int.Parse(ubicOk.Groups[2].Value) } } });
    m = await Esperar(idcB, "aplicado", "error");
    Ver(S(m?["estado"]) == "aplicado", "reubicar cajas: con la posición escaneada del destino lo acepta: " + S(m?["respuesta_wms"]), m);
    var idcV = Guid.NewGuid().ToString();   // se devuelve la unidad para no cambiar las pruebas que siguen
    (e, j) = await Api("POST", "/api/movimientos", new { movimientos = new[] { new { id = idcV, tipo = "reubicar_cajas", contenedor = "I-11", destino = "I-10", cantidad = 1.0, posicion_destino = "50002", nivel = 1 } } });
    m = await Esperar(idcV, "aplicado", "error");
    Ver(S(m?["estado"]) == "aplicado", "reubicar cajas: vuelta a I-10 en DD-002-01: " + S(m?["respuesta_wms"]), m);
    var idc2 = Guid.NewGuid().ToString();
    (e, j) = await Api("POST", "/api/movimientos", new { movimientos = new[] { new { id = idc2, tipo = "reubicar_cajas", contenedor = "I-10", destino = "PC-199417", cantidad = 1 } } });
    Ver(S(j?["resultados"]?[0]?["estado"]) == "rechazado", "no deja pasar cajas entre empresas", j);

    var id5 = Guid.NewGuid().ToString();
    (e, j) = await Api("POST", "/api/movimientos", new { movimientos = new[] { new { id = id5, tipo = "guardado", contenedor = "I-177719", destino = "DD0202" } } });
    m = await Esperar(id5, "aplicado", "error");
    Ver(S(m?["estado"]) == "aplicado", "guardado en el WMS (sugerida WMS " + S(m?["detalle"]?["sugerida_wms"]) + ")", m);

    (e, j) = await Api("POST", "/api/conteo/tareas", new { nombre = "Prueba", posiciones = new[] { "DD0201" } });
    var tarea = j?["id"]?.GetValue<long>();
    (e, j) = await Api("GET", "/api/conteo/posicion/DD0201");
    var esperados = j?["contenedores"]?.AsArray().Select(c => S(c!["contenedor"])).ToArray() ?? Array.Empty<string>();
    Ver(esperados.Length >= 2 && j?["contenedores"]?[0]?["cantidad"] == null, "conteo ciego no muestra cantidad", j);
    (e, j) = await Api("POST", "/api/conteo/lineas", new { tarea_id = tarea, posicion = "DD0201", esperados,
        lineas = new object[] { new { contenedor = "I-10", encontrado = true, cantidad = 95 }, new { contenedor = "I-11", encontrado = true, cantidad = 50 } } });
    Ver(j?["revisar"]?.GetValue<bool>() == true, "conteo cargado, marca revisar", j);
    (e, j) = await Api("GET", "/api/conteo/diferencias?tarea=" + tarea);
    Ver(j?.AsArray().Count == 1 && S(j[0]?["novedad"]) == "diferencia", "diferencia detectada para el analista", j);
    var lineaDif = j?[0]?["id"]?.GetValue<long>() ?? 0;
    (e, j) = await Api("POST", "/api/conteo/reconteo", new { lineas = new[] { lineaDif }, tipo = "posicion" });
    Ver(e == 200 && j?.AsArray().Count == 1 && j[0]?["posiciones"]?.AsArray().Any(x => S(x) == "DD0201") == true, "reconteo de la posición con diferencia", j);
    (e, j) = await Api("POST", "/api/conteo/reconteo", new { lineas = new[] { lineaDif }, tipo = "articulo" });
    Ver(e == 200 && S(j?[0]?["modo"]) == "articulo" && S(j?[0]?["articulo"]) == "PG-AR1", "conteo del artículo en todas sus posiciones desde la diferencia", j);
    (e, j) = await Api("GET", "/api/conteo/diferencias?tarea=" + tarea + "&revision=recontar");
    Ver(j?.AsArray().Count == 1, "la línea queda \"a recontar\"", j);
    (e, j) = await Api("POST", "/api/contenedores/info", new { contenedores = new[] { "I-12", "NOEXISTE-1" } });
    Ver(e == 200 && j?[0]?["encontrado"]?.GetValue<bool>() == true && j?[1]?["encontrado"]?.GetValue<bool>() == false, "datos de un lote de contenedores (para NN)", j);
    (e, j) = await Api("POST", "/api/conteo/lineas", new { posicion = "DC0402", esperados = new[] { "PC-199417" },
        lineas = new object[] { new { contenedor = "PC-199417", encontrado = true, cantidad = 1200 } } });
    Ver(e == 200 && j?["revisar"]?.GetValue<bool>() == false, "conteo suelto sin diferencias", j);
    (e, j) = await Api("GET", "/api/conteo/diferencias?todas=1&tarea=suelto");
    Ver(j?.AsArray().Any(l => S(l!["contenedor"]) == "PC-199417" && S(l["novedad"]) == "ok") == true, "el panel ve también los conteos OK y sueltos", j);

    // posición vacía: se confirma y queda contada
    (e, j) = await Api("POST", "/api/conteo/tareas", new { nombre = "Vacías", posiciones = new[] { "DD0103" }, modo = "ciego" });
    var tVacia = j?["id"]?.GetValue<long>();
    (e, j) = await Api("GET", $"/api/conteo/posicion/DD0103?tarea={tVacia}");
    Ver(j?["vacia"]?.GetValue<bool>() == true, "posición vacía detectada para contar", j);
    (e, j) = await Api("POST", "/api/conteo/lineas", new { tarea_id = tVacia, posicion = "DD0103", esperados = Array.Empty<string>(), lineas = Array.Empty<object>() });
    (e, j) = await Api("GET", $"/api/conteo/tareas/{tVacia}");
    Ver(j?["detalle"]?[0]?["contada"]?.GetValue<bool>() == true, "posición vacía confirmada queda contada (no vuelve a pendiente)", j);
    // modo confirmación: ve la cantidad del sistema
    // 2.14: la tarea con posiciones en formato del buscador (DD00103 = columna de 3 dígitos) queda contada al contar DD0103
    (e, j) = await Api("POST", "/api/conteo/tareas", new { nombre = "Formato", posiciones = new[] { "DD00103", "DD-002-01" }, modo = "ciego" });
    var tFmt = j?["id"]?.GetValue<long>();
    (e, j) = await Api("POST", "/api/conteo/lineas", new { tarea_id = tFmt, posicion = "DD0103", esperados = Array.Empty<string>(), lineas = Array.Empty<object>() });
    (e, j) = await Api("GET", $"/api/conteo/tareas/{tFmt}");
    var detFmt = j?["detalle"]?.AsArray();
    (e, var jl) = await Api("GET", "/api/conteo/tareas");
    Ver(detFmt?.Count == 2 && detFmt.Any(x => S(x!["posicion"]) == "DD0103" && x["contada"]!.GetValue<bool>()) && detFmt.Any(x => S(x!["posicion"]) == "DD0201" && !x["contada"]!.GetValue<bool>())
        && jl?.AsArray().Any(t => S(t!["id"]) == S(tFmt) && S(t["contadas"]) == "1") == true,
        "conteo: la posición queda contada aunque la tarea tenga otro formato de código", $"{j} {jl}");
    (e, j) = await Api("POST", "/api/conteo/tareas", new { nombre = "Conf", posiciones = new[] { "DD0201" }, modo = "confirmacion" });
    var tConf = j?["id"]?.GetValue<long>();
    (e, j) = await Api("GET", $"/api/conteo/posicion/DD0201?tarea={tConf}");
    Ver(S(j?["modo"]) == "confirmacion" && j?["contenedores"]?[0]?["cantidad"] != null, "modo confirmación muestra la cantidad del sistema", j);
    // por artículo: arma la lista de posiciones con el buscador y solo muestra ese artículo
    (e, j) = await Api("POST", "/api/conteo/articulo", new { articulo = "PG-AR1" });
    var tArt = j?["id"]?.GetValue<long>();
    var posArt = j?["posiciones"]?.AsArray().Select(x => S(x)).ToList() ?? new();
    Ver(S(j?["modo"]) == "articulo" && posArt.Contains("DD0201"), "conteo por artículo arma las posiciones: " + string.Join(",", posArt), j);
    (e, j) = await Api("GET", $"/api/conteo/posicion/DD0201?tarea={tArt}");
    Ver(j?["contenedores"]?.AsArray().All(x => S(x!["articulo"]) == "PG-AR1") == true && j?["contenedores"]?[0]?["cantidad"] == null, "por artículo: solo ese artículo y a ciegas", j);
    // tarea cerrada: no se puede contar
    await Api("POST", $"/api/conteo/tareas/{tConf}/cerrar");
    (e, j) = await Api("GET", $"/api/conteo/posicion/DD0201?tarea={tConf}");
    Ver(e == 400, "tarea cerrada no deja contar", j);
    (e, j) = await Api("GET", "/api/conteo/tareas?abiertas=1");
    Ver(j?.AsArray().All(t => S(t!["estado"]) != "cerrada") == true, "el celular no ve tareas cerradas", j);

    // multicontenedor: empresas distintas a la misma posición, controla huecos
    (e, j) = await Api("POST", "/api/validar", new { contenedores = new[] { "I-12", "PC-199417" }, destino = "DD0204", multi = true });
    Ver(j?["ok"]?.GetValue<bool>() == true && j?["autorizacion"] == null, "multicontenedor con 2 empresas permitido", j);
    (e, j) = await Api("POST", "/api/validar", new { contenedores = new[] { "I-1", "I-2", "I-3", "I-4", "I-12" }, destino = "DD0204", multi = true });
    Ver(j?["ok"]?.GetValue<bool>() == false && j?["bloqueos"]?.AsArray().Any(b => S(b).Contains("Ampliá la profundidad")) == true, "multicontenedor sin huecos suficientes rechaza con aviso", j);
    var gm = Guid.NewGuid().ToString(); var m1 = Guid.NewGuid().ToString(); var m2 = Guid.NewGuid().ToString();
    (e, j) = await Api("POST", "/api/movimientos", new { movimientos = new object[] {
        new { id = m1, grupo = gm, tipo = "transferencia", contenedor = "I-12", destino = "DD0204", multi = true },
        new { id = m2, grupo = gm, tipo = "transferencia", contenedor = "PC-199417", destino = "DD0204", multi = true } } });
    Ver(j?["resultados"]?.AsArray().All(r => S(r!["estado"]) == "pendiente_wms") == true, "multicontenedor no pide autorización de zona", j);
    var mm1 = await Esperar(m1, "aplicado", "error"); var mm2 = await Esperar(m2, "aplicado", "error");
    Ver(S(mm1?["estado"]) == "aplicado" && S(mm2?["estado"]) == "aplicado", "multicontenedor cargado en el WMS (cada uno con su empresa)", new { mm1, mm2 });

    // eliminar contenedor (ajuste a 0) – solo administradores
    var aj = Guid.NewGuid().ToString();
    (e, j) = await Api("POST", "/api/movimientos", new { movimientos = new[] { new { id = aj, tipo = "ajuste", contenedor = "I-13", motivo = "INVENTARIO" } } });
    m = await Esperar(aj, "aplicado", "error");
    Ver(S(m?["estado"]) == "aplicado" && S(m?["respuesta_wms"]).Contains("quedó en: 0"), "eliminar contenedor (ajuste a 0): " + S(m?["respuesta_wms"]), m);

    // 2.11: ajuste sumando / restando una cantidad
    var ajS = Guid.NewGuid().ToString();
    (e, j) = await Api("POST", "/api/movimientos", new { movimientos = new[] { new { id = ajS, tipo = "ajuste", contenedor = "I-11", motivo = "INVENTARIO", cantidad = 5.0, sumar = true } } });
    m = await Esperar(ajS, "aplicado", "error");
    Ver(S(m?["estado"]) == "aplicado" && S(m?["respuesta_wms"]).Contains("se ajustó en: 5 unidades"), "ajuste: suma 5 unidades: " + S(m?["respuesta_wms"]), m);
    var ajR = Guid.NewGuid().ToString();
    (e, j) = await Api("POST", "/api/movimientos", new { movimientos = new[] { new { id = ajR, tipo = "ajuste", contenedor = "I-177719", motivo = "INVENTARIO", cantidad = 50.0, sumar = false } } });
    Ver(S(j?["resultados"]?[0]?["estado"]) == "rechazado", "ajuste: no deja restar más de lo que tiene", j);

    // 2.11: "Ver un hueco" pagina de a 5 y frena cuando ya tiene todos los contenedores que informa el WMS
    var logAntes = fake.Log.Count;
    (e, j) = await Api("GET", "/api/consulta/98014?nivel=3");
    var n3 = j?["niveles"]?.AsArray().FirstOrDefault();
    var paginas = fake.Log.Skip(logAntes).Count(l => l.StartsWith("hueco DD7-3 pagina"));
    Ver(n3?["contenedores"]?.AsArray().Count == 8 && S(n3?["capacidad"]) == "50" && paginas == 1 && n3?["aviso"] == null,
        $"posición con más de 5 contenedores: lee los 8 de DD-007-03 en {paginas + 1} páginas (de 10)", new { n3, paginas });
    // 2.12: transferir la posición completa aunque el handheld no deje paginar: al terminar, SONA vuelve a mirar el origen y mueve los que quedan
    fake.PaginacionRota = true; fake.BuscadorSinPosicion = true;
    (e, j) = await Api("GET", "/api/posicion/98014?nivel=3");
    var leidosRoto = j?["niveles"]?[0]?["detalle"]?.AsArray().Select(x => S(x!["contenedor"])).ToList() ?? new();
    Ver(leidosRoto.Count == 5 && S(j?["niveles"]?[0]?["aviso"]).Contains("vuelve a mirar el origen"), "posición completa: con el WMS sin paginar lee 5 de 8 y avisa", j);
    var grupoPos = Guid.NewGuid().ToString();
    var movsPos = leidosRoto.Select(cc => new { id = Guid.NewGuid().ToString(), grupo = grupoPos, tipo = "transferencia", contenedor = cc, origen = "DD0703", destino = "DD0705", nivel = 5, completa = true }).ToArray();
    (e, j) = await Api("POST", "/api/movimientos", new { movimientos = movsPos });
    Ver(e == 200 && j?["resultados"]?.AsArray().All(x => S(x!["estado"]) == "pendiente_wms") == true, "posición completa: 5 transferencias registradas", j);
    var todosMovidos = false;
    for (var i = 0; i < 80 && !todosMovidos; i++)
    {
        await Task.Delay(500);
        todosMovidos = fake.Ocupados("DD", 7, 3) == 0 && fake.Ocupados("DD", 7, 5) == 8;
    }
    (e, j) = await Api("GET", "/api/movimientos?texto=DD0705&limite=50");
    var agregados = j?["filas"]?.AsArray().Count(x => S(x!["grupo"]) == grupoPos && S(x!["comentario"]).Contains("Agregado por SONA")) ?? 0;
    Ver(todosMovidos && agregados == 3, $"posición completa: SONA agregó los {agregados} que quedaban y movió los 8 a DD-007-05", new { o = fake.Ocupados("DD", 7, 3), d = fake.Ocupados("DD", 7, 5), agregados });
    fake.PaginacionRota = false; fake.BuscadorSinPosicion = false;


    // 2.11: crear contenedor = número de LES + carga inicial en el WMS (posición como siempre: Ubic.Ctrl + nivel)
    (e, j) = await Api("POST", "/api/contenedores/crear", new { articulo = "pg-ar1", cantidad = 25, posicion = "50002", nivel = 1, prefijo = "PP" });
    var nuevo = S(j?["contenedor"]);
    Ver(e == 200 && j?["ok"]?.GetValue<bool>() == true && nuevo == "PP-201350" && fake.Log.Any(l => l == "carga inicial emp=6 PP-201350 PG-AR1 25 ubic=50002 niv=1"),
        "crear contenedor: LES da PP-201350 y se hace la carga inicial en DD-002-01 (empresa 6) con 25 u.", j);
    (e, j) = await Api("GET", "/api/consulta/PP-201350");
    Ver(S(j?["contenedor"]?["articulo"]) == "PG-AR1" && j?["movimientos"]?.AsArray().Any(x => S(x!["tipo"]) == "alta" && S(x!["estado"]) == "aplicado") == true,
        "crear contenedor: queda en el stock y grabado como alta", j);
    (e, j) = await Api("POST", "/api/contenedores/crear", new { articulo = "PG-AR1", cantidad = 1, posicion = "50001", nivel = 1, prefijo = "PP" });
    Ver(e == 200 && j?["ok"]?.GetValue<bool>() == false && S(j?["contenedor"]) == "PP-201351" && S(j?["mensaje"]).Contains("NO HAY LUGAR"),
        "crear contenedor: si el WMS rechaza la carga inicial avisa el número que dio LES", j);
    (e, j) = await Api("POST", "/api/contenedores/crear", new { articulo = "PG-AR1", cantidad = 1, posicion = "50003", nivel = 1, contenedor = "PP-201351" });
    Ver(j?["ok"]?.GetValue<bool>() == false && !fake.Log.Any(l => l.Contains("PP-201352")), "crear contenedor: reintento usa el mismo número (no pide otro a LES)", j);
    (e, j) = await Api("GET", "/api/consulta/" + Uri.EscapeDataString("3kg ariel"));
    Ver(S(j?["tipo"]) == "descripcion" && S(j?["articulos"]?[0]?["articulo"]) == "PG-AR1", "consultar por descripción (LES): \"3kg ariel\" encuentra PG-AR1", j);
    (e, j) = await Api("GET", "/api/articulos/sugerir?q=ari");
    Ver(e == 200 && j?["items"]?.AsArray().Any(x => S(x!["articulo"]) == "PG-AR1") == true, "buscador a medida que se escribe: \"ari\" ya sugiere PG-AR1", j);
    (e, j) = await Api("GET", "/api/articulos/sugerir?q=3kg%20ariel");
    Ver(e == 200 && j?["items"]?.AsArray().Any(x => S(x!["articulo"]) == "PG-AR1") == true, "buscador: varias palabras en cualquier orden", j);
    (e, j) = await Api("GET", "/api/nn");
    Ver(S(j?["destino"]) == "5422" && S(j?["nivel"]) == "1", "posición NN precargada (5422 nivel 1)", j);

    (e, j) = await Api("GET", $"/api/movimientos?usuario=SLEDESM6&desde={DateTime.Today:yyyy-MM-dd}");
    Ver((j?["total"]?.GetValue<long>() ?? 0) >= 5, $"movimientos filtrados por operario y fecha: {S(j?["total"])}", j?["total"]);
    var x = await http.GetAsync("/api/movimientos/export.xlsx");
    Ver(x.IsSuccessStatusCode && (await x.Content.ReadAsByteArrayAsync()).Length > 1000, "exportación a Excel");
    (e, j) = await Api("GET", "/api/monitor");
    Ver(e == 200 && j?["totales"] != null, "monitor", j);
    (e, j) = await Api("GET", "/api/config");
    Ver(e == 200 && S(j?["wms"]?["local"]).Contains("18090"), "configuración de plataformas", j);
    (e, j) = await Api("POST", "/api/config/probar");
    Ver(e == 200 && j?.AsArray().Any(p => S(p!["plataforma"]).Contains("red local") && p["ok"]!.GetValue<bool>()) == true, "prueba de conexión al WMS", j);
    (e, j) = await Api("POST", "/api/respaldo");
    Ver(e == 200 && File.Exists(S(j?["archivo"])), "respaldo de la base", j);

    // chat panel ↔ operarios
    var httpOp = new HttpClient { BaseAddress = http.BaseAddress, Timeout = TimeSpan.FromSeconds(60) };
    var lr = await httpOp.PostAsJsonAsync("/api/login", new { usuario = "oper1", clave = "pw", dispositivo = "TEST2" });
    var tokOp = S(JsonNode.Parse(await lr.Content.ReadAsStringAsync())?["token"]);
    httpOp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokOp);
    var rm = await httpOp.PostAsJsonAsync("/api/mensajes", new { texto = "Hola panel, falta un pallet en DD0201" });
    Ver(rm.IsSuccessStatusCode, "operario manda mensaje al panel");
    (e, j) = await Api("GET", "/api/mensajes/conversaciones");
    var convOp = j?.AsArray().FirstOrDefault(x => S(x!["conversacion"]) == "OPER1");
    Ver(S(convOp?["sin_leer"]) == "1" && S(convOp?["ultimo"]).Contains("falta"), "el panel ve la conversación sin leer", j);
    (e, j) = await Api("POST", "/api/mensajes", new { para = "OPER1", texto = "Gracias, ya lo reviso" });
    Ver(e == 200 && j?["desde_panel"]?.GetValue<bool>() == true, "el panel responde", j);
    (e, j) = await Api("POST", "/api/mensajes", new { para = "*", texto = "Aviso a todos: inventario a las 15" });
    var sl = JsonNode.Parse(await httpOp.GetStringAsync("/api/mensajes/sin-leer"));
    Ver(S(sl?["operario"]) == "2", "el operario tiene 2 sin leer (respuesta + aviso a todos)", sl);
    var lista = JsonNode.Parse(await httpOp.GetStringAsync("/api/mensajes"))?.AsArray();
    Ver(lista?.Count == 3, "el operario ve su conversación y los avisos a todos", lista);
    await httpOp.PostAsJsonAsync("/api/mensajes/leido", new { });
    sl = JsonNode.Parse(await httpOp.GetStringAsync("/api/mensajes/sin-leer"));
    Ver(S(sl?["operario"]) == "0", "al abrir el chat queda leído", sl);
    (e, j) = await Api("POST", "/api/mensajes/leido", new { con = "OPER1" });
    (e, j) = await Api("GET", "/api/mensajes/sin-leer");
    Ver(S(j?["panel"]) == "0", "el panel marcó leído", j);

    var rAj = await httpOp.PostAsJsonAsync("/api/movimientos", new { movimientos = new[] { new { id = Guid.NewGuid().ToString(), tipo = "ajuste", contenedor = "I-10", motivo = "INVENTARIO" } } });
    Ver((int)rAj.StatusCode == 403, "un operario no puede eliminar contenedores", (int)rAj.StatusCode);
    rAj = await httpOp.PostAsJsonAsync("/api/movimientos", new { movimientos = new[] { new { id = Guid.NewGuid().ToString(), tipo = "ajuste", contenedor = "I-10", motivo = "INVENTARIO", cantidad = 1.0, sumar = true } } });
    Ver((int)rAj.StatusCode == 403, "un operario no puede ajustar", (int)rAj.StatusCode);
    var rCr = await httpOp.PostAsJsonAsync("/api/contenedores/crear", new { articulo = "PG-AR1", cantidad = 1, posicion = "50002", nivel = 1 });
    Ver((int)rCr.StatusCode == 403, "un operario no puede crear contenedores", (int)rCr.StatusCode);
    (e, j) = await Api("GET", "/api/ia/estado");
    Ver(e == 200 && j?["modelo"] != null, "estado del reconocimiento por foto", j);
    // picking consolidado (7 → 9): lista → ir → tomar → isla → fin
    (e, j) = await Api("GET", "/api/picking/empresas");
    Ver(e == 200 && j?.AsArray().Any(x => S(x!["codigo"]) == "6") == true, "picking: empresas del usuario en el WMS", j);
    (e, j) = await Api("POST", "/api/picking/iniciar", new { modo = "consolidado" });
    Ver(S(j?["paso"]?["pantalla"]) == "lista" && j?["paso"]?["items"]?.AsArray().Count == 2 && S(j?["paso"]?["items"]?[0]?["contenedor"]) == "I-10", "picking: SONA encuentra sola la empresa con OP (6) y el WMS asigna la 2051 con 2 renglones", j);
    // postergar (opción 6 del handheld): queda con su avance y SONA pide la siguiente OP (no vuelve al menú)
    // (con faltante el WMS muestra otro campo visible: SONA escribe ahí, como el operario en la pistola)
    fake.ListaConFaltante = true;
    var (eR, jR) = await Api("POST", "/api/picking/retomar");
    Ver(S(jR?["paso"]?["pantalla"]) == "lista", "picking: lista de la OP con faltante (campo visible distinto)", jR);
    (e, j) = await Api("POST", "/api/picking/accion", new { accion = "postergar" });
    Ver(S(j?["paso"]?["pantalla"]) == "lista" && S(j?["aviso"]).Contains("OP 2051 postergada") && S(j?["aviso"]).Contains("% de avance")
        && j?["postergadas"]?.AsArray().Any(x => S(x!["op"]) == "2051") == true,
        "picking: postergar deja la OP con su % de avance y pasa a la siguiente orden", j);
    (e, j) = await Api("POST", "/api/picking/accion", new { accion = "continuar" });
    Ver(S(j?["paso"]?["pantalla"]) == "ir" && S(j?["paso"]?["articulo"]) == "PG-AR1" && S(j?["paso"]?["ubicCtrl"]) == "50002", "picking: ir a la posición del primer artículo", j);
    // 2.12: pausar el picking para hacer otra cosa (consultar) y retomarlo donde quedó, sin tocar el WMS
    var postsAntesPausa = fake.Log.Count(l => l.StartsWith("POST /sglwms_prod/hpck"));
    (e, j) = await Api("POST", "/api/picking/pausar");
    var (_, enc) = await Api("GET", "/api/picking/encurso");
    Ver(e == 200 && j?["enCurso"]?.GetValue<bool>() == true && enc?["pausa"]?.GetValue<bool>() == true && fake.Log.Count(l => l.StartsWith("POST /sglwms_prod/hpck")) == postsAntesPausa,
        "picking: pausa sin mandar nada al WMS", new { j, enc });
    (e, j) = await Api("GET", "/api/consulta/I-11");
    (e, j) = await Api("POST", "/api/picking/retomar");
    Ver(S(j?["paso"]?["pantalla"]) == "ir" && S(j?["paso"]?["articulo"]) == "PG-AR1", "picking: retoma en el mismo paso después de una consulta", j);

    // faltante: el artículo no está → queda guardado, mensaje al panel y evento para inventario / producción
    (e, j) = await Api("POST", "/api/picking/accion", new { accion = "faltante", comentario = "posición vacía" });
    Ver(e == 200 && S(j?["aviso"]).Contains("Faltante de PG-AR1 informado al panel") && S(j?["aviso"]).Contains("OP 2051 postergada")
        && j?["postergadas"]?.AsArray().Any(x => S(x!["op"]) == "2051" && x!["faltantes"]?.AsArray().Any(f => S(f) == "PG-AR1") == true) == true,
        "picking: faltante informado y la OP queda postergada (stand by) con el faltante", j);
    (e, j) = await Api("GET", "/api/faltantes?estado=abierto");
    var falt = j?.AsArray().FirstOrDefault();
    Ver(S(falt?["articulo"]) == "PG-AR1" && S(falt?["op"]) == "2051" && S(falt?["comentario"]) == "posición vacía", "picking: el faltante queda para el panel", j);
    (e, j) = await Api("GET", "/api/mensajes/conversaciones");
    Ver(j?.AsArray().Any(x => S(x!["ultimo"]).Contains("FALTANTE") && S(x!["ultimo"]).Contains("PG-AR1")) == true, "picking: el faltante llega como mensaje al panel", j);
    (e, j) = await Api("POST", $"/api/faltantes/{S(falt?["id"])}/atender", new { motivo = "repuesto" });
    (e, j) = await Api("GET", "/api/faltantes?estado=abierto");
    Ver(j?.AsArray().Count == 0, "picking: faltante atendido desde el panel", j);
    (e, j) = await Api("POST", "/api/picking/accion", new { accion = "continuar" });
    var postsIr = fake.Log.Count(l => l.StartsWith("POST /sglwms_prod/hpck1"));
    (e, j) = await Api("POST", "/api/picking/accion", new { accion = "leer", ubicacion = "50001", nivel = "1", articulo = "PG-AR1" });
    Ver(S(j?["error"]).Contains("no es la posición") && S(j?["paso"]?["pantalla"]) == "ir" && fake.Log.Count(l => l.StartsWith("POST /sglwms_prod/hpck1")) == postsIr, "picking: posición equivocada se frena en SONA (no llega al WMS)", j);
    (e, j) = await Api("POST", "/api/picking/accion", new { accion = "leer", ubicacion = "50002", nivel = "1", articulo = "PG-OTRO" });
    Ver(S(j?["error"]).Contains("Artículo equivocado"), "picking: artículo equivocado se frena", j);
    fake.AvisoLote = true;
    (e, j) = await Api("POST", "/api/picking/accion", new { accion = "leer", ubicacion = "50002", nivel = "1", articulo = "PG-AR1" });
    Ver(S(j?["paso"]?["pantalla"]) == "ir" && j?["paso"]?["puedeContinuar"]?.GetValue<bool>() == true && j?["error"] == null && S(j?["paso"]?["mensaje"]).Contains("FIFO/FEFO"),
        "picking: aviso de lote/vencimiento del WMS se muestra como advertencia (no como error)", j);
    (e, j) = await Api("POST", "/api/picking/accion", new { accion = "continuar_aviso" });
    Ver(S(j?["paso"]?["pantalla"]) == "tomar" && S(j?["paso"]?["cantidad"]) == "2" && fake.Log.Contains("picking continuar aviso"), "picking: con el aviso deja continuar y pasa a tomar la cantidad", j);
    (e, j) = await Api("POST", "/api/picking/accion", new { accion = "tomar", cantidad = 5, contenedor = "ETQ-1" });
    Ver(S(j?["error"]).Contains("más de lo pedido"), "picking: no deja tomar más de lo pedido", j);
    (e, j) = await Api("POST", "/api/picking/accion", new { accion = "tomar", cantidad = 2, contenedor = "ETQ-1" });
    Ver(S(j?["paso"]?["pantalla"]) == "lista" && j?["paso"]?["items"]?.AsArray().Count == 1, "picking: tomado, vuelve a la lista con lo pendiente", j);
    (e, j) = await Api("POST", "/api/picking/accion", new { accion = "continuar" });
    (e, j) = await Api("POST", "/api/picking/accion", new { accion = "leer", ubicacion = "DD0101", nivel = "1", articulo = "PG-OTRO" });
    Ver(S(j?["paso"]?["pantalla"]) == "tomar", "picking: se puede leer la posición por código (DD0101)", j);
    (e, j) = await Api("POST", "/api/picking/accion", new { accion = "tomar", cantidad = 1, contenedor = "ETQ-1" });
    Ver(S(j?["paso"]?["pantalla"]) == "etiquetar" && S(j?["paso"]?["isla"]) == "NC-01", "picking: etiquetar y dejar en la isla NC-01", j);
    (e, j) = await Api("POST", "/api/picking/accion", new { accion = "aceptar" });
    Ver(S(j?["paso"]?["pantalla"]) == "fin", "picking: OP concluida", j);
    (e, j) = await Api("POST", "/api/picking/accion", new { accion = "aceptar" });
    Ver(S(j?["paso"]?["pantalla"]) == "menu", "picking: vuelve al menú de picking", j);
    (e, j) = await Api("GET", "/api/picking/eventos");
    Ver(j?.AsArray().Count(x => S(x!["tipo"]) == "pick") == 2 && j?.AsArray().Count(x => S(x!["tipo"]) == "error_lectura") == 3 && j?.AsArray().Any(x => S(x!["tipo"]) == "fin") == true,
        "picking: queda registrado cada pick, los errores evitados y el fin de la OP", j);
    (e, j) = await Api("GET", "/api/productividad");
    Ver(S(j?["totales"]?["lineas_pick"]) == "2" && S(j?["totales"]?["unidades_pick"]) == "3", "productividad incluye el picking", j);
    (e, j) = await Api("GET", "/api/pedido/2051");
    Ver(e == 200 && S(j?["tipo"]) == "op" && j?["articulos"]?.AsArray().Count == 2 && S(j?["articulos"]?[0]?["articulo"]) == "PG-AR1"
        && S(j?["articulos"]?[0]?["pickeada"]) == "2" && j?["articulos"]?[0]?["fotos"]?.AsArray().Count > 0 && S(j?["unidades"]) == "3",
        "consultar pedido por OP: los artículos que se pickearon con sus fotos", j);

    // control de bulto (LES Api/Bultos)
    (e, j) = await Api("GET", "/api/bultos/16481-29");
    Ver(e == 200 && j?["pedido"]?["controlado"]?.GetValue<bool>() == true && S(j?["pedido"]?["cliente"]) == "Notro Bazar", "control de bulto: busca la solicitud en LES", j);
    (e, j) = await Api("POST", "/api/bultos/16481-29", new { origen = "PUESTO1", nuevos = new[] { new { alto = 172, ancho = 60, profundidad = 30 } } });
    var nroCont = j?["pedido"]?["bultos"]?[0]?["nroContenedor"]?.GetValue<long>() ?? 0;
    Ver(e == 200 && j?["pedido"]?["bultos"]?.AsArray().Count == 1 && nroCont >= 3_000_000 && nroCont < 5_000_000, "control de bulto: nuevo bulto con medidas y número de contenedor", j);
    (e, j) = await Api("POST", "/api/bultos/16481-29", new { origen = "PUESTO2", nuevos = new[] { new { alto = 50, ancho = 40, profundidad = 30 } } });
    Ver(j?["pedido"]?["bultos"]?.AsArray().Count == 2 && j?["pedido"]?["bultos"]?.AsArray().All(x => S(x!["origen"]) == "PUESTO2") == true, "control de bulto: agrega sin perder los anteriores", j);
    (e, j) = await Api("POST", "/api/bultos/999-1", new { nuevos = new[] { new { alto = 10, ancho = 10, profundidad = 10 } } });
    Ver(e == 400 && S(j?["error"]).Contains("debe ser controlado"), "control de bulto: pedido no controlado no deja generar etiquetas", j);
    (e, j) = await Api("POST", "/api/bultos/16481-29", new { nuevos = new[] { new { alto = 0, ancho = 10, profundidad = 10 } } });
    Ver(e == 400, "control de bulto: medidas inválidas", j);
    // 2.12: lo que hacen los pickers queda como movimiento (picking, medición) y se ve en la productividad
    (e, j) = await Api("GET", "/api/movimientos?tipo=picking");
    Ver(e == 200 && j?["filas"]?.AsArray().Count == 2 && j?["filas"]?.AsArray().All(x => S(x!["estado"]) == "registrado") == true, "picking: cada línea queda como movimiento", j);
    (e, j) = await Api("GET", "/api/movimientos?tipo=medicion");
    Ver(e == 200 && j?["filas"]?.AsArray().Count == 2 && S(j?["filas"]?[0]?["contenedor"]) == "16481-29", "control de bulto: la medición queda como movimiento", j);

    // 2.12: consultar pedido (buscador + LES) con los artículos y sus fotos; control de pedido
    (e, j) = await Api("GET", "/api/consulta/16481-29");
    Ver(e == 200 && S(j?["tipo"]) == "pedido" && S(j?["pdv"]) == "YAG0018 Yaguar Moreno" && j?["bultos"]?.AsArray().Count == 2 && j?["controlado"]?.GetValue<bool>() == true
        && j?["articulos"]?.AsArray().Count == 2 && S(j?["articulos"]?[0]?["articulo"]) == "PG-AR1" && j?["articulos"]?[0]?["fotos"]?.AsArray().Count > 0,
        "consultar pedido: datos, bultos y artículos con foto", j);
    (e, j) = await Api("POST", "/api/pedido/16481-29/control", new { cantidad = 3, bultos = 2, completo = false });
    Ver(e == 400, "control de pedido: sin \"completo\" pide la observación", j);
    (e, j) = await Api("POST", "/api/pedido/16481-29/control", new { cantidad = 3, bultos = 2, completo = true, articulos = new[] { new { articulo = "PG-AR1", cantidad = 2 } } });
    Ver(e == 200 && j?["ok"]?.GetValue<bool>() == true && S(j?["telegram"]) == "sin configurar", "control de pedido registrado (Telegram sin configurar)", j);
    (e, j) = await Api("GET", "/api/productividad");
    var yo = j?["operarios"]?.AsArray().FirstOrDefault(x => S(x!["usuario"]) == "SLEDESM6");
    Ver(S(yo?["lineas_pick"]) == "2" && S(yo?["solicitudes_medidas"]) == "1" && S(yo?["bultos_medidos"]) == "2" && S(yo?["pedidos_controlados"]) == "1"
        && S(yo?["pausas"]) == "1" && S(j?["totales"]?["pedidos_controlados"]) == "1", "productividad: picking, medición y control de pedido por operario", yo);
    {
        (e, j) = await Api("GET", "/api/bultos/16481-29");
        var bs = j?["pedido"]?["bultos"]?.AsArray() ?? new JsonArray();
        var b1 = bs[0]!["nroContenedor"]!.GetValue<long>(); var b2 = bs[1]!["nroContenedor"]!.GetValue<long>();
        (e, j) = await Api("POST", "/api/bultos/16481-29", new { origen = "PUESTO2", cambios = new[] { new { nroContenedor = b1, alto = 99.5, ancho = 61, profundidad = 31 } } });
        var mb = j?["pedido"]?["bultos"]?.AsArray().FirstOrDefault(x => x!["nroContenedor"]!.GetValue<long>() == b1);
        Ver(e == 200 && mb?["alto"]?.GetValue<double>() == 99.5 && j?["pedido"]?["bultos"]?.AsArray().Count == 2 && j?["modificados"]?.GetValue<int>() == 1, "control de bulto: modificar medidas de un bulto cargado", j);
        (e, j) = await Api("POST", "/api/bultos/16481-29", new { origen = "PUESTO2", quitar = new[] { b2 } });
        Ver(e == 200 && j?["pedido"]?["bultos"]?.AsArray().Count == 1 && j?["borrados"]?.GetValue<int>() == 1, "control de bulto: borrar un bulto", j);
        (e, j) = await Api("POST", "/api/bultos/16481-29", new { cambios = new[] { new { nroContenedor = 123L, alto = 10, ancho = 10, profundidad = 10 } } });
        Ver(e == 400 && S(j?["error"]).Contains("ya no está"), "control de bulto: modificar un bulto que no existe", j);
        (e, j) = await Api("POST", "/api/bultos/16481-29", new { origen = "PUESTO2", nuevos = new[] { new { alto = 50, ancho = 40, profundidad = 30 } } });
        Ver(e == 200 && j?["impresion"] is JsonObject im && (OperatingSystem.IsWindows() || S(im["error"]).Contains("Windows")), "etiquetas de bulto: al guardar intenta imprimir (en Linux avisa)", j);
        var nb = j?["pedido"]?["bultos"]?[0]?["nroContenedor"]?.GetValue<long>() ?? 0;
        var rp = await http.GetAsync($"/api/bultos/16481-29/etiqueta/{nb}");
        var png = await rp.Content.ReadAsByteArrayAsync();
        File.WriteAllBytes("/tmp/claude-0/etiqueta_bulto.png", png);
        Ver(rp.IsSuccessStatusCode && png.Length > 1000 && png[1] == (byte)'P', "etiquetas de bulto: vista previa PNG", (int)rp.StatusCode);
        (e, j) = await Api("PUT", "/api/bultos/impresion", new { impresoras = new Dictionary<string, string> { ["PUESTO2"] = "ZEBRA PUESTO 2" }, copias = 2 });
        (e, j) = await Api("GET", "/api/bultos/impresion");
        Ver(e == 200 && j?["origenes"]?.AsArray().Any(x => S(x!["codigo"]) == "PUESTO2" && S(x["impresora"]) == "ZEBRA PUESTO 2") == true && j?["copias"]?.GetValue<int>() == 2, "etiquetas de bulto: impresora por puesto", j);
        (e, j) = await Api("POST", "/api/bultos/16481-29", new { origen = "PUESTO2", imprimir = false, nuevos = new[] { new { alto = 20, ancho = 20, profundidad = 20 } } });
        Ver(e == 200 && j?["impresion"] == null, "etiquetas de bulto: se puede guardar sin imprimir", j);
    }
    (e, j) = await Api("GET", "/api/productividad/actividad?usuario=SLEDESM6");
    Ver(e == 200 && j?.AsArray().Any(x => S(x!["tipo"]) == "picking") == true && j?.AsArray().Any(x => S(x!["tipo"]) == "control_pedido") == true
        && j?.AsArray().Any(x => S(x!["tipo"]) == "pausa") == true, "productividad: actividad completa del operario", j?.AsArray().Count);

    // 2.12: reubicar cajas ofrece los contenedores del mismo artículo
    (e, j) = await Api("GET", "/api/cajas/destinos/I-11");
    Ver(e == 200 && j?["destinos"]?.AsArray().Any(x => S(x!["contenedor"]) == "I-10") == true && j?["destinos"]?.AsArray().All(x => S(x!["contenedor"]) != "I-11") == true,
        "reubicar cajas: ofrece los contenedores del mismo artículo", j);

    // 2.12: impresora virtual (etiqueta con código de barras)
    var et = await http.GetAsync("/api/etiquetas/PP-201350");
    var etHtml = await et.Content.ReadAsStringAsync();
    Ver(et.IsSuccessStatusCode && etHtml.Contains("PP-201350") && etHtml.Contains("<svg") && etHtml.Contains("PG-AR1"), "crear contenedor: etiqueta en la impresora virtual", et.StatusCode);

    // 2.12: panel web del supervisor (solo lectura)
    var sup = await http.GetAsync("/supervisor");
    Ver(sup.IsSuccessStatusCode && (await sup.Content.ReadAsStringAsync()).Contains("SONA Supervisor"), "panel web del supervisor en /supervisor", sup.StatusCode);
    (e, j) = await Api("PUT", "/api/usuarios/OPER1", new { nombre = "Operario 1", rol = "supervisor", activo = true });
    var httpSup = new HttpClient { BaseAddress = http.BaseAddress, Timeout = TimeSpan.FromSeconds(60) };
    var ls = await httpSup.PostAsJsonAsync("/api/login", new { usuario = "oper1", clave = "pw", web = true });
    httpSup.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", S(JsonNode.Parse(await ls.Content.ReadAsStringAsync())?["token"]));
    var supProd = await httpSup.GetAsync("/api/productividad");
    var supMov = await httpSup.PostAsJsonAsync("/api/movimientos", new { movimientos = new[] { new { id = Guid.NewGuid().ToString(), tipo = "transferencia", contenedor = "I-10", destino = "DD0101" } } });
    Ver(ls.IsSuccessStatusCode && supProd.IsSuccessStatusCode && (int)supMov.StatusCode == 403, "supervisor: entra al panel web, ve la productividad y no puede mover nada",
        new { login = ls.StatusCode, prod = supProd.StatusCode, mov = supMov.StatusCode });
    (e, j) = await Api("PUT", "/api/usuarios/OPER1", new { nombre = "Operario 1", rol = "maquinista", activo = true });


    // historia del WMS (hconshistplt → archivo XLS/HTML) e ingresos/egresos del e-TMS
    (e, j) = await Api("GET", "/api/historia?contenedor=I-10");
    Ver(e == 200 && j?["filas"]?.AsArray().Count == 3 && S(j["filas"]![2]!["Tipo"]) == "PICKING" && j["columnas"]?.AsArray().Any(x => S(x) == "Contenedor") == true, "historia del contenedor en el WMS (3 movimientos, incluye picking)", j);
    (e, j) = await Api("GET", "/api/historia?articulo=PG-AR1");
    Ver(e == 200 && j?["filas"]?.AsArray().Count == 3, "historia del artículo en el WMS", j);
    (e, j) = await Api("GET", "/api/ingresos-egresos/PG-AR1?empresa=6");
    Ver(e == 200 && j?["movimientos"]?.AsArray().Count == 3 && j?["egresado"]?.GetValue<double>() == 5 && S(j["movimientos"]![0]!["pdv"]) == "Mayorista Tornado", "ingresos/egresos del e-TMS (con PDV y remito)", j);
    // LES: habilitado y artículos sin stock
    (e, j) = await Api("GET", "/api/consulta/PG-NOSTOCK");
    Ver(S(j?["tipo"]) == "articulo" && j?["articulo"]?["contenedores"]?.AsArray().Count == 0 && j?["articulo"]?["les"]?["habilitado"]?.GetValue<bool>() == false
        && S(j?["articulo"]?["descripcion"]) == "SHAMPOO VIEJO", "artículo sin stock se encuentra en LES (deshabilitado)", j);
    (e, j) = await Api("GET", "/api/consulta/PG-AR1");
    Ver(j?["articulo"]?["les"]?["habilitado"]?.GetValue<bool>() == true && S(j?["articulo"]?["les"]?["stockFisico"]) == "170", "artículo con stock muestra habilitado y stock de LES", j);
    (e, j) = await Api("GET", "/api/consulta/I-11");
    Ver(j?["contenedor"]?["articuloHabilitado"]?.GetValue<bool>() == true, "contenedor muestra si el artículo está habilitado en LES", j);
    // un artículo que alguna vez se escaneó como contenedor igual se busca como artículo (caso PAMP1776)
    (e, j) = await Api("POST", "/api/movimientos", new { movimientos = new[] { new { id = Guid.NewGuid().ToString(), tipo = "cambio_estado", contenedor = "PG-AR1", disponible = true } } });
    (e, j) = await Api("GET", "/api/consulta/PG-AR1");
    Ver(S(j?["tipo"]) == "articulo" && j?["articulo"]?["contenedores"]?.AsArray().Count > 0, "código de artículo con historial como contenedor se busca como artículo", j);
    (e, j) = await Api("GET", "/api/consulta/I-10");
    var fotoEtms = S(j?["contenedor"]?["foto"]);
    Ver(fotoEtms.Contains("Docs"), "artículo sin foto en tradebox toma la foto del e-TMS", j);
    var rimg = await http.GetAsync(fotoEtms);
    Ver(rimg.IsSuccessStatusCode && (await rimg.Content.ReadAsByteArrayAsync()).Length > 200, "la foto del e-TMS se sirve desde la caché de SONA", (int)rimg.StatusCode);
    // catálogo de fotos propias
    var fcat = new MultipartFormDataContent { { new ByteArrayContent(Enumerable.Repeat((byte)0xCD, 500).ToArray()) { Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") } }, "foto", "a.jpg" } };
    var rcat = await http.PostAsync("/api/catalogo/PG-AR1/fotos", fcat);
    Ver(rcat.IsSuccessStatusCode, "catálogo: subir una foto propia al artículo", (int)rcat.StatusCode);
    (e, j) = await Api("GET", "/api/catalogo/PG-AR1");
    Ver(j?["propias"]?.AsArray().Count == 1, "catálogo: la foto queda asociada al código", j);
    (e, j) = await Api("GET", "/api/consulta/I-10");
    var fprop = S(j?["contenedor"]?["foto"]);
    var rimg2 = await http.GetAsync(fprop);
    Ver(fprop.Contains("catalogo") && rimg2.IsSuccessStatusCode, "catálogo: la foto propia se muestra primero", fprop);
    (e, j) = await Api("GET", "/api/ingresos-egresos/NOEXISTE?empresa=6");
    Ver(e == 400 && S(j?["error"]).Contains("no tiene el artículo"), "e-TMS: artículo inexistente avisa", j);
    (e, j) = await Api("POST", "/api/ia/ninguno", new { fotoId = "prueba123.jpg" });
    (e, j) = await Api("GET", "/api/ia/historial?estado=pendientes");
    Ver(e == 200 && j?.AsArray().Any(f => S(f!["foto"]) == "prueba123.jpg" && S(f["url"]).Contains("/api/fotos/ia/")) == true, "IA: \"ninguno es\" queda en sin identificar", j);
    (e, j) = await Api("POST", "/api/ia/descartar", new { fotoId = "prueba123.jpg" });
    (e, j) = await Api("GET", "/api/ia/historial?estado=pendientes");
    Ver(e == 200 && j?.AsArray().All(f => S(f!["foto"]) != "prueba123.jpg") == true, "IA: descartada sale de pendientes", j);
    (e, j) = await Api("GET", "/api/ia/estado");
    Ver(j?["sinIdentificar"] != null, "IA: estado informa sin identificar", j);

    // ================================================================ 2.14: panel de producción (WMS de escritorio)
    {
        var dirPag = Path.Combine(AppContext.BaseDirectory, "paginas");
        var realA = Sona.Wms.SesionEscritorio.LeerPendientesAceptar(File.ReadAllText(Path.Combine(dirPag, "escritorio_aceptar.html")));
        var o1 = realA.FirstOrDefault(x => x.Op == "28513");
        Ver(realA.Count == 2 && o1?.Empresa == 144 && o1.Solicitud == "9189-125" && o1.Viaje == "STAFE" && o1.Lugar == "Depósito Oeste 10" && o1.Articulos == 4 && o1.Unidades == 80,
            "producción: lee la grilla real de Pendientes de Aceptar", string.Join(" | ", realA));
        var realP = Sona.Wms.SesionEscritorio.LeerPendientesPreparar(File.ReadAllText(Path.Combine(dirPag, "escritorio_preparar.html")));
        var o2 = realP.FirstOrDefault(x => x.Op == "178");
        Ver(realP.Count == 6 && o2?.Picker == "LFARIAS" && o2.Avance == 37 && o2.Comenzada && realP.Count(x => x.Picker == "GPONCE") == 2 && realP.First(x => x.Op == "5581").Viaje == "R-97939",
            "producción: lee la grilla real de Pendientes de Preparación (picker y avance)", string.Join(" | ", realP));
        var pk = Sona.Wms.SesionEscritorio.LeerPickers(File.ReadAllText(Path.Combine(dirPag, "escritorio_aceptar.html")));
        Ver(pk.Any(p => p.Codigo == "VAGUIRRE" && p.Nombre == "VICTORIA AGUIRRE") && pk.Count > 50, "producción: lista de preparadores del WMS", pk.Count);
        var pdf = File.ReadAllBytes(Path.Combine(dirPag, "sim_faltantes.pdf"));
        var ren = Caratula.Renglones(pdf);
        Ver(ren.Count == 4 && ren[0].Articulo == "JJVC0510" && ren[0].Disponible == 202 && ren[0].Pedido == 50 && ren.All(x => x.Faltante == 0), "carátula: lee los renglones de la simulación", string.Join(" | ", ren));
        var sellado = Caratula.Sellar(pdf, "VICTORIA AGUIRRE", "VAGUIRRE", "OP 28513 · prueba");
        Ver(sellado.Length > 1000 && Encoding.ASCII.GetString(sellado, 0, 4) == "%PDF" && Caratula.Imagenes(pdf, "VICTORIA AGUIRRE", "VAGUIRRE", "OP 28513 · prueba").Count == 1, "carátula: PDF sellado con el picker", sellado.Length);
        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "sona_caratula_prueba.pdf"), sellado);
        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "sona_caratula_prueba.png"), Caratula.Imagenes(pdf, "VICTORIA AGUIRRE", "VAGUIRRE", "OP 28513 · prueba")[0]);
        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "sona_caratula_prueba2.png"), Caratula.Imagenes(sellado)[0]);

        try { var crudo = await http.GetStringAsync("/api/produccion/tablero?refrescar=1"); if (!crudo.StartsWith("{")) Console.WriteLine("TABLERO CRUDO: " + crudo[..Math.Min(400, crudo.Length)]); } catch (Exception ex2) { Console.WriteLine("TABLERO EXC " + ex2.Message); }
        (e, j) = await Api("GET", "/api/produccion/tablero?refrescar=1");
        Ver(e == 200 && j?["aAsignar"]?.AsArray().Count == 2 && j?["preparacion"]?.AsArray().Any(x => S(x!["op"]) == "57559" && S(x["picker"]) == "VAGUIRRE" && S(x["avance"]) == "72") == true
            && j?["pickers"]?.AsArray().Any(x => S(x!["codigo"]) == "VAGUIRRE" && S(x["ops"]) == "1") == true && j?["viajes"]?.AsArray().Any(x => S(x) == "SALTA") == true,
            "producción: tablero con para asignar, en preparación, pickers y viajes", $"{e} {j}");
        fake.Escritorio.SesionPerdida = 1;
        (e, j) = await Api("GET", "/api/produccion/tablero?refrescar=1");
        Ver(e == 200 && j?["error"] == null && j?["aAsignar"]?.AsArray().Count == 2, "producción: si el WMS cerró la sesión, vuelve a ingresar solo", $"{e} {j?["error"]}");
        fake.Escritorio.SesionPerdida = 5;
        (e, j) = await Api("GET", "/api/produccion/tablero?refrescar=1");
        Ver(S(j?["error"]).Contains("Sesion finalizada"), "producción: pantalla inesperada del WMS se informa clara", j?["error"]);
        fake.Escritorio.SesionPerdida = 0;
        (e, j) = await Api("POST", "/api/produccion/asignar", new { empresa = 6, tipo = "OPR", op = "57558", picker = "VAGUIRRE", imprimir = false, prioridad = "urgente" });
        Ver(e == 200 && fake.Escritorio.Preparar.FirstOrDefault(o => o.Nro == "57558")?.Prior == 10 && S(j?["mensaje"]).Contains("urgente"), "producción: asignar con prioridad urgente (la pone en el WMS)", $"{e} {j?["mensaje"]} prior={fake.Escritorio.Preparar.FirstOrDefault(o => o.Nro == "57558")?.Prior}");
        var logEsc = string.Join(" / ", fake.Escritorio.Log.TakeLast(6));
        Ver(e == 200 && j?["ok"]?.GetValue<bool>() == true && S(j?["pickerNombre"]) == "VICTORIA AGUIRRE" && S(j?["caratula"]).Length > 0
            && logEsc.Contains("E'SIMULAR'. 57558:VAGUIRRE") && logEsc.Contains("pdf osimulacion") && logEsc.Contains("E'CONFSINDEPURACION'. 57558:VAGUIRRE"),
            "producción: asignar = simular faltantes + guardar PDF + confirmar SIN depuración", $"{e} {j} {logEsc}");
        (e, j) = await Api("POST", "/api/produccion/prioridad", new { empresa = 6, tipo = "OPR", op = "57558", prioridad = "baja" });
        Ver(e == 200 && j?["ok"]?.GetValue<bool>() == true && fake.Escritorio.Preparar.First(o => o.Nro == "57558").Prior == 70 && fake.Escritorio.Preparar.First(o => o.Nro == "57558").Picker == "VAGUIRRE",
            "producción: cambiar la prioridad de una OP asignada (sin tocar el picker)", $"{e} {j}");
        (e, j) = await Api("GET", "/api/produccion/tablero");
        Ver(j?["aAsignar"]?.AsArray().Count == 1 && j?["preparacion"]?.AsArray().Any(x => S(x!["op"]) == "57558" && S(x["picker"]) == "VAGUIRRE" && x["caratula"] != null) == true,
            "producción: la OP pasa a asignadas con su carátula guardada", j);
        var rpdf = await http.GetAsync("/api/produccion/caratula?empresa=6&tipo=OPR&op=57558");
        var bpdf = await rpdf.Content.ReadAsByteArrayAsync();
        Ver(rpdf.IsSuccessStatusCode && rpdf.Content.Headers.ContentType?.MediaType == "application/pdf" && Encoding.ASCII.GetString(bpdf, 0, 4) == "%PDF", "producción: ver la carátula (PDF sellado)", rpdf.StatusCode);
        (e, j) = await Api("POST", "/api/produccion/asignar", new { empresa = 6, tipo = "OPR", op = "57558", picker = "JPERALTA", imprimir = false });
        Ver(e == 502 && S(j?["error"]).Contains("ya no está"), "producción: no se asigna dos veces la misma OP", j);
        (e, j) = await Api("POST", "/api/produccion/reasignar", new { empresa = 6, tipo = "OPR", op = "57558", picker = "JPERALTA", imprimir = false });
        Ver(e == 200 && j?["ok"]?.GetValue<bool>() == true && S(j?["pickerNombre"]) == "JULIETA PERALTA", "producción: reasignar a otro picker", j);
        var rimp = await http.GetAsync("/api/produccion/caratulas/imprimir.html?ops=" + Uri.EscapeDataString("6|OPR|57558"));
        var timp = await rimp.Content.ReadAsStringAsync();
        Ver(rimp.IsSuccessStatusCode && timp.Contains("data:image/png") && timp.Contains("window.print") && timp.Contains("OP 57558"),
            "producción: carátula para imprimir desde la PC (diálogo del navegador)", $"{rimp.StatusCode} {timp[..Math.Min(300, timp.Length)]}");
        // flujo de documentos (como en SAP): por OP, por solicitud y por remito llega a la misma cadena
        (e, j) = await Api("GET", "/api/produccion/flujo?q=57558");
        File.WriteAllText("/tmp/claude-0/flujo.json", j?.ToJsonString() ?? "");
        var fl = j?["flujo"];
        Ver(e == 200 && S(j?["solicitud"]) == "11415-10" && S(j?["remito"]) == "160197" && S(fl?["tipo"]) == "solicitud"
            && fl?["hijos"]?.AsArray().Any(h => S(h!["tipo"]) == "op" && S(h["numero"]) == "57558" && h["hijos"]?.AsArray().Count(x => S(x!["tipo"]) == "asignacion" && S(x["estado"]) == "ok") == 2) == true
            && fl?["hijos"]?.AsArray().Any(h => S(h!["tipo"]) == "control") == true && fl?["hijos"]?.AsArray().Any(h => S(h!["tipo"]) == "despacho") == true,
            "flujo de documentos: solicitud → OP → asignación y reasignación → control → despacho", j);
        var (e2f, j2f) = await Api("GET", "/api/produccion/flujo?q=11415-10");
        var (e3f, j3f) = await Api("GET", "/api/produccion/flujo?q=Rem:160197");
        Ver(e2f == 200 && e3f == 200 && j2f?["ops"]?.AsArray().Any(x => S(x) == "57558") == true && j3f?["ops"]?.AsArray().Any(x => S(x) == "57558") == true,
            "flujo de documentos: se llega igual por solicitud o por remito", $"{j2f?["ops"]} {j3f?["ops"]}");
        var (e4f, j4f) = await Api("GET", "/api/produccion/flujo?q=99999-9");
        Ver(e4f == 404 || (e4f == 200 && j4f?["ops"]?.AsArray().Count == 0), "flujo de documentos: número desconocido", e4f);
        // impresora inexistente: en la PC (Windows) no imprime nada durante las pruebas
        await Api("PUT", "/api/produccion/config", new { impresora = "NO-EXISTE-SONA-PRUEBA" });
        (e, j) = await Api("POST", "/api/produccion/reimprimir", new { empresa = 6, tipo = "OPR", op = "57558" });
        Ver(e == 200 && j?["ok"]?.GetValue<bool>() == false && S(j?["mensaje"]).Contains("imprimir"), "producción: reimprimir con impresora inexistente avisa", j);
        (e, j) = await Api("GET", "/api/produccion/historial");
        Ver(j?.AsArray().Count(x => S(x!["op"]) == "57558") == 3 && j?.AsArray().Any(x => S(x!["estado"]) == "reasignada" && S(x["picker"]) == "JPERALTA") == true, "producción: historial de asignaciones", j);
        (e, j) = await Api("PUT", "/api/produccion/config", new { impresora = "HP Deposito", imprimirAlAsignar = true, copias = 2 });
        Ver(e == 200 && S(j?["impresora"]) == "HP Deposito" && S(j?["copias"]) == "2", "producción: configurar impresora", j);
        (e, j) = await Api("PUT", "/api/usuarios/OPER2", new { nombre = "Asignador", rol = "produccion", activo = true });
        Ver(e == 200, "rol producción", j);

        // app del picker: "Mis OP"
        var tokAdmin = http.DefaultRequestHeaders.Authorization;
        (e, j) = await Api("POST", "/api/login", new { usuario = "oper1", clave = "pw", dispositivo = "TEST2" });
        (e, _) = await Api("PUT", "/api/usuarios/OPER1", new { nombre = "Operario Uno", rol = "picker", activo = true });
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", S(j?["token"]));
        (e, j) = await Api("GET", "/api/picking/mis-ops");
        Ver(e == 200 && j?["ops"]?.AsArray().Count == 1 && S(j?["ops"]?[0]?["op"]) == "258" && S(j?["ops"]?[0]?["solicitud"]) == "8085-24", "picker: ve sus OP pendientes del WMS", $"{e} {j}");
        (e, j) = await Api("POST", "/api/produccion/asignar", new { empresa = 144, tipo = "OPR", op = "28513", picker = "OPER1" });
        Ver(e == 403, "picker no puede asignar", e);
        (e, j) = await Api("POST", "/api/picking/elegir", new { empresa = 38, tipo = "OPR", op = "258", modo = "consolidado" });
        Ver(e == 200 && fake.Escritorio.Preparar.First(o => o.Nro == "258").Prior == 1 && fake.Escritorio.Preparar.First(o => o.Nro == "258").Picker == "OPER1",
            "picker: elige una OP de su lista y SONA la pone primera en el WMS", $"{e} {j}");
        http.DefaultRequestHeaders.Authorization = tokAdmin;
    }

    {
        var (eo, jo) = await Api("GET", "/api/monitor/ocupacion");
        var dd = jo?["caras"]?.AsArray().FirstOrDefault(x => S(x!["cara"]) == "DD");
        Ver(eo == 200 && dd != null && dd["pos"]?.AsArray().Count > 0 && (jo?["total"]?.GetValue<int>() ?? 0) > 0
            && dd["pos"]!.AsArray().All(p => "varxs".Contains(S(p![2]))), "monitor: semáforo de ocupación por posición", $"{eo} total={jo?["total"]} caras={jo?["caras"]?.AsArray().Count}");
    }

    {
        // actualización de la app de los celulares
        using var mp = new MultipartFormDataContent { { new StringContent("9.9"), "version" }, { new StringContent("Prueba"), "notas" }, { new StringContent("false"), "obligatoria" } };
        mp.Add(new ByteArrayContent(Encoding.ASCII.GetBytes("PK\u0003\u0004apk de prueba")), "apk", "SONA_Deposito_9.9.apk");
        var ra = await http.PostAsync("/api/app/subir", mp);
        using var anon = new HttpClient { BaseAddress = http.BaseAddress };
        var vj = JsonNode.Parse(await anon.GetStringAsync("/api/app/version"));
        var rd = await http.GetAsync("/api/app/descargar");
        var bd = await rd.Content.ReadAsByteArrayAsync();
        Ver(ra.IsSuccessStatusCode && S(vj?["version"]) == "9.9" && S(vj?["notas"]) == "Prueba" && rd.IsSuccessStatusCode && bd.Length > 4 && bd[0] == 'P' && bd[1] == 'K',
            "app: publicar APK desde el panel, versión pública y descarga", $"{ra.StatusCode} {vj} {rd.StatusCode}");
        var (ee, je) = await Api("GET", "/api/app/equipos");
        Ver(ee == 200, "app: lista de celulares con su versión", ee);
    }

    {
        // reimprimir la etiqueta del contenedor (QR): vista previa Zebra y A4, página para imprimir desde la PC e impresora del servidor
        var r1 = await http.GetAsync("/api/contenedores/I-10/etiqueta.png?formato=zebra");
        var z = await r1.Content.ReadAsByteArrayAsync(); File.WriteAllBytes("/tmp/claude-0/etiqueta_zebra.png", z);
        var r2 = await http.GetAsync("/api/contenedores/I-10/etiqueta.png?formato=a4");
        var a4 = await r2.Content.ReadAsByteArrayAsync(); File.WriteAllBytes("/tmp/claude-0/etiqueta_a4.png", a4);
        Ver(r1.IsSuccessStatusCode && r2.IsSuccessStatusCode && z.Length > 1000 && a4.Length > z.Length, "etiqueta del contenedor: Zebra y A4 con QR", $"{(int)r1.StatusCode} {(int)r2.StatusCode}");
        var r3 = await http.GetStringAsync("/api/contenedores/I-10/etiqueta.html?formato=a4");
        Ver(r3.Contains("A4 landscape") && r3.Contains("class=\"pg h\"") && r3.Contains("window.print"), "etiqueta del contenedor: A4 horizontal para imprimir desde la PC", r3.Length);
        var r4 = await http.GetStringAsync("/api/contenedores/I-10/etiqueta.html?formato=zebra");
        File.WriteAllText("/tmp/claude-0/etiqueta_zebra.html", r4);
        Ver(r4.Contains("size: 100mm 70mm") && r4.Contains("data:image/png"), "etiqueta del contenedor: Zebra de 100 x 70 mm para imprimir desde la PC", r4.Length);
        var (ei, ji) = await Api("POST", "/api/contenedores/I-10/imprimir", new { formato = "zebra" });
        Ver(ei == 200 && ji?["formato"] != null && (OperatingSystem.IsWindows() || S(ji?["error"]).Contains("Windows")), "etiqueta del contenedor: imprimir en el servidor", ji);
        (ei, ji) = await Api("PUT", "/api/impresion/etiquetas", new { impresoraZebra = "ZEBRA ZD220", impresoraA4 = "HP A4" });
        (ei, ji) = await Api("GET", "/api/impresion/etiquetas");
        Ver(S(ji?["impresoraZebra"]) == "ZEBRA ZD220" && S(ji?["impresoraA4"]) == "HP A4", "etiqueta del contenedor: impresoras Zebra y A4", ji);
        var (en, jn) = await Api("GET", "/api/contenedores/NOEXISTE-9/etiqueta.png");
        Ver(en == 404, "etiqueta del contenedor: contenedor inexistente", en);
        var qr = Sona.EtiquetaContenedor.Qr("PP-195774");
        Ver(qr.GetLength(0) >= 21, "QR del contenedor", qr.GetLength(0));
    }
    {
        // artículos por empresa (maestro de LES copiado en SONA)
        var (ec, jc) = await Api("POST", "/api/articulos/catalogo/actualizar");
        for (var i = 0; i < 50 && (jc?["actualizando"]?.GetValue<bool>() ?? true); i++) { await Task.Delay(100); (ec, jc) = await Api("GET", "/api/articulos/catalogo?limite=1"); jc = jc?["estado"]; }
        (ec, jc) = await Api("GET", "/api/articulos/catalogo?empresa=6");
        Ver(ec == 200 && jc?["items"]?.AsArray().Count == 2 && jc?["items"]?.AsArray().All(x => S(x!["empresaNombre"]).Length > 0) == true, "artículos por empresa: los de Notro Bazar", jc);
        (ec, jc) = await Api("GET", "/api/articulos/catalogo?empresa=6&conStock=1&q=ariel");
        Ver(jc?["items"]?.AsArray().Count == 1 && S(jc?["items"]?[0]?["articulo"]) == "PG-AR1", "artículos por empresa: con stock y texto", jc);
        (ec, jc) = await Api("GET", "/api/articulos/catalogo");
        Ver(jc?["total"]?.GetValue<long>() == 3 && jc?["empresas"]?.AsArray().Count == 2, "artículos por empresa: todas las empresas", jc);
    }
    {
        // estado real del contenedor en el WMS (handheld: Ver un hueco › Datos de Ubicaciones)
        fake.BLOQUEADOS["I-10"] = "ROTURA";
        var (eb, jb) = await Api("GET", "/api/consulta/I-10");
        Ver(eb == 200 && jb?["contenedor"]?["disponible"]?.GetValue<bool>() == false && S(jb?["contenedor"]?["estadoTexto"]).Contains("ROTURA"), "estado del WMS: contenedor bloqueado (ROTURA)", jb);
        (eb, jb) = await Api("GET", "/api/consulta/PG-AR1");
        var conts = jb?["articulo"]?["contenedores"]?.AsArray().OfType<JsonObject>().ToList() ?? new();
        Ver(eb == 200 && conts.Count > 0 && conts.Any(x => S(x["contenedor"]) == "I-10" && x["disponible"]?.GetValue<bool>() == false && S(x["estadoTexto"]) == "ROTURA")
            && conts.Where(x => S(x["contenedor"]) != "I-10").All(x => x["disponible"]?.GetValue<bool>() == true), "estado del WMS: en la consulta de artículo cada contenedor con su estado", jb);
        fake.BLOQUEADOS.Clear();
        (eb, jb) = await Api("GET", "/api/consulta/I-10");
        Ver(jb?["contenedor"]?["disponible"]?.GetValue<bool>() == true, "estado del WMS: vuelve a disponible", jb);
    }
    {
        // permisos por usuario (perfiles + agregados / quitados)
        var (ep, jp) = await Api("GET", "/api/permisos");
        Ver(ep == 200 && jp?["perfiles"]?.AsArray().Count >= 7 && jp?["catalogo"]?.AsArray().Any(x => S(x!["codigo"]) == "transferir") == true, "permisos: catálogo y perfiles de sistema", ep);
        (ep, jp) = await Api("PUT", "/api/perfiles/jefe_turno", new { nombre = "Jefe de turno", permisos = new[] { "consultar", "transferir", "ajustar", "autorizar" } });
        Ver(ep == 200 && jp?["perfiles"]?.AsArray().Any(x => S(x!["codigo"]) == "jefe_turno") == true, "permisos: crear un perfil propio", ep);
        (ep, jp) = await Api("PUT", "/api/usuarios/OPER1/permisos", new { perfiles = new[] { "picker" }, mas = new[] { "transferir" }, menos = new[] { "picking" } });
        Ver(ep == 200 && jp?["efectivos"]?.AsArray().Any(x => S(x) == "transferir") == true && jp?["efectivos"]?.AsArray().Any(x => S(x) == "picking") == false, "permisos: agregar y quitar a un usuario", jp);
        var tokA = http.DefaultRequestHeaders.Authorization;
        (ep, jp) = await Api("POST", "/api/login", new { usuario = "oper1", clave = "pw", dispositivo = "TEST4" });
        var permLogin = jp?["permisos"]?.AsArray().Select(x => S(x)).ToList() ?? new();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", S(jp?["token"]));
        var (e1, _) = await Api("POST", "/api/picking/iniciar", new { modo = "consolidado" });
        var (e2, j2) = await Api("POST", "/api/movimientos", new { movimientos = new[] { new { id = Guid.NewGuid().ToString(), tipo = "transferencia", contenedor = "I-10", destino = "DD-007-05", nivel = 5 } } });
        var (e3, j3) = await Api("POST", "/api/movimientos", new { movimientos = new[] { new { id = Guid.NewGuid().ToString(), tipo = "cambio_estado", contenedor = "I-10", disponible = false, motivo = "CUARENTENA" } } });
        var (e4, j4) = await Api("GET", "/api/yo");
        http.DefaultRequestHeaders.Authorization = tokA;
        Ver(permLogin.Contains("transferir") && e1 == 403 && e2 == 200 && e3 == 403 && j4?["permisos"]?.AsArray().Any(x => S(x) == "transferir") == true,
            "permisos: el servidor los controla (sin picking, con transferir, sin cambiar estado)", $"{e1} {e2} {j2} {e3} {j3}");
        (ep, _) = await Api("PUT", "/api/usuarios/OPER1/permisos", new { perfiles = new[] { "picker" }, mas = Array.Empty<string>(), menos = Array.Empty<string>() });
        (ep, jp) = await Api("DELETE", "/api/perfiles/jefe_turno");
        Ver(ep == 200, "permisos: borrar perfil propio", ep);
    }

    // ---------------------------------------------------------------- módulo de ingreso (LES Ingresos pendientes + WMS)
    {
        var fi = fake.Ingreso;
        (e, j) = await Api("GET", "/api/ingresos");
        Ver(e == 200, "ingreso: sin cargar nada entra al LES con el usuario y la clave del WMS", $"{e} {j}");
        (e, j) = await Api("PUT", "/api/yo/les", new { usuario = "SLEDESM6", clave = "mal" });
        Ver(e == 428 && S(j?["error"]).Contains("no aceptó"), "ingreso: usuario del LES incorrecto no se guarda (y no cierra la sesión de SONA)", $"{e} {j}");
        (e, j) = await Api("PUT", "/api/yo/les", new { usuario = "SLEDESM6", clave = "pw" });
        Ver(e == 200 && j?["cargado"]?.GetValue<bool>() == true && S(j?["usuario"]) == "SLEDESM6", "ingreso: usuario del LES guardado (cifrado)", $"{e} {j}");
        (e, j) = await Api("GET", "/api/ingresos");
        Ver(e == 200 && j?["ingresos"]?.AsArray().Count == 1 && S(j?["ingresos"]?[0]?["nroRecepcion"]) == "2877" && j?["sla"]?.AsArray().Count == 2, "ingreso: bandeja del LES con SLA", $"{e} {j}");
        (e, j) = await Api("GET", "/api/ingresos/5001");
        Ver(e == 200 && S(j?["articulos"]?[0]?["codigo"]) == "LO4236" && j?["popFaltantes"]?.AsArray().Any(x => S(x) == "LO4236") == true && S(j?["cliente"]) == "Cuyo - Promo",
            "ingreso: ficha con la precarga del LES", $"{e} {j}");
        (e, j) = await Api("PUT", "/api/ingresos/5001/descarga", new { pallets = 3, deposito = "SONA", observacion = "llegó bien", aDesglosar = false });
        (e, j) = await Api("POST", "/api/ingresos/5001/descarga/confirmar");
        Ver(e == 400 && S(j?["error"]).Contains("remito"), "ingreso: no confirma la descarga sin el remito firmado", $"{e} {j}");
        async Task<(int, JsonNode?)> Foto(string tipo, string? codigo)
        {
            using var mp = new MultipartFormDataContent { { new StringContent(tipo), "tipo" } };
            if (codigo != null) mp.Add(new StringContent(codigo), "codigo");
            var img = new ByteArrayContent(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 0xFF, 0xD9 }); img.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            mp.Add(img, "foto", tipo + ".jpg");
            var rr = await http.PostAsync("/api/ingresos/5001/fotos", mp);
            JsonNode? jj = null; try { jj = JsonNode.Parse(await rr.Content.ReadAsStringAsync()); } catch { }
            return ((int)rr.StatusCode, jj);
        }
        (e, j) = await Foto("remito", null);
        Ver(e == 200 && j?["subida"]?.GetValue<bool>() == false, "ingreso: foto del remito firmado guardada", $"{e} {j}");
        (e, j) = await Foto("pallet", null);
        Ver(e == 200 && j?["subida"]?.GetValue<bool>() == true && fi.FotosSubidas == 1, "ingreso: foto de pallet subida al LES", $"{e} {j}");
        (e, j) = await Foto("pop", "LO4236");
        Ver(e == 200 && j?["subida"]?.GetValue<bool>() == true, "ingreso: foto POP subida al LES", $"{e} {j}");
        var fid = (await Api("GET", "/api/ingresos/5001")).J?["fotos"]?[0]?["id"];
        var rFoto = await http.GetAsync("/api/ingresos/fotos/" + fid);
        Ver(rFoto.IsSuccessStatusCode && rFoto.Content.Headers.ContentType?.MediaType == "image/jpeg", "ingreso: SONA guarda las fotos", rFoto.StatusCode);
        (e, j) = await Api("POST", "/api/ingresos/5001/descarga/confirmar");
        var desc = fi.Descarga?.RootElement;
        Ver(e == 200 && fi.EstadoLes == "PEND INGRESO WMS" && desc?.GetProperty("Deposito").GetString() == "SONA" && desc?.GetProperty("CantidadPallets").GetString() == "3"
            && desc?.GetProperty("ObservacionCambioEstado").GetString() == "llegó bien" && desc?.GetProperty("Articulos")[0].GetProperty("Codigo").GetString() == "LO4236"
            && fi.Log.Any(l => l.Contains("les descarga") && l.EndsWith("remito=1")),
            "ingreso: descarga confirmada en el LES con pallets, depósito y remito", $"{e} {j} {string.Join(" / ", fi.Log.TakeLast(3))}");

        (e, j) = await Api("POST", "/api/ingresos/5001/articulos/verificar");
        Ver(e == 200 && S(j?["articulos"]?[0]?["estado"]) == "no_configurado", "ingreso: detecta artículo sin configurar en el WMS", $"{e} {j}");
        (e, j) = await Api("POST", "/api/ingresos/5001/articulos/configurar", new { codigo = "LO4236" });
        Ver(e == 200 && j?["ok"]?.GetValue<bool>() == true && fi.Configurado, "ingreso: configura el artículo (ficha + zonificación)", $"{e} {j} {string.Join(" / ", fi.Log.TakeLast(3))}");
        (e, j) = await Api("POST", "/api/ingresos/5001/articulos/verificar");
        Ver(S(j?["articulos"]?[0]?["estado"]) == "ok", "ingreso: artículo ok", j);
        (e, j) = await Api("GET", "/api/ingresos/5001/proveedores");
        Ver(e == 200 && S(j?["elegido"]) == "326", "ingreso: proveedor del WMS por nombre (cortado a 20)", $"{e} {j}");

        (e, j) = await Api("PUT", "/api/ingresos/5001/contenedores", new object[] {
            new { contenedor = "pc-201425", articulo = "LO4236", cantidad = 100, lote = "1" }, new { contenedor = (string?)null, articulo = "LO4236", cantidad = 100, lote = "1" },
            new { contenedor = (string?)null, articulo = "LO4236", cantidad = 100, lote = "1" } });
        Ver(e == 200, "ingreso: contenedores cargados", $"{e} {j}");
        (e, j) = await Api("POST", "/api/ingresos/5001/contenedores/sona", new { prefijo = "PP" });
        Ver(e == 200 && j?["contenedores"]?.AsArray().Count == 2 && S(j?["contenedores"]?[0]).StartsWith("PP-"), "ingreso: SONA genera los contenedores faltantes (numeración LES)", $"{e} {j}");
        (e, j) = await Api("POST", "/api/ingresos/5001/contenedores/les", new { codigo = "LO4236", prefijo = "PC", cantidad = 2 });
        Ver(e == 200 && j?["contenedores"]?.AsArray().Count == 2, "ingreso: generar etiquetas en el LES devuelve los contenedores", $"{e} {j}");
        (e, j) = await Api("POST", "/api/ingresos/5001/recibir");
        Ver(e == 400 && S(j?["error"]).Contains("recepción"), "ingreso: no recibe sin recepción en el WMS", $"{e} {j}");
        (e, j) = await Api("POST", "/api/ingresos/5001/recepcion", new { proveedor = "326", centroEmisor = 1, letra = "r", fechaEmision = "2026-10-22" });
        var grabo = fi.Log.LastOrDefault(l => l.StartsWith("hingeve2 grabar")) ?? "";
        Ver(e == 200 && S(j?["numero"]) == "930" && j?["comprobante"]?.GetValue<bool>() == true && grabo.Contains("rem=19963") && grabo.Contains("ce=1") && grabo.Contains("letra=R")
            && grabo.Contains("doc=2877") && grabo.Contains("fecha=22/10/2026") && fi.Log.Any(l => l.StartsWith("hingeve2 renglon LO4236 300")),
            "ingreso: recepción 930 creada en el WMS (remito, doc. externo, renglones de los pallets)", $"{e} {j} {grabo}");
        (e, j) = await Api("POST", "/api/ingresos/5001/recepcion", new { proveedor = "326" });
        Ver(e == 409, "ingreso: no crea dos veces la recepción", e);
        (e, j) = await Api("POST", "/api/ingresos/5001/recibir");
        Ver(e == 200 && S(j?["recibidos"]) == "3" && fi.Recibidos.Count == 3 && fi.Recibidos[0] == "PC-201425", "ingreso: recibe los 3 contenedores por handheld", $"{e} {j} {string.Join(" / ", fi.Log.TakeLast(4))}");
        (e, j) = await Api("POST", "/api/ingresos/5001/recibir");
        Ver(e == 400, "ingreso: nada pendiente de recibir", e);
        var rc = await http.GetAsync("/api/ingresos/5001/caratulas.pdf");
        Ver(rc.IsSuccessStatusCode && (await rc.Content.ReadAsStringAsync()).StartsWith("%PDF"), "ingreso: carátulas del LES (PDF)", rc.StatusCode);
        var rci = await http.GetAsync("/api/ingresos/5001/caratulas/imprimir.html");
        var tci = await rci.Content.ReadAsStringAsync();
        Ver(rci.IsSuccessStatusCode && tci.Contains("data:image/png") && tci.Contains("window.print"), "ingreso: carátulas para imprimir desde la PC (diálogo del navegador)", $"{rci.StatusCode} {tci[..Math.Min(200, tci.Length)]}");
        // mis credenciales
        (e, j) = await Api("GET", "/api/yo/credenciales");
        var sis = j?["sistemas"]?.AsArray();
        Ver(e == 200 && sis?.Count == 3 && sis.Any(x => S(x!["codigo"]) == "les_ingresos" && x["cargada"]?.GetValue<bool>() == true && S(x["usuario"]) == "SLEDESM6")
            && sis.Any(x => S(x!["codigo"]) == "wms" && x["cargada"]?.GetValue<bool>() == true) && !(j!.ToJsonString().Contains("\"clave\"")),
            "mis credenciales: lista WMS, LES Ingresos y e-TMS (sin devolver claves)", j);
        (e, j) = await Api("POST", "/api/yo/credenciales/les_ingresos/probar");
        Ver(e == 200 && j?["ok"]?.GetValue<bool>() == true, "mis credenciales: probar el LES", j);
        (e, j) = await Api("PUT", "/api/yo/credenciales/les_ingresos", new { usuario = "SLEDESM6", clave = "otra" });
        Ver(e == 428, "mis credenciales: si no entra no se guarda", $"{e} {j}");
        (e, j) = await Api("PUT", "/api/yo/credenciales/wms", new { usuario = "X", clave = "y" });
        Ver(e == 400, "mis credenciales: el WMS no se carga ahí (es el login de SONA)", $"{e} {j}");
        (e, j) = await Api("POST", "/api/ingresos/5001/cerrar");
        Ver(e == 200 && fi.EstadoLes == "PEND VALIDAR", "ingreso: procesado en el LES (Pend. validar)", $"{e} {j}");
        (e, j) = await Api("GET", "/api/ingresos/5001");
        Ver(S(j?["sona"]?["recepcionWms"]) == "930" && j?["contenedores"]?.AsArray().All(x => x!["recibido"]?.GetValue<bool>() == true) == true && j?["eventos"]?.AsArray().Count >= 10,
            "ingreso: queda todo registrado en SONA", j?["eventos"]?.AsArray().Count);
        var rh = await http.GetAsync("/ingresos");
        Ver(rh.IsSuccessStatusCode && (await rh.Content.ReadAsStringAsync()).Contains("SONA Ingreso"), "ingreso: página /ingresos", rh.StatusCode);

        var rz = await http.PostAsync("/api/ingresos/diagnostico", null);
        var zb = await rz.Content.ReadAsByteArrayAsync();
        var entradas = new List<string>(); var todo = new StringBuilder();
        try
        {
            using var za = new System.IO.Compression.ZipArchive(new MemoryStream(zb));
            foreach (var en in za.Entries) { entradas.Add(en.FullName); using var sr = new StreamReader(en.Open()); todo.Append(sr.ReadToEnd()); }
        }
        catch { }
        Ver(rz.IsSuccessStatusCode && entradas.Any(x => x.Contains("diagnostico") && x.EndsWith("GetAll.json")) && entradas.Any(x => x.Contains("hingeve2"))
            && entradas.Any(x => x.Contains("tgrpart")) && entradas.Any(x => x.Contains("hingetqextrf")) && entradas.Any(x => x.Contains("ingreso_5001") && x.Contains("DownloadPallets"))
            && entradas.Any(x => x.EndsWith("000_notas.txt")) && !todo.ToString().Contains("_PASSWORD=pw") && !todo.ToString().Contains("Password=pw")
            && !fi.Log.Any(l => l.StartsWith("hingeve2 grabar") && fi.Log.IndexOf(l) > fi.Log.FindLastIndex(x => x.StartsWith("les procesar"))),
            "ingreso: diagnóstico solo lectura en zip (LES + WMS + capturas de lo hecho, sin claves)", $"{rz.StatusCode} {entradas.Count}: {string.Join(", ", entradas.Take(60))}");

        var tokAdm = http.DefaultRequestHeaders.Authorization;
        (e, j) = await Api("POST", "/api/login", new { usuario = "oper1", clave = "pw", dispositivo = "TEST3" });
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", S(j?["token"]));
        (e, j) = await Api("GET", "/api/ingresos");
        Ver(e == 403, "ingreso: solo analistas y administradores", e);
        http.DefaultRequestHeaders.Authorization = tokAdm;
    }

    using var ws = new System.Net.WebSockets.ClientWebSocket();
    await ws.ConnectAsync(new Uri("ws://127.0.0.1:18080/api/eventos?token=" + Uri.EscapeDataString(http.DefaultRequestHeaders.Authorization!.Parameter!)), default);
    var buf = new byte[4096];
    var r = await ws.ReceiveAsync(buf, new CancellationTokenSource(5000).Token);
    Ver(Encoding.UTF8.GetString(buf, 0, r.Count).Contains("hola"), "eventos en vivo (WebSocket)");
}
catch (Exception ex) { Falla("excepción: " + ex); }

Console.WriteLine(fallas == 0 ? "\nTODO OK" : $"\n{fallas} FALLAS");
if (Environment.GetEnvironmentVariable("SONA_DEMO") == "1") { Console.WriteLine("DEMO http://127.0.0.1:18080/produccion"); await Task.Delay(TimeSpan.FromMinutes(20)); }
if (fallas > 0) { Console.WriteLine("--- WMS de prueba (últimas) ---"); foreach (var l in fake.Log.TakeLast(25)) Console.WriteLine(l); }
await app.StopAsync();
await appFake.StopAsync();
return fallas == 0 ? 0 : 1;
