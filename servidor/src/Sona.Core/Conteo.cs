using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Sona.Dominio;

namespace Sona;

public sealed record LineaConteo(string Contenedor, bool Encontrado, double? Cantidad, string? Articulo);

/// <summary>
/// Conteo ciego: el contador ve contenedor, artículo y foto, pero no la cantidad del sistema.
/// El servidor compara con el buscador SONA y el analista revisa las diferencias.
/// </summary>
public sealed class Conteo(Servicios sv, Eventos ev)
{
    readonly Db _db = sv.Db;

    public static readonly string[] Modos = { "ciego", "confirmacion", "articulo" };

    /// <summary>
    /// Código único de la posición: "DD01902" (columna de 3 dígitos, como viene del buscador), "DD-019-02" y "DD1902" son la misma.
    /// Antes la tarea guardaba un formato y el celular mandaba otro, y la posición contada seguía "pendiente".
    /// </summary>
    string Canon(string p)
    {
        var t = p.Trim().ToUpperInvariant();
        var x = Posicion.Interpretar(t, sv.CarasConocidas());
        return x?.Nivel != null ? Posicion.Codigo(x.Cara, x.Columna, x.Nivel.Value) : t;
    }

    /// <summary>
    /// Crea una tarea. Modos: "ciego" (no ve cantidades), "confirmacion" (ve lo que dice el sistema y confirma o corrige)
    /// y "articulo" (las posiciones salen de dónde figura el artículo; solo se cuentan los contenedores de ese artículo).
    /// </summary>
    public Fila CrearTarea(string nombre, IEnumerable<string> posiciones, string? asignadoA, string creadoPor, string? modo = null, string? articulo = null)
    {
        var m = Modos.Contains(modo) ? modo! : "ciego";
        var lista = posiciones.Where(p => !string.IsNullOrWhiteSpace(p)).Select(Canon).Distinct().ToList();
        if (lista.Count == 0) throw new ErrorUsuario(m == "articulo" ? $"El artículo {articulo} no figura en ninguna posición (buscador SONA)." : "La tarea no tiene posiciones.");
        var asig = string.IsNullOrWhiteSpace(asignadoA) ? null : asignadoA.ToUpperInvariant();
        var art = string.IsNullOrWhiteSpace(articulo) ? null : articulo.Trim().ToUpperInvariant();
        var id = Convert.ToInt64(_db.Escalar("INSERT INTO conteo_tareas (nombre, posiciones, asignado_a, creado_por, creado, modo, articulo) VALUES ($1,$2,$3,$4,$5,$6,$7) RETURNING id",
            nombre, lista, asig, creadoPor, Db.Ahora(), m, art));
        ev.Emitir(new() { ["tipo"] = "conteo", ["id"] = id, ["estado"] = "abierta", ["asignado_a"] = asig },
            (u, rol) => asig == null || u == asig || Seguridad.RolesPanel.Contains(rol));
        return _db.Q1("SELECT * FROM conteo_tareas WHERE id=$1", id)!;
    }

    /// <summary>Posiciones donde figura un artículo según el buscador SONA (para el conteo por artículo).</summary>
    public async Task<List<string>> PosicionesDeArticulo(string articulo)
    {
        var regs = await sv.Buscador.Articulo(articulo.Trim());
        return regs.Select(r => Posicion.Normalizar(r.Posicion)).Where(p => p != null && Regex.IsMatch(p, @"\d{2}$")).Select(p => p!).Distinct().OrderBy(p => p).ToList();
    }

    public List<string> PosicionesDeRango(string cara, int desde, int hasta, List<int>? niveles)
    {
        var c = cara.ToUpperInvariant();
        var r = _db.Q("SELECT codigo, nivel FROM posiciones WHERE cara=$1 AND columna BETWEEN $2 AND $3 ORDER BY columna, nivel", c, desde, hasta)
            .Where(f => niveles == null || niveles.Count == 0 || niveles.Contains(f.I("nivel")!.Value)).Select(f => f.S("codigo")!).ToList();
        if (r.Count > 0) return r;
        var lista = new List<string>();
        for (var col = desde; col <= hasta; col++)
            foreach (var n in niveles is { Count: > 0 } ? niveles : new List<int> { 1, 2, 3, 4, 5, 6 }) lista.Add(Posicion.Codigo(c, col, n));
        return lista;
    }

