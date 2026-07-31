using Microsoft.EntityFrameworkCore;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;

namespace RRHHNovedades.Web.Services;

/// <summary>Un día del historial de un empleado (para "¿qué pasó con Pérez el 15/07?").</summary>
public record HistorialDia(
    DateOnly Fecha,
    EstadoJornada Estado,
    string? Motivo,
    TimeOnly? HoraEntrada,
    TimeOnly? HoraSalida,
    int MinutosTardanza,
    bool EsFeriado,
    bool EsManual);

public record HistorialEmpleado(
    int EmpleadoId,
    string ApellidoNombre,
    string? Legajo,
    string? Area,
    IReadOnlyList<HistorialDia> Dias);

/// <summary>Tardanzas de una persona en el rango (no existe reporte de tardanzas fuera del asistente).</summary>
public record TardanzaPersona(
    int EmpleadoId,
    string? Legajo,
    string ApellidoNombre,
    string? Area,
    int Dias,
    int MinutosTotales,
    DateOnly UltimaFecha);

/// <summary>Ausencias agregadas por persona en el rango (el reporte de ausentismo solo agrupa por período).</summary>
public record AusenciasPersona(
    int EmpleadoId,
    string? Legajo,
    string ApellidoNombre,
    string? Area,
    int Justificadas,
    int Injustificadas,
    IReadOnlyList<string> Motivos)
{
    public int Total => Justificadas + Injustificadas;
}

/// <summary>
/// Qué períodos tienen datos cargados. Crítico para el asistente: el histórico se llena por
/// backfill manual, así que un rango sin filas puede significar "no hubo ausencias" o
/// "nunca se sincronizó" — sin esto el asistente mentiría por omisión.
/// </summary>
public record CoberturaDatos(
    DateOnly? PrimeraFecha,
    DateOnly? UltimaFecha,
    int DiasConDatos,
    IReadOnlyList<(DateOnly Desde, DateOnly Hasta)> Huecos);

/// <summary>Conteo del día por estado + nombres de las excepciones (para "¿qué pasó ayer?").</summary>
public record ResumenDia(
    DateOnly Fecha,
    Turno? TurnoFiltrado,
    IReadOnlyDictionary<string, int> PorEstado,
    IReadOnlyList<string> Tardes,
    IReadOnlyList<string> AusentesInjustificados,
    IReadOnlyList<string> AusentesJustificados,
    bool EsFeriado);

/// <summary>Día justificado con fecha futura ya sincronizado: licencia "programada" en Humand o manual.</summary>
public record LicenciaProgramada(
    int EmpleadoId,
    string ApellidoNombre,
    string? Area,
    DateOnly Desde,
    DateOnly Hasta,
    string Motivo);

public interface IConsultaAsistenteService
{
    /// <summary>Historial día por día de un empleado en [desde, hasta]. Null si el empleado no existe.</summary>
    Task<HistorialEmpleado?> HistorialAsync(int empleadoId, DateOnly desde, DateOnly hasta, CancellationToken ct = default);

    /// <summary>Resumen de un día: conteo por estado y nombres de tardes/ausentes, filtrable por turno.</summary>
    Task<ResumenDia> ResumenDiaAsync(DateOnly fecha, Turno? turno = null, CancellationToken ct = default);

    /// <summary>Licencias con días futuros ya sincronizados, como rangos consecutivos por persona y motivo.</summary>
    Task<IReadOnlyList<LicenciaProgramada>> LicenciasProgramadasAsync(DateOnly hoy, int? empleadoId = null, CancellationToken ct = default);

    /// <summary>Tardanzas por persona en el rango, ordenadas por minutos totales descendente.</summary>
    Task<IReadOnlyList<TardanzaPersona>> TardanzasAsync(DateOnly desde, DateOnly hasta, string? area = null, int? empleadoId = null, CancellationToken ct = default);

