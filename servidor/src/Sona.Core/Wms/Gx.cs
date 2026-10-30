using System.Net;
using System.Text.RegularExpressions;

namespace Sona.Wms;

/// <summary>Lectura de páginas GeneXus 9 del WMS: formularios, botones con evento, grillas y mensajes.</summary>
public static class Gx
{
    public sealed record Boton(string Nombre, string Valor, string Evento, string? GridId, string? RowId, bool Oculto);
    public sealed record Opcion(string Valor, string Texto, bool Elegida);
    /// <summary>Campo de entrada que el operario puede escribir (text/number/password, visible y no de solo lectura).</summary>
    public sealed record Campo(string Nombre, string Tipo);
    public sealed class Formulario
    {
        public string Accion = "";
        public Dictionary<string, string> Valores = new();
        public List<Boton> Botones = new();
        public List<Campo> Editables = new();
        public Dictionary<string, List<Opcion>> Selects = new();
        public Boton? BotonEvento(string evento) => Botones.FirstOrDefault(b => b.Evento == evento);
    }

    // Los valores entre comillas pueden traer ">" (el botón de página siguiente de "Ver un hueco" es value="->"):
    // si se corta en el primer ">", el botón queda sin evento y SONA no puede pasar de página (solo veía 5 huecos).
    static readonly Regex ReForm = new(@"<form\b((?:[^>""]|""[^""]*"")*)>", RegexOptions.IgnoreCase);
    static readonly Regex ReInput = new(@"<input\b((?:[^>""]|""[^""]*"")*)>", RegexOptions.IgnoreCase);
    static readonly Regex ReSelect = new(@"<select\b([^>]*)>([\s\S]*?)</select>", RegexOptions.IgnoreCase);
    static readonly Regex ReOption = new(@"<option\b([^>]*)>([\s\S]*?)(?=<option\b|$)", RegexOptions.IgnoreCase);
    static readonly Regex ReScript = new(@"<(script|style)\b[\s\S]*?</\1>", RegexOptions.IgnoreCase);
    static readonly Regex ReAttr = new(@"([A-Za-z_:][-A-Za-z0-9_:.]*)\s*(?:=\s*(""([^""]*)""|'([^']*)'|([^\s""'>]+)))?");
    static readonly Regex ReGridEvt = new(@"GX_setgridevent\(\s*(\d+)\s*,\s*(\d+)\s*\)");
    static readonly Regex ReSetEvt = new(@"GX_setevent\('((?:\\'|[^'])*)'\)");
    static readonly Regex ReSpan = new(@"<span\b[^>]*\bid=""?span_([A-Za-z0-9_]+?)_(\d{4})""?[^>]*>([\s\S]*?)</span>", RegexOptions.IgnoreCase);
    static readonly Regex ReMensaje = new(@"^_(MENS20POS\d*|MENSAJE\d*|MENSLOC\d*|MENSOK|MSG)$", RegexOptions.IgnoreCase);

    public static string Desescapar(string s) => s.Contains('&') ? WebUtility.HtmlDecode(s) : s;

