using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;

namespace RRHHNovedades.Web.Services;

/// <summary>Una tardanza puntual (un empleado, un día) para el detalle.</summary>
public record TardanzaDetalle(
    DateOnly Fecha,
    TimeOnly? HoraEntrada,
    int Minutos);

/// <summary>Resumen de tardanzas de un empleado en el rango.</summary>
public record TardanzaEmpleado(
    int EmpleadoId,
    string? Legajo,
    string ApellidoNombre,
    string? Area,
    int Cantidad,
    int MinutosTotal,
    int RachaMaxima, // días CON FICHAJE consecutivos llegando tarde (francos/feriados no cortan)
    IReadOnlyList<TardanzaDetalle> Detalle)
{
    public double MinutosPromedio => Cantidad == 0 ? 0 : (double)MinutosTotal / Cantidad;
}

public record TardanzasReporte(
    DateOnly Desde,
    DateOnly Hasta,
    IReadOnlyList<TardanzaEmpleado> PorEmpleado, // orden: más minutos primero
    int JornadasConFichaje);                     // presentes + tardes del rango (para el % del KPI)

public interface ITardanzasService
{
    /// <summary>Reporte de tardanzas del rango [desde, hasta] (ambos inclusive), opcionalmente por área.</summary>
    Task<TardanzasReporte> ReporteAsync(DateOnly desde, DateOnly hasta, string? area = null, CancellationToken ct = default);

    /// <summary>Excel (.xlsx): hoja "Resumen" (por empleado) + hoja "Detalle" (tardanza por tardanza).</summary>
    Task<byte[]> ExcelAsync(DateOnly desde, DateOnly hasta, string? area = null, CancellationToken ct = default);
}

/// <summary>
/// Reporte de tardanzas por rango. La tardanza la marca Humand (incidencia LATE) y ya
/// está persistida en Novedades (Estado=Tarde + MinutosTardanza); acá solo se agrega.
/// La "racha" mide reincidencia: días consecutivos DE TRABAJO llegando tarde — un franco
/// o feriado en el medio no la corta, un día presente en hora sí.
/// </summary>
public class TardanzasService(IDbContextFactory<AppDbContext> dbFactory) : ITardanzasService
{
    public async Task<TardanzasReporte> ReporteAsync(DateOnly desde, DateOnly hasta, string? area = null, CancellationToken ct = default)
    {
        if (hasta < desde) throw new ArgumentException("hasta < desde", nameof(hasta));

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Presentes y tardes del rango: las tardes arman el reporte; los presentes
        // completan el denominador del KPI y permiten cortar rachas.
        var fichajes = await db.Novedades
            .Include(n => n.Empleado)
            .Where(n => n.Fecha >= desde && n.Fecha <= hasta
                && (n.Estado == EstadoJornada.Tarde || n.Estado == EstadoJornada.Presente)
                && (area == null || n.Empleado.Area == area))
            .OrderBy(n => n.Fecha)
            .ToListAsync(ct);

        var porEmpleado = fichajes
            .GroupBy(n => n.EmpleadoId)
            .Select(g =>
            {
                var tardes = g.Where(n => n.Estado == EstadoJornada.Tarde).ToList();
                if (tardes.Count == 0) return null;
                var e = g.First().Empleado;
                return new TardanzaEmpleado(
                    e.Id, e.Legajo, e.ApellidoNombre, e.Area,
                    tardes.Count,
                    tardes.Sum(n => n.MinutosTardanza),
                    RachaMaxima(g.Select(n => (n.Fecha, EsTarde: n.Estado == EstadoJornada.Tarde))),
                    tardes.Select(n => new TardanzaDetalle(n.Fecha, n.HoraEntrada, n.MinutosTardanza)).ToList());
            })
            .Where(x => x is not null).Select(x => x!)
            .OrderByDescending(x => x.MinutosTotal).ThenByDescending(x => x.Cantidad)
            .ToList();

        return new TardanzasReporte(desde, hasta, porEmpleado, fichajes.Count);
    }

    /// <summary>
    /// Racha máxima de jornadas con fichaje consecutivas llegando tarde. Recibe los días
    /// trabajados (presente o tarde) en orden cronológico. internal para testear.
    /// </summary>
    internal static int RachaMaxima(IEnumerable<(DateOnly Fecha, bool EsTarde)> jornadas)
    {
        int max = 0, actual = 0;
        foreach (var (_, esTarde) in jornadas.OrderBy(j => j.Fecha))
        {
            actual = esTarde ? actual + 1 : 0;
            if (actual > max) max = actual;
        }
        return max;
    }

    public async Task<byte[]> ExcelAsync(DateOnly desde, DateOnly hasta, string? area = null, CancellationToken ct = default)
    {
        var r = await ReporteAsync(desde, hasta, area, ct);
        var esAr = CultureInfo.GetCultureInfo("es-AR");

        using var wb = new XLWorkbook();

        var ws = wb.Worksheets.Add("Resumen");
        ws.Cell(1, 1).Value = $"Tardanzas {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}" + (area is null ? "" : $" — {area}");
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13);
        string[] cab = ["Legajo", "Apellido y nombre", "Área", "Tardanzas", "Min. total", "Min. promedio", "Racha máx."];
        for (int c = 0; c < cab.Length; c++) ws.Cell(3, c + 1).Value = cab[c];
        ws.Range(3, 1, 3, cab.Length).Style.Font.SetBold();
        int fila = 4;
        foreach (var t in r.PorEmpleado)
        {
            ws.Cell(fila, 1).Value = t.Legajo ?? "—";
            ws.Cell(fila, 2).Value = t.ApellidoNombre;
            ws.Cell(fila, 3).Value = t.Area ?? "—";
            ws.Cell(fila, 4).Value = t.Cantidad;
            ws.Cell(fila, 5).Value = t.MinutosTotal;
            ws.Cell(fila, 6).Value = Math.Round(t.MinutosPromedio, 1);
            ws.Cell(fila, 7).Value = t.RachaMaxima;
            fila++;
        }
        ws.SheetView.FreezeRows(3);
        ws.Columns().AdjustToContents();

        var wd = wb.Worksheets.Add("Detalle");
        string[] cabD = ["Fecha", "Legajo", "Apellido y nombre", "Área", "Entrada", "Minutos tarde"];
        for (int c = 0; c < cabD.Length; c++) wd.Cell(1, c + 1).Value = cabD[c];
        wd.Range(1, 1, 1, cabD.Length).Style.Font.SetBold();
        int fd = 2;
        foreach (var t in r.PorEmpleado)
            foreach (var d in t.Detalle)
            {
                wd.Cell(fd, 1).Value = d.Fecha.ToDateTime(TimeOnly.MinValue);
                wd.Cell(fd, 1).Style.NumberFormat.Format = "dd/mm/yyyy";
                wd.Cell(fd, 2).Value = t.Legajo ?? "—";
                wd.Cell(fd, 3).Value = t.ApellidoNombre;
                wd.Cell(fd, 4).Value = t.Area ?? "—";
                wd.Cell(fd, 5).Value = d.HoraEntrada?.ToString("HH:mm", esAr) ?? "—";
                wd.Cell(fd, 6).Value = d.Minutos;
                fd++;
            }
        wd.SheetView.FreezeRows(1);
        if (fd > 2) wd.RangeUsed()!.SetAutoFilter();
        wd.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
