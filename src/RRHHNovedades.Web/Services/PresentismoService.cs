using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;

namespace RRHHNovedades.Web.Services;

/// <summary>Fila de la planilla de presentismo de un empleado en el período.</summary>
public record PresentismoEmpleado(
    int EmpleadoId,
    string? Legajo,
    string ApellidoNombre,
    string? Area,
    int Trabajados,
    int Feriados,
    int Injustificadas,
    IReadOnlyDictionary<string, int> Licencias, // días por tipo de licencia de Humand (nombre normalizado)
    int HorasNocturnas,
    string Observacion,
    string Ppp,
    int TotalInasistencia,
    int TotalLiquidados);

/// <summary>Planilla del período: columnas de licencia dinámicas (todos los tipos cargados en Humand).</summary>
public record PresentismoReporte(
    IReadOnlyList<string> TiposLicencia,
    IReadOnlyList<PresentismoEmpleado> Filas);

public interface IPresentismoService
{
    /// <summary>Planilla del período de liquidación (26 del mes anterior al 25, inclusive).</summary>
    Task<PresentismoReporte> ReporteMensualAsync(int anio, int mes, CancellationToken ct = default);

    /// <summary>Excel (.xlsx) con el formato de la planilla de RRHH. Respeta el filtro de área.</summary>
    Task<byte[]> ExcelMensualAsync(int anio, int mes, string? area = null, CancellationToken ct = default);
}

