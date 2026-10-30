using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Sona.Pruebas;

/// <summary>
/// Ingreso de prueba: LES "Ingresos pendientes" (JSON) y las pantallas del WMS que usa el módulo de ingreso
/// (hingeve / hingeve2, hgrpart / tgrpart / zonificación, y en el handheld hmenurecepciones / hingetqextrf).
/// Datos como la grabación del 22/10: recepción 2877 de Cuyo - Promo (empresa 14), artículo LO4236 sin configurar, proveedor 326.
/// </summary>
public sealed class FakeIngreso
{
    public readonly List<string> Log = new();
    public string EstadoLes = "PENDIENTE DE DESCARGA";
    public bool Configurado;
    public int? Recepcion;
    public readonly List<(string Art, string Cant)> Renglones = new();
    public readonly List<string> Recibidos = new();
    public JsonDocument? Descarga;
    public int FotosSubidas;
    int _ultimoPc = 201424;
    string _ingeveTipo = "", _ingeveDeriv = "", _ingeveNat = "";
    string _emp = "", _cat = "", _prov = "";

    static string H(string s) => WebUtility.HtmlEncode(s);
    static string Hid(string n, string v) => $"<input type=\"hidden\" name=\"{n}\" value=\"{H(v)}\">";
    static string Txt(string n, string v) => $"<input type=\"text\" name=\"{n}\" value=\"{H(v)}\">";
    static string Sel(string n, string val, params (string V, string T)[] ops) =>
        $"<select name=\"{n}\" onchange=\"gxonchange(this)\">" + string.Concat(ops.Select(o => $"<option value=\"{H(o.V)}\"{(o.V == val ? " selected" : "")}>{H(o.T)}</option>")) + "</select>";
    static string Boton(string n, string v, string ev) => $"<input type=\"SUBMIT\" name=\"{n}\" value=\"{H(v)}\" gxevent=\"{ev.Replace("'", "\\'")}\" onclick=\"GX_setevent('{ev.Replace("'", "\\'")}');\">";
    static string Pagina(string accion, string cuerpo) =>
        $"<html><body><div>Bienvenido, PRUEBA</div><form id=\"MAINFORM\" name=\"MAINFORM\" method=\"POST\" action=\"{accion}\">" + Hid("_EventName", "") + Hid("_EventGridId", "") + Hid("_EventRowId", "") + cuerpo + "</form></body></html>";
    static async Task Html(HttpContext c, string h, int estado = 200, string? loc = null)
    {
        c.Response.StatusCode = estado; if (loc != null) c.Response.Headers.Location = loc;
        c.Response.ContentType = "text/html; charset=utf-8"; await c.Response.WriteAsync(h);
    }
    static async Task Json(HttpContext c, object o) { c.Response.ContentType = "application/json"; await c.Response.WriteAsync(JsonSerializer.Serialize(o)); }

    // ================================================================ WMS escritorio y handheld

