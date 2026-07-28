using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;

namespace RRHHNovedades.Web.Services;

/// <summary>Bucket de licencia de la planilla de presentismo (columnas E–K del formato de RRHH).</summary>
public enum TipoLicencia
{
    Ninguna,
    Enfermedad,     // "Lic. por enfermedad"
    SinGoce,        // (sin tipo en Humand hoy; se mapea por nombre si aparece)
    ConGoce,        // (ídem)
    Especial,       // enfermedad familiar, día gremial, mudanza, etc.
    Accidente,      // "Lic. por accidente de trabajo"
    Vacaciones
}

/// <summary>Fila de la planilla de presentismo de un empleado en el período.</summary>
public record PresentismoEmpleado(
    int EmpleadoId,
    string? Legajo,
    string ApellidoNombre,
    string? Area,
    int Trabajados,
    int Feriados,
    int Injustificadas,
    int Enfermedad,
    int SinGoce,
    int ConGoce,
    int Especial,
    int Accidente,
    int Vacaciones,
    int HorasNocturnas,
    string Observacion,
    string Ppp,
    int TotalInasistencia,
    int TotalLiquidados);

public interface IPresentismoService
{
    /// <summary>Planilla del período de liquidación (26 del mes anterior al 25, inclusive).</summary>
    Task<IReadOnlyList<PresentismoEmpleado>> ReporteMensualAsync(int anio, int mes, CancellationToken ct = default);

    /// <summary>Excel (.xlsx) con el formato de la planilla de RRHH. Respeta el filtro de área.</summary>
    Task<byte[]> ExcelMensualAsync(int anio, int mes, string? area = null, CancellationToken ct = default);
}

