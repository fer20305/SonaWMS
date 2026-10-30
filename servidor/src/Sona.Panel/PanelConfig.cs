using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sona.Panel;

/// <summary>
/// Configuración propia de este panel (no del servidor): si corre el servidor adentro o se conecta a uno remoto (Docker),
/// y el usuario guardado para reconectar solo. Vive en %AppData%\SONA\panel.json; la clave se protege con DPAPI de Windows.
/// </summary>
public sealed class PanelConfig
{
    /// <summary>"integrado": este programa es el servidor · "remoto": se conecta a un servidor (Docker u otra PC).</summary>
    public string Modo { get; set; } = "integrado";
    public string ServidorRemoto { get; set; } = "http://localhost:8080";
    public string Usuario { get; set; } = "";
    public string ClaveProtegida { get; set; } = "";
    public bool Recordar { get; set; } = true;
    public bool IniciarConWindows { get; set; } = false;
    public bool MinimizarABandeja { get; set; } = true;

    static string Archivo => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SONA", "panel.json");

    public static PanelConfig Cargar()
    {
        try { if (File.Exists(Archivo)) return JsonSerializer.Deserialize<PanelConfig>(File.ReadAllText(Archivo)) ?? new(); } catch { }
        return new();
    }

    public void Guardar()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Archivo)!);
        File.WriteAllText(Archivo, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public string Clave
    {
        get
        {
            if (string.IsNullOrEmpty(ClaveProtegida)) return "";
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ClaveProtegida), null, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }
        set => ClaveProtegida = string.IsNullOrEmpty(value) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }

    /// <summary>Arranque automático con Windows (clave Run del usuario).</summary>
    public void AplicarInicioWindows()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (k == null) return;
            if (IniciarConWindows) k.SetValue("SONA Deposito", $"\"{Environment.ProcessPath}\" --minimizado");
            else k.DeleteValue("SONA Deposito", false);
        }
        catch { }
    }
}
