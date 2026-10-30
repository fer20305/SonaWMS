using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sona;

/// <summary>
/// Configuración de todas las plataformas que usa SONA Depósito (WMS, LES/tradebox, buscador SONA, servidor).
/// Vive en la carpeta de datos como config.json y se edita desde el panel (Sistema › Configuración).
/// </summary>
public sealed class Configuracion
{
    public int Puerto { get; set; } = 8080;
    public WmsConfig Wms { get; set; } = new();
    public LesConfig Les { get; set; } = new();
    public EtmsConfig Etms { get; set; } = new();
    public ServidorConfig Servidor { get; set; } = new();
    public ProduccionConfig Produccion { get; set; } = new();
    public BultosConfig Bultos { get; set; } = new();
    public EtiquetasConfig Etiquetas { get; set; } = new();
    public List<string> Admins { get; set; } = new() { "SLEDESM6" };
    public int SincronizarLayoutHoras { get; set; } = 12;

    public sealed class WmsConfig
    {
        /// <summary>Red local del depósito.</summary>
        public string Local { get; set; } = "http://10.0.50.207/sglwms_prod/";
        /// <summary>IP externa (se usa si la local no responde).</summary>
        public string Externo { get; set; } = "http://203.0.113.10/sglwms_prod/";
        public int TimeoutSegundos { get; set; } = 25;
    }

    public sealed class LesConfig
    {
        public string Url { get; set; } = "http://les.les-ejemplo.com/";
        public string Account { get; set; } = "http://account.les-ejemplo.com/";
        public string Buscador { get; set; } = "https://les.les-ejemplo.com/Api/Buscador";
        public string Usuario { get; set; } = "";
        /// <summary>Se guarda cifrada en el archivo.</summary>
        public string Clave { get; set; } = "";
    }

    public sealed class EtmsConfig
    {
        public string Url { get; set; } = "https://www.tms-ejemplo.com.ar/";
        /// <summary>Vacío = se usa el usuario y la clave del WMS de quien consulta.</summary>
        public string Usuario { get; set; } = "";
        /// <summary>Se guarda cifrada en el archivo.</summary>
        public string Clave { get; set; } = "";
    }

    public sealed class ServidorConfig
    {
        /// <summary>Direcciones que se les informa a los celulares (para el QR de configuración).</summary>
        public string DireccionLocal { get; set; } = "http://10.0.50.207:8080";
        public string DireccionExterna { get; set; } = "http://203.0.113.10:8080";
    }

    /// <summary>Impresión de las etiquetas de medida del control de bulto (app › Control de bulto › Guardar y generar etiquetas).</summary>
    /// <summary>Reimpresión de la etiqueta del contenedor (con QR): impresora Zebra y hoja A4.</summary>
    public sealed class EtiquetasConfig
    {
        public string ImpresoraZebra { get; set; } = "";
        public string ImpresoraA4 { get; set; } = "";
        public int AnchoMm { get; set; } = 100;
        public int AltoMm { get; set; } = 70;
        /// <summary>La medida vieja por defecto (100 x 80) se pasó una vez a la etiqueta real del rollo (100 x 70).</summary>
        public bool MedidaRevisada { get; set; }
    }

    public sealed class BultosConfig
    {
        /// <summary>Al guardar en LES, SONA imprime las etiquetas en la impresora del puesto.</summary>
        public bool ImprimirAlGuardar { get; set; } = true;
        /// <summary>Impresora (de la PC del servidor) por puesto de origen: PUESTO1..PUESTO4. Sin impresora para el puesto = la "general".</summary>
        public Dictionary<string, string> Impresoras { get; set; } = new();
        /// <summary>Impresora general (vacío = la predeterminada de Windows).</summary>
        public string Impresora { get; set; } = "";
        public int Copias { get; set; } = 1;
        /// <summary>true = solo los bultos nuevos y los modificados; false = todos los de la solicitud (como la página oficial).</summary>
        public bool SoloNuevos { get; set; } = true;
        /// <summary>Tamaño de la etiqueta en mm.</summary>
        public int AnchoMm { get; set; } = 100;
        public int AltoMm { get; set; } = 80;
    }