    public async Task<bool> Atender(HttpContext c, string pg, Dictionary<string, string> f, bool post, string qs)
    {
        var ev = f.GetValueOrDefault("_EventName") ?? "";
        switch (pg)
        {
            case "hingeve.aspx":
            {
                if (post)
                {
                    _ingeveTipo = f.GetValueOrDefault("_OTIPOOPERC") ?? _ingeveTipo; _ingeveDeriv = f.GetValueOrDefault("_OTIPOOPDERIV") ?? _ingeveDeriv; _ingeveNat = f.GetValueOrDefault("_NNATROPDERIVADA") ?? _ingeveNat;
                    if (ev == "EENTER.")
                    {
                        lock (Log) Log.Add($"hingeve {_ingeveTipo}/{_ingeveDeriv}/{_ingeveNat}");
                        if (_ingeveTipo == "EEXT" && _ingeveDeriv == "EEXT" && _ingeveNat == "ORIG") { _emp = ""; _cat = ""; _prov = ""; Renglones.Clear(); await Html(c, "", 303, "hingeve2.aspx?EEXT,EEXT,ORIG"); return true; }
                    }
                }
                else { _ingeveTipo = _ingeveDeriv = _ingeveNat = ""; }
                await Html(c, Pagina("hingeve.aspx",
                    Sel("_CFINGSALOTROS", f.GetValueOrDefault("_CFINGSALOTROS") ?? "", ("", ""), ("E", "Ingreso"), ("S", "Salida")) +
                    Sel("_OTIPOOPERC", _ingeveTipo, ("", ""), ("EEXT", "RECEPCION POR CONTENEDOR")) +
                    Sel("_OTIPOOPDERIV", _ingeveDeriv, ("", ""), ("EEXT", "RECEPCION POR CONTENEDOR")) +
                    Sel("_NNATROPDERIVADA", _ingeveNat, ("", ""), ("ORIG", "Original")) +
                    Boton("BUTTON1", "Confirmar", "EENTER.")));
                return true;
            }
            case "hingeve2.aspx":
            {
                var msg = "";
                string? link = null;
                if (post)
                {
                    _emp = f.GetValueOrDefault("_EVEMPRESA") ?? _emp; _cat = f.GetValueOrDefault("_EVCATCLIENTE") ?? _cat; _prov = f.GetValueOrDefault("_PROVEEDOR") ?? _prov;
                    if (ev == "E'CARGA LA SDT'.")
                    {
                        var art = f.GetValueOrDefault("_ARTCOD") ?? "";
                        if (art != "LO4236") msg = "Artículo inexistente";
                        else if (!Configurado) msg = "El artículo no está configurado";
                        else Renglones.Add((art, f.GetValueOrDefault("_CANTLOG") ?? ""));
                        lock (Log) Log.Add($"hingeve2 renglon {art} {f.GetValueOrDefault("_CANTLOG")} {msg}");
                    }
                    if (ev == "E'GRABAR'.")
                    {
                        lock (Log) Log.Add($"hingeve2 grabar emp={_emp} prov={_prov} rem={f.GetValueOrDefault("_NROPEDCTE")} ce={f.GetValueOrDefault("_EVCTOEMCOM")} letra={f.GetValueOrDefault("_EVLETRCOMP")} doc={f.GetValueOrDefault("_EVDOCEXTERNO")} fecha={f.GetValueOrDefault("_FECHA")}");
                        if (Renglones.Count == 0 || _prov != "326" || _emp != "14") msg = "Faltan datos";
                        else { Recepcion = 930; link = $"<a href=\"oimpintpdf.aspx?PDF,{_emp},930,EEXT,ORIG\" target=\"_blank\"><img></a>"; }
                    }
                }
                var provs = _emp == "14" && _cat == "02" ? new[] { ("", "Seleccione"), ("100", "Otro Proveedor SA"), ("326", "Gráfica Austral") } : new[] { ("", "Seleccione") };
                var grilla = string.Concat(Renglones.Select((r, i) => $"<span id=\"span_CTLGRPCOD_{i + 1:0000}\">{H(r.Art)}</span><span id=\"span_CTLCANT_{i + 1:0000}\">{H(r.Cant)}</span>"));
                await Html(c, Pagina("hingeve2.aspx?EEXT,EEXT,ORIG",
                    Sel("_EVEMPRESA", _emp, ("", ""), ("6", "Notro"), ("14", "Cuyo - Promo")) + Sel("_EVCATCLIENTE", _cat, ("01", "CLIENTE"), ("02", "PROVEEDOR")) +
                    Sel("_PROVEEDOR", _prov, provs) + Txt("_NROPEDCTE", "") + Txt("_EVCTOEMCOM", "") + Txt("_EVLETRCOMP", "") + Txt("_FECHA", "") + Txt("_EVDOCEXTERNO", "") +
                    Txt("_ARTCOD", "") + Txt("_CANTLOG", "") + Boton("AGREGALINEA", "Agregar", "E'CARGA LA SDT'.") + Boton("GRABAR", "Grabar", "E'GRABAR'.") +
                    grilla + Hid("_MSG2", msg) + (link ?? "")));
                return true;
            }
            case "oimpintpdf.aspx":
                c.Response.ContentType = "application/pdf";
                await c.Response.WriteAsync("%PDF-1.4 recepcion 930");
                return true;
            case "hgrpart.aspx":
            {
                var filas = "";
                if (post && ev == "E'SEARCH'.")
                {
                    var cod = f.GetValueOrDefault("_GRPCOD") ?? ""; var cfg = f.GetValueOrDefault("_ARTCFG") ?? "";
                    var hay = cod == "LO4236" && f.GetValueOrDefault("_COD_EMP") == "14" && (cfg == "" || (cfg == "N" && !Configurado) || (cfg == "S" && Configurado));
                    if (hay) filas = $"<span id=\"span_GRPCOD_0001\">LO4236</span><span id=\"span_GRPDSC_0001\">LONA PROMO</span>";
                }
                await Html(c, Pagina("hgrpart.aspx", Txt("_COD_EMP", "") + Txt("_GRPCOD", "") + Sel("_ARTCFG", "", ("", "Todos"), ("S", "Si"), ("N", "No")) + Txt("_ARTDSC", "") +
                    Boton("SEARCHBUTTON", "Buscar", "E'SEARCH'.") + filas));
                return true;
            }
            case "tgrpart.aspx":
                if (post && ev == "E'GRABAR'.") { lock (Log) Log.Add("tgrpart grabar " + qs); await Html(c, "", 303, "hzonificacionporherencia.aspx?14,LO4236"); return true; }
                await Html(c, Pagina("tgrpart.aspx?" + qs, Hid("ARTCFG", "S") + Txt("_GRPDSC", "LONA PROMO") + Boton("BUTTON2", "Grabar", "E'GRABAR'.")));
                return true;
            case "hzonificacionporherencia.aspx":
                if (post) { Configurado = true; lock (Log) Log.Add("zonificacion confirmada"); await Html(c, "", 303, "hgrpart.aspx"); return true; }
                await Html(c, Pagina("hzonificacionporherencia.aspx?" + qs, Boton("BUTTON2", "Confirmar", "EENTER.")));
                return true;

            // ---------------------------------------------------------------- handheld
            case "hmenurecepciones.aspx":
                if (post && f.GetValueOrDefault("_OPCION") == "1") { await Html(c, "", 303, "hingetqextrf.aspx?" + qs); return true; }
                await Html(c, Pagina("hmenurecepciones.aspx?" + qs, Txt("_OPCION", "") + Boton("BUTTON1", "ACEPTAR", "EENTER.")));
                return true;
            case "hingetqextrf.aspx":
            {
                var ok = ""; var m1 = "";
                var orden = f.GetValueOrDefault("_ORDENDERECEPCION") ?? "";
                if (post && f.GetValueOrDefault("_OPCION") == "3") { await Html(c, "", 303, "hmenurecepciones.aspx?" + qs); return true; }
                if (post && f.GetValueOrDefault("_PALLET") is { Length: > 0 } pal)
                {
                    lock (Log) Log.Add($"rf recibir {orden.Trim()} {pal} {f.GetValueOrDefault("_ARTCOD")} {f.GetValueOrDefault("_CANTBULTOS")} {f.GetValueOrDefault("_LOTE")}");
                    if (!orden.StartsWith("R:")) m1 = "Seleccione la orden";
                    else if (Recibidos.Contains(pal)) m1 = "Contenedor ya existente";
                    else if (f.GetValueOrDefault("_ARTCOD") != "LO4236") m1 = "Articulo no pertenece a la orden";
                    else { Recibidos.Add(pal); ok = "OK !!"; }
                }
                var ordenes = Recepcion is { } r ? new[] { ("", "Seleccione"), ($"R:      {r} - Gráfica Austral", $"R: {r} - Gráfica Austral") } : new[] { ("", "Seleccione") };
                await Html(c, Pagina("hingetqextrf.aspx?" + qs, Sel("_ORDENDERECEPCION", orden, ordenes) + Txt("_PALLET", "") + Txt("_ARTCOD", "") + Txt("_CANTBULTOS", "") + Txt("_LOTE", "") +
                    Txt("_OPCION", "") + Hid("_MENSOK", ok) + Hid("_MENSLOC1", m1) + Boton("LEERETIQUETA", "ACEPTAR", "EENTER.")));
                return true;
            }
        }
        return false;
    }