    public List<Fila> TareasDe(string usuario, string rol, bool soloAbiertas = false)
    {
        var todas = Seguridad.RolesPanel.Contains(rol) && !soloAbiertas;
        var tareas = _db.Q("""
            SELECT t.*, json_array_length(t.posiciones) AS total,
                   (SELECT count(*) FROM conteo_lineas l WHERE l.tarea_id = t.id AND l.novedad <> 'ok') AS diferencias
            FROM conteo_tareas t
            WHERE ($1 OR $3 OR t.asignado_a IS NULL OR t.asignado_a = $2) AND ($1 OR t.estado <> 'cerrada')
            ORDER BY t.estado = 'cerrada', t.creado DESC LIMIT 200
            """, todas, usuario, Seguridad.RolesPanel.Contains(rol));
        if (tareas.Count == 0) return tareas;
        // contadas = posiciones de la tarea que tienen conteo (comparando por el código único de la posición)
        var ids = string.Join(",", tareas.Select(t => t.L("id")));
        var hechas = _db.Q($"SELECT DISTINCT tarea_id, posicion FROM conteo_lineas WHERE tarea_id IN ({ids})")
            .GroupBy(f => f.L("tarea_id")).ToDictionary(g => g.Key ?? 0, g => g.Select(f => Canon(f.S("posicion")!)).ToHashSet());
        foreach (var t in tareas)
        {
            var pos = (t["posiciones"] as JsonArray)?.Select(x => Canon(x!.GetValue<string>())).ToList() ?? new();
            t["contadas"] = hechas.TryGetValue(t.L("id") ?? 0, out var h) ? pos.Count(h.Contains) : 0;
        }
        return tareas;
    }

    public object Tarea(long id)
    {
        var t = _db.Q1("SELECT * FROM conteo_tareas WHERE id=$1", id) ?? throw new ErrorUsuario("No existe la tarea.", 404);
        // se agrupa por el código único (tareas y conteos viejos pueden tener otro formato)
        var contadas = _db.Q("SELECT posicion, count(*) AS n, sum(novedad <> 'ok') AS difs FROM conteo_lineas WHERE tarea_id=$1 GROUP BY posicion", id)
            .GroupBy(f => Canon(f.S("posicion")!)).ToDictionary(g => g.Key, g => (N: g.Sum(f => f.L("n") ?? 0), Difs: g.Sum(f => f.L("difs") ?? 0)));
        var pos = (t["posiciones"] as JsonArray)?.Select(x => x!.GetValue<string>()).ToList() ?? new();
        var tot = _db.Q1("SELECT sum(cantidad_contada) AS contado, sum(cantidad_wms) AS sistema FROM conteo_lineas WHERE tarea_id=$1 AND contenedor IS NOT NULL", id);
        t["total_contado"] = tot?.D("contado"); t["total_sistema"] = tot?.D("sistema");
        t["detalle"] = pos.Select(p => { var k = Canon(p); var hay = contadas.TryGetValue(k, out var c); return new { posicion = p, contada = hay, lineas = hay ? c.N : 0, diferencias = hay ? c.Difs : 0 }; }).ToList();
        t["contadas"] = pos.Count(p => contadas.ContainsKey(Canon(p)));
        return t;
    }