    public sealed class ProduccionConfig
    {
        /// <summary>Usuario del WMS con el que el panel de producción lee las bandejas (vacío = el de quien está usando el panel).</summary>
        public string Usuario { get; set; } = "";
        /// <summary>Se guarda cifrada en el archivo.</summary>
        public string Clave { get; set; } = "";
        /// <summary>Impresora de la PC del servidor para las carátulas (vacío = la predeterminada de Windows).</summary>
        public string Impresora { get; set; } = "";
        public bool ImprimirAlAsignar { get; set; } = true;
        public int Copias { get; set; } = 1;
        /// <summary>Cómo ordena el WMS la "Prioridad de preparación": true = el número más alto sale primero; false = el más bajo (1 = más urgente).</summary>
        public bool PrioridadMayorPrimero { get; set; } = false;
    }

    // ------------------------------------------------------------------ carga y guardado

    [JsonIgnore] public string Carpeta { get; private set; } = "";
    [JsonIgnore] public string Secreto { get; private set; } = "";

    static readonly JsonSerializerOptions Opciones = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Carpeta de datos: SONA_DATOS, o %ProgramData%\SONA\Deposito en Windows, o ./datos.</summary>
    public static string CarpetaPorDefecto()
    {
        var env = Environment.GetEnvironmentVariable("SONA_DATOS");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SONA", "Deposito");
        return Path.Combine(AppContext.BaseDirectory, "datos");
    }

    public static Configuracion Cargar(string? carpeta = null)
    {
        carpeta ??= CarpetaPorDefecto();
        Directory.CreateDirectory(carpeta);
        var archivo = Path.Combine(carpeta, "config.json");
        Configuracion c;
        try { c = File.Exists(archivo) ? JsonSerializer.Deserialize<Configuracion>(File.ReadAllText(archivo), Opciones) ?? new() : new(); }
        catch { c = new(); }
        c.Carpeta = carpeta;
        var sec = Path.Combine(carpeta, "secreto.key");
        if (!File.Exists(sec)) File.WriteAllText(sec, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        c.Secreto = File.ReadAllText(sec).Trim();
        // etiqueta del contenedor: la medida vieja por defecto (100 x 80) pasa a la del rollo real (100 x 70), una sola vez
        if (!c.Etiquetas.MedidaRevisada)
        {
            if (c.Etiquetas.AnchoMm == 100 && c.Etiquetas.AltoMm == 80) c.Etiquetas.AltoMm = 70;
            c.Etiquetas.MedidaRevisada = true;
        }
        // variables de entorno pisan el archivo (útil en Docker)
        c.Puerto = int.TryParse(Environment.GetEnvironmentVariable("SONA_PUERTO"), out var p) ? p : c.Puerto;
        if (Environment.GetEnvironmentVariable("SONA_WMS_LOCAL") is { Length: > 0 } wl) c.Wms.Local = wl;
        if (Environment.GetEnvironmentVariable("SONA_WMS_EXTERNO") is { Length: > 0 } we) c.Wms.Externo = we;
        if (!File.Exists(archivo)) c.Guardar();
        return c;
    }

    public void Guardar()
    {
        File.WriteAllText(Path.Combine(Carpeta, "config.json"), JsonSerializer.Serialize(this, Opciones));
    }

    [JsonIgnore] public string ClaveLes => Seguridad.DescifrarSiHace(Les.Clave, Secreto);
    public void PonerClaveLes(string clave) => Les.Clave = string.IsNullOrEmpty(clave) ? "" : Seguridad.Cifrar(clave, Secreto);

    [JsonIgnore] public string ClaveProduccion => Seguridad.DescifrarSiHace(Produccion.Clave, Secreto);
    public void PonerClaveProduccion(string clave) => Produccion.Clave = string.IsNullOrEmpty(clave) ? "" : Seguridad.Cifrar(clave, Secreto);

    [JsonIgnore] public string ClaveEtms => Seguridad.DescifrarSiHace(Etms.Clave, Secreto);
    public void PonerClaveEtms(string clave) => Etms.Clave = string.IsNullOrEmpty(clave) ? "" : Seguridad.Cifrar(clave, Secreto);

    /// <summary>Bases del WMS en el orden a probar.</summary>
    [JsonIgnore]
    public IEnumerable<string> BasesWms => new[] { Wms.Local, Wms.Externo }.Where(b => !string.IsNullOrWhiteSpace(b)).Select(b => b.EndsWith('/') ? b : b + "/").Distinct();
}
