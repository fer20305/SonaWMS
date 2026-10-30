using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Sona;

/// <summary>
/// Maestro de artículos de LES (tradebox) copiado en SONA para buscar todos los artículos de una empresa: código, descripción,
/// habilitado y stock según LES. Se actualiza solo cada 12 horas (o con el botón "Actualizar desde LES").
/// </summary>
public sealed class CatalogoArticulos(Db db, Servicios sv, ILogger<CatalogoArticulos> log)
{
    public const string Esquema = """
CREATE TABLE IF NOT EXISTS articulos_les (
  codigo TEXT PRIMARY KEY, descripcion TEXT, busqueda TEXT, id_cliente INTEGER, cliente TEXT, empresa INTEGER, habilitado INTEGER,
  marca TEXT, proyecto TEXT, stock_fisico REAL, stock_disponible REAL, stock_no_disponible REAL, stock_reservado REAL, actualizado TEXT);
CREATE INDEX IF NOT EXISTS articulos_les_empresa ON articulos_les(empresa);
""";

    public sealed record Estado(bool Actualizando, int Leidos, int? Total, string? Ultima, string? Error);
    volatile bool _actualizando;
    int _leidos; int? _total; string? _error;
    Task? _tarea;

    public Estado Situacion() => new(_actualizando, _leidos, _total, db.Q1("SELECT max(actualizado) AS u FROM articulos_les")?.S("u"), _error);

    /// <summary>Arranca la actualización en segundo plano (si no está corriendo).</summary>
    public void Actualizar()
    {
        lock (this)
        {
            if (_actualizando) return;
            _actualizando = true; _leidos = 0; _total = null; _error = null;
            _tarea = Task.Run(async () =>
            {
                try
                {
                    var hora = Db.Ahora();
                    const int tam = 200;
                    for (var pag = 1; pag <= 1000; pag++)
                    {
                        var (filas, total) = await sv.Les.ListarFichas(pag, tam);
                        _total ??= total;
                        foreach (var f in filas)
                        {
                            var emp = sv.ResolverEmpresa(f.Cliente) ?? f.IdCliente;
                            db.Ejecutar("""
                                INSERT INTO articulos_les (codigo, descripcion, busqueda, id_cliente, cliente, empresa, habilitado, marca, proyecto, stock_fisico, stock_disponible, stock_no_disponible, stock_reservado, actualizado)
                                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14)
                                ON CONFLICT(codigo) DO UPDATE SET descripcion=excluded.descripcion, busqueda=excluded.busqueda, id_cliente=excluded.id_cliente, cliente=excluded.cliente,
                                  empresa=excluded.empresa, habilitado=excluded.habilitado, marca=excluded.marca, proyecto=excluded.proyecto, stock_fisico=excluded.stock_fisico,
                                  stock_disponible=excluded.stock_disponible, stock_no_disponible=excluded.stock_no_disponible, stock_reservado=excluded.stock_reservado, actualizado=excluded.actualizado
                                """, f.Codigo, f.Descripcion, Norm(f.Codigo + " " + f.Descripcion + " " + f.Marca), f.IdCliente, f.Cliente, emp, f.Habilitado, f.Marca, f.Proyecto,
                                f.StockFisico, f.StockDisponible, f.StockNoDisponible, f.StockReservado, hora);
                        }
                        _leidos += filas.Count;
                        if (filas.Count < tam || (_total is int tt && _leidos >= tt)) break;
                    }
                    log.LogInformation("Catálogo de artículos de LES actualizado: {N} artículos", _leidos);
                }
                catch (Exception e) { _error = e.Message; log.LogWarning("No se pudo actualizar el catálogo de artículos de LES: {E}", e.Message); }
                finally { _actualizando = false; }
            });
        }
    }

    public Task? Tarea => _tarea;

    static string Norm(string? s)
    {
        var d = (s ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in d) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToUpperInvariant(ch));
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    /// <summary>Artículos de una empresa (o de todas), con texto (todas las palabras, sin acentos), solo habilitados y/o solo con stock.</summary>
    public object Buscar(int? empresa, string? texto, bool soloHabilitados, bool soloConStock, int limite = 1000)
    {
        // se actualiza solo si nunca se cargó o tiene más de 12 horas
        var ult = db.Q1("SELECT max(actualizado) AS u, count(*) AS n FROM articulos_les");
        if (!_actualizando && (ult?.L("n") is null or 0 || (DateTime.TryParse(ult.S("u"), null, DateTimeStyles.RoundtripKind, out var u) && DateTime.UtcNow - u.ToUniversalTime() > TimeSpan.FromHours(12))))
            Actualizar();
        var sql = new StringBuilder("SELECT codigo, descripcion, cliente, empresa, habilitado, marca, proyecto, stock_fisico, stock_disponible, stock_no_disponible, stock_reservado FROM articulos_les WHERE 1=1");
        var ps = new List<object?>();
        if (empresa != null) { ps.Add(empresa); sql.Append($" AND empresa=${ps.Count}"); }
        foreach (var w in Norm(texto).Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6)) { ps.Add("%" + w + "%"); sql.Append($" AND busqueda LIKE ${ps.Count}"); }
        if (soloHabilitados) sql.Append(" AND habilitado=1");
        if (soloConStock) sql.Append(" AND coalesce(stock_fisico,0) > 0");
        var total = db.Q1("SELECT count(*) AS n FROM (" + sql + ")", ps.ToArray())?.L("n") ?? 0;
        ps.Add(Math.Clamp(limite, 1, 5000));
        sql.Append($" ORDER BY coalesce(stock_fisico,0) DESC, codigo LIMIT ${ps.Count}");
        var filas = db.Q(sql.ToString(), ps.ToArray()).Select(f => new
        {
            articulo = f.S("codigo"), descripcion = f.S("descripcion"), cliente = f.S("cliente"), empresa = f.L("empresa"),
            empresaNombre = sv.NombreEmpresa((int?)f.L("empresa")) ?? f.S("cliente"), habilitado = f.B("habilitado"), marca = f.S("marca"), proyecto = f.S("proyecto"),
            stockFisico = f.D("stock_fisico"), stockDisponible = f.D("stock_disponible"), stockNoDisponible = f.D("stock_no_disponible"), stockReservado = f.D("stock_reservado"),
        }).ToList();
        var porEmpresa = db.Q("SELECT empresa, max(cliente) AS cliente, count(*) AS n FROM articulos_les GROUP BY empresa ORDER BY n DESC")
            .Select(f => new { empresa = f.L("empresa"), nombre = sv.NombreEmpresa((int?)f.L("empresa")) ?? f.S("cliente") ?? "(sin cliente)", articulos = f.L("n") }).ToList();
        return new { total, mostrados = filas.Count, items = filas, empresas = porEmpresa, estado = Situacion() };
    }
}
