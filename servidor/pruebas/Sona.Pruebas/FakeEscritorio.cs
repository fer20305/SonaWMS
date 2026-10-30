using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Sona.Pruebas;

/// <summary>
/// WMS de escritorio de prueba para el panel de producción: "Órdenes Pendientes de Aceptar" (hpenddeautorizar.aspx),
/// "Pendientes de Preparación" (hasigusuario.aspx), la simulación de faltantes (PDF real capturado) y "Ver OP".
/// Arma las grillas con los mismos nombres de campos que las páginas reales (22/10).
/// </summary>
public sealed class FakeEscritorio
{
    public sealed class Op { public int Emp; public string Tipo = "OPR"; public string Nro = ""; public string Empresa = ""; public string Destino = ""; public string Viaje = ""; public int Art; public int Uni; public string? Picker; public int Pend; public int Prior = 50; }
    public readonly List<Op> Aceptar = new()
    {
        new() { Emp = 6, Nro = "57558", Empresa = "Notro Bazar", Destino = "(DT) Rem:160197 / 11415-10 / Sucursal Shopping 26", Viaje = "SALTA", Art = 1, Uni = 1 },
        new() { Emp = 144, Nro = "28513", Empresa = "Río Claro Bebidas", Destino = "(DT) Rem:160224 / 9189-125 / Depósito Oeste 10", Viaje = "STAFE", Art = 4, Uni = 80 },
    };
    public readonly List<Op> Preparar = new()
    {
        new() { Emp = 6, Nro = "57559", Empresa = "Notro Bazar", Destino = "(DT) Rem:160199 / 10395-445 / Local Estación 16", Viaje = "JUJUY", Art = 11, Uni = 69, Picker = "VAGUIRRE", Pend = 3 },
        new() { Emp = 38, Nro = "258", Empresa = "Mburucuyá Natural", Destino = "(RU) Rem:160092 / 8085-24 / Sucursal Shopping 15", Viaje = "GBA Norte", Art = 1, Uni = 281, Picker = "OPER1", Pend = 1 },
    };
    static readonly (string Cod, string Nombre)[] PICKERS = { ("VAGUIRRE", "VICTORIA AGUIRRE"), ("JPERALTA", "JULIETA PERALTA"), ("LFARIAS", "LUCIA FARIAS"), ("OPER1", "OPERARIO UNO") };
    public readonly HashSet<string> Simuladas = new();
    /// <summary>Simula que el WMS cerró la sesión de SONA (otro ingreso con el mismo usuario): devuelve una pantalla sin la grilla.</summary>
    public int SesionPerdida;
    public readonly List<string> Log = new();
    readonly byte[] PDF = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "paginas", "sim_faltantes.pdf"));

    static string H(string s) => WebUtility.HtmlEncode(s);
    static string Hid(string n, string v) => $"<input type=\"hidden\" name=\"{n}\" value=\"{H(v)}\">";
    static string Boton(string n, string v, string ev, int grid) =>
        $"<input type=\"SUBMIT\" name=\"{n}\" value=\"{H(v)}\" class=\"SpecialButtons\" gxevent=\"E\\'{ev}\\'.\" onclick=\"GX_setgridevent({grid},   1);gxdisableformnvis();GX_setevent('E\\'{ev}\\'.');\">";
    static string Usuarios(string nombre, string? elegido) =>
        $"<select name=\"{nombre}\" class=\"Attribute\"><option value=\"\">Asignar</option>" +
        string.Concat(PICKERS.Select(p => $"<option value=\"{p.Cod}\"{(p.Cod == elegido ? " selected" : "")}>{p.Nombre}</option>")) + "</select>";
    static string Pagina(string accion, string titulo, string cuerpo) =>
        $"<html><head><title>{titulo}</title></head><body><div>Bienvenido, PRUEBA - CD: DEPOSITO SONA</div><form id=\"MAINFORM\" name=\"MAINFORM\" method=\"POST\" action=\"{accion}\">" +
        Hid("_EventName", "") + Hid("_EventGridId", "") + Hid("_EventRowId", "") + cuerpo + "</form></body></html>";

    string PaginaAceptar(int empresa, string extra = "")
    {
        var sb = new StringBuilder();
        sb.Append("<select name=\"_EMPRESA\" onchange=\"gxonchange(this)\"><option value=\"0\">Seleccione una Empresa</option><option value=\"9999\"" + (empresa == 9999 ? " selected" : "") + ">Pre-visualizar Todas</option><option value=\"6\"" + (empresa == 6 ? " selected" : "") + ">Notro</option><option value=\"144\"" + (empresa == 144 ? " selected" : "") + ">Río Claro Bebidas</option></select>");
        sb.Append(Boton("BUSCARCOD2", "Buscar", "CODIGO POSTAL", 121));
        var i = 0;
        foreach (var o in Aceptar.Where(o => empresa == 9999 || o.Emp == empresa))
        {
            var n = (++i).ToString("0000");
            sb.Append($"<tr><td><input type=\"checkbox\" name=\"_OP_{n}\" value=\"1\"></td><td><span id=\"span__EMPDSC_{n}\">{H(o.Empresa)}</span></td>");
            sb.Append(Hid("_EMPDSC_" + n, o.Empresa) + Hid("PENDFHPROG_" + n, "22/10/2026") + Hid("PENDTOAPLI_" + n, o.Tipo) + Hid("_DESTINO_" + n, o.Destino) + Hid("PENDNROAPL_" + n, o.Nro) +
                      Hid("PENDEMP_" + n, o.Emp.ToString()) + Hid("_SUMART_" + n, o.Art.ToString()) + Hid("_SUMUNI_" + n, o.Uni.ToString()) + Hid("_SUMKG_" + n, "40,00") + Hid("_SUMM3_" + n, "0,0216") +
                      Hid("_PENDVIAJESNROEXTERNO_" + n, o.Viaje) + Hid("PENDPRIOR_" + n, "50") + Hid("PENDPICKINGNODISP_" + n, "") + Hid("PENDLOTENODISP_" + n, "") + Hid("PENDVTONODISPONIBLE_" + n, "/  /"));
            sb.Append("<td>" + Usuarios("_NEWUSU_" + n, null) + "</td></tr>");
        }
        sb.Append(Usuarios("_USUARIOASIGMASIVA", null));
        sb.Append(Boton("CONFIRMAR4", "Confirmar Cambios", "ACTUALIZAR", 121) + Boton("CONFSINDEPURACION", "Confirmación SIN Depuración", "CONFSINDEPURACION", 121) +
                  Boton("BOTONSIMULAR", "Sim de Faltantes", "SIMULAR", 121) + Hid("_MSG2", ""));
        return Pagina("hpenddeautorizar.aspx", "Ordenes Pendientes de Aceptación", sb.ToString() + extra);
    }

    string PaginaPreparar(bool conGrilla)
    {
        var sb = new StringBuilder();
        sb.Append("<select name=\"_EMPRESA\"><option value=\"0\" selected>Todas las Empresas</option></select>");
        sb.Append("<select name=\"_SOLOASIGNADAS\"><option value=\"Todas\" selected>Todas las asignaciones</option><option value=\"Asignadas\">Asignadas</option></select>");
        sb.Append(Boton("BUSCARCOD4", "Buscar", "CODIGO POSTAL", 112));
        var i = 0;
        if (conGrilla)
            foreach (var o in Preparar)
            {
                var n = (++i).ToString("0000");
                var barra = o.Art == 0 ? 0 : (o.Art - o.Pend) * 10 / o.Art * 10;
                sb.Append($"<input type=\"checkbox\" name=\"_OP_{n}\" value=\"1\">" + Hid("GXimg_IMGSTATUS_" + n, barra > 0 ? "images/stop.bmp" : "images/play.bmp") +
                          Hid("GXimg_IMGBARRITA_" + n, $"Images/Barras/{barra:00}.bmp") + Hid("_CANTPARANATORIG_" + n, o.Art.ToString()) + Hid("_CANTTOTAL_" + n, o.Pend.ToString()) +
                          Hid("ASGFHPROG_" + n, "21/10/2026") + Hid("_EMPDSC_" + n, o.Empresa) + Hid("ASGEMP_" + n, o.Emp.ToString()) + Hid("ASGTOAPLI_" + n, o.Tipo) +
                          Hid("_DESTINO_" + n, o.Destino) + Hid("ASGNROAPL_" + n, o.Nro) + Hid("_SUMART_" + n, o.Art.ToString()) + Hid("_SUMUNI_" + n, o.Uni.ToString()) +
                          Hid("_VIAJE_" + n, o.Viaje) + Hid("_YACOMENZOAPREP_" + n, "") + Usuarios("_NEWUSU_" + n, o.Picker) +
                          $"<input type=\"text\" name=\"_ASGPRIOR_{n}\" value=\"{o.Prior}\">");
            }
        sb.Append(Boton("CONFIRMAR2", "Confirmar Cambios", "ACTUALIZAR", 112));
        return Pagina("hasigusuario.aspx", "Ordenes Pendientes de Preparación", sb.ToString());
    }

    static IEnumerable<string> Tildadas(Dictionary<string, string> f) => f.Where(kv => kv.Key.StartsWith("_OP_") && kv.Value == "1").Select(kv => kv.Key[4..]);

    /// <summary>Devuelve true si atendió la página.</summary>
    public async Task<bool> Atender(HttpContext c, string pg, Dictionary<string, string> f, bool post)
    {
        async Task Html(string h) { c.Response.ContentType = "text/html; charset=utf-8"; await c.Response.WriteAsync(h); }
        var ev = f.GetValueOrDefault("_EventName") ?? "";
        switch (pg)
        {
            case "hpenddeautorizar.aspx":
            {
                lock (Log) Log.Add("aceptar " + ev + " " + string.Join(",", Tildadas(f).Select(n => f.GetValueOrDefault("PENDNROAPL_" + n) + ":" + f.GetValueOrDefault("_NEWUSU_" + n))));
                if (!post) { await Html(PaginaAceptar(0)); return true; }
                var emp = int.TryParse(f.GetValueOrDefault("_EMPRESA"), out var e) ? e : 0;
                var filas = Tildadas(f).Select(n => (Nro: f.GetValueOrDefault("PENDNROAPL_" + n) ?? "", Picker: f.GetValueOrDefault("_NEWUSU_" + n) ?? "")).ToList();
                if (ev == "E'SIMULAR'." && filas.Count > 0)
                {
                    foreach (var x in filas) Simuladas.Add(x.Nro);
                    var link = $"<a href=\"osimulacionpdfcomboysustituto.aspx?PDF,C%3a%5cSGL%5cPRODUCCION%5c,1,{emp},{filas[0].Nro}%2c,OPR\" target=\"_blank\"><img src=\"images/pdf.bmp\"></a>";
                    await Html(PaginaAceptar(emp, link)); return true;
                }
                if (ev == "E'CONFSINDEPURACION'." && filas.Count > 0)
                {
                    foreach (var x in filas)
                    {
                        var o = Aceptar.FirstOrDefault(a => a.Nro == x.Nro);
                        if (o == null || x.Picker.Length == 0) continue;
                        Aceptar.Remove(o); o.Picker = x.Picker; o.Pend = o.Art; Preparar.Add(o);
                    }
                    await Html(PaginaAceptar(emp, "<span>Controlo Stock </span><span>Faltantes N</span>")); return true;
                }
                await Html(PaginaAceptar(emp)); return true;
            }
            case "hasigusuario.aspx":
            {
                lock (Log) Log.Add("preparar " + ev);
                if (!post && SesionPerdida > 0) { SesionPerdida--; await Html("<html><head><title>Sesion finalizada</title></head><body><form id=\"MAINFORM\" name=\"MAINFORM\">La sesion fue cerrada</form></body></html>"); return true; }
                if (!post) { await Html(PaginaPreparar(false)); return true; }
                if (ev == "E'ACTUALIZAR'.")
                    foreach (var n in Tildadas(f))
                        if (Preparar.FirstOrDefault(o => o.Nro == f.GetValueOrDefault("ASGNROAPL_" + n)) is { } o)
                        {
                            if (f.GetValueOrDefault("_NEWUSU_" + n) is { Length: > 0 } p) o.Picker = p;
                            if (int.TryParse(f.GetValueOrDefault("_ASGPRIOR_" + n), out var pr)) o.Prior = pr;
                        }
                await Html(PaginaPreparar(true)); return true;
            }
            case "osimulacionpdfcomboysustituto.aspx":
            case "overoppdf.aspx":
                lock (Log) Log.Add("pdf " + pg);
                c.Response.ContentType = "application/pdf";
                await c.Response.Body.WriteAsync(PDF);
                return true;
        }
        return false;
    }
}
