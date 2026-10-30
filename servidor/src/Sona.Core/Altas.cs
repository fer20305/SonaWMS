using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Sona.Les;
using Sona.Wms;

namespace Sona;

/// <summary>
/// Crear contenedor (analistas y administradores): LES genera el número de contenedor (Ingresos pendientes › Nueva etiqueta)
/// y SONA lo carga en el WMS con la Carga inicial (Chequeos y ajustes › 7) en la posición y nivel indicados, con la cantidad.
/// La etiqueta se imprime en la terminal. Queda registrado como movimiento "alta".
/// </summary>
public sealed class Altas(Servicios sv, IngresosLes ingresos, Eventos ev, ILogger<Altas> log)
{
    readonly Db _db = sv.Db;

    public sealed record Resultado(bool Ok, string? Contenedor, string Mensaje, string Etapa, string? Articulo, string? Descripcion, string? Empresa, string? Posicion)
    {
        /// <summary>Impresora virtual: la etiqueta para ver / imprimir / guardar en PDF (no hace falta impresora enchufada).</summary>
        public string? Etiqueta => Contenedor != null ? "/api/etiquetas/" + Uri.EscapeDataString(Contenedor) : null;
    }

    /// <param name="contenedor">Si ya se creó el contenedor en LES (y falló la carga en el WMS) se reintenta solo la carga inicial.</param>
    public async Task<Resultado> Crear(string usuario, string articulo, double cantidad, string posicion, int? nivel, string? prefijo, string? contenedor)
    {
        var art = (articulo ?? "").Trim().ToUpperInvariant();
        if (art.Length == 0) throw new ErrorUsuario("Falta el artículo.");
        if (cantidad <= 0) throw new ErrorUsuario("La cantidad tiene que ser mayor que cero.");
        var info = await sv.InfoArticulo(art);
        var empresa = info.Empresa ?? throw new ErrorUsuario($"No se pudo saber de qué empresa es {art} (buscador SONA / LES).");
        var pos = await sv.ResolverPosicion(posicion, nivel, usuario) ?? throw new ErrorUsuario($"Posición \"{posicion}\" no reconocida.");
        if (pos.Nivel == null) throw new ErrorUsuario("Falta el nivel.");
        var ubic = pos.UbicCtrl ?? throw new ErrorUsuario($"La posición {pos.Legible} no tiene Ubic.Ctrl: sincronizá el layout.");
        var ficha = _db.Q1("SELECT habilitada FROM pos_efectiva WHERE codigo=$1", pos.Codigo);
        if (ficha != null && !ficha.B("habilitada")) throw new ErrorUsuario($"La posición {pos.Legible} está inhabilitada.");

        var s = sv.SesionDe(usuario);
        var cont = contenedor?.Trim().ToUpperInvariant();
        string? aviso = null;
        if (string.IsNullOrEmpty(cont))
        {
            var e = await ingresos.CrearContenedor(usuario, s.Clave, art, prefijo ?? "PP");
            cont = e.Contenedor; aviso = e.Aviso;
            log.LogInformation("LES creó el contenedor {Cont} para {Art} ({Usuario})", cont, art, usuario);
        }
        ResultadoWms r;
        try { r = await s.CargaInicial(empresa, cont, art, cantidad, ubic, pos.Nivel.Value); }
        catch (Exception ex) { r = new ResultadoWms(false, ex.Message); }

        var id = Guid.NewGuid().ToString();
        _db.Ejecutar("""
            INSERT INTO movimientos (id, grupo, tipo, contenedor, articulo, cantidad, empresa, origen, destino, destino_ubic, nivel,
              usuario, hora_servidor, estado, respuesta_wms, comentario, detalle, aplicado_en)
            VALUES ($1,$1,'alta',$2,$3,$4,$5,NULL,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15)
            """, id, cont, art, cantidad, empresa, pos.Codigo, ubic, pos.Nivel, usuario, Db.Ahora(), r.Ok ? "aplicado" : "error", r.Mensaje,
            "Crear contenedor (LES + carga inicial)", new JsonObject { ["descripcion"] = info.Descripcion, ["prefijo"] = prefijo ?? "PP", ["aviso_les"] = aviso },
            r.Ok ? Db.Ahora() : null);
        if (r.Ok)
            _db.Ejecutar("""
                INSERT INTO contenedores (codigo, articulo, empresa, cantidad, posicion, actualizado) VALUES ($1,$2,$3,$4,$5,$6)
                ON CONFLICT(codigo) DO UPDATE SET articulo=excluded.articulo, empresa=excluded.empresa, cantidad=excluded.cantidad, posicion=excluded.posicion, actualizado=excluded.actualizado
                """, cont, art, empresa, cantidad, pos.Codigo, Db.Ahora());
        sv.OlvidarContenedor(cont);
        ev.Emitir(new() { ["tipo"] = "movimiento", ["id"] = id, ["estado"] = r.Ok ? "aplicado" : "error", ["usuario"] = usuario, ["contenedor"] = cont });
        var msj = r.Ok ? $"{cont} cargado en {pos.Legible} con {cantidad:0.##} unidades." : $"LES creó {cont} pero el WMS no hizo la carga inicial: {r.Mensaje}";
        if (aviso != null) msj += " · LES: " + aviso;
        return new Resultado(r.Ok, cont, msj, r.Ok ? "listo" : "carga_inicial", art, info.Descripcion, info.EmpresaNombre, pos.Legible);
    }
}
