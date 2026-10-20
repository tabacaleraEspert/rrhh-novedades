using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;

namespace RRHHNovedades.Web.Services;

/// <summary>Fila del reporte mensual de horas extra y refrigerio de un empleado.</summary>
public record HorasExtrasEmpleado(
    int EmpleadoId,
    string? Legajo,
    string ApellidoNombre,
    string? Area,
    int DiasConExtras,
    double Extras50,
    double Extras100,
    double Refrigerio,
    int DiasConRefrigerio)
{
    public double TotalExtras => Math.Round(Extras50 + Extras100, 2);
}

/// <summary>Un día del desglose por empleado. Entrada/Salida null = sin fichada ese día.</summary>
public record HorasExtrasDia(
    DateOnly Fecha,
    TimeOnly? Entrada,
    TimeOnly? Salida,
    double Extras50,
    double Extras100,
    double Refrigerio)
{
    public bool TieneHoras => Extras50 > 0 || Extras100 > 0 || Refrigerio > 0;
}

public interface IHorasExtrasService
{
    /// <summary>
    /// Reporte del mes de liquidación (26 del mes anterior al 25 del elegido, inclusive): horas
    /// extra al 50 %, al 100 % y refrigerio por empleado, tal como las calcula Humand.
    /// </summary>
    Task<IReadOnlyList<HorasExtrasEmpleado>> ReporteMensualAsync(int anio, int mes, CancellationToken ct = default);

    /// <summary>
    /// Desglose día por día de un empleado en el mes de liquidación. Por defecto solo días con
    /// horas; con <paramref name="incluirDiasEnCero"/> devuelve TODOS los días del período.
    /// </summary>
    Task<IReadOnlyList<HorasExtrasDia>> DetalleMensualAsync(int empleadoId, int anio, int mes, bool incluirDiasEnCero = false, CancellationToken ct = default);

    /// <summary>Planilla Excel del período: hoja "Resumen" y hoja "Detalle". Respeta el filtro de área.</summary>
    Task<byte[]> ExcelMensualAsync(int anio, int mes, string? area = null, CancellationToken ct = default);
}

/// <summary>
/// Horas extra y refrigerio: NO se calculan acá. Las calcula Humand con sus políticas de marcaje
/// sobre las fichadas del kiosco y las entrega por día en `categorizedHours` del day-summary
/// ("Horas extras al 50", "Horas extras al 100", "Refrigerio"); la ingesta las guarda en
/// <see cref="NovedadDiaria"/>. Acá solo se acumulan por período de liquidación (26 → 25).
/// </summary>
public class HorasExtrasService(IDbContextFactory<AppDbContext> dbFactory) : IHorasExtrasService
{
    public async Task<IReadOnlyList<HorasExtrasEmpleado>> ReporteMensualAsync(int anio, int mes, CancellationToken ct = default)
    {
        var (desde, hasta) = NocturnidadService.PeriodoLiquidacion(anio, mes);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var dias = await db.Novedades
            .Include(n => n.Empleado)
            .Where(n => n.Fecha >= desde && n.Fecha < hasta
                     && (n.HorasExtras50 > 0 || n.HorasExtras100 > 0 || n.HorasRefrigerio > 0))
            .ToListAsync(ct);

        return dias
            .GroupBy(n => n.Empleado.Id)
            .Select(g => Fila(g.First().Empleado, g))
            .OrderByDescending(r => r.TotalExtras)
            .ThenByDescending(r => r.Refrigerio)
            .ThenBy(r => r.ApellidoNombre)
            .ToList();
    }

    public async Task<IReadOnlyList<HorasExtrasDia>> DetalleMensualAsync(int empleadoId, int anio, int mes, bool incluirDiasEnCero = false, CancellationToken ct = default)
    {
        var (desde, hasta) = NocturnidadService.PeriodoLiquidacion(anio, mes);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var novedades = await db.Novedades
            .Where(n => n.EmpleadoId == empleadoId && n.Fecha >= desde && n.Fecha < hasta)
            .OrderBy(n => n.Fecha)
            .ToListAsync(ct);

        var dias = novedades.Select(Dia).ToList();
        if (!incluirDiasEnCero)
            return dias.Where(d => d.TieneHoras).ToList();

        // Período completo, día por día: los días sin horas salen en 0 (con la fichada si la
        // hubo, o sin nada si ni vino / no está sincronizado).
        var porFecha = dias.ToDictionary(d => d.Fecha);
        var res = new List<HorasExtrasDia>();
        for (var f = desde; f < hasta; f = f.AddDays(1))
            res.Add(porFecha.TryGetValue(f, out var d) ? d : new HorasExtrasDia(f, null, null, 0, 0, 0));
        return res;
    }