/// <summary>
/// Planilla de presentismo del período de liquidación (26 al 25).
/// Reglas (definidas con RRHH, 28-jul-2026):
///   La base del mes es SIEMPRE 30 días, sin importar las fichadas.
///   CANT. DIAS TRABAJADOS = 30 − feriados − todas las ausencias (las columnas suman 30).
///   Las ausencias salen de Humand: feriado > licencia por tipo de solicitud > injustificada
///   (un día cuenta una sola vez; francos y fichadas no alteran la base).
///   TOTAL INASISTENCIA = injustificadas + todas las licencias.
///   TOTAL DÍAS LIQUIDADOS = 30 − injustificadas − sin goce (justificadas y vacaciones se pagan).
///   PPP = "DESCONTAR" si hay al menos 1 injustificada; si no "Si".
/// </summary>
public class PresentismoService(
    IDbContextFactory<AppDbContext> dbFactory,
    INocturnidadService nocturnidad) : IPresentismoService
{
    public async Task<IReadOnlyList<PresentismoEmpleado>> ReporteMensualAsync(int anio, int mes, CancellationToken ct = default)
    {
        var (desde, hasta) = NocturnidadService.PeriodoLiquidacion(anio, mes);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var novedades = await db.Novedades
            .Include(n => n.Empleado)
            .Where(n => n.Fecha >= desde && n.Fecha < hasta)
            .OrderBy(n => n.Fecha)
            .ToListAsync(ct);

        var nocturnas = (await nocturnidad.ReporteMensualAsync(anio, mes, ct))
            .ToDictionary(x => x.EmpleadoId, x => x.HorasNocturnas);

        return novedades
            .GroupBy(n => n.Empleado.Id)
            .Select(g => ArmarFila(g.First().Empleado, [.. g], nocturnas.GetValueOrDefault(g.Key)))
            .Where(f => f.TotalLiquidados > 0 || f.TotalInasistencia > 0)
            .OrderBy(f => f.ApellidoNombre)
            .ToList();
    }

    /// <summary>Base mensual fija de la liquidación: siempre 30 días, sin importar fichadas.</summary>
    internal const int BaseDias = 30;

    private static PresentismoEmpleado ArmarFila(Empleado emp, List<NovedadDiaria> dias, int horasNocturnas)
    {
        int feriados = 0, injustificadas = 0;
        var lic = new Dictionary<TipoLicencia, int>();
        var obs = new List<string>();

        foreach (var d in dias)
        {
            // Un día cuenta una sola vez: feriado > licencia > injustificada. Las fichadas y los
            // francos no alteran la base (los días trabajados salen por resta de la base 30).
            if (d.EsFeriado) { feriados++; continue; }
            if (d.Estado == EstadoJornada.AusenteJustificado)
            {
                var tipo = ClasificarMotivo(d.MotivoNovedad);
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

        int enfermedad = lic.GetValueOrDefault(TipoLicencia.Enfermedad);
        int sinGoce = lic.GetValueOrDefault(TipoLicencia.SinGoce);
        int conGoce = lic.GetValueOrDefault(TipoLicencia.ConGoce);
        int especial = lic.GetValueOrDefault(TipoLicencia.Especial) + lic.GetValueOrDefault(TipoLicencia.Ninguna);
        int accidente = lic.GetValueOrDefault(TipoLicencia.Accidente);
        int vacaciones = lic.GetValueOrDefault(TipoLicencia.Vacaciones);

        int totalInasistencia = injustificadas + enfermedad + sinGoce + conGoce + especial + accidente + vacaciones;
        // Base 30 fija: trabajados por resta (las columnas suman 30) y liquidados solo pierde
        // las no pagas (injustificadas y sin goce).
        int trabajados = Math.Max(0, BaseDias - feriados - totalInasistencia);
        int totalLiquidados = Math.Max(0, BaseDias - injustificadas - sinGoce);

        return new PresentismoEmpleado(
            emp.Id, emp.Legajo, emp.ApellidoNombre, emp.Area,
            trabajados, feriados, injustificadas,
            enfermedad, sinGoce, conGoce, especial, accidente, vacaciones,
            horasNocturnas,
            string.Join("; ", obs),
            injustificadas > 0 ? "DESCONTAR" : "Si",
            totalInasistencia, totalLiquidados);
    }

    /// <summary>
    /// Mapea el nombre del tipo de solicitud de Humand al bucket de la planilla.
    /// internal para testear (InternalsVisibleTo RRHHNovedades.Tests).
    /// </summary>
    internal static TipoLicencia ClasificarMotivo(string? motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo)) return TipoLicencia.Ninguna;
        var m = motivo.ToLowerInvariant();
        if (m.Contains("vacacion")) return TipoLicencia.Vacaciones;
        if (m.Contains("accidente")) return TipoLicencia.Accidente;
        if (m.Contains("sin goce")) return TipoLicencia.SinGoce;
        if (m.Contains("con goce") || m.Contains("c/goce")) return TipoLicencia.ConGoce;
        // Enfermedad FAMILIAR va a Especial; la propia a Enfermedad (evaluar "familiar" primero).
        if (m.Contains("familiar") || m.Contains("gremial") || m.Contains("mudanza") || m.Contains("estudio")
            || m.Contains("matrimonio") || m.Contains("nacimiento") || m.Contains("fallecimiento") || m.Contains("duelo"))
            return TipoLicencia.Especial;
        if (m.Contains("enfermedad")) return TipoLicencia.Enfermedad;
        return TipoLicencia.Especial;
    }

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
        var filas = (await ReporteMensualAsync(anio, mes, ct))
            .Where(f => area is null || f.Area == area)
            .ToList();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add($"{mes:00}-{anio}");

        string[] cab = ["LEGAJOS", "NOMBRE Y APELLIDO", "CANT. DIAS TRABAJADOS", "FERIADOS",
            "INASISTENCIA INJUSTIFICADA", "LIC. ENFERMEDAD", "LIC SIN GOCE", "LIC. C/GOCE",
            "LIC. ESPECIAL", "LIC. ACCIDENTE PROF", "VACACIONES", "HS NOCTURNAS",
            "OBSERVACION", "PPP", "TOTAL INASISTENCIA", "TOTAL DIAS LIQUIDADOS"];
        for (int c = 0; c < cab.Length; c++) ws.Cell(1, c + 1).Value = cab[c];
        ws.Range(1, 1, 1, cab.Length).Style.Font.SetBold();

        int f = 2;
        foreach (var x in filas)
        {
            ws.Cell(f, 1).Value = x.Legajo ?? "—";
            ws.Cell(f, 2).Value = x.ApellidoNombre;
            ws.Cell(f, 3).Value = x.Trabajados;
            Num(ws.Cell(f, 4), x.Feriados);
            Num(ws.Cell(f, 5), x.Injustificadas);
            Num(ws.Cell(f, 6), x.Enfermedad);
            Num(ws.Cell(f, 7), x.SinGoce);
            Num(ws.Cell(f, 8), x.ConGoce);
            Num(ws.Cell(f, 9), x.Especial);
            Num(ws.Cell(f, 10), x.Accidente);
            Num(ws.Cell(f, 11), x.Vacaciones);
            if (x.HorasNocturnas > 0) ws.Cell(f, 12).Value = x.HorasNocturnas; else ws.Cell(f, 12).Value = "-";
            ws.Cell(f, 13).Value = x.Observacion;
            ws.Cell(f, 14).Value = x.Ppp;
            Num(ws.Cell(f, 15), x.TotalInasistencia);
            ws.Cell(f, 16).Value = x.TotalLiquidados;
            f++;
        }
        ws.Cell(f + 1, 2).Value = $"Período: {desde:dd/MM/yyyy} al {hasta.AddDays(-1):dd/MM/yyyy}" + (area is null ? "" : $" — {area}");
        ws.Cell(f + 1, 2).Style.Font.SetItalic();
        ws.Columns().AdjustToContents();
        ws.Column(13).Width = Math.Min(ws.Column(13).Width, 60);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // Como en la planilla original: los ceros van vacíos para que se lean las excepciones.
    private static void Num(IXLCell cell, int v) { if (v > 0) cell.Value = v; }
}