/// <summary>
/// Planilla de presentismo del período de liquidación (26 al 25).
/// Reglas (definidas con RRHH, 28-jul-2026):
///   La base del mes es SIEMPRE 30 días, sin importar las fichadas.
///   CANT. DIAS TRABAJADOS = 30 − feriados − todas las ausencias (las columnas suman 30).
///   Cada tipo de licencia de Humand tiene su PROPIA columna (dinámicas: los tipos salen de los
///   datos sincronizados; un tipo nuevo en Humand aparece solo).
///   TOTAL INASISTENCIA = injustificadas + todas las licencias.
///   TOTAL DÍAS LIQUIDADOS = 30 − injustificadas − licencias "sin goce" (el resto se paga).
///   PPP = "DESCONTAR" si hay al menos 1 injustificada; si no "Si".
/// </summary>
public class PresentismoService(
    IDbContextFactory<AppDbContext> dbFactory,
    INocturnidadService nocturnidad) : IPresentismoService
{
    /// <summary>Base mensual fija de la liquidación: siempre 30 días, sin importar fichadas.</summary>
    internal const int BaseDias = 30;

    public async Task<PresentismoReporte> ReporteMensualAsync(int anio, int mes, CancellationToken ct = default)
    {
        var (desde, hasta) = NocturnidadService.PeriodoLiquidacion(anio, mes);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var novedades = await db.Novedades
            .Include(n => n.Empleado)
            .Where(n => n.Fecha >= desde && n.Fecha < hasta)
            .OrderBy(n => n.Fecha)
            .ToListAsync(ct);

        // Catálogo de columnas: todos los tipos vistos en la base (no solo el período), así la
        // planilla es estable mes a mes y refleja lo cargado en Humand.
        var tipos = (await db.Novedades.AsNoTracking()
                .Where(n => n.MotivoNovedad != null)
                .Select(n => n.MotivoNovedad!)
                .Distinct()
                .ToListAsync(ct))
            .SelectMany(SepararTipos)
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        var nocturnas = (await nocturnidad.ReporteMensualAsync(anio, mes, ct))
            .ToDictionary(x => x.EmpleadoId, x => x.HorasNocturnas);

        var filas = novedades
            .GroupBy(n => n.Empleado.Id)
            .Select(g => ArmarFila(g.First().Empleado, [.. g], nocturnas.GetValueOrDefault(g.Key)))
            .OrderBy(f => f.ApellidoNombre)
            .ToList();

        return new PresentismoReporte(tipos, filas);
    }

    private static PresentismoEmpleado ArmarFila(Empleado emp, List<NovedadDiaria> dias, int horasNocturnas)
    {
        int feriados = 0, injustificadas = 0;
        var lic = new Dictionary<string, int>();
        var obs = new List<string>();

        foreach (var d in dias)
        {
            // Un día cuenta una sola vez: feriado > licencia > injustificada. Las fichadas y los
            // francos no alteran la base (los días trabajados salen por resta de la base 30).
            if (d.EsFeriado) { feriados++; continue; }
            if (d.Estado == EstadoJornada.AusenteJustificado)
            {
                var tipo = SepararTipos(d.MotivoNovedad).FirstOrDefault() ?? "Licencia";
                lic[tipo] = lic.GetValueOrDefault(tipo) + 1;
                continue;
            }
            if (d.Estado == EstadoJornada.AusenteInjustificado) injustificadas++;
        }

        // Observación: rangos consecutivos por motivo (ej. "Vacaciones 20/07 al 26/07") + injustificadas.
        foreach (var grupo in Rangos(dias, d => d.Estado == EstadoJornada.AusenteJustificado && !d.EsFeriado, d => d.MotivoNovedad ?? "Licencia"))
            obs.Add(grupo);
        foreach (var grupo in Rangos(dias, d => d.Estado == EstadoJornada.AusenteInjustificado && !d.EsFeriado, _ => "Injustificada"))
            obs.Add(grupo);

        int totalLicencias = lic.Values.Sum();
        int sinGoce = lic.Where(kv => EsSinGoce(kv.Key)).Sum(kv => kv.Value);
        int totalInasistencia = injustificadas + totalLicencias;
        // Base 30 fija: trabajados por resta (las columnas suman 30) y liquidados solo pierde
        // las no pagas (injustificadas y sin goce).
        int trabajados = Math.Max(0, BaseDias - feriados - totalInasistencia);
        int totalLiquidados = Math.Max(0, BaseDias - injustificadas - sinGoce);

        return new PresentismoEmpleado(
            emp.Id, emp.Legajo, emp.ApellidoNombre, emp.Area,
            trabajados, feriados, injustificadas, lic,
            horasNocturnas,
            string.Join("; ", obs),
            injustificadas > 0 ? "DESCONTAR" : "Si",
            totalInasistencia, totalLiquidados);
    }

    /// <summary>
    /// El motivo puede venir con varios tipos unidos por coma ("Vacaciones, Lic. por enfermedad").
    /// Devuelve los nombres normalizados (trim). internal para testear.
    /// </summary>
    internal static IEnumerable<string> SepararTipos(string? motivo) =>
        (motivo ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0);

    /// <summary>Licencia no paga: no suma a los días liquidados.</summary>
    internal static bool EsSinGoce(string tipo) =>
        tipo.Contains("sin goce", StringComparison.OrdinalIgnoreCase);

    // Agrupa días consecutivos que cumplen el filtro y comparten etiqueta → "Etiqueta dd/MM al dd/MM".
    private static IEnumerable<string> Rangos(List<NovedadDiaria> dias, Func<NovedadDiaria, bool> filtro, Func<NovedadDiaria, string> etiqueta)
    {
        var sel = dias.Where(filtro).OrderBy(d => d.Fecha).ToList();
        int i = 0;
        while (i < sel.Count)
        {
            int j = i;
            while (j + 1 < sel.Count
                   && etiqueta(sel[j + 1]) == etiqueta(sel[i])
                   && sel[j + 1].Fecha.DayNumber - sel[j].Fecha.DayNumber <= 2) // tolera fin de semana en el medio
                j++;
            yield return sel[i].Fecha == sel[j].Fecha
                ? $"{etiqueta(sel[i]).Trim()} {sel[i].Fecha:dd/MM}"
                : $"{etiqueta(sel[i]).Trim()} {sel[i].Fecha:dd/MM} al {sel[j].Fecha:dd/MM}";
            i = j + 1;
        }
    }

    public async Task<byte[]> ExcelMensualAsync(int anio, int mes, string? area = null, CancellationToken ct = default)
    {
        var (desde, hasta) = NocturnidadService.PeriodoLiquidacion(anio, mes);
        var reporte = await ReporteMensualAsync(anio, mes, ct);
        var tipos = reporte.TiposLicencia;
        var filas = reporte.Filas.Where(f => area is null || f.Area == area).ToList();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add($"{mes:00}-{anio}");

        var cab = new List<string> { "LEGAJOS", "NOMBRE Y APELLIDO", "CANT. DIAS TRABAJADOS", "FERIADOS", "INASISTENCIA INJUSTIFICADA" };
        cab.AddRange(tipos.Select(t => t.ToUpperInvariant()));
        cab.AddRange(["HS NOCTURNAS", "OBSERVACION", "PPP", "TOTAL INASISTENCIA", "TOTAL DIAS LIQUIDADOS"]);
        for (int c = 0; c < cab.Count; c++) ws.Cell(1, c + 1).Value = cab[c];
        ws.Range(1, 1, 1, cab.Count).Style.Font.SetBold();

        int f = 2;
        foreach (var x in filas)
        {
            int col = 1;
            ws.Cell(f, col++).Value = x.Legajo ?? "—";
            ws.Cell(f, col++).Value = x.ApellidoNombre;
            ws.Cell(f, col++).Value = x.Trabajados;
            Num(ws.Cell(f, col++), x.Feriados);
            Num(ws.Cell(f, col++), x.Injustificadas);
            foreach (var t in tipos)
                Num(ws.Cell(f, col++), x.Licencias.GetValueOrDefault(t));
            if (x.HorasNocturnas > 0) ws.Cell(f, col).Value = x.HorasNocturnas; else ws.Cell(f, col).Value = "-";
            col++;
            ws.Cell(f, col++).Value = x.Observacion;
            ws.Cell(f, col++).Value = x.Ppp;
            Num(ws.Cell(f, col++), x.TotalInasistencia);
            ws.Cell(f, col).Value = x.TotalLiquidados;
            f++;
        }
        ws.Cell(f + 1, 2).Value = $"Período: {desde:dd/MM/yyyy} al {hasta.AddDays(-1):dd/MM/yyyy}" + (area is null ? "" : $" — {area}");
        ws.Cell(f + 1, 2).Style.Font.SetItalic();
        ws.Columns().AdjustToContents();
        int obsCol = 5 + tipos.Count + 2;
        ws.Column(obsCol).Width = Math.Min(ws.Column(obsCol).Width, 60);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // Como en la planilla original: los ceros van vacíos para que se lean las excepciones.
    private static void Num(IXLCell cell, int v) { if (v > 0) cell.Value = v; }
}
