using System.Net;
using System.Text.RegularExpressions;

namespace Sona.Wms;

/// <summary>Recorridos SOLO LECTURA para el diagnóstico del módulo de ingreso (las pantallas quedan en la captura vía Traza).</summary>
public sealed partial class SesionEscritorio
{
    static readonly Regex Interesa = new(@"etiq|rollo|recep|ingres|contened|articul|operac", RegexOptions.IgnoreCase);

    /// <param name="empresas">Empresa → artículos de los ingresos pendientes.</param>
    public Task<List<string>> Diagnostico(Dictionary<int, List<string>> empresas) => EnSerie(async () =>
    {
        var notas = new List<string>();
        _base = null;
        var b = await Login();   // la pantalla de bienvenida queda capturada
        // menú: frames y links a pantallas (solo paneles h*.aspx, sin parámetros de alta/baja/modificación)
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cola = new Queue<(string Url, int Nivel)>();
        cola.Enqueue((b + "hinicio.aspx", 0));
        var abiertas = 0;
        while (cola.Count > 0 && abiertas < 30)
        {
            var (url, nivel) = cola.Dequeue();
            if (!vistos.Add(url)) continue;
            Respuesta r;
            try { r = await _nav.Get(url); abiertas++; } catch (Exception e) { notas.Add($"{url}: {e.Message}"); continue; }
            if (nivel >= 2) continue;
            foreach (Match m in Regex.Matches(r.Texto, @"(?:href|src)\s*=\s*[""']([^""'#]+?\.aspx[^""']*)[""'][^>]*>([^<]{0,80})", RegexOptions.IgnoreCase))
            {
                var link = WebUtility.HtmlDecode(m.Groups[1].Value.Trim());
                var texto = WebUtility.HtmlDecode(m.Groups[2].Value);
                var pagina = link.Split('?')[0].Split('/').Last();
                if (!pagina.StartsWith("h", StringComparison.OrdinalIgnoreCase)) continue;   // solo paneles (no procedimientos ni reportes)
                if (Regex.IsMatch(link, @"[?,](INS|UPD|DLT|DEL|BAJA|ELIM)", RegexOptions.IgnoreCase)) continue;
                // nada que cierre la sesión o pueda hacer algo al abrirse
                if (Regex.IsMatch(pagina + " " + texto, @"cerrar|salir|logout|desconec|\bbaja|dardebaja|elimin|borra|anula|depur", RegexOptions.IgnoreCase)) continue;
                if (nivel >= 1 && !Interesa.IsMatch(link + " " + texto)) continue;
                try { cola.Enqueue((new Uri(r.Url, link).ToString(), nivel + 1)); } catch { }
            }
        }
        notas.Add($"menú: {abiertas} pantalla(s) leídas");

        foreach (var (emp, arts) in empresas)
        {
            try
            {
                var r = await AbrirRecepcion(emp);
                var provs = Gx.Leer(r.Texto).Selects.GetValueOrDefault("_PROVEEDOR")?.Count ?? 0;
                notas.Add($"recepción empresa {emp}: {provs} proveedor(es) en la lista (no se grabó nada)");
            }
            catch (Exception e) { notas.Add($"recepción empresa {emp}: {e.Message}"); }
            foreach (var a in arts)
            {
                try
                {
                    var c = a.Trim().ToUpperInvariant();
                    var existe = await BuscarArticulo(emp, c, "");
                    var sinConf = existe && await BuscarArticulo(emp, c, "N");
                    notas.Add($"artículo {emp}/{c}: {(existe ? (sinConf ? "existe, sin configurar" : "existe, configurado") : "no existe")}");
                    if (existe)
                        try { await _nav.Get(_base + $"tgrpart.aspx?DSP,{emp},{c}"); } catch { }   // ficha en modo consulta
                }
                catch (Exception e) { notas.Add($"artículo {emp}/{a}: {e.Message}"); }
            }
            // pantalla de alta de artículo (solo se abre, no se graba)
            try { await _nav.Get(_base + $"tgrpart.aspx?INS,{emp}"); notas.Add($"alta de artículo empresa {emp}: pantalla abierta (sin grabar)"); }
            catch (Exception e) { notas.Add($"alta de artículo: {e.Message}"); }
        }
        return notas;
    });
}

public sealed partial class SesionHandheld
{
    /// <summary>Abre Recepciones › Recepción por contenedor y lista las órdenes (no recibe nada) y sale.</summary>
    public Task<string> DiagnosticoRecepciones(int empresa) => EnSerie(async () =>
    {
        var menu = await OpcionMenu(empresa, 1);
        var r = await Enviar(menu, new() { ["_OPCION"] = "1" }, f => f.BotonEvento("EENTER.") ?? Gx.BotonEnter(f));
        var ordenes = Gx.Leer(r.Texto).Selects.GetValueOrDefault("_ORDENDERECEPCION")?.Where(o => o.Valor.Trim().Length > 0).Select(o => o.Valor.Trim()).ToList() ?? new();
        return $"empresa {empresa}: pantalla {r.Url.AbsolutePath.Split('/').Last()}, {ordenes.Count} orden(es) de recepción: {string.Join(" | ", ordenes.Take(10))}";
    });
}
