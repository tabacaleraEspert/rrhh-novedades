using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;

namespace RRHHNovedades.Web.Services;

/// <summary>Una ausencia puntual (un empleado, un día) para la hoja de detalle.</summary>
public record AusentismoDetalle(
    DateOnly Fecha,
    int EmpleadoId,
    string? Legajo,
    string ApellidoNombre,
    string? Area,
    bool Justificada,
    string? Motivo); // primer tipo de licencia de Humand; null en injustificadas

/// <summary>Agregado de un bucket (día, semana o mes). Los % se calculan sobre el total de ausencias.</summary>
public record AusentismoAgregado(
    string Etiqueta,
    DateOnly Desde,
    DateOnly Hasta, // inclusive, recortado al rango consultado
    int Justificadas,
    int Injustificadas,
    int JornadasEvaluables)
{
    public int Total => Justificadas + Injustificadas;
    public double PctJustificadas => Total == 0 ? 0 : (double)Justificadas / Total;
    public double PctInjustificadas => Total == 0 ? 0 : (double)Injustificadas / Total;
    /// <summary>Ausencias sobre jornadas evaluables del bucket (presentes + tardes + ausencias).</summary>
    public double TasaAusentismo => JornadasEvaluables == 0 ? 0 : (double)Total / JornadasEvaluables;
}

public record AusentismoReporte(
    DateOnly Desde,
    DateOnly Hasta,
    IReadOnlyList<AusentismoDetalle> Detalle,
    IReadOnlyList<AusentismoAgregado> PorDia,
    IReadOnlyList<AusentismoAgregado> PorSemana,
    IReadOnlyList<AusentismoAgregado> PorMes);

public interface IAusentismoService
{
    /// <summary>Reporte de ausentismo del rango [desde, hasta] (ambos inclusive), opcionalmente por área.</summary>
    Task<AusentismoReporte> ReporteAsync(DateOnly desde, DateOnly hasta, string? area = null, CancellationToken ct = default);

    /// <summary>Excel (.xlsx): hoja "Resumen" (día/semana/mes) + hoja "Detalle" (ausencia por ausencia).</summary>
    Task<byte[]> ExcelAsync(DateOnly desde, DateOnly hasta, string? area = null, CancellationToken ct = default);
}

/// <summary>
/// Reporte de ausentismo por rango arbitrario de fechas (a diferencia del presentismo, que
/// liquida 26→25). Reglas (definidas con RRHH, 30-jul-2026):
///   Ausencia = AusenteJustificado (cualquier licencia) o AusenteInjustificado, nunca en feriado
///   (misma precedencia feriado &gt; licencia que la planilla de presentismo).
///   Semanas de lunes a domingo, recortadas al rango consultado; meses calendario.
///   Tasa de ausentismo = ausencias / jornadas evaluables (presente + tarde + ausencias del día;
///   excluye francos y pendientes — es la dotación que debía trabajar, histórica, no la actual).
/// </summary>
public class AusentismoService(IDbContextFactory<AppDbContext> dbFactory) : IAusentismoService
{
    private static readonly CultureInfo EsAr = CultureInfo.GetCultureInfo("es-AR");