    /// <summary>Ausencias por persona en el rango (feriados excluidos), ordenadas por total descendente.</summary>
    Task<IReadOnlyList<AusenciasPersona>> AusenciasPorPersonaAsync(DateOnly desde, DateOnly hasta, string? area = null, CancellationToken ct = default);

    /// <summary>Cobertura de datos: min/max fecha con novedades, cantidad de días y huecos internos.</summary>
    Task<CoberturaDatos> CoberturaAsync(CancellationToken ct = default);
}

/// <summary>
/// Consultas de solo lectura que alimentan las herramientas del asistente IA. Viven en un
/// servicio propio (no dentro de las tools) para ser testeables con EF InMemory y reutilizables.
/// </summary>
public class ConsultaAsistenteService(IDbContextFactory<AppDbContext> dbFactory) : IConsultaAsistenteService
{
    public async Task<HistorialEmpleado?> HistorialAsync(int empleadoId, DateOnly desde, DateOnly hasta, CancellationToken ct = default)
    {
        if (hasta < desde) throw new ArgumentException("hasta < desde", nameof(hasta));

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var emp = await db.Empleados.AsNoTracking().FirstOrDefaultAsync(e => e.Id == empleadoId, ct);
        if (emp is null) return null;

        var dias = await db.Novedades.AsNoTracking()
            .Where(n => n.EmpleadoId == empleadoId && n.Fecha >= desde && n.Fecha <= hasta)
            .OrderBy(n => n.Fecha)
            .Select(n => new HistorialDia(
                n.Fecha, n.Estado, n.MotivoNovedad, n.HoraEntrada, n.HoraSalida,
                n.MinutosTardanza, n.EsFeriado, n.EsManual))
            .ToListAsync(ct);

        return new HistorialEmpleado(emp.Id, emp.ApellidoNombre, emp.Legajo, emp.Area, dias);
    }

    public async Task<IReadOnlyList<TardanzaPersona>> TardanzasAsync(DateOnly desde, DateOnly hasta, string? area = null, int? empleadoId = null, CancellationToken ct = default)
    {
        if (hasta < desde) throw new ArgumentException("hasta < desde", nameof(hasta));

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tardes = await db.Novedades.AsNoTracking()
            .Include(n => n.Empleado)
            .Where(n => n.Fecha >= desde && n.Fecha <= hasta && n.Estado == EstadoJornada.Tarde
                && (area == null || n.Empleado.Area == area)
                && (empleadoId == null || n.EmpleadoId == empleadoId))
            .ToListAsync(ct);

        return tardes
            .GroupBy(n => n.EmpleadoId)
            .Select(g => new TardanzaPersona(
                g.Key,
                g.First().Empleado.Legajo,
                g.First().Empleado.ApellidoNombre,
                g.First().Empleado.Area,
                g.Count(),
                g.Sum(n => n.MinutosTardanza),
                g.Max(n => n.Fecha)))
            .OrderByDescending(t => t.MinutosTotales).ThenBy(t => t.ApellidoNombre)
            .ToList();
    }

    public async Task<IReadOnlyList<AusenciasPersona>> AusenciasPorPersonaAsync(DateOnly desde, DateOnly hasta, string? area = null, CancellationToken ct = default)
    {
        if (hasta < desde) throw new ArgumentException("hasta < desde", nameof(hasta));

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Misma definición de "ausencia" que AusentismoService: justificada o injustificada, nunca en feriado.
        var ausencias = await db.Novedades.AsNoTracking()
            .Include(n => n.Empleado)
            .Where(n => n.Fecha >= desde && n.Fecha <= hasta && !n.EsFeriado
                && (n.Estado == EstadoJornada.AusenteJustificado || n.Estado == EstadoJornada.AusenteInjustificado)
                && (area == null || n.Empleado.Area == area))
            .ToListAsync(ct);

        return ausencias
            .GroupBy(n => n.EmpleadoId)
            .Select(g => new AusenciasPersona(
                g.Key,
                g.First().Empleado.Legajo,
                g.First().Empleado.ApellidoNombre,
                g.First().Empleado.Area,
                g.Count(n => n.Estado == EstadoJornada.AusenteJustificado),
                g.Count(n => n.Estado == EstadoJornada.AusenteInjustificado),
                g.Where(n => n.Estado == EstadoJornada.AusenteJustificado)
                 .SelectMany(n => PresentismoService.SepararTipos(n.MotivoNovedad))
                 .Distinct().OrderBy(m => m).ToList()))
            .OrderByDescending(a => a.Total).ThenBy(a => a.ApellidoNombre)
            .ToList();
    }