    // ================================================================ LES (ya logueado)

    object Ingreso() => new { Id = 5001, NroRecepcion = 2877, Cliente = "Cuyo - Promo", Remito = "00019963", Fecha = "2026-10-22T09:00:00", Proveedor = "Gráfica Austral SA",
        Deposito = "", Observacion = "Lonas", Usuario = "RECEPCION", CantidadPallets = 3, HorarioLLegada = "09:00", HorarioSalida = "", DocumentacionIncompleta = false, Estado = EstadoLes,
        FechaCambioEstado = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds(), ObservacionCambioEstado = "" };

    public async Task<bool> AtenderLes(HttpContext c, string accion, Dictionary<string, string> f)
    {
        var q = c.Request.Query;
        switch (accion)
        {
            case "GetAll": await Json(c, new[] { Ingreso() }); return true;
            case "GetSlaConfig": await Json(c, new[] { new { Estado = "PENDIENTE DE DESCARGA", Horas = 4 }, new { Estado = "PEND INGRESO WMS", Horas = 24 } }); return true;
            case "GetIngresoDetail":
                if (q["id"] != "5001") { await Json(c, new { success = false, message = "No existe" }); return true; }
                await Json(c, new
                {
                    ingreso = new { Id = 5001, ClienteId = 14, Remito = "00019963", ProveedorId = "42985483", CantidadPallets = 3, Observacion = "Lonas", Estado = EstadoLes, Deposito = "", ADesglosar = false,
                        tieneRemitoFirmado = Descarga != null, urlRemitoFirmado = (string?)null, ObservacionCambioEstado = "" },
                    articulos = new[] { new { Id = 77, Codigo = "LO4236", Descripcion = "LONA PROMO", Cantidad = 300, RequiereValidacion = false, TieneEtiquetas = false } },
                });
                return true;
            case "GetPalletPhotos": await Json(c, new { fotos = Enumerable.Range(1, FotosSubidas).Select(i => new { nombre = $"p{i}.jpg", url = $"/les/fotos/p{i}.jpg" }) }); return true;
            case "CheckFotosPop": await Json(c, new { missingCodes = new[] { "LO4236" }, urls = new { } }); return true;
            case "CheckFotosContenedor": await Json(c, new { missingCodes = Array.Empty<string>() }); return true;
            case "ExportPdf": c.Response.ContentType = "application/pdf"; await c.Response.Body.WriteAsync(Sona.Caratula.PaginaPrueba("Carátula de pallet (prueba)")); return true;
            case "UploadPalletPhoto":
            case "UploadFotoPop":
            case "UploadFotoArticulo":
            {
                var form = await c.Request.ReadFormAsync();
                lock (Log) Log.Add($"les {accion} {form.Files.Count} archivo(s) {form["idIngreso"]}{form["codigo"]}");
                if (form.Files.Count != 1) { await Json(c, new { success = false, message = "Sin foto" }); return true; }
                if (accion == "UploadPalletPhoto") FotosSubidas++;
                await Json(c, new { success = true });
                return true;
            }
            case "DownloadPallets":
            {
                var form = await c.Request.ReadFormAsync();
                Descarga = JsonDocument.Parse(form["ingresoJson"].ToString());
                lock (Log) Log.Add($"les descarga {form["ingresoJson"]} remito={form.Files.Count}");
                EstadoLes = "PEND INGRESO WMS";
                await Json(c, new { success = true });
                return true;
            }
            case "GenerarEtiqueta":
            {
                var n = int.TryParse(f.GetValueOrDefault("cantidad"), out var x) ? x : 1;
                var et = Enumerable.Range(0, n).Select(_ => new { NroContenedor = $"{f.GetValueOrDefault("prefijo")}-{++_ultimoPc}", Codigo = f.GetValueOrDefault("codigo") }).ToList();
                lock (Log) Log.Add("les generar " + string.Join(",", et.Select(e => e.NroContenedor)));
                await Json(c, new { success = true, etiquetas = et });
                return true;
            }
            case "ConfirmarIngresoWms":
                lock (Log) Log.Add("les procesar " + f.GetValueOrDefault("id"));
                EstadoLes = "PEND VALIDAR";
                await Json(c, new { success = true });
                return true;
        }
        if (c.Request.Path.Value?.Contains("/les/fotos/") == true) { c.Response.ContentType = "image/jpeg"; await c.Response.Body.WriteAsync(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }); return true; }
        return false;
    }
}
