using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;

namespace RRHHNovedades.Web.Services;

/// <summary>Fila del reporte mensual de nocturnidad de un empleado.</summary>
public record NocturnidadEmpleado(
    int EmpleadoId,
    string ApellidoNombre,
    string? Area,
    int Noches,
    int HorasNocturnas);

/// <summary>Una noche puntual del desglose por empleado.</summary>
public record NocturnidadNoche(
    DateOnly Fecha,
    TimeOnly Entrada,
    TimeOnly Salida,
    int Minutos,
    int Horas);

public interface INocturnidadService
{
    /// <summary>
    /// Reporte del mes de liquidación: horas en banda nocturna (21:00–06:00) por empleado.
    /// El "mes" va del 26 del mes anterior al 25 del elegido, inclusive.
    /// </summary>
    Task<IReadOnlyList<NocturnidadEmpleado>> ReporteMensualAsync(int anio, int mes, CancellationToken ct = default);

    /// <summary>Desglose noche por noche de un empleado en el mes de liquidación (solo noches con minutos > 0).</summary>
    Task<IReadOnlyList<NocturnidadNoche>> DetalleMensualAsync(int empleadoId, int anio, int mes, CancellationToken ct = default);

    /// <summary>
    /// Planilla Excel (.xlsx) del período: hoja "Resumen" (acumulado por empleado) y hoja
    /// "Detalle" (todas las noches, una por fila). Respeta el filtro de área si se pasa.
    /// </summary>
    Task<byte[]> ExcelMensualAsync(int anio, int mes, string? area = null, CancellationToken ct = default);
}

/// <summary>
/// Nocturnidad: horas efectivamente trabajadas entre las 21:00 y las 06:00, según las fichadas
/// reales (entrada/salida). Aplica a cualquier empleado, no solo al turno noche (una salida 22:00
/// del turno tarde suma 1 hora). Redondeo POR NOCHE: fracción ≥ 45 min ⇒ hora completa hacia arriba.
/// </summary>
public class NocturnidadService(IDbContextFactory<AppDbContext> dbFactory) : INocturnidadService
{
    private static readonly TimeOnly InicioBanda = new(21, 0);
    private static readonly TimeOnly FinBanda = new(6, 0);

    /// <summary>
    /// Período de liquidación del mes: del 26 del mes ANTERIOR al 25 del mes elegido, inclusive
    /// (así se liquidan las novedades en Espert). Devuelve [desde, hasta) con hasta exclusivo.
    /// internal para testear (InternalsVisibleTo RRHHNovedades.Tests).
    /// </summary>
    internal static (DateOnly Desde, DateOnly Hasta) PeriodoLiquidacion(int anio, int mes)
    {
        var hasta = new DateOnly(anio, mes, 26);       // exclusivo ⇒ incluye hasta el 25
        return (hasta.AddMonths(-1), hasta);
    }

    public async Task<IReadOnlyList<NocturnidadEmpleado>> ReporteMensualAsync(int anio, int mes, CancellationToken ct = default)
    {
        var (desde, hasta) = PeriodoLiquidacion(anio, mes);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var novedades = await db.Novedades
            .Include(n => n.Empleado)
            .Where(n => n.Fecha >= desde && n.Fecha < hasta && n.HoraEntrada != null && n.HoraSalida != null)
            .ToListAsync(ct);

        return novedades
            .Select(n => (n.Empleado, Minutos: MinutosNocturnos(n.HoraEntrada!.Value, n.HoraSalida)))
            .Where(x => x.Minutos > 0)
            .GroupBy(x => x.Empleado.Id)
            .Select(g => new NocturnidadEmpleado(
                g.Key,
                g.First().Empleado.ApellidoNombre,
                g.First().Empleado.Area,
                Noches: g.Count(),
                HorasNocturnas: g.Sum(x => HorasRedondeadas(x.Minutos))))
            .OrderByDescending(r => r.HorasNocturnas)
            .ThenBy(r => r.ApellidoNombre)
            .ToList();
    }

    public async Task<IReadOnlyList<NocturnidadNoche>> DetalleMensualAsync(int empleadoId, int anio, int mes, CancellationToken ct = default)
    {
        var (desde, hasta) = PeriodoLiquidacion(anio, mes);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var novedades = await db.Novedades
            .Where(n => n.EmpleadoId == empleadoId && n.Fecha >= desde && n.Fecha < hasta
                     && n.HoraEntrada != null && n.HoraSalida != null)
            .OrderBy(n => n.Fecha)
            .ToListAsync(ct);

        return novedades
            .Select(n => new NocturnidadNoche(
                n.Fecha, n.HoraEntrada!.Value, n.HoraSalida!.Value,
                Minutos: MinutosNocturnos(n.HoraEntrada!.Value, n.HoraSalida),
                Horas: HorasRedondeadas(MinutosNocturnos(n.HoraEntrada!.Value, n.HoraSalida))))
            .Where(x => x.Minutos > 0)
            .ToList();
    }

