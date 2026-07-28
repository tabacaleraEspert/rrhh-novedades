using Microsoft.EntityFrameworkCore;
using RRHHNovedades.Web.Data;

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