    public async Task<AusentismoReporte> ReporteAsync(DateOnly desde, DateOnly hasta, string? area = null, CancellationToken ct = default)
    {
        if (hasta < desde) throw new ArgumentException("hasta < desde", nameof(hasta));

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var ausencias = await db.Novedades
            .Include(n => n.Empleado)
            .Where(n => n.Fecha >= desde && n.Fecha <= hasta && !n.EsFeriado
                && (n.Estado == EstadoJornada.AusenteJustificado || n.Estado == EstadoJornada.AusenteInjustificado)
                && (area == null || n.Empleado.Area == area))
            .OrderBy(n => n.Fecha).ThenBy(n => n.Empleado.Apellido).ThenBy(n => n.Empleado.Nombre)
            .ToListAsync(ct);

        var detalle = ausencias.Select(n => new AusentismoDetalle(
            n.Fecha, n.EmpleadoId, n.Empleado.Legajo, n.Empleado.ApellidoNombre, n.Empleado.Area,
            n.Estado == EstadoJornada.AusenteJustificado,
            n.Estado == EstadoJornada.AusenteJustificado
                ? PresentismoService.SepararTipos(n.MotivoNovedad).FirstOrDefault() ?? "Licencia"
                : null)).ToList();

        var evaluables = (await db.Novedades
            .Where(n => n.Fecha >= desde && n.Fecha <= hasta && !n.EsFeriado
                && (n.Estado == EstadoJornada.Presente || n.Estado == EstadoJornada.Tarde
                    || n.Estado == EstadoJornada.AusenteJustificado || n.Estado == EstadoJornada.AusenteInjustificado)
                && (area == null || n.Empleado.Area == area))
            .GroupBy(n => n.Fecha)
            .Select(g => new { Fecha = g.Key, Count = g.Count() })
            .ToListAsync(ct))
            .ToDictionary(x => x.Fecha, x => x.Count);

        return new AusentismoReporte(desde, hasta, detalle,
            Agrupar(detalle, evaluables, desde, hasta, f => (f, f, f.ToString("ddd dd/MM", EsAr))),
            Agrupar(detalle, evaluables, desde, hasta, BucketSemana),
            Agrupar(detalle, evaluables, desde, hasta, BucketMes));
    }

    private static (DateOnly Ini, DateOnly Fin, string Etiqueta) BucketSemana(DateOnly f)
    {
        var lunes = f.AddDays(-(((int)f.DayOfWeek + 6) % 7));
        return (lunes, lunes.AddDays(6), $"Sem {lunes:dd/MM} al {lunes.AddDays(6):dd/MM}");
    }

    private static (DateOnly Ini, DateOnly Fin, string Etiqueta) BucketMes(DateOnly f)
    {
        var primero = new DateOnly(f.Year, f.Month, 1);
        var nombre = EsAr.DateTimeFormat.GetMonthName(f.Month);
        return (primero, primero.AddMonths(1).AddDays(-1), $"{char.ToUpperInvariant(nombre[0])}{nombre[1..]} {f.Year}");
    }

    /// <summary>
    /// Agrega el detalle en buckets consecutivos que cubren TODO el rango (los buckets sin
    /// ausencias salen con 0, sin división por cero). Desde/Hasta se recortan al rango consultado.
    /// internal para testear.
    /// </summary>
    internal static IReadOnlyList<AusentismoAgregado> Agrupar(
        IReadOnlyList<AusentismoDetalle> detalle,
        IReadOnlyDictionary<DateOnly, int> evaluablesPorDia,
        DateOnly desde, DateOnly hasta,
        Func<DateOnly, (DateOnly Ini, DateOnly Fin, string Etiqueta)> bucket)
    {
        var porFecha = detalle.GroupBy(d => d.Fecha).ToDictionary(g => g.Key, g => g.ToList());
        var res = new List<AusentismoAgregado>();

        for (var f = desde; f <= hasta;)
        {
            var (ini, fin, etiqueta) = bucket(f);
            var iniRec = ini < desde ? desde : ini;
            var finRec = fin > hasta ? hasta : fin;

            int just = 0, injust = 0, evaluables = 0;
            for (var d = iniRec; d <= finRec; d = d.AddDays(1))
            {
                if (porFecha.TryGetValue(d, out var dias))
                {
                    just += dias.Count(x => x.Justificada);
                    injust += dias.Count(x => !x.Justificada);
                }
                evaluables += evaluablesPorDia.GetValueOrDefault(d);
            }
            res.Add(new AusentismoAgregado(etiqueta, iniRec, finRec, just, injust, evaluables));
            f = finRec.AddDays(1);
        }
        return res;
    }

