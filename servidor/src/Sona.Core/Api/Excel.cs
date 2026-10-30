using System.Globalization;
using ClosedXML.Excel;

namespace Sona.Api;

public static class Excel
{
    static readonly (string Titulo, string Columna, double Ancho)[] Columnas =
    {
        ("Fecha y hora", "hora_servidor", 18), ("Operario", "usuario", 14), ("Tipo", "tipo", 14), ("Contenedor", "contenedor", 14),
        ("Artículo", "articulo", 16), ("Descripción", "descripcion", 40), ("Empresa", "empresa_nombre", 28), ("Cantidad", "cantidad", 10),
        ("Origen", "origen", 12), ("Destino", "destino", 12), ("Estado nuevo", "estado_nuevo", 22), ("Estado", "estado", 18),
        ("Respuesta WMS", "respuesta_wms", 40), ("Comentario", "comentario", 30),
    };

    public static byte[] Movimientos(List<Fila> filas)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Movimientos");
        for (var i = 0; i < Columnas.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = Columnas[i].Titulo;
            c.Style.Font.Bold = true; c.Style.Font.FontColor = XLColor.White; c.Style.Fill.BackgroundColor = XLColor.FromHtml("#0B2A42");
            ws.Column(i + 1).Width = Columnas[i].Ancho;
        }
        var r = 2;
        foreach (var f in filas)
        {
            for (var i = 0; i < Columnas.Length; i++)
            {
                var col = Columnas[i].Columna;
                var cell = ws.Cell(r, i + 1);
                if (col == "hora_servidor" && DateTime.TryParse(f.S(col), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d))
                { cell.Value = d.ToLocalTime(); cell.Style.DateFormat.Format = "dd/mm/yyyy hh:mm"; }
                else if (col == "cantidad" && f.D(col) is double n) cell.Value = n;
                else cell.Value = f.S(col) ?? "";
            }
            r++;
        }
        ws.Range(1, 1, Math.Max(1, r - 1), Columnas.Length).SetAutoFilter();
        ws.SheetView.FreezeRows(1);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