    public async Task<byte[]> ExcelMensualAsync(int anio, int mes, string? area = null, CancellationToken ct = default)
    {
        var (desde, hasta) = NocturnidadService.PeriodoLiquidacion(anio, mes);
        var periodoTxt = $"{desde:dd/MM/yyyy} al {hasta.AddDays(-1):dd/MM/yyyy}";

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var dias = (await db.Novedades
                .Include(n => n.Empleado)
                .Where(n => n.Fecha >= desde && n.Fecha < hasta
                         && (n.HorasExtras50 > 0 || n.HorasExtras100 > 0 || n.HorasRefrigerio > 0))
                .ToListAsync(ct))
            .Where(n => area is null || n.Empleado.Area == area)
            .OrderBy(n => n.Empleado.Apellido).ThenBy(n => n.Empleado.Nombre).ThenBy(n => n.Fecha)
            .ToList();

        using var wb = new XLWorkbook();

        var res = wb.Worksheets.Add("Resumen");
        res.Cell(1, 1).Value = $"Horas extras y refrigerio — período {periodoTxt}" + (area is null ? "" : $" — {area}");
        res.Range(1, 1, 1, 9).Merge().Style.Font.SetBold();
        res.Cell(2, 1).Value = "Horas según las políticas de marcaje de Humand (fichadas reales). Refrigerio: horas de la categoría \"Refrigerio\".";
        res.Range(2, 1, 2, 9).Merge().Style.Font.SetItalic();
        string[] cabRes = ["Legajo", "Empleado", "Área", "Días c/ extras", "HE 50%", "HE 100%", "Total HE", "Refrigerio (hs)", "Días c/ refrigerio"];
        for (int c = 0; c < cabRes.Length; c++) res.Cell(4, c + 1).Value = cabRes[c];
        res.Range(4, 1, 4, cabRes.Length).Style.Font.SetBold();
        int f = 5;
        foreach (var g in dias.GroupBy(x => x.Empleado.Id))
        {
            var fila = Fila(g.First().Empleado, g);
            res.Cell(f, 1).Value = fila.Legajo ?? "—";
            res.Cell(f, 2).Value = fila.ApellidoNombre;
            res.Cell(f, 3).Value = fila.Area ?? "—";
            res.Cell(f, 4).Value = fila.DiasConExtras;
            res.Cell(f, 5).Value = fila.Extras50;
            res.Cell(f, 6).Value = fila.Extras100;
            res.Cell(f, 7).Value = fila.TotalExtras;
            res.Cell(f, 8).Value = fila.Refrigerio;
            res.Cell(f, 9).Value = fila.DiasConRefrigerio;
            f++;
        }
        res.Cell(f, 2).Value = "TOTAL";
        foreach (var col in new[] { 4, 5, 6, 7, 8, 9 })
        {
            var letra = (char)('A' + col - 1);
            res.Cell(f, col).FormulaA1 = f > 5 ? $"SUM({letra}5:{letra}{f - 1})" : "0";
        }
        res.Range(f, 1, f, 9).Style.Font.SetBold();
        res.Range(5, 5, f, 8).Style.NumberFormat.Format = "0.00";
        res.Columns().AdjustToContents();

        var det = wb.Worksheets.Add("Detalle");
        string[] cabDet = ["Legajo", "Empleado", "Área", "Fecha", "Entrada", "Salida", "HE 50%", "HE 100%", "Refrigerio (hs)"];
        for (int c = 0; c < cabDet.Length; c++) det.Cell(1, c + 1).Value = cabDet[c];
        det.Range(1, 1, 1, cabDet.Length).Style.Font.SetBold();
        f = 2;
        foreach (var x in dias)
        {
            det.Cell(f, 1).Value = x.Empleado.Legajo ?? "—";
            det.Cell(f, 2).Value = x.Empleado.ApellidoNombre;
            det.Cell(f, 3).Value = x.Empleado.Area ?? "—";
            det.Cell(f, 4).Value = x.Fecha.ToDateTime(TimeOnly.MinValue);
            det.Cell(f, 4).Style.DateFormat.Format = "dd/mm/yyyy";
            det.Cell(f, 5).Value = x.HoraEntrada?.ToString("HH:mm") ?? "—";
            det.Cell(f, 6).Value = x.HoraSalida?.ToString("HH:mm") ?? "—";
            det.Cell(f, 7).Value = R(x.HorasExtras50);
            det.Cell(f, 8).Value = R(x.HorasExtras100);
            det.Cell(f, 9).Value = R(x.HorasRefrigerio);
            f++;
        }
        if (f > 2) det.Range(2, 7, f - 1, 9).Style.NumberFormat.Format = "0.00";
        det.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static HorasExtrasEmpleado Fila(Empleado emp, IEnumerable<NovedadDiaria> dias)
    {
        var lista = dias.ToList();
        return new HorasExtrasEmpleado(
            emp.Id, emp.Legajo, emp.ApellidoNombre, emp.Area,
            DiasConExtras: lista.Count(d => d.HorasExtras50 > 0 || d.HorasExtras100 > 0),
            Extras50: R(lista.Sum(d => d.HorasExtras50)),
            Extras100: R(lista.Sum(d => d.HorasExtras100)),
            Refrigerio: R(lista.Sum(d => d.HorasRefrigerio)),
            DiasConRefrigerio: lista.Count(d => d.HorasRefrigerio > 0));
    }

    private static HorasExtrasDia Dia(NovedadDiaria n) =>
        new(n.Fecha, n.HoraEntrada, n.HoraSalida, R(n.HorasExtras50), R(n.HorasExtras100), R(n.HorasRefrigerio));

    // Humand entrega las horas con decimales (ej. 3,89). Se muestran a 2 decimales, sin redondeo propio.
    private static double R(double h) => Math.Round(h, 2);
}