    public async Task<byte[]> ExcelAsync(DateOnly desde, DateOnly hasta, string? area = null, CancellationToken ct = default)
    {
        var r = await ReporteAsync(desde, hasta, area, ct);

        using var wb = new XLWorkbook();

        // ── Hoja Resumen: tres secciones apiladas (mes / semana / día) ──
        var ws = wb.Worksheets.Add("Resumen");
        ws.Cell(1, 1).Value = $"Ausentismo {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}" + (area is null ? "" : $" — {area}");
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13);
        ws.SheetView.FreezeRows(1);

        int fila = 3;
        fila = Seccion(ws, fila, "POR MES", r.PorMes);
        fila = Seccion(ws, fila + 1, "POR SEMANA", r.PorSemana);
        Seccion(ws, fila + 1, "POR DÍA", r.PorDia);
        ws.Columns().AdjustToContents();

        // ── Hoja Detalle: la data cruda, una fila por ausencia ──
        var wd = wb.Worksheets.Add("Detalle");
        string[] cab = ["Fecha", "Legajo", "Apellido y nombre", "Área", "Estado", "Motivo"];
        for (int c = 0; c < cab.Length; c++) wd.Cell(1, c + 1).Value = cab[c];
        wd.Range(1, 1, 1, cab.Length).Style.Font.SetBold();

        int fd = 2;
        foreach (var d in r.Detalle)
        {
            wd.Cell(fd, 1).Value = d.Fecha.ToDateTime(TimeOnly.MinValue);
            wd.Cell(fd, 1).Style.NumberFormat.Format = "dd/mm/yyyy";
            wd.Cell(fd, 2).Value = d.Legajo ?? "—";
            wd.Cell(fd, 3).Value = d.ApellidoNombre;
            wd.Cell(fd, 4).Value = d.Area ?? "—";
            wd.Cell(fd, 5).Value = d.Justificada ? "Justificada" : "Injustificada";
            wd.Cell(fd, 6).Value = d.Motivo ?? "—";
            fd++;
        }
        wd.SheetView.FreezeRows(1);
        if (r.Detalle.Count > 0) wd.RangeUsed()!.SetAutoFilter();
        wd.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // Escribe una sección del resumen y devuelve la fila siguiente a la última usada.
    private static int Seccion(IXLWorksheet ws, int fila, string titulo, IReadOnlyList<AusentismoAgregado> datos)
    {
        ws.Cell(fila, 1).Value = titulo;
        ws.Cell(fila, 1).Style.Font.SetBold();
        fila++;

        string[] cab = ["Período", "Justificadas", "Injustificadas", "Total", "% Justif.", "% Injustif.", "Jornadas", "Tasa ausentismo"];
        for (int c = 0; c < cab.Length; c++) ws.Cell(fila, c + 1).Value = cab[c];
        ws.Range(fila, 1, fila, cab.Length).Style.Font.SetBold();
        fila++;

        foreach (var a in datos)
        {
            ws.Cell(fila, 1).Value = a.Etiqueta;
            Num(ws.Cell(fila, 2), a.Justificadas);
            Num(ws.Cell(fila, 3), a.Injustificadas);
            Num(ws.Cell(fila, 4), a.Total);
            Pct(ws.Cell(fila, 5), a.PctJustificadas, a.Total);
            Pct(ws.Cell(fila, 6), a.PctInjustificadas, a.Total);
            ws.Cell(fila, 7).Value = a.JornadasEvaluables;
            Pct(ws.Cell(fila, 8), a.TasaAusentismo, a.JornadasEvaluables);
            fila++;
        }
        return fila;
    }

    // Como en la planilla de presentismo: los ceros van vacíos para que se lean las excepciones.
    private static void Num(IXLCell cell, int v) { if (v > 0) cell.Value = v; }

    private static void Pct(IXLCell cell, double v, int baseCalculo)
    {
        if (baseCalculo == 0) return; // sin base no hay porcentaje (celda vacía, no 0%)
        cell.Value = v;
        cell.Style.NumberFormat.Format = "0.0%";
    }
}