    public async Task<byte[]> ExcelMensualAsync(int anio, int mes, string? area = null, CancellationToken ct = default)
    {
        var (desde, hasta) = PeriodoLiquidacion(anio, mes);
        var periodoTxt = $"{desde:dd/MM/yyyy} al {hasta.AddDays(-1):dd/MM/yyyy}";

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var noches = (await db.Novedades
                .Include(n => n.Empleado)
                .Where(n => n.Fecha >= desde && n.Fecha < hasta && n.HoraEntrada != null && n.HoraSalida != null)
                .ToListAsync(ct))
            .Select(n => (n.Empleado, n.Fecha, Entrada: n.HoraEntrada!.Value, Salida: n.HoraSalida!.Value,
                          Minutos: MinutosNocturnos(n.HoraEntrada!.Value, n.HoraSalida)))
            .Where(x => x.Minutos > 0 && (area is null || x.Empleado.Area == area))
            .OrderBy(x => x.Empleado.Apellido).ThenBy(x => x.Empleado.Nombre).ThenBy(x => x.Fecha)
            .ToList();

        using var wb = new XLWorkbook();

        var res = wb.Worksheets.Add("Resumen");
        res.Cell(1, 1).Value = $"Nocturnidad — período {periodoTxt}" + (area is null ? "" : $" — {area}");
        res.Range(1, 1, 1, 5).Merge().Style.Font.SetBold();
        res.Cell(2, 1).Value = "Horas entre 21:00 y 06:00 según fichadas reales. Redondeo por noche: ≥ 45 min ⇒ hora completa.";
        res.Range(2, 1, 2, 5).Merge().Style.Font.SetItalic();
        string[] cabRes = ["Legajo", "Empleado", "Área", "Noches", "Horas nocturnas"];
        for (int c = 0; c < cabRes.Length; c++) res.Cell(4, c + 1).Value = cabRes[c];
        res.Range(4, 1, 4, cabRes.Length).Style.Font.SetBold();
        int f = 5;
        foreach (var g in noches.GroupBy(x => x.Empleado.Id))
        {
            var emp = g.First().Empleado;
            res.Cell(f, 1).Value = emp.Legajo ?? "—";
            res.Cell(f, 2).Value = emp.ApellidoNombre;
            res.Cell(f, 3).Value = emp.Area ?? "—";
            res.Cell(f, 4).Value = g.Count();
            res.Cell(f, 5).Value = g.Sum(x => HorasRedondeadas(x.Minutos));
            f++;
        }
        res.Cell(f, 2).Value = "TOTAL";
        res.Cell(f, 4).FormulaA1 = f > 5 ? $"SUM(D5:D{f - 1})" : "0";
        res.Cell(f, 5).FormulaA1 = f > 5 ? $"SUM(E5:E{f - 1})" : "0";
        res.Range(f, 1, f, 5).Style.Font.SetBold();
        res.Columns().AdjustToContents();

        var det = wb.Worksheets.Add("Detalle");
        string[] cabDet = ["Legajo", "Empleado", "Área", "Fecha", "Entrada", "Salida", "Min. nocturnos", "Horas (redondeadas)"];
        for (int c = 0; c < cabDet.Length; c++) det.Cell(1, c + 1).Value = cabDet[c];
        det.Range(1, 1, 1, cabDet.Length).Style.Font.SetBold();
        f = 2;
        foreach (var x in noches)
        {
            det.Cell(f, 1).Value = x.Empleado.Legajo ?? "—";
            det.Cell(f, 2).Value = x.Empleado.ApellidoNombre;
            det.Cell(f, 3).Value = x.Empleado.Area ?? "—";
            det.Cell(f, 4).Value = x.Fecha.ToDateTime(TimeOnly.MinValue);
            det.Cell(f, 4).Style.DateFormat.Format = "dd/mm/yyyy";
            det.Cell(f, 5).Value = x.Entrada.ToString("HH:mm");
            det.Cell(f, 6).Value = x.Salida.ToString("HH:mm");
            det.Cell(f, 7).Value = x.Minutos;
            det.Cell(f, 8).Value = HorasRedondeadas(x.Minutos);
            f++;
        }
        det.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Minutos trabajados dentro de la banda 21:00→06:00 (del día siguiente). Si la salida es
    /// menor o igual a la entrada, la jornada cruza medianoche. Sin salida no se puede calcular ⇒ 0.
    /// internal para testear (InternalsVisibleTo RRHHNovedades.Tests).
    /// </summary>
    internal static int MinutosNocturnos(TimeOnly entrada, TimeOnly? salida)
    {
        if (salida is not { } s) return 0;

        // Todo en minutos desde las 00:00 del día de la entrada (la jornada puede cruzar medianoche).
        double e = entrada.ToTimeSpan().TotalMinutes;
        double f = s.ToTimeSpan().TotalMinutes;
        if (f <= e) f += 24 * 60;

        // La banda nocturna que puede tocar esa jornada: 21:00 del día de entrada → 06:00 del
        // siguiente, y también las 00:00→06:00 del mismo día (madrugada, ej. entrada 02:00).
        double total = 0;
        double[][] bandas =
        [
            [0, FinBanda.ToTimeSpan().TotalMinutes],                                  // 00:00–06:00 del día de entrada
            [InicioBanda.ToTimeSpan().TotalMinutes, (24 + 6) * 60],                   // 21:00 → 06:00 (+1 día)
            [(24 + 21) * 60, (48 + 6) * 60]                                           // banda siguiente (por si entra después de medianoche)
        ];
        foreach (var b in bandas)
            total += Math.Max(0, Math.Min(f, b[1]) - Math.Max(e, b[0]));

        return (int)total;
    }

    /// <summary>Redondeo por noche: horas completas, y la fracción suma 1 hora si es ≥ 45 min.</summary>
    internal static int HorasRedondeadas(int minutos) =>
        minutos / 60 + (minutos % 60 >= 45 ? 1 : 0);
}