    public async Task<ResumenDia> ResumenDiaAsync(DateOnly fecha, Turno? turno = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var dia = await db.Novedades.AsNoTracking()
            .Include(n => n.Empleado)
            .Where(n => n.Fecha == fecha && (turno == null || n.Turno == turno))
            .ToListAsync(ct);

        var porEstado = dia
            .GroupBy(n => n.Estado)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key.ToString(), g => g.Count());

        static IReadOnlyList<string> Nombres(IEnumerable<NovedadDiaria> ns) =>
            ns.Select(n => n.Empleado.ApellidoNombre).OrderBy(n => n).ToList();

        return new ResumenDia(
            fecha, turno, porEstado,
            Nombres(dia.Where(n => n.Estado == EstadoJornada.Tarde)),
            Nombres(dia.Where(n => n.Estado == EstadoJornada.AusenteInjustificado)),
            Nombres(dia.Where(n => n.Estado == EstadoJornada.AusenteJustificado)),
            dia.Any(n => n.EsFeriado));
    }

    public async Task<IReadOnlyList<LicenciaProgramada>> LicenciasProgramadasAsync(DateOnly hoy, int? empleadoId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Días futuros ya justificados (Humand programadas o manuales aplicadas al futuro).
        var dias = await db.Novedades.AsNoTracking()
            .Include(n => n.Empleado)
            .Where(n => n.Fecha > hoy && n.Estado == EstadoJornada.AusenteJustificado
                && (empleadoId == null || n.EmpleadoId == empleadoId))
            .OrderBy(n => n.EmpleadoId).ThenBy(n => n.Fecha)
            .ToListAsync(ct);

        // Días consecutivos del mismo empleado y motivo → un rango (los permisos de Humand no
        // guardan desde/hasta; el rango se reconstruye igual que en la observación de presentismo).
        var res = new List<LicenciaProgramada>();
        int i = 0;
        while (i < dias.Count)
        {
            int j = i;
            while (j + 1 < dias.Count
                   && dias[j + 1].EmpleadoId == dias[i].EmpleadoId
                   && (dias[j + 1].MotivoNovedad ?? "Licencia") == (dias[i].MotivoNovedad ?? "Licencia")
                   && dias[j + 1].Fecha.DayNumber - dias[j].Fecha.DayNumber <= 2) // tolera finde en el medio
                j++;
            var e = dias[i].Empleado;
            res.Add(new LicenciaProgramada(e.Id, e.ApellidoNombre, e.Area,
                dias[i].Fecha, dias[j].Fecha, dias[i].MotivoNovedad ?? "Licencia"));
            i = j + 1;
        }
        return res;
    }

    public async Task<CoberturaDatos> CoberturaAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var fechas = await db.Novedades.AsNoTracking()
            .Select(n => n.Fecha).Distinct().OrderBy(f => f)
            .ToListAsync(ct);

        if (fechas.Count == 0) return new CoberturaDatos(null, null, 0, []);

        // Huecos internos: tramos entre fechas consecutivas con más de 1 día de diferencia.
        var huecos = new List<(DateOnly, DateOnly)>();
        for (int i = 1; i < fechas.Count; i++)
            if (fechas[i].DayNumber - fechas[i - 1].DayNumber > 1)
                huecos.Add((fechas[i - 1].AddDays(1), fechas[i].AddDays(-1)));

        return new CoberturaDatos(fechas[0], fechas[^1], fechas.Count, huecos);
    }
}
