using Sona.Les;

namespace Sona;

/// <summary>
/// Credenciales de cada usuario para las otras plataformas (LES Ingresos, e-TMS…): cada uno las carga una vez en SONA
/// (panel web, panel de escritorio o módulo de Ingreso), quedan cifradas en el servidor y SONA entra con ellas en su nombre.
/// El WMS no se carga acá: es el usuario y la clave con los que se entra a SONA (se actualiza sola en cada ingreso).
/// </summary>
public sealed class MisCredenciales(Configuracion cfg, Servicios sv)
{
    public const string Esquema = """
CREATE TABLE IF NOT EXISTS credenciales (
  usuario TEXT NOT NULL, sistema TEXT NOT NULL, usuario_ext TEXT, clave TEXT, actualizado TEXT, probado TEXT, ok INTEGER, mensaje TEXT,
  PRIMARY KEY (usuario, sistema));
""";

    public sealed record Sistema(string Codigo, string Nombre, string Descripcion, bool Editable, bool UsaWmsSiFalta);

    public static readonly List<Sistema> Sistemas = new()
    {
        new("wms", "WMS (handheld y escritorio)", "Es tu usuario y tu clave de SONA. Si cambiás la clave en el WMS, entrá a SONA con la nueva y se actualiza sola.", false, false),
        new("les_ingresos", "LES · Ingresos pendientes", "Módulo de Ingreso (recepciones, fotos, carátulas, pase a Pend. validar). Si no cargás nada, SONA prueba con tu usuario y clave del WMS.", true, true),
        new("etms", "e-TMS", "Consultas de ingresos y egresos por artículo. Si no cargás nada, se usa el usuario general del sistema o tu usuario del WMS.", true, true),
    };

    readonly Db _db = sv.Db;

    /// <summary>Pasa el usuario del LES que se cargaba antes en "Mi usuario del LES" (columnas de usuarios) a la tabla nueva.</summary>
    public void Migrar()
    {
        _db.Ejecutar("""
            INSERT OR IGNORE INTO credenciales (usuario, sistema, usuario_ext, clave, actualizado)
            SELECT usuario, 'les_ingresos', les_usuario, les_clave, $1 FROM usuarios
            WHERE les_usuario IS NOT NULL AND les_usuario <> '' AND les_clave IS NOT NULL AND les_clave <> ''
            """, Db.Ahora());
    }

    static Sistema De(string sistema) => Sistemas.FirstOrDefault(s => s.Codigo == sistema) ?? throw new ErrorUsuario($"Sistema desconocido: {sistema}.", 404);

    public object Listar(string usuario)
    {
        var u = usuario.ToUpperInvariant();
        var filas = _db.Q("SELECT * FROM credenciales WHERE usuario=$1", u).ToDictionary(f => f.S("sistema")!);
        var wmsCargada = !string.IsNullOrEmpty(_db.Q1("SELECT clave_cifrada FROM usuarios WHERE usuario=$1", u)?.S("clave_cifrada"));
        return new
        {
            usuario = u,
            sistemas = Sistemas.Select(s =>
            {
                filas.TryGetValue(s.Codigo, out var f);
                var propia = f != null && !string.IsNullOrEmpty(f.S("clave"));
                return new
                {
                    codigo = s.Codigo, nombre = s.Nombre, descripcion = s.Descripcion, editable = s.Editable,
                    usuario = s.Codigo == "wms" ? u : f?.S("usuario_ext"),
                    cargada = s.Codigo == "wms" ? wmsCargada : propia,
                    usaWms = s.Codigo != "wms" && !propia && s.UsaWmsSiFalta,
                    actualizado = f?.S("actualizado"), probado = f?.S("probado"), ok = f?.L("ok") is long ok ? ok == 1 : (bool?)null, mensaje = f?.S("mensaje"),
                };
            }),
        };
    }

    /// <summary>Usuario y clave propios del sistema; si no cargó, null (el que llama decide si usa los del WMS).</summary>
    public (string Usuario, string Clave)? Propias(string usuario, string sistema)
    {
        var f = _db.Q1("SELECT usuario_ext, clave FROM credenciales WHERE usuario=$1 AND sistema=$2", usuario.ToUpperInvariant(), sistema);
        if (f == null || string.IsNullOrEmpty(f.S("usuario_ext")) || string.IsNullOrEmpty(f.S("clave"))) return null;
        return (f.S("usuario_ext")!, Seguridad.Descifrar(f.S("clave")!, cfg.Secreto));
    }

    /// <summary>Las del WMS (las de SONA), guardadas cifradas al ingresar.</summary>
    public (string Usuario, string Clave)? Wms(string usuario)
    {
        var u = usuario.ToUpperInvariant();
        var c = _db.Q1("SELECT clave_cifrada FROM usuarios WHERE usuario=$1", u)?.S("clave_cifrada");
        return string.IsNullOrEmpty(c) ? null : (u, Seguridad.Descifrar(c, cfg.Secreto));
    }