    public static Dictionary<string, string> Atributos(string s)
    {
        var m = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match r in ReAttr.Matches(s))
        {
            var k = r.Groups[1].Value.ToLowerInvariant();
            if (m.ContainsKey(k)) continue;
            var v = r.Groups[3].Success ? r.Groups[3].Value : r.Groups[4].Success ? r.Groups[4].Value : r.Groups[5].Success ? r.Groups[5].Value : "";
            m[k] = Desescapar(v);
        }
        return m;
    }

    public static string Texto(string html) =>
        Regex.Replace(Desescapar(Regex.Replace(ReScript.Replace(html, " "), "<[^>]+>", " ")).Replace(' ', ' '), @"\s+", " ").Trim();

    public static Formulario Leer(string html)
    {
        var f = new Formulario();
        var fm = ReForm.Match(html);
        if (fm.Success && Atributos(fm.Groups[1].Value).TryGetValue("action", out var acc)) f.Accion = acc;
        var limpio = ReScript.Replace(html, " ");
        foreach (Match r in ReInput.Matches(limpio))
        {
            var a = Atributos(r.Groups[1].Value);
            if (!a.TryGetValue("name", out var nombre) || string.IsNullOrEmpty(nombre) || a.ContainsKey("disabled")) continue;
            var tipo = (a.GetValueOrDefault("type") ?? "text").ToLowerInvariant();
            if (tipo is "submit" or "button" or "image" or "reset")
            {
                var onclick = a.GetValueOrDefault("onclick") ?? "";
                var evento = (a.GetValueOrDefault("gxevent") ?? (ReSetEvt.Match(onclick) is { Success: true } se ? se.Groups[1].Value : "")).Replace("\\'", "'");
                var g = ReGridEvt.Match(onclick);
                f.Botones.Add(new Boton(nombre, a.GetValueOrDefault("value") ?? nombre, evento, g.Success ? g.Groups[1].Value : null, g.Success ? g.Groups[2].Value : null,
                    Regex.IsMatch(a.GetValueOrDefault("style") ?? "", @"display\s*:\s*none", RegexOptions.IgnoreCase)));
            }
            else if (tipo is "checkbox" or "radio")
            {
                if (a.ContainsKey("checked")) f.Valores[nombre] = a.GetValueOrDefault("value") ?? "on";
            }
            else
            {
                f.Valores[nombre] = a.GetValueOrDefault("value") ?? "";
                var oculto = tipo == "hidden" || Regex.IsMatch(a.GetValueOrDefault("style") ?? "", @"display\s*:\s*none|visibility\s*:\s*hidden", RegexOptions.IgnoreCase);
                if (!oculto && !a.ContainsKey("readonly") && tipo is "text" or "number" or "tel" or "password" or "") f.Editables.Add(new Campo(nombre, tipo));
            }
        }
        foreach (Match r in ReSelect.Matches(limpio))
        {
            var a = Atributos(r.Groups[1].Value);
            if (!a.TryGetValue("name", out var nombre) || a.ContainsKey("disabled")) continue;
            var ops = new List<Opcion>();
            foreach (Match o in ReOption.Matches(r.Groups[2].Value))
            {
                var oa = Atributos(o.Groups[1].Value);
                var t = Texto(o.Groups[2].Value);
                ops.Add(new Opcion((oa.GetValueOrDefault("value") ?? t).Trim(), t, oa.ContainsKey("selected")));
            }
            if (ops.Count == 0) continue;
            f.Selects[nombre] = ops;
            f.Valores[nombre] = (ops.FirstOrDefault(o => o.Elegida) ?? ops[0]).Valor;
        }
        return f;
    }

    /// <summary>Valores que manda el navegador al apretar un botón (o Enter si no hay).</summary>
    public static Dictionary<string, string> Envio(Formulario f, Boton? boton, Dictionary<string, string>? cambios = null)
    {
        var v = new Dictionary<string, string>(f.Valores);
        if (cambios != null) foreach (var (k, x) in cambios) v[k] = x;
        if (boton != null && boton.Nombre.Length > 0) v[boton.Nombre] = boton.Valor;
        v["_EventName"] = string.IsNullOrEmpty(boton?.Evento) ? "EENTER." : boton!.Evento;
        v["_EventGridId"] = boton?.GridId ?? "";
        v["_EventRowId"] = boton?.RowId ?? "";
        return v;
    }

    public static Boton? BotonEnter(Formulario f) =>
        f.Botones.FirstOrDefault(b => b.Evento == "EENTER." && !b.Oculto) ?? f.Botones.FirstOrDefault(b => b.Evento == "EENTER.");

    public static bool EsLogin(string html) =>
        Regex.IsMatch(html, @"name=""?_USR""?", RegexOptions.IgnoreCase) && Regex.IsMatch(html, @"name=""?_PASSWORD""?", RegexOptions.IgnoreCase);

    /// <summary>Celdas de grilla: span_COL_0001 → fila "0001" { COL: valor }.</summary>
    public static SortedDictionary<string, Dictionary<string, string>> Grilla(string html)
    {
        var filas = new SortedDictionary<string, Dictionary<string, string>>();
        foreach (Match m in ReSpan.Matches(html))
        {
            if (!filas.TryGetValue(m.Groups[2].Value, out var fila)) filas[m.Groups[2].Value] = fila = new();
            fila[m.Groups[1].Value] = Texto(m.Groups[3].Value);
        }
        return filas;
    }

    public static string Mensajes(Formulario f) =>
        string.Join(" ", f.Valores.Where(kv => ReMensaje.IsMatch(kv.Key)).Select(kv => kv.Value.Trim()).Where(s => s.Length > 0));
}