    /// <summary>
    /// Lo que el contador espera encontrar en la posición. En modo ciego y por artículo no se manda la cantidad;
    /// en modo confirmación sí. En modo por artículo solo van los contenedores de ese artículo.
    /// </summary>
    public async Task<object> PosicionParaContar(string usuario, string codigo, long? tareaId, string? modoSuelto)
    {
        var p = Posicion.Interpretar(codigo, sv.CarasConocidas());
        if (p == null || p.Nivel == null) throw new ErrorUsuario($"Posición \"{codigo}\" no válida (falta el nivel).");
        string modo = Modos.Contains(modoSuelto) ? modoSuelto! : "ciego";
        string? articulo = null;
        if (tareaId != null)
        {
            var t = _db.Q1("SELECT estado, modo, articulo FROM conteo_tareas WHERE id=$1", tareaId) ?? throw new ErrorUsuario("No existe la tarea.", 404);
            if (t.S("estado") == "cerrada") throw new ErrorUsuario("La tarea está cerrada: ya no se puede contar.");
            modo = t.S("modo") ?? "ciego"; articulo = t.S("articulo");
        }
        var o = await sv.Ocupacion(usuario, p.Cara, p.Columna, p.Nivel.Value);
        var conts = new List<object>();
        foreach (var c in o?.Contenedores ?? new())
        {
            var i = await sv.InfoContenedor(c);
            if (articulo != null && i.Articulo != null && !i.Articulo.Equals(articulo, StringComparison.OrdinalIgnoreCase)) continue;
            conts.Add(new
            {
                contenedor = i.Contenedor, articulo = i.Articulo, descripcion = i.Descripcion, empresa = i.EmpresaNombre, foto = i.Foto, fotos = i.Fotos,
                cantidad = modo == "confirmacion" ? i.Cantidad : null,
            });
        }
        return new
        {
            posicion = Posicion.Codigo(p.Cara, p.Columna, p.Nivel.Value), legible = Posicion.Legible(p.Cara, p.Columna, p.Nivel),
            modo, articulo, vacia = conts.Count == 0, contenedores = conts,
        };
    }

    public async Task<object> Cargar(string usuario, long? tareaId, string posicion, List<LineaConteo> lineas, List<string> esperados)
    {
        var codigo = Canon(posicion);
        var esp = esperados.Select(e => e.ToUpperInvariant()).ToList();
        if (tareaId != null)
        {
            if (_db.Q1("SELECT estado FROM conteo_tareas WHERE id=$1", tareaId)?.S("estado") == "cerrada") throw new ErrorUsuario("La tarea está cerrada: ya no se puede contar.");
            _db.Ejecutar("UPDATE conteo_tareas SET estado='en_curso' WHERE id=$1 AND estado='abierta'", tareaId);
            _db.Ejecutar("DELETE FROM conteo_lineas WHERE tarea_id=$1 AND posicion=$2 AND revision='pendiente'", tareaId, codigo);
        }
        var vistos = new HashSet<string>();
        var resultado = new List<(string C, string N)>();
        foreach (var l in lineas)
        {
            var c = l.Contenedor.Trim().ToUpperInvariant();
            vistos.Add(c);
            sv.OlvidarContenedor(c);
            var info = await sv.InfoContenedor(c, false);
            string nov;
            if (!l.Encontrado) nov = "falta";
            else if (!esp.Contains(c)) nov = "ajeno";
            else if (l.Cantidad == null || info.Cantidad == null || Math.Abs(l.Cantidad.Value - info.Cantidad.Value) < 1e-6) nov = "ok";
            else if (l.Cantidad > info.Cantidad) nov = "sobra";
            else nov = l.Cantidad == 0 ? "falta" : "diferencia";
            _db.Ejecutar("INSERT INTO conteo_lineas (tarea_id, posicion, contenedor, articulo, cantidad_contada, cantidad_wms, novedad, usuario, hora) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9)",
                tareaId, codigo, c, l.Articulo ?? info.Articulo, l.Cantidad, info.Cantidad, nov, usuario, Db.Ahora());
            resultado.Add((c, nov));
        }
        foreach (var e in esp.Where(e => !vistos.Contains(e)))
        {
            var info = await sv.InfoContenedor(e, false);
            _db.Ejecutar("INSERT INTO conteo_lineas (tarea_id, posicion, contenedor, articulo, cantidad_contada, cantidad_wms, novedad, usuario, hora) VALUES ($1,$2,$3,$4,NULL,$5,'falta',$6,$7)",
                tareaId, codigo, e, info.Articulo, info.Cantidad, usuario, Db.Ahora());
            resultado.Add((e, "falta"));
        }
        if (resultado.Count == 0)
        {
            // posición vacía confirmada: queda registrada para que la tarea la tome como contada
            _db.Ejecutar("INSERT INTO conteo_lineas (tarea_id, posicion, contenedor, articulo, cantidad_contada, cantidad_wms, novedad, usuario, hora) VALUES ($1,$2,NULL,NULL,0,0,'ok',$3,$4)",
                tareaId, codigo, usuario, Db.Ahora());
            resultado.Add(("(vacía)", "ok"));
        }
        var difs = resultado.Count(r => r.N != "ok");
        ev.Emitir(new() { ["tipo"] = "conteo", ["tarea"] = tareaId, ["posicion"] = codigo, ["lineas"] = resultado.Count, ["diferencias"] = difs, ["usuario"] = usuario }, (u, rol) => Seguridad.RolesPanel.Contains(rol));
        // el contador no ve el detalle (sigue siendo ciego): solo si hay que revisar
        return new { posicion = codigo, lineas = resultado.Count, revisar = difs > 0 };
    }

