using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Sona.Pruebas;

/// <summary>WMS de prueba: sirve las páginas reales capturadas del handheld y simula el estado de huecos y contenedores.</summary>
public sealed class FakeWms
{
    static string P(string n) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "paginas", n), Encoding.UTF8);
    readonly string LOGIN = P("login.html"), MENU = P("menu.html"), CONS = P("cons.html"), AJUSTES = P("ajustes.html"), REUB1 = P("reub1.html"),
        REUB2 = P("reub2.html"), CAMB = P("camb.html"), GUARD1 = P("guard1.html"), GUARD5 = P("guard5.html"), INICIO = P("inicio.html");

    const int CAP = 4;
    readonly Dictionary<long, (string Cara, int Col)> UBIC = new() { [96456] = ("DC", 4), [50001] = ("DD", 1), [50002] = ("DD", 2), [50003] = ("IG", 1), [50004] = ("FI", 15), [98014] = ("DD", 7) };
    readonly Dictionary<(string, int, int), string?[]> HUECOS = new();
    /// <summary>Contenedores bloqueados en el WMS (Datos de Ubicaciones) con su motivo.</summary>
    public readonly Dictionary<string, string> BLOQUEADOS = new();
    readonly Dictionary<string, (string Cli, string Art, string Desc, double Cant, double M3)> CONT = new()
    {
        ["PC-199417"] = ("Mburucuyá Natural", "BIM-123", "PAN LACTAL 500G", 1200, 1.8),
        ["I-10"] = ("Notro Bazar", "PG-AR1", "ARIEL 3KG", 100, 2.0), ["I-11"] = ("Notro Bazar", "PG-AR1", "ARIEL 3KG", 60, 1.5),
        ["I-12"] = ("Notro Bazar", "PG-OTRO", "PANTENE", 3, 0.05), ["I-13"] = ("Notro Bazar", "PG-OTRO", "PANTENE", 50, 1.0), ["I-177719"] = ("Notro Bazar", "PG-AR1", "ARIEL 3KG", 10, 0.3),
        ["I-1"] = ("Notro Bazar", "PG-AR1", "ARIEL 3KG", 10, 2), ["I-2"] = ("Notro Bazar", "PG-AR1", "ARIEL 3KG", 10, 2),
        ["I-3"] = ("Notro Bazar", "PG-AR1", "ARIEL 3KG", 10, 2), ["I-4"] = ("Notro Bazar", "PG-AR1", "ARIEL 3KG", 10, 2),
        // los 8 de DD-007-03 (2.12: transferir la posición completa)
        ["PP-195774"] = ("Notro Bazar", "PG-DD7", "EXHIBIDOR", 1, 0.5), ["PP-195771"] = ("Notro Bazar", "PG-DD7", "EXHIBIDOR", 1, 0.5), ["I-192099"] = ("Notro Bazar", "PG-DD7", "EXHIBIDOR", 1, 0.5),
        ["I-191795"] = ("Notro Bazar", "PG-DD7", "EXHIBIDOR", 1, 0.5), ["I-191794"] = ("Notro Bazar", "PG-DD7", "EXHIBIDOR", 1, 0.5), ["I-191793"] = ("Notro Bazar", "PG-DD7", "EXHIBIDOR", 1, 0.5),
        ["I-181873"] = ("Notro Bazar", "PG-DD7", "EXHIBIDOR", 1, 0.5), ["I-191792"] = ("Notro Bazar", "PG-DD7", "EXHIBIDOR", 1, 0.5),
    };
    /// <summary>2.12: simula que el WMS no pagina (siempre devuelve la primera página) y que el buscador no informa la posición.</summary>
    public bool PaginacionRota, BuscadorSinPosicion;
    /// <summary>Lista de la OP con faltante: el WMS muestra otro campo visible (con "1") y lee ese, no _I.</summary>
    public bool ListaConFaltante;
    public int Ocupados(string cara, int col, int niv) => HUECOS.TryGetValue((cara, col, niv), out var h) ? h.Count(x => x != null) : 0;
    readonly Dictionary<string, string> ESTADO = new();
    readonly HashSet<string> PEND = new();
    public readonly List<string> Log = new();
    // picking de prueba: OP 2051 consolidada con 2 renglones
    readonly List<(string Art, string Desc, string Cara, int Col, int Niv, long Ubic, double Cant, string Pallet)> PICK = new()
    {
        ("PG-AR1", "ARIEL 3KG", "DD", 2, 1, 50002, 2, "I-10"), ("PG-OTRO", "PANTENE", "DD", 1, 1, 50001, 1, "I-1"),
    };
    public bool AvisoLote;
    public readonly FakeEscritorio Escritorio = new();
    public readonly FakeIngreso Ingreso = new();
    int _pickIdx; bool _pickHecho; string _pickCont = ""; int _ultimoPp = 201349;
    readonly Dictionary<string, List<object>> BULTOS = new();

    public FakeWms()
    {
        H("DC", 4, 2)[0] = "PC-199417";
        var lleno = H("DD", 1, 1); lleno[0] = "I-1"; lleno[1] = "I-2"; lleno[2] = "I-3"; lleno[3] = "I-4";
        H("DD", 2, 1)[0] = "I-10";
        // DD-007-03 como en la captura del 19/10: 50 huecos con 8 contenedores, el handheld los muestra de a 5 (hay que paginar)
        var dd7 = HUECOS[("DD", 7, 3)] = new string?[50];
        foreach (var (i, cont) in new[] { (0, "PP-195774"), (9, "PP-195771"), (10, "I-192099"), (11, "I-191795"), (12, "I-191794"), (13, "I-191793"), (15, "I-181873"), (16, "I-191792") }) dd7[i] = cont;
        HUECOS[("DD", 7, 5)] = new string?[50];   // destino con lugar para la posición completa
        var ig = H("IG", 1, 1); ig[0] = "X-1"; ig[1] = "X-2"; ig[2] = "X-3"; ig[3] = "X-4";   // el handheld muestra 4 huecos llenos, pero en el WMS web la profundidad es 20
    }

    string?[] H(string c, int col, int n) { if (!HUECOS.TryGetValue((c, col, n), out var h)) HUECOS[(c, col, n)] = h = new string?[CAP]; return h; }
    (string, int, int)? Donde(string c) { foreach (var (k, v) in HUECOS) if (v.Contains(c)) return k; return null; }

    static string SetVal(string page, string name, string val) =>
        new Regex($"(<input[^>]*name=\"{Regex.Escape(name)}\"[^>]*?\\svalue=\")[^\"]*(\")").Replace(page, m => m.Groups[1].Value + WebUtility.HtmlEncode(val) + m.Groups[2].Value, 1);
    static string SetAction(string page, string act) => new Regex("(<form[^>]*action=\")[^\"]*(\")").Replace(page, m => m.Groups[1].Value + act + m.Groups[2].Value, 1);

    /// <summary>"Ver un hueco" como el real: de a 5 huecos por página (_ITEMS) con los totales (_I, _CANTVACIAS, _CANTINH) y el botón -> (E'SGTE').</summary>
    string HuecoHtml(string c, int col, int n, int pag = 1)
    {
        var hs = HUECOS.TryGetValue((c, col, n), out var h) ? h : new string?[CAP];
        var desde = (pag - 1) * 5;
        // como el real (captura del 19/10): primero los huecos ocupados y después los vacíos
        var filas = string.Concat(hs.Select((x, i) => (x, i)).OrderBy(t => t.x == null).Skip(desde).Take(5).Select((t, k) => $"<input type=\"hidden\" name=\"_COD_DI4_UB_{k + 1:0000}\" value=\"{t.i + 1}\"><input type=\"hidden\" name=\"_UBCCPL_{k + 1:0000}\" value=\"{t.x}\"><input type=\"hidden\" name=\"_COD_MOT_IN_{k + 1:0000}\" value=\"No\">"));
        var totales = Hid("_PAG", pag.ToString()) + Hid("_I", hs.Length.ToString()) + Hid("_ITEMS", "5") + Hid("_CANTINH", "0,00") + Hid("_CANTVACIAS", hs.Count(x => x == null) + ",00");
        // tal cual el HTML real (22/10): type sin comillas y value="->" / "<-" sin escapar
        var botones = "<input type=SUBMIT name=\"BUTTON2\" value=\"<-\" gxevent=\"E\\'ANT\\'.\" onclick=\"GX_setgridevent( 20,   6);GX_setevent('E\\'ANT\\'.');\">" +
                      "<input type=SUBMIT name=\"BUTTON1\" value=\"->\" gxevent=\"E\\'SGTE\\'.\" onclick=\"GX_setgridevent( 20,   6);GX_setevent('E\\'SGTE\\'.');\">";
        return $"<html><body><form name=\"MAINFORM\" method=\"POST\" action=\"hselconsubicac.aspx?X,0,{c},{col:000},{n:00}\"><input type=\"hidden\" name=\"_COD_DI1_UBSF\" value=\"{c}\"><input type=\"hidden\" name=\"_COD_DI2_UBSF\" value=\"{col}\"><input type=\"hidden\" name=\"_COD_DI3_UBSF\" value=\"{n}\"><input type=\"hidden\" name=\"_AREADESCRIPCION\" value=\"RESERVA\">{filas}{totales}{botones}</form></body></html>";
    }

    public WebApplication Crear(int puerto)
    {
        var b = WebApplication.CreateBuilder();
        b.WebHost.UseUrls($"http://127.0.0.1:{puerto}");
        b.Logging.ClearProviders();
        var app = b.Build();
        app.Run(async ctx => { lock (this) { } await Atender(ctx); });
        return app;
    }

    static string Hid(string n, string v) => $"<input type=\"hidden\" name=\"{n}\" value=\"{WebUtility.HtmlEncode(v)}\">";
    static string Txt(string n, string v) => $"<input type=\"text\" name=\"{n}\" value=\"{WebUtility.HtmlEncode(v)}\">";
    static string Btn(string n, string v, string ev) => $"<input type=\"submit\" name=\"{n}\" value=\"{v}\" gxevent=\"{ev}\">";
    static string Form(string accion, string cuerpo, string texto = "") => $"<html><body>{texto}<form name=\"MAINFORM\" method=\"POST\" action=\"{accion}\">{cuerpo}</form></body></html>";

    static async Task Html(HttpContext c, string html, int estado = 200, string? loc = null, bool cookie = false)
    {
        c.Response.StatusCode = estado;
        if (loc != null) c.Response.Headers.Location = loc;
        if (cookie) c.Response.Headers.SetCookie = "ASP.NET_SessionId=abc; path=/";
        c.Response.ContentType = "text/html; charset=utf-8";
        await c.Response.WriteAsync(html);
    }

    async Task Atender(HttpContext c)
    {
        var p = c.Request.Path.Value ?? "";
        var qs = c.Request.QueryString.Value?.TrimStart('?') ?? "";
        var f = new Dictionary<string, string>();
        if (c.Request.Method == "POST" && c.Request.HasFormContentType)
            foreach (var (k, v) in await c.Request.ReadFormAsync()) f[k] = v.ToString();
        var post = c.Request.Method == "POST";
        lock (Log) Log.Add($"{c.Request.Method} {p}?{qs} {string.Join(" ", f.Where(x => x.Key is "_PLTCOD" or "_NEWPLTCOD" or "_CANTCAJAS" or "_NUEVAUBIC" or "_STRCANTI" or "_SR" or "_OPCION" or "_NUEVAUBIC" or "_NIVEL" or "_MTSCOD" or "_UBICMNEMOT" or "_NIV").Select(x => x.Key + "=" + x.Value))} {f.GetValueOrDefault("_EventName")}");

        if (p.StartsWith("/Api/Buscador/GetInfoBusqueda"))
        {
            var sp = c.Request.Query["storeProcedure"].ToString(); var v = c.Request.Query["valor"].ToString().ToUpperInvariant();
            string Pos(string cont) => !BuscadorSinPosicion && Donde(cont) is { } k ? $"{k.Item1}-{k.Item2:000}-{k.Item3:00}" : "";
            var bloques = new List<string>();
            if (sp.Contains("Contenedor") && CONT.TryGetValue(v, out var ci))
                bloques.Add($"CLIENTE: {ci.Cli}\nCODIGO_ARTICULO: {ci.Art}\nDESCRIPCION_ARTICULO: {ci.Desc}\nCONTENEDOR: {v}\nCANTIDAD: {ci.Cant}\nM3: {ci.M3.ToString(System.Globalization.CultureInfo.InvariantCulture)}\nPOS: {Pos(v)}");
            if (sp.Contains("Articulo"))
                foreach (var (k, x) in CONT.Where(x => x.Value.Art == v))
                    bloques.Add($"CLIENTE: {x.Cli}\nCODIGO_ARTICULO: {x.Art}\nDESCRIPCION_ARTICULO: {x.Desc}\nCONTENEDOR: {k}\nCANTIDAD: {x.Cant}\nPOS: {Pos(k)}");
            if (sp.Contains("Pedido") && v == "16481-29")
                bloques.Add("NroSolicitud: 16481-29\nCliente: Notro Bazar\nProyecto: Pedidos POP FY2627\nPDV: YAG0018 Yaguar Moreno\nEstado: A realizar\nViaje: 97923\n" +
                            "Bultos:  B1 - 3492885 - 125cm x50cm x30cm // B2 - 3493923 - 40cm x50cm x30cm \nArticulos: PG-AR1 x 2 // BIM-123 x 1\nPreparado: SI\nDespachado: NO");
            c.Response.ContentType = "application/json";
            await c.Response.WriteAsync(JsonSerializer.Serialize(new { Data = bloques.Count == 0 ? Array.Empty<object>() : new object[] { new { ResultadoTXT = string.Join("\n//-----\n", bloques) } } }));
            return;
        }
        var pg = p.StartsWith("/sglwms_prod/") ? p["/sglwms_prod/".Length..] : p.TrimStart('/');
        var parts = qs.Split(',');
        if (pg == "hdis.aspx")
        {
            if (post && f.GetValueOrDefault("_USR") is "SLEDESM6" or "OPER1" && f.GetValueOrDefault("_PASSWORD") == "pw")
            { await Html(c, "", 303, $"hmenurf.aspx?{f["_USR"]},0,", true); return; }
            await Html(c, LOGIN); return;
        }
        if (pg == "hinicio.aspx")
        {
            if (post && f.GetValueOrDefault("_PASSWORD") == "pw") { await Html(c, "<html>Bienvenido</html>", 200, null, true); return; }
            await Html(c, INICIO); return;
        }
        if (await Escritorio.Atender(c, pg, f, post)) return;
        if (await Ingreso.Atender(c, pg, f, post, qs)) return;
        if (pg == "hvolumenes.aspx")
        {
            if (!post) { await Html(c, "<html><form name=\"MAINFORM\" method=\"POST\" action=\"hvolumenes.aspx\"><input type=\"text\" name=\"_COD_DI1_UB\" value=\"\"><input type=\"submit\" name=\"SEARCHBUTTON\" value=\"Buscar\" gxevent=\"E'SEARCH'.\"></form></html>"); return; }
            var cara = f.GetValueOrDefault("_COD_DI1_UB") ?? ""; var sb = new StringBuilder(); var i = 0;
            foreach (var (ub, (cc, col)) in UBIC.Where(x => x.Value.Cara == cara))
                for (var n = 1; n <= 4; n++)
                {
                    i++; var hs = HUECOS.TryGetValue((cc, col, n), out var h) ? h : new string?[CAP]; var ocu = hs.Any(x => x != null);
                    string S(string k, object v) => $"<span id=\"span_{k}_{i:0000}\">{v}</span>";
                    sb.Append(S("COD_DI1_UB", cc) + S("COD_DI2_UB", col) + S("COD_DI3_UB", n) + S("UBICMNEMOT", ub) + S("CANTUBICAC", cc == "IG" && n == 1 ? 20 : CAP) + S("CARACOLANC", 120) + S("CARACOLALT", 150) +
                              S("CARACOLPRF", 480) + S("VCESTADO", ocu ? "Ocupado" : "Libre") + S("VOLCODEMP", ocu ? "6" : "") + S("GRPCOD", "") + S("MEZCLA", cc == "DD" && col == 2 ? "S" : "N"));
                }
            await Html(c, $"<html>{sb}</html>"); return;
        }
        if (pg == "hconshistplt.aspx")
        {
            if (!post) { await Html(c, "<html><form name=\"MAINFORM\" method=\"POST\" action=\"hconshistplt.aspx\"><select name=\"_EMPRESA\"><option value=\"0\" selected>TODAS</option></select><input type=\"text\" name=\"_FECDESDE\" value=\"\"><input type=\"text\" name=\"_FECHASTA\" value=\"\"><input type=\"text\" name=\"_PLTCODDESDE\" value=\"\"><input type=\"text\" name=\"_PLTCODHASTA\" value=\"\"><input type=\"text\" name=\"_ARTCODIGODESDE\" value=\"\"><input type=\"text\" name=\"_ARTCODIGOHASTA\" value=\"\"><input type=\"SUBMIT\" name=\"CONFIRMAR\" value=\"Confirmar\" gxevent=\"EENTER.\"></form></html>"); return; }
            var pc = f.GetValueOrDefault("_PLTCODDESDE") ?? ""; var pa = f.GetValueOrDefault("_ARTCODIGODESDE") ?? "";
            await Html(c, $"<html><a href=\"hdownloadlishiplt.aspx?XLS,C%3a%5cX.XLS,X.,0,1,{pc},{pc},{pa}\" target=\"_blank\"><img></a></html>"); return;
        }
        if (pg == "hdownloadlishiplt.aspx")
        {
            var cont = parts.Length > 5 ? parts[5] : "";
            string Fila(params string[] v) => "<TR>" + string.Concat(v.Select(x => $"<TD NOWRAP>{x}</TD>")) + "</TR>\r\n";
            var sb = new StringBuilder("<HTML><BODY><TABLE border=\"1\">");
            sb.Append(Fila("<b>Depósito: </b>", "DEPOSITO SONA", "<b>Desde: </b>", "15/10/2025"));
            sb.Append(Fila("Código", "Artículo", "Ubicación", "Fecha de Generación", "Hora de Generación", "Tipo", "Naturaleza", "Número", "Doc. Base", "Contenedor", "Usuario", "Cantidad", "Observaciones", "Lote"));
            var c0 = cont.Length > 0 ? cont : "I-10";
            sb.Append(Fila("PG-AR1", "ARIEL 3KG", "", "01/10/2026", "10:00:00", "INGRESO", "E", "1001", "REM 55", c0, "SLEDESM6", "100", "", ""));
            sb.Append(Fila("PG-AR1", "ARIEL 3KG", "DD /002/01", "02/10/2026", "11:00:00", "GUARDADO", "", "1002", "", c0, "OPER1", "100", "", ""));
            sb.Append(Fila("PG-AR1", "ARIEL 3KG", "DD /002/01", "08/10/2026", "15:30:00", "PICKING", "S", "1003", "PED 9143-6", c0, "OPER1", "5", "", ""));
            sb.Append("</TABLE></BODY></HTML>");
            await Html(c, sb.ToString()); return;
        }
        if (p.StartsWith("/les/"))
        {
            var lp = p["/les/".Length..];
            if (lp == "IngresoPendiente/Login")
            {
                if (post && f.GetValueOrDefault("Username") == "SLEDESM6" && f.GetValueOrDefault("Password") == "pw")
                { c.Response.Headers.SetCookie = "ingresos=ok; path=/"; c.Response.StatusCode = 302; c.Response.Headers.Location = "/les/IngresoPendiente/Index"; return; }
                await Html(c, "<html><form method=post><input name=\"Username\"><input name=\"Password\" type=password></form></html>"); return;
            }
            if (lp.StartsWith("fotos/") && await Ingreso.AtenderLes(c, "", f)) return;
            if (lp.StartsWith("IngresoPendiente/"))
            {
                if (!c.Request.Headers.Cookie.ToString().Contains("ingresos=ok")) { c.Response.StatusCode = 302; c.Response.Headers.Location = "/les/IngresoPendiente/Login"; return; }
                if (lp == "IngresoPendiente/Index") { await Html(c, "<html>Ingresos pendientes</html>"); return; }
                if (await Ingreso.AtenderLes(c, lp["IngresoPendiente/".Length..], f)) return;
                if (lp == "IngresoPendiente/ImprimirEtiquetaRapida")
                {
                    Log.Add($"LES etiqueta {f.GetValueOrDefault("codigo")} {f.GetValueOrDefault("cantidad")} {f.GetValueOrDefault("prefijo")}");
                    var cod = (f.GetValueOrDefault("codigo") ?? "").ToUpperInvariant();
                    var nro = $"{f.GetValueOrDefault("prefijo")}-{++_ultimoPp}";
                    c.Response.ContentType = "application/json";
                    await c.Response.WriteAsync(JsonSerializer.Serialize(new { success = true, etiquetas = new[] { new { Tipo = "10x8", Prefijo = f.GetValueOrDefault("prefijo"), Codigo = cod,
                        Cliente = "Notro Bazar", Descripcion = "ARIEL 3KG", NroContenedor = nro } }, warning = (string?)null }));
                    return;
                }
            }
            if (lp == "Api/Bultos/GetBultos")
            {
                var nro = c.Request.Query["NroSolicitud"].ToString();
                c.Response.ContentType = "application/json";
                if (nro is not ("16481-29" or "999-1")) { await c.Response.WriteAsync(JsonSerializer.Serialize(new { Data = (object?)null })); return; }
                await c.Response.WriteAsync(JsonSerializer.Serialize(new { Data = new { NroSolicitud = nro, FkPedido = 441468, Cliente = "Notro Bazar", Proyecto = "Ruteo OCT26", CheckControlado = nro == "16481-29",
                    UsuarioControl = (string?)null, FechaControl = "0001-01-01T00:00:00", ListBultos = BULTOS.GetValueOrDefault(nro) ?? new List<object>() } }));
                return;
            }
            if (lp == "Api/Bultos/PostGuardarBultos")
            {
                using var doc = await JsonDocument.ParseAsync(c.Request.Body);
                var nro = doc.RootElement.GetProperty("NroSolicitud").GetString()!;
                BULTOS[nro] = doc.RootElement.GetProperty("ListBultos").EnumerateArray().Select(x => (object)JsonSerializer.Deserialize<Dictionary<string, object>>(x.GetRawText())!).ToList();
                c.Response.ContentType = "application/json";
                await c.Response.WriteAsync(JsonSerializer.Serialize(new { Data = new { Message = "Success", Value = 123 } })); return;
            }
            if (lp == "Account/LogOn") { c.Response.Headers.SetCookie = "les=ok; path=/"; await Html(c, "<html>ok</html>"); return; }
            if (!c.Request.Headers.Cookie.ToString().Contains("les=ok")) { await Html(c, "<html><a href=\"/Account/LogOn\">Account/LogOn</a></html>"); return; }
            if (lp == "GenericSearchRemote/GenericListRemoteData")
            {
                var cod = f.GetValueOrDefault("filter[0][data][value]") ?? "";
                var arts = new Dictionary<string, (int Id, string Desc, bool Hab, double Stock)> { ["PG-AR1"] = (11, "ARIEL 3KG", true, 170), ["PG-NOSTOCK"] = (12, "SHAMPOO VIEJO", false, 0) };
                var porDesc = f.GetValueOrDefault("filter[0][field]") == "Descripcion";
                if (!f.ContainsKey("filter[0][field]"))
                {
                    // maestro completo paginado (sin filtro): los dos de Notro Bazar y uno de otro cliente
                    var todos = arts.Select(a => (object)new { IDArticulo = a.Value.Id, CodArticulo = a.Key, Descripcion = a.Value.Desc, FkCliente = new { IDCliente = 6, Nombre = "Notro Bazar" },
                        Marca = "Ariel", Proyecto = "no aplica", Habilitado = a.Value.Hab, StockFisico = a.Value.Stock, StockDisponible = a.Value.Stock, StockNoDisponible = 0, StockReservado = 0 })
                        .Append(new { IDArticulo = 13, CodArticulo = "OT-001", Descripcion = "ARTICULO DE OTRO CLIENTE", FkCliente = new { IDCliente = 99, Nombre = "OTRO CLIENTE SA" },
                            Marca = "X", Proyecto = "", Habilitado = true, StockFisico = 5.0, StockDisponible = 5.0, StockNoDisponible = 0, StockReservado = 0 }).ToList();
                    var pagLes = int.TryParse(f.GetValueOrDefault("page"), out var pgn) ? pgn : 1;
                    c.Response.ContentType = "application/json";
                    await c.Response.WriteAsync(JsonSerializer.Serialize(new { total = todos.Count, data = pagLes == 1 ? todos : new List<object>() })); return;
                }
                var data = arts.Where(a => porDesc ? System.Text.RegularExpressions.Regex.IsMatch(a.Value.Desc, string.Join(".*", cod.Split('%').Select(System.Text.RegularExpressions.Regex.Escape)), System.Text.RegularExpressions.RegexOptions.IgnoreCase) : a.Key == cod.ToUpperInvariant()).Select(a => new { IDArticulo = a.Value.Id, CodArticulo = a.Key, Descripcion = a.Value.Desc,
                    FkCliente = new { IDCliente = 6, Nombre = "Notro Bazar" }, Marca = "Ariel", Proyecto = "no aplica", Habilitado = a.Value.Hab,
                    StockFisico = a.Value.Stock, StockDisponible = a.Value.Stock, StockNoDisponible = 0, StockReservado = 0, StockPedido = 0, PublicadoPedidosPOP = true });
                c.Response.ContentType = "application/json";
                await c.Response.WriteAsync(JsonSerializer.Serialize(new { data })); return;
            }
            if (lp == "General/LoadArticulo") { await Html(c, "<html>sin fotos</html>"); return; }
        }
        if (p.StartsWith("/Docs/POPs/"))
        {
            if (!c.Request.Headers.Cookie.ToString().Contains("etms=ok")) { c.Response.StatusCode = 404; return; }
            c.Response.ContentType = "image/jpeg";
            await c.Response.Body.WriteAsync(Enumerable.Repeat((byte)0xAB, 600).ToArray()); return;
        }
        if (p.StartsWith("/etms/"))
        {
            var ep = p["/etms/".Length..];
            const string VS = "<input type=\"hidden\" name=\"__VIEWSTATE\" value=\"vs\"><input type=\"hidden\" name=\"__EVENTVALIDATION\" value=\"ev\">";
            if (ep == "Login.aspx")
            {
                if (post && f.GetValueOrDefault("ctl00$Contenido$txtUsuario") == "SLEDESM6" && f.GetValueOrDefault("ctl00$Contenido$txtPassword") == "pw")
                { c.Response.Headers.SetCookie = "etms=ok; path=/"; c.Response.StatusCode = 302; c.Response.Headers.Location = "/etms/Cliente/MicroSite/MainDashboard.aspx"; return; }
                await Html(c, $"<html><form method=\"post\" action=\"./Login.aspx\">{VS}<input name=\"ctl00$Contenido$txtUsuario\" type=\"text\"><input name=\"ctl00$Contenido$txtPassword\" type=\"password\"></form></html>"); return;
            }
            if (!c.Request.Headers.Cookie.ToString().Contains("etms=ok")) { c.Response.StatusCode = 302; c.Response.Headers.Location = "/etms/Login.aspx"; return; }
            if (ep == "Cliente/MicroSite/MainDashboard.aspx")
            {
                var link = post && f.GetValueOrDefault("ctl00$Contenido$SelectorCliente1$DdlClientes") == "6" ? "<a href=\"IngresoEgresoPOP.aspx?IdCliente=abc%2b1%3d%3d\">Ingresos Egresos</a>" : "";
                await Html(c, $"<html><a>Logout</a><form method=\"post\">{VS}<select name=\"ctl00$Contenido$SelectorCliente1$DdlClientes\"><option selected value=\"0\">Seleccione</option><option value=\"6\">Notro Bazar</option></select></form>{link}</html>"); return;
            }
            if (ep == "Cliente/MicroSite/IngresoEgresoPOP.aspx")
            {
                var filas = post && f.GetValueOrDefault("ctl00$Contenido$txtCodigo") == "PG-AR1"
                    ? "<tr><td><img src=\"/Docs/POPs/[1]PG-AR1_1.jpg\"></td><td>PG-AR1</td><td>ARIEL 3KG</td><td>Ariel</td><td><a href=\"/etms/Cliente/MicroSite/IngresoEgresoReporteExcel.ashx?idCliente=abc+1==&amp;idPop=77\">Descargar Excel</a></td></tr>" : "";
                await Html(c, $"<html><a>Logout</a><form method=\"post\">{VS}<input name=\"ctl00$Contenido$txtCodigo\" type=\"text\" value=\"\"><input name=\"ctl00$Contenido$txtDescripcion\" type=\"text\"></form><table><tr><th>&nbsp;</th><th>Codigo</th></tr>{filas}</table></html>"); return;
            }
            if (ep == "Cliente/MicroSite/IngresoEgresoReporteExcel.ashx")
            {
                using var wb = new ClosedXML.Excel.XLWorkbook();
                var ws = wb.AddWorksheet("Informe");
                object?[][] datos =
                {
                    new object?[] { "Cliente", "Fecha", "Codigo", "Descripcion", "Referencia", "Movimiento", "Comprobante", "Entrada", "Salida", "Saldo", "NroSolicitud", "Codigo04", "PDV", "Estado" },
                    new object?[] { "Notro Bazar", "04/11/2025", "PG-AR1", "ARIEL 3KG", "ENFOQUE", "INGRESO", null, 100, 0, 100, null, null, null, null },
                    new object?[] { "Notro Bazar", "08/10/2026", "PG-AR1", "ARIEL 3KG", "(RU) Rem:159816 / 10395-414 / ", "EGRESO", "0", 0, 5, -5, "10395-414", "TOR002", "Mayorista Tornado", "Finalizado Ok" },
                    new object?[] { "Notro Bazar", "11/11/2025", "PG-AR1", "ARIEL 3KG", "(RU) Rem:144141 / 7849-189 / M", "EGRESO", "144141", 0, 0, 0, "7849-189", "PYG6696", "MAGA SHOP", "Rechazado" },
                };
                for (var r = 0; r < datos.Length; r++) for (var k = 0; k < datos[r].Length; k++) ws.Cell(r + 1, k + 1).Value = datos[r][k] switch { null => "", int i => i, var o => o.ToString() };
                using var ms = new MemoryStream(); wb.SaveAs(ms);
                c.Response.ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                await c.Response.Body.WriteAsync(ms.ToArray()); return;
            }
        }
        if (!(c.Request.Headers.Cookie.ToString().Contains("ASP.NET_SessionId=abc"))) { await Html(c, LOGIN); return; }
        var usr = parts[0]; var emp = parts.Length > 1 ? parts[1] : "0";
        switch (pg)
        {
            case "hmenurf.aspx":
                if (post)
                {
                    var dest = f.GetValueOrDefault("_OPCION") switch { "1" => "hmenurecepciones.aspx?{0},{1},", "2" => "hingbpr1.aspx?{0},{1},,RF,", "3" => "hcambcal.aspx?{0},{1},,N,", "4" => "hreubpl1.aspx?{0},{1},,", "5" => "hajustes.aspx?{0},{1},", "7" => "hselpck.aspx?{0},{1},,", _ => null };
                    if (dest != null) { await Html(c, "", 303, string.Format(dest, usr, emp)); return; }
                }
                await Html(c, SetAction(MENU, "hmenurf.aspx?" + qs)); return;
            case "hselpck.aspx":
                if (post && f.GetValueOrDefault("_OPCION") is "9" or "5")
                {
                    if (_pickHecho || emp != "6") { await Html(c, Form("hselpck.aspx?" + qs, Hid("_MSG", "NO HAY ORDENES DE PREPARACION PENDIENTES") + Txt("_OPCION", "") + Btn("BUTTON1", "ACEPTAR", "EENTER."))); return; }
                    await Html(c, "", 303, $"hpck0.aspx?{usr},{emp},1,0,PTL,RF,OPBC,2051"); return;
                }
                await Html(c, Form("hselpck.aspx?" + qs, Hid("_EMPRESA", emp) + Txt("_OPCION", "") + Btn("BUTTON1", "ACEPTAR", "EENTER."), "ÓRDENES DE PREPARACIÓN 1-OPs PENDIENTES 5-PICKING DE UNIDADES 9-PICKING CONSOLIDADO")); return;
            case "hpck0.aspx":
                if (post)
                {
                    var op = ListaConFaltante ? f.GetValueOrDefault("_IFALTANTE") : f.GetValueOrDefault("_I");
                    if (ListaConFaltante && op != "6" && op != "99") { Log.Add("lista faltante: tipeó " + op); await Html(c, Form("hpck0.aspx?" + qs, Hid("_ODESCOPERC", "ORDEN DE PREP. C/STOCK") + Hid("_EVNOAPLICA", "2051") + Hid("_NEWPLT", _pickCont) + Hid("_I", "1") + Txt("_IFALTANTE", "1") + Hid("_MSG", "NUMERO TIPEADO ERRONEO VERIFIQUE TRASLADOS O ABASTECIMIENTOS") + Btn("BOTONACEPTAR", "ACEPTAR", "EENTER."), "FALTANTE DE MERCADERIA EN PICKING")); return; }
                    if (ListaConFaltante) ListaConFaltante = false;
                    if (op == "6") { await Html(c, "", 303, $"hselpck.aspx?{usr},{emp},,"); return; }
                    await Html(c, "", 303, $"hpck1.aspx?{usr},{emp},2051,{_pickIdx}"); return;
                }
                {
                    var sb = new StringBuilder(Hid("_ODESCOPERC", "ORDEN DE PREP. CONSOLIDADA") + Hid("_EVNOAPLICA", "2051") + Hid("_NEWPLT", _pickCont));
                    var k = 0;
                    foreach (var it in PICK.Skip(_pickIdx))
                    {
                        k++;
                        sb.Append(Hid($"ARTORDEN{k + 3}", k.ToString()) + Hid($"ARTCODIGO{k}", it.Art) + Hid($"UBICACION{k * 2 - 1}", $"{it.Cara}   -{it.Col:000}-{it.Niv:00}")
                                  + Hid($"ARTICULOPENDIENTE{k + 5}", it.Desc) + Hid($"CANTIDAD{k + 5}", it.Cant.ToString()) + Hid($"_PALLETYATOMADO{k}", it.Pallet));
                    }
                    if (ListaConFaltante) { await Html(c, Form("hpck0.aspx?" + qs, Hid("_ODESCOPERC", "ORDEN DE PREP. C/STOCK") + Hid("_EVNOAPLICA", "2051") + Hid("_NEWPLT", _pickCont) + Hid("_I", "1") + Txt("_IFALTANTE", "1") + Btn("BOTONACEPTAR", "ACEPTAR", "EENTER."), $"ORDEN DE PREP. C/STOCK 2051 DESTINO: CONTENEDOR: {_pickCont} ITEMS A PICKEAR 1 FALTANTE DE MERCADERIA EN PICKING VERIFIQUE TRASLADOS O ABASTECIMIENTOS")); return; }
                    sb.Append(Hid("ARTORDEN9", "0") + Hid("UBICACION9", "No hay stock") + Txt("_I", "1") + Btn("BOTONACEPTAR", "ACEPTAR", "EENTER."));
                    await Html(c, Form("hpck0.aspx?" + qs, sb.ToString(), $"ORDEN DE PREP. CONSOLIDADA 2051 DESTINO: CONTENEDOR: {_pickCont} ITEMS A PICKEAR")); return;
                }
            case "hpck1.aspx":
            {
                var it = PICK[_pickIdx];
                var msj = ""; var msj2 = ""; var continuar = false;
                if (post && f.GetValueOrDefault("_EventName") == "E'VOLVER'.") { await Html(c, "", 303, $"hpck0.aspx?{usr},{emp},1,0,PTL,RF,OPBC,2051"); return; }
                if (post && f.GetValueOrDefault("_EventName") == "E'CONTINUAR'.") { Log.Add("picking continuar aviso"); await Html(c, "", 303, $"hpck2.aspx?{usr},{emp},2051,{_pickIdx}"); return; }
                if (post && f.GetValueOrDefault("_EventName") == "EENTER.")
                {
                    if (f.GetValueOrDefault("_NUEVAUBIC") == it.Ubic.ToString() && f.GetValueOrDefault("_NIVEL") == it.Niv.ToString() && f.GetValueOrDefault("_ARTCOD") == it.Art)
                    {
                        // aviso de lote / vencimiento como el real (19/10): el WMS deja seguir con el link "Continuar"
                        if (AvisoLote) { AvisoLote = false; msj2 = "Aviso: Contenedor Supera Dias"; msj = "Rango FIFO/FEFO del Articulo"; continuar = true; }
                        else { await Html(c, "", 303, $"hpck2.aspx?{usr},{emp},2051,{_pickIdx}"); return; }
                    }
                    else msj = "UBICACION O ARTICULO INCORRECTO";
                }
                await Html(c, Form("hpck1.aspx?" + qs, Hid("_ODESCOPERC", "ORDEN DE PREP. CONSOLIDADA") + Hid("_EVNOAPLICA", "2051") + Btn("INTERRUMPIR", "CANCELAR", "E'CANCELAR'.") + Btn("BUTTON2", "VOLVER", "E'VOLVER'.")
                    + Hid("_PROFUNDIDAD", "1") + Hid("_COD_DI1_UB", it.Cara) + Hid("_COD_DI2_UB", it.Col.ToString("000")) + Hid("_COD_DI3_UB", it.Niv.ToString("00")) + Hid("_ARTICULO", it.Art)
                    + Hid("_DESCART", it.Desc) + Hid("_SDOAPCK", it.Cant.ToString()) + Hid("_ARTUNIDALM", "Unidades") + Hid("_PALLET", it.Pallet) + Txt("_NUEVAUBIC", "0") + Hid("_UBICMNEMOT", it.Ubic.ToString())
                    + Txt("_NIVEL", "") + Txt("_ARTCOD", "") + Txt("_CONTENEDORAPICKEAR", "") + Hid("_MENSLOC2", msj2) + Hid("_MENSLOC1", msj)
                    + $"<span id=\"CONTINUAR\" class=\"TextBlock\" style=\"{(continuar ? "" : ";display:none;")}font-family:'Verdana'\"><a href=\"javascript:gxdisableformnvis();GX_setevent('E\\'CONTINUAR\\'.');gxSubmit();\">Continuar</a></span>"
                    + Btn("BUTTON1", "ACEPTAR", "EENTER."))); return;
            }
            case "hpck2.aspx":
            {
                var it = PICK[_pickIdx];
                var msj = "";
                if (post && f.GetValueOrDefault("_EventName") == "EENTER.")
                {
                    if (string.IsNullOrEmpty(f.GetValueOrDefault("_PLTEXTERNO"))) msj = "DISPARE AL CONTENEDOR";
                    else
                    {
                        _pickCont = f["_PLTEXTERNO"]; _pickIdx++;
                        lock (Log) Log.Add($"PICK {it.Art} x{f.GetValueOrDefault("_CANTPCK")} → {_pickCont}");
                        await Html(c, "", 303, _pickIdx < PICK.Count ? $"hpck0.aspx?{usr},{emp},1,0,PTL,RF,OPBC,2051" : $"hpck4.aspx?{usr},{emp},2051"); return;
                    }
                }
                await Html(c, Form("hpck2.aspx?" + qs, Hid("_EVNOAPLICA", "2051") + Hid("_ARTICULO", it.Art) + Hid("_ARTABR", it.Desc) + Hid("_CANTXBULTO", "10") + Hid("_CANTSUGE", it.Cant.ToString())
                    + Hid("_ARTUNIDAD", "Unidades") + Txt("_CANTPCK", it.Cant.ToString()) + Txt("_PLTEXTERNO", _pickCont) + Hid("_MENSLOC1", msj)
                    + Btn("BTNACEPTAR", "ACEPTAR", "EENTER.") + Btn("REINICIAR", "REINICIAR", "E'BORRAR'.") + Btn("VOLVER", "VOLVER", "E'VOLVER'."))); return;
            }
            case "hpck4.aspx":
                if (post) { await Html(c, "", 303, $"hpck9.aspx?{usr},{emp},2051"); return; }
                await Html(c, Form("hpck4.aspx?" + qs, Hid("_EVNOAPLICA", "2051") + Hid("_TOTALUNIDADES", "3") + Hid("_TOTALM3", "0,05") + Hid("_TOTALKGS", "1,00") + Hid("_EVFHPROG", "15/05/2026")
                    + Hid("_NEWPLT", _pickCont) + Btn("ACEPTAR", "ACEPTAR", "EENTER."), $"PEGAR ETIQUETA EN EL CONTENEDOR DEJAR CONTENEDOR: {_pickCont} EN ISLA DE CONTROL NC-01")); return;
            case "hpck9.aspx":
                if (post) { _pickHecho = true; await Html(c, "", 303, $"hselpck.aspx?{usr},{emp},,"); return; }
                await Html(c, Form("hpck9.aspx?" + qs, Hid("_EVNOAPLICA", "2051") + Hid("_CANTTOTAL", "3") + Hid("_M3TOTAL", "0,05") + Btn("BUTTONACEPTAR", "ACEPTAR", "EENTER."),
                    "ORDEN DE PREPARACIÓN CONCLUIDA NC-01 Total de Unidades: 3")); return;
            case "hajustes.aspx":
                if (post && f.GetValueOrDefault("_OPCION") == "8") { await Html(c, "", 303, $"hconsubicac.aspx?{usr},{emp}"); return; }
                if (post && f.GetValueOrDefault("_OPCION") == "5") { await Html(c, "", 303, $"hajuplt.aspx?{usr},{emp},"); return; }
                if (post && f.GetValueOrDefault("_OPCION") == "13") { await Html(c, "", 303, $"hreubcj1.aspx?{usr},{emp},,,"); return; }
                if (post && f.GetValueOrDefault("_OPCION") == "7") { await Html(c, "", 303, $"hingdcin.aspx?{usr},{emp},"); return; }
                await Html(c, SetAction(AJUSTES, "hajustes.aspx?" + qs)); return;
            case "hconsubicac.aspx":
                if (post)
                {
                    if (long.TryParse(f.GetValueOrDefault("_UBICMNEMOT"), out var ub) && UBIC.TryGetValue(ub, out var u))
                    { await Html(c, "", 303, $"hselconsubicac.aspx?{usr},{emp},{u.Cara},{u.Col:000},{f.GetValueOrDefault("_COD_DI3_UB")}"); return; }
                    await Html(c, CONS); return;
                }
                await Html(c, SetAction(CONS, "hconsubicac.aspx?" + qs)); return;
            case "hdatubicaciones.aspx":
            {
                // Datos de Ubicaciones (Ver un hueco › profundidad): bloqueado S/N y motivo
                var cont = Uri.UnescapeDataString(parts.Length > 2 ? parts[2] : "").ToUpperInvariant();
                Log.Add("datos contenedor " + cont);
                string I(string n, string v) => $"<input type=\"hidden\" name=\"{n}\" value=\"{System.Net.WebUtility.HtmlEncode(v)}\">";
                if (!CONT.TryGetValue(cont, out var dc)) { await Html(c, $"<html><head><title>Datos de Ubicaciones</title></head><body><form name=\"MAINFORM\">{I("_PLTCOD", cont)}{I("_PLTBLK", "")}{I("_COD_DI1_UB", "")}{I("_MENSGRAL", "El contenedor no existe.")}</form></body></html>"); return; }
                var bloq = BLOQUEADOS.TryGetValue(cont, out var mot);
                var dd = Donde(cont);
                await Html(c, $"<html><head><title>Datos de Ubicaciones</title></head><body><form name=\"MAINFORM\">{I("_PLTCOD", cont)}{I("_PLTBLK", bloq ? "S" : "N")}{I("_MTSDSC", bloq ? mot! : "")}" +
                    $"{I("_COD_DI1_UB", dd?.Item1 ?? "")}{I("_COD_DI2_UB", dd != null ? dd.Value.Item2.ToString("000") : "")}{I("_COD_DI3_UB", dd != null ? dd.Value.Item3.ToString("00") : "")}{I("_COD_DI4_UB", "1")}" +
                    $"{I("_EMPPLT", "6")}{I("_PUAQTY_0001", dc.Cant.ToString(System.Globalization.CultureInfo.InvariantCulture))}{I("_MENSLOC1_0001", "Artículo: " + dc.Art)}{I("_MENSLOC2_0001", dc.Desc)}" +
                    $"{I("_MENSLOC5_0001", "REUBICACION HACIA (DESTINO)    RADIO FREC")}{I("_MENSLOC6_0001", "19/10/2026 16:44:47 PGODOY")}{I("_PLTFARTVTO_0001", "05/05/2026")}{I("_MENSGRAL", "")}</form></body></html>");
                return;
            }
            case "hselconsubicac.aspx":
            {
                var cara = parts[2]; var col = int.Parse(parts[3]); var n = int.Parse(parts[4]);
                if (!UBIC.Values.Any(x => x.Cara == cara && x.Col == col)) { await Html(c, "<html><form name=\"MAINFORM\"></form></html>"); return; }
                var pagH = int.TryParse(f.GetValueOrDefault("_PAG"), out var ph) ? ph : 1;
                if (post && f.GetValueOrDefault("_EventName") == "E'SGTE'.") { Log.Add($"hueco {cara}{col}-{n} pagina {pagH + 1}"); await Html(c, HuecoHtml(cara, col, n, PaginacionRota ? 1 : pagH + 1)); return; }
                await Html(c, HuecoHtml(cara, col, n)); return;
            }
            case "hreubpl1.aspx":
                if (post)
                {
                    var cont = (f.GetValueOrDefault("_PLTCOD") ?? "").ToUpperInvariant();
                    if (!CONT.ContainsKey(cont)) { await Html(c, "", 303, $"hreubpl1.aspx?{usr},{emp},CONTENEDOR+INEXISTENTE,"); return; }
                    var k = Donde(cont) ?? ("NN", 1, 1);
                    await Html(c, "", 303, $"hreubpl2.aspx?{usr},{emp},{cont},{CONT[cont].Art},{k.Item1},{k.Item2:000},{k.Item3:00},1,"); return;
                }
                await Html(c, SetAction(REUB1, "hreubpl1.aspx?" + qs)); return;
            case "hreubpl2.aspx":
                if (post)
                {
                    var cont = parts[2];
                    if (!long.TryParse(f.GetValueOrDefault("_NUEVAUBIC"), out var ub) || !UBIC.TryGetValue(ub, out var u)) { await Html(c, "", 303, $"hreubpl1.aspx?{usr},{emp},UBICACION+INEXISTENTE,"); return; }
                    var n = int.Parse(f.GetValueOrDefault("_NIVEL") ?? "0");
                    var dst = H(u.Cara, u.Col, n);
                    if (!dst.Contains(null)) { await Html(c, "", 303, $"hreubpl1.aspx?{usr},{emp},UBICACION+COMPLETA,"); return; }
                    if (Donde(cont) is { } k) HUECOS[k][Array.IndexOf(HUECOS[k], cont)] = null;
                    dst[Array.IndexOf(dst, null)] = cont;
                    await Html(c, "", 303, $"hreubpl1.aspx?{usr},{emp},CONTENEDOR+REUBICADO+EN%3a+{u.Cara}+++%2f{u.Col:000}%2f{n:00},"); return;
                }
                await Html(c, SetAction(SetVal(REUB2, "_PLTCOD", parts[2]), "hreubpl2.aspx?" + qs)); return;
            case "hcambcal.aspx":
            {
                var page = SetAction(CAMB, "hcambcal.aspx?" + qs);
                if (post)
                {
                    var cont = (f.GetValueOrDefault("_PLTCOD") ?? "").ToUpperInvariant();
                    if (!CONT.ContainsKey(cont)) { await Html(c, SetVal(page, "_MENS20POS", "CONTENEDOR INEXISTENTE")); return; }
                    page = SetVal(page, "_PLTCOD", cont);
                    var ev = f.GetValueOrDefault("_EventName");
                    if (ev == "EENTER.")
                    {
                        var ant = ESTADO.GetValueOrDefault(cont, "DISPONIBLE");
                        var op = f.GetValueOrDefault("_OPCION") ?? "";
                        ESTADO[cont] = op + (op.StartsWith("NO") ? "/" + f.GetValueOrDefault("_MTSCOD") : "");
                        await Html(c, SetVal(SetAction(CAMB, "hcambcal.aspx?" + qs), "_MENS20POS", $"CAMBIADO DE {ant} A {ESTADO[cont]}")); return;
                    }
                    if (ev == "E_OPCION.CLICK.")
                    {
                        var op = f["_OPCION"];
                        page = page.Replace(" selected=\"\"", "").Replace($"value=\"{op}\"", $"value=\"{op}\" selected");
                        if (op == "NODISPONIB")
                        {
                            var ops = string.Concat(new[] { (1, "ROTURA"), (2, "INCOMPLETO"), (3, "OBSOLETO"), (14, "NO CONFORME/REVISAR"), (7, "CUARENTENA") }.Select(o => $"<option value=\"{o.Item1}\">{o.Item2}</option>"));
                            page = new Regex("(<select[^>]*name=\"_MTSCOD\"[^>]*>)[\\s\\S]*?(</select>)").Replace(page, m => m.Groups[1].Value + ops + m.Groups[2].Value, 1);
                        }
                    }
                }
                await Html(c, page); return;
            }
            case "hingdcin.aspx":
                if (post) { await Html(c, "", 303, $"hingcarin.aspx?{emp},CINI,RF,0,{usr},"); return; }
                await Html(c, Form("hingdcin.aspx?" + qs, Btn("BTNACEPTAR", "ACEPTAR", "EENTER.") + Btn("BTNATRAS", "ATRÁS", "E'ATRAS'."), "CARGA INICIAL")); return;
            case "hingcarin.aspx":
            {
                // Carga inicial (campos reales de la captura del 19/10): el usuario va al final de los parámetros
                var empCi = parts[0];
                string F(string msj) => Form("hingcarin.aspx?" + qs, Hid("_MENSLOC1", msj) + "<input type=\"text\" name=\"_PLTCOD\" value=\"\"><input type=\"text\" name=\"_ARTICULO\" value=\"\">" +
                    "<input type=\"text\" name=\"_CANTLOG\" value=\"0\"><input type=\"text\" name=\"_UBICMNEMOT\" value=\"0\"><input type=\"text\" name=\"_NIVEL_3\" value=\"\">" +
                    Hid("_PVEZ", "S") + Btn("BUTTON1", "ACEPTAR", "EENTER."), "CARGA INICIAL " + msj + " CONTENEDOR:");
                if (!post) { await Html(c, F("")); return; }
                var pcod = (f.GetValueOrDefault("_PLTCOD") ?? "").ToUpperInvariant();
                var art = (f.GetValueOrDefault("_ARTICULO") ?? "").ToUpperInvariant();
                var cantCi = double.Parse((f.GetValueOrDefault("_CANTLOG") ?? "0").Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
                Log.Add($"carga inicial emp={empCi} {pcod} {art} {cantCi} ubic={f.GetValueOrDefault("_UBICMNEMOT")} niv={f.GetValueOrDefault("_NIVEL_3")}");
                if (CONT.ContainsKey(pcod)) { await Html(c, F("CONTENEDOR YA EXISTE")); return; }
                if (!long.TryParse(f.GetValueOrDefault("_UBICMNEMOT"), out var ubCi) || !UBIC.TryGetValue(ubCi, out var uCi) || !int.TryParse(f.GetValueOrDefault("_NIVEL_3"), out var nCi))
                { await Html(c, F("UBICACION INEXISTENTE")); return; }
                var hs = H(uCi.Cara, uCi.Col, nCi); var libre = Array.IndexOf(hs, null);
                if (libre < 0) { await Html(c, F("NO HAY LUGAR EN LA UBICACION")); return; }
                hs[libre] = pcod;
                CONT[pcod] = ("Notro Bazar", art, "ARIEL 3KG", cantCi, 0.1);
                await Html(c, F("Carga Inicial Exitosa")); return;
            }
            case "hajuplt.aspx":
            {
                // Ajuste de contenedores (campos reales de la captura del 14/10)
                string Form(string pltcod, string aux, double cant, string msj) => $"<html><form name=\"MAINFORM\" method=\"POST\" action=\"hajuplt.aspx?{qs}\">" +
                    $"<input type=\"text\" name=\"_PLTCOD\" value=\"{pltcod}\"><input type=\"hidden\" name=\"_PLTCODAUX\" value=\"{aux}\"><input type=\"hidden\" name=\"_CANTPLT\" value=\"{cant}\">" +
                    "<input type=\"text\" name=\"_UBICMNEMOT\" value=\"0\"><input type=\"text\" name=\"_NIVEL\" value=\"\"><input type=\"text\" name=\"_STRCANTI\" value=\"0\">" +
                    "<select name=\"_SR\"><option value=\"R\">Resta<option value=\"S\">Suma<option value=\"\" selected=\"\">Operación</select>" +
                    "<select name=\"_MTSCOD\"><option value=\"5\" selected=\"\">CON_FALTANTES_AJU<option value=\"19\">ERROR_INGRESO<option value=\"12\">INVENTARIO<option value=\"11\">SCRAP_AJU</select>" +
                    $"<input type=\"hidden\" name=\"_MENS20POS\" value=\"{msj}\"><input type=\"text\" name=\"_OPCION\" value=\"\"><input type=\"submit\" name=\"BTNACEPTAR\" value=\"ACEPTAR\" gxevent=\"EENTER.\"></form></html>";
                if (!post) { await Html(c, Form("", "", 0, "")); return; }
                var pc = (f.GetValueOrDefault("_PLTCOD") ?? "").ToUpperInvariant();
                var aux = f.GetValueOrDefault("_PLTCODAUX") ?? "";
                if (!CONT.ContainsKey(pc)) { await Html(c, Form("", "", 0, "CONTENEDOR INEXISTENTE")); return; }
                var sr = f.GetValueOrDefault("_SR") ?? ""; var q = double.Parse((f.GetValueOrDefault("_STRCANTI") ?? "0").Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
                if (aux != pc || sr == "" || q <= 0) { await Html(c, Form(pc, pc, CONT[pc].Cant, "Seleccione, Suma o Resta.")); return; }
                var o = CONT[pc]; var nueva = sr == "R" ? o.Cant - q : o.Cant + q;
                CONT[pc] = (o.Cli, o.Art, o.Desc, nueva, o.M3);
                await Html(c, Form("", "", 0, $"El Contenedor: {pc} se ajustó en: {q} unidades y quedó en: {nueva} unidades")); return;
            }
            case "hreubcj1.aspx":
            {
                // Reubicar cajas, paso 1 (campos reales de la captura del 13/10)
                if (post)
                {
                    var ori = (f.GetValueOrDefault("_PLTCOD") ?? "").ToUpperInvariant();
                    if (!CONT.ContainsKey(ori)) { await Html(c, "", 303, $"hreubcj1.aspx?{usr},{emp},,,CONTENEDOR+INEXISTENTE"); return; }
                    var k = Donde(ori) ?? ("NN", 1, 1);
                    await Html(c, "", 303, $"hreubcj2.aspx?{usr},{emp},{ori},,0,{CONT[ori].Art},{k.Item1},{k.Item2:000},{k.Item3:00},1,"); return;
                }
                await Html(c, $"<html><form name=\"MAINFORM\" method=\"POST\" action=\"hreubcj1.aspx?{qs}\"><input type=\"text\" name=\"_PLTCOD\" value=\"\"><input type=\"text\" name=\"_UBICMNEMOT\" value=\"0\"><input type=\"text\" name=\"_NIVEL\" value=\"\"><input type=\"text\" name=\"_OPCION\" value=\"\"><input type=\"submit\" name=\"BTNACEPTAR\" value=\"ACEPTAR\" gxevent=\"EENTER.\"></form></html>"); return;
            }
            case "hreubcj2.aspx":
            {
                var ori = parts[2];
                if (post)
                {
                    var des = (f.GetValueOrDefault("_NEWPLTCOD") ?? "").ToUpperInvariant();
                    if (f.GetValueOrDefault("_OPCION") != "1" || !CONT.ContainsKey(des)) { await Html(c, "", 303, $"hreubcj1.aspx?{usr},{emp},,,CONTENEDOR+DESTINO+INEXISTENTE"); return; }
                    // como el real (22/10): la Ubic.Ctrl y el nivel tienen que ser los de donde está el contenedor que recibe
                    Log.Add($"reubicar cajas destino {des} ubic={f.GetValueOrDefault("_NUEVAUBIC")} niv={f.GetValueOrDefault("_NIVEL")}");
                    if (Donde(des) is { } dd && !(UBIC.Any(x => x.Key.ToString() == f.GetValueOrDefault("_NUEVAUBIC") && x.Value.Cara == dd.Item1 && x.Value.Col == dd.Item2) && f.GetValueOrDefault("_NIVEL") == dd.Item3.ToString()))
                    { await Html(c, "", 303, $"hreubcj1.aspx?{usr},{emp},,,LA+UBICACI%c3%93N+DE+DESTINO+ES+INCORRECTA."); return; }
                    var cant = double.Parse(f.GetValueOrDefault("_CANTCAJAS") ?? "0");
                    var o = CONT[ori]; var d = CONT[des];
                    CONT[ori] = (o.Cli, o.Art, o.Desc, o.Cant - cant, o.M3); CONT[des] = (d.Cli, d.Art, d.Desc, d.Cant + cant, d.M3);
                    var k = Donde(des) ?? ("NN", 1, 1);
                    await Html(c, "", 303, $"hreubcj1.aspx?{usr},{emp},,,CAJAS+REUBICADAS+EN%3a+{des}+{k.Item1}+++%2f{k.Item2:000}%2f{k.Item3:00}"); return;
                }
                await Html(c, $"<html><form name=\"MAINFORM\" method=\"POST\" action=\"hreubcj2.aspx?{qs}\"><input type=\"hidden\" name=\"_PLTCOD\" value=\"{ori}\"><input type=\"text\" name=\"_CANTCAJAS\" value=\"0\"><input type=\"text\" name=\"_NEWPLTCOD\" value=\"\"><input type=\"text\" name=\"_NUEVAUBIC\" value=\"0\"><input type=\"text\" name=\"_NIVEL\" value=\"\"><input type=\"text\" name=\"_OPCION\" value=\"1\"><input type=\"submit\" name=\"BTNACEPTAR\" value=\"ACEPTAR\" gxevent=\"EENTER.\"></form></html>"); return;
            }
            case "hingbpr1.aspx":
                if (post)
                {
                    var cont = (f.GetValueOrDefault("_PLTCOD") ?? "").ToUpperInvariant();
                    if (f.GetValueOrDefault("_EventName") == "E'SI'.") cont = "I-177719";
                    else if (cont != "I-177719") { await Html(c, SetVal(SetAction(GUARD1, "hingbpr1.aspx?" + qs), "_MENSAJE1", "CONTENEDOR NO PENDIENTE")); return; }
                    await Html(c, "", 303, $"hingbpr5.aspx?{cont},IA,007,01,11,IEXT,RF"); return;
                }
                await Html(c, SetAction(GUARD1, "hingbpr1.aspx?" + qs)); return;
            case "hingbpr5.aspx":
                if (post)
                {
                    if (!long.TryParse(f.GetValueOrDefault("_UBICMNEMOT"), out var ub) || !UBIC.TryGetValue(ub, out var u)) { await Html(c, SetVal(SetAction(GUARD5, "hingbpr5.aspx?" + qs), "_MENSAJE", "UBICACION INCORRECTA")); return; }
                    var dst = H(u.Cara, u.Col, int.Parse(f.GetValueOrDefault("_NIV") ?? "1"));
                    dst[Array.IndexOf(dst, null)] = parts[0];
                    await Html(c, "", 303, "hingbpr1.aspx?SLEDESM6,6,,RF,"); return;
                }
                await Html(c, SetAction(GUARD5, "hingbpr5.aspx?" + qs)); return;
        }
        await Html(c, "?");
    }
}