    /// <summary>Guarda (después de probar que entra). Si no entra, no se guarda y se avisa qué dijo la plataforma.</summary>
    public async Task<object> Guardar(string usuario, string sistema, string usuarioExt, string clave)
    {
        var s = De(sistema);
        if (!s.Editable) throw new ErrorUsuario("Esta credencial no se carga acá.");
        if (string.IsNullOrWhiteSpace(usuarioExt) || string.IsNullOrEmpty(clave)) throw new ErrorUsuario("Usuario y clave obligatorios.");
        var u = usuario.ToUpperInvariant();
        await Probar(u, sistema, usuarioExt.Trim(), clave);
        _db.Ejecutar("""
            INSERT INTO credenciales (usuario, sistema, usuario_ext, clave, actualizado, probado, ok, mensaje) VALUES ($1,$2,$3,$4,$5,$5,1,'Entró bien.')
            ON CONFLICT(usuario, sistema) DO UPDATE SET usuario_ext=excluded.usuario_ext, clave=excluded.clave, actualizado=excluded.actualizado,
              probado=excluded.probado, ok=1, mensaje=excluded.mensaje
            """, u, sistema, usuarioExt.Trim(), Seguridad.Cifrar(clave, cfg.Secreto), Db.Ahora());
        if (sistema == "les_ingresos") _db.Ejecutar("UPDATE usuarios SET les_usuario=$2, les_clave=$3 WHERE usuario=$1", u, usuarioExt.Trim(), Seguridad.Cifrar(clave, cfg.Secreto));
        Cambio?.Invoke(u, sistema);
        _db.Auditar("credenciales", u + "/" + sistema, new { usuario_ext = usuarioExt.Trim(), accion = "guardar" }, u);
        return Listar(u);
    }

    public object Borrar(string usuario, string sistema)
    {
        De(sistema);
        var u = usuario.ToUpperInvariant();
        _db.Ejecutar("DELETE FROM credenciales WHERE usuario=$1 AND sistema=$2", u, sistema);
        if (sistema == "les_ingresos") _db.Ejecutar("UPDATE usuarios SET les_usuario=NULL, les_clave=NULL WHERE usuario=$1", u);
        Cambio?.Invoke(u, sistema);
        _db.Auditar("credenciales", u + "/" + sistema, new { accion = "borrar" }, u);
        return Listar(u);
    }

    /// <summary>Prueba las guardadas (o, si no hay, las del WMS) y deja anotado el resultado.</summary>
    public async Task<object> ProbarGuardadas(string usuario, string sistema)
    {
        var s = De(sistema);
        var u = usuario.ToUpperInvariant();
        var cr = sistema == "wms" ? Wms(u) : Propias(u, sistema) ?? (s.UsaWmsSiFalta ? Wms(u) : null);
        if (cr == null) throw new ErrorUsuario("No hay usuario y clave para probar.");
        bool ok; string msj;
        try { await Probar(u, sistema, cr.Value.Usuario, cr.Value.Clave); ok = true; msj = Propias(u, sistema) == null && sistema != "wms" ? "Entró bien con tu usuario del WMS." : "Entró bien."; }
        catch (Exception e) when (e is ErrorUsuario or HttpRequestException or TaskCanceledException or Sona.Wms.WmsError) { ok = false; msj = e.Message; }
        _db.Ejecutar("UPDATE credenciales SET probado=$3, ok=$4, mensaje=$5 WHERE usuario=$1 AND sistema=$2", u, sistema, Db.Ahora(), ok, msj);
        return new { ok, mensaje = msj, lista = Listar(u) };
    }

    /// <summary>Se avisa cuando alguien cambia una credencial (para olvidar sesiones abiertas con la vieja).</summary>
    public Action<string, string>? Cambio { get; set; }

    async Task Probar(string usuario, string sistema, string usuarioExt, string clave)
    {
        switch (sistema)
        {
            case "les_ingresos":
            {
                var cli = new LesIngresos(cfg, usuarioExt, clave);
                var cap = new Captura(cfg, "login_les_" + usuario);
                cli.Traza = cap.Les;   // queda en capturas lo que respondió el LES (con la clave tapada)
                try { await cli.Login(); cap.Nota("Login LES Ingresos OK: " + cli.UltimoLogin); }
                catch (Exception e) { cap.Nota($"Login LES Ingresos ERROR ({cli.UltimoLogin}): {e.Message}"); throw; }
                break;
            }
            case "etms":
                await new Etms(cfg).Probar(usuarioExt, clave);
                break;
            case "wms":
                await sv.NuevaSesion(usuario, clave).Login();
                break;
        }
    }
}