    /// <summary>Líneas de conteo. Por defecto solo las que tienen diferencia; con <paramref name="todas"/> también las que dieron OK (conteos sueltos incluidos).</summary>
    public List<Fila> Diferencias(long? tarea, string? revision, bool todas = false) => _db.Q("""
        SELECT l.*, COALESCE(t.nombre, 'Conteo suelto') AS tarea, a.descripcion FROM conteo_lineas l
        LEFT JOIN conteo_tareas t ON t.id = l.tarea_id LEFT JOIN articulos a ON a.codigo = l.articulo
        WHERE ($3 OR l.novedad <> 'ok') AND ($1 IS NULL OR l.tarea_id = $1 OR ($1 = -1 AND l.tarea_id IS NULL)) AND ($2 IS NULL OR l.revision = $2)
        ORDER BY l.hora DESC LIMIT 2000
        """, tarea, revision, todas);

    /// <summary>
    /// Desde las diferencias (ZDIF): arma una tarea de reconteo. tipo "posicion" = las posiciones de las líneas elegidas;
    /// tipo "articulo" = una tarea por artículo con todas las posiciones donde figura. Las líneas quedan "a recontar".
    /// </summary>
    public async Task<List<Fila>> Reconteo(List<long> lineas, string tipo, string? asignadoA, string? modo, string analista)
    {
        var filas = lineas.Distinct().Select(id => _db.Q1("SELECT * FROM conteo_lineas WHERE id=$1", id)).Where(f => f != null).Select(f => f!).ToList();
        if (filas.Count == 0) throw new ErrorUsuario("Elegí al menos una línea.");
        var creadas = new List<Fila>();
        if (tipo == "articulo")
        {
            var arts = filas.Select(f => f.S("articulo")).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a!.ToUpperInvariant()).Distinct().ToList();
            if (arts.Count == 0) throw new ErrorUsuario("Las líneas elegidas no tienen artículo.");
            foreach (var art in arts)
            {
                var pos = (await PosicionesDeArticulo(art)).Union(filas.Where(f => string.Equals(f.S("articulo"), art, StringComparison.OrdinalIgnoreCase)).Select(f => f.S("posicion")!)).Distinct().ToList();
                creadas.Add(CrearTarea($"Reconteo artículo {art}", pos, asignadoA, analista, "articulo", art));
            }
        }
        else
        {
            var pos = filas.Select(f => f.S("posicion")!).Distinct().OrderBy(p => p).ToList();
            creadas.Add(CrearTarea("Reconteo " + string.Join(", ", pos.Take(3)) + (pos.Count > 3 ? $" y {pos.Count - 3} más" : ""), pos, asignadoA, analista, modo ?? "ciego"));
        }
        foreach (var f in filas) Revisar(f.L("id")!.Value, "recontar", analista);
        return creadas;
    }

    public void Revisar(long id, string revision, string analista) => _db.Ejecutar("UPDATE conteo_lineas SET revision=$2, revisado_por=$3 WHERE id=$1", id, revision, analista);

    public void Cerrar(long id)
    {
        _db.Ejecutar("UPDATE conteo_tareas SET estado='cerrada' WHERE id=$1", id);
        ev.Emitir(new() { ["tipo"] = "conteo", ["id"] = id, ["estado"] = "cerrada" });
    }
}
