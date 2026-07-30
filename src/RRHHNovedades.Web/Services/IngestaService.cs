using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;
using RRHHNovedades.Web.Options;

namespace RRHHNovedades.Web.Services;

public interface IIngestaService
{
    Task<int> SincronizarEmpleadosAsync(CancellationToken ct = default);
    Task<int> SincronizarDiaAsync(DateOnly fecha, CancellationToken ct = default);

    /// <summary>Sincroniza SOLO un empleado en una fecha (1 llamada a Humand; para correcciones puntuales).</summary>
    Task<int> SincronizarEmpleadoDiaAsync(int empleadoId, DateOnly fecha, CancellationToken ct = default);
}

/// <summary>
/// Trae datos de Humand y los persiste como <see cref="NovedadDiaria"/> (idempotente por empleado+fecha).
/// La clasificación se apoya en `incidences` de Humand y resuelve justificado vs injustificado
/// cruzando contra los permisos que cubren el día.
/// </summary>
public class IngestaService(
    IDbContextFactory<AppDbContext> dbFactory,
    IHumandService humand,
    IOptions<AsistenciaOptions> asistencia,
    IReloj reloj,
    ILogger<IngestaService> logger) : IIngestaService
{
    private readonly AsistenciaOptions _opt = asistencia.Value;

    public async Task<int> SincronizarEmpleadosAsync(CancellationToken ct = default)
    {
        var remotos = await humand.ObtenerEmpleadosAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var locales = await db.Empleados.ToDictionaryAsync(e => e.EmployeeInternalId, ct);

        foreach (var r in remotos)
        {
            if (!locales.TryGetValue(r.EmployeeInternalId, out var emp))
            {
                emp = new Empleado { EmployeeInternalId = r.EmployeeInternalId };
                db.Empleados.Add(emp);
            }
            emp.Nombre = r.Nombre;
            emp.Apellido = r.Apellido;
            emp.Telefono = r.Telefono;
            emp.Area = r.Area;
            emp.Legajo = r.Legajo;
            emp.Activo = true;

            // Turno noche: lo define la segmentación "Turno" de Humand (ej. "Turno C Noche"),
            // no el horario. Si el empleado deja de estar segmentado como nocturno, vuelve a
            // Mañana y la inferencia por horario lo acomoda en el próximo sync del día.
            if (EsSegmentacionNocturna(r.SegTurno))
                emp.Turno = Turno.Noche;
            else if (emp.Turno == Turno.Noche)
                emp.Turno = Turno.Manana;
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Ingesta: {Count} empleados sincronizados", remotos.Count);
        return remotos.Count;
    }

    public Task<int> SincronizarDiaAsync(DateOnly fecha, CancellationToken ct = default) =>
        SincronizarDiaCoreAsync(fecha, soloEmpleadoId: null, ct);

    public Task<int> SincronizarEmpleadoDiaAsync(int empleadoId, DateOnly fecha, CancellationToken ct = default) =>
        SincronizarDiaCoreAsync(fecha, empleadoId, ct);

    private async Task<int> SincronizarDiaCoreAsync(DateOnly fecha, int? soloEmpleadoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var empleados = await db.Empleados
            .Where(e => e.Activo && (soloEmpleadoId == null || e.Id == soloEmpleadoId))
            .ToListAsync(ct);
        if (empleados.Count == 0)
        {
            logger.LogWarning("Ingesta: no hay empleados; correr SincronizarEmpleados primero");
            return 0;
        }

        var porId = empleados.ToDictionary(e => e.EmployeeInternalId);
        var jornadas = await humand.ObtenerJornadasAsync(porId.Keys, fecha, ct);

        var existentes = await db.Novedades
            .Where(n => n.Fecha == fecha)
            .ToDictionaryAsync(n => n.EmpleadoId, ct);

        var corte = ParseTime(_opt.CorteTurnoTarde) ?? new TimeOnly(13, 0);
        var feriadosCfg = FeriadosConfigurados(_opt.Feriados);

        // "Fichadores": empleados con alguna fichada en los últimos 30 días. Los que NUNCA fichan
        // (ventas, oficinas, dirección) no tienen horario en Humand y caerían como Franco todos
        // los días; para ellos, un día hábil sin feriado cuenta como Presente (regla RRHH 28-jul-2026).
        // Los fichadores conservan sus francos rotativos.
        var fichadores = (await db.Novedades
            .Where(x => x.Fecha < fecha && x.Fecha >= fecha.AddDays(-30) && x.HoraEntrada != null)
            .Select(x => x.EmpleadoId)
            .Distinct()
            .ToListAsync(ct)).ToHashSet();

        int n = 0;

        foreach (var j in jornadas)
        {
            if (!porId.TryGetValue(j.EmployeeInternalId, out var emp)) continue;

            var (estado, motivo, minTarde) = Clasificar(j, reloj.Ahora);
            if (estado == EstadoJornada.FrancoNoLaborable
                && EsPresentePorNoFichaje(fecha, j, feriadosCfg, esFichador: fichadores.Contains(emp.Id)))
                estado = EstadoJornada.Presente;
            var turno = InferirTurno(j, emp, corte);

            if (!existentes.TryGetValue(emp.Id, out var nov))
            {
                nov = new NovedadDiaria { EmpleadoId = emp.Id, Fecha = fecha };
                db.Novedades.Add(nov);
            }
            nov.Turno = turno;
            nov.Estado = estado;
            nov.MinutosTardanza = minTarde;
            nov.HoraEntrada = j.HoraEntrada;
            nov.HoraSalida = j.HoraSalida;
            nov.MotivoNovedad = motivo;
            // Feriado: lo que marque Humand (hoy no cargan el calendario) + la lista de appsettings.
            nov.EsFeriado = j.EsFeriado || feriadosCfg.Contains(fecha);
            nov.ActualizadoUtc = DateTime.UtcNow;
            n++;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Ingesta: {Count} novedades del {Fecha} actualizadas", n, fecha);
        return n;
    }

    // internal para poder testearla directamente (InternalsVisibleTo RRHHNovedades.Tests).
    // `ahora` (hora Argentina) permite distinguir "todavía no fichó porque su turno no empezó"
    // (Pendiente) de "no vino" (Ausente). Si es null, se comporta como antes (sin Pendiente).
    internal static (EstadoJornada estado, string? motivo, int minTarde) Clasificar(JornadaHumand j, DateTimeOffset? ahora = null)
    {
        bool absent = j.Incidences.Contains("ABSENT");
        bool late = j.Incidences.Contains("LATE");
        bool ficho = j.HoraEntrada is not null;
        var motivo = j.PermisosDelDia.Count > 0 ? string.Join(", ", j.PermisosDelDia) : null;

        // ¿El turno todavía no arrancó? Un día FUTURO nunca puede ser ausencia (nadie fichó porque
        // no ocurrió); en el día de HOY, antes de la hora teórica de entrada no fichó porque aún
        // no le toca, no porque faltó.
        bool turnoNoIniciado =
            ahora is { } a
            && (j.Fecha > DateOnly.FromDateTime(a.DateTime)
                || (j.Fecha == DateOnly.FromDateTime(a.DateTime)
                    && j.InicioTeorico is { } inicio
                    && TimeOnly.FromDateTime(a.DateTime) < inicio));

        // Permiso aprobado y no fichó ⇒ Justificado. Va ANTES que la regla de franco:
        // con permiso (vacaciones, etc.) Humand quita el horario del día (isWorkday/hasSchedule
        // = false) y NO marca ABSENT; el permiso viene embebido en timeOffRequests.
        if (j.PermisosDelDia.Count > 0 && !ficho)
            return (EstadoJornada.AusenteJustificado, motivo, 0);

        if (!j.IsWorkday || !j.HasSchedule)
            return (EstadoJornada.FrancoNoLaborable, motivo, 0);

        if (absent)
            return (EstadoJornada.AusenteInjustificado, null, 0);

        if (late)
        {
            int min = 0;
            if (j.HoraEntrada is { } he && j.InicioTeorico is { } it && he > it)
                min = (int)(he - it).TotalMinutes;
            return (EstadoJornada.Tarde, motivo, min);
        }

        if (ficho)
            return (EstadoJornada.Presente, motivo, 0);

        // Laborable, sin fichada y sin ABSENT, pero su turno todavía no empezó: aún no es ausente.
        if (turnoNoIniciado)
            return (EstadoJornada.Pendiente, motivo, 0);

        // Laborable, sin fichada, sin ABSENT explícito y sin permiso.
        return (EstadoJornada.AusenteInjustificado, null, 0);
    }

    private static Turno InferirTurno(JornadaHumand j, Empleado emp, TimeOnly corte)
    {
        // Nocturno por segmentación: siempre Noche (su inicio ~22:00 caería como "Tarde" por hora).
        if (emp.Turno == Turno.Noche) return Turno.Noche;
        var inicio = j.InicioTeorico ?? j.HoraEntrada;
        if (inicio is { } i) return i >= corte ? Turno.Tarde : Turno.Manana;
        return emp.Turno;
    }

    /// <summary>
    /// Un "Franco" de alguien que no ficha nunca, en día hábil (lun-vie) no feriado, se considera
    /// Presente: son puestos sin fichaje (ventas, oficinas), no descanso.
    /// internal para testear (InternalsVisibleTo RRHHNovedades.Tests).
    /// </summary>
    internal static bool EsPresentePorNoFichaje(DateOnly fecha, JornadaHumand j, HashSet<DateOnly> feriadosCfg, bool esFichador) =>
        !esFichador
        && fecha.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
        && !j.EsFeriado
        && !feriadosCfg.Contains(fecha);

    // internal para testearla (InternalsVisibleTo RRHHNovedades.Tests).
    internal static bool EsSegmentacionNocturna(string? segTurno) =>
        segTurno?.Contains("noche", StringComparison.OrdinalIgnoreCase) == true;

    private static TimeOnly? ParseTime(string? s) => TimeOnly.TryParse(s, out var t) ? t : null;

    // internal para testear (InternalsVisibleTo RRHHNovedades.Tests).
    internal static HashSet<DateOnly> FeriadosConfigurados(IEnumerable<string>? fechas) =>
        (fechas ?? []).Select(f => DateOnly.TryParse(f, out var d) ? d : (DateOnly?)null)
                      .Where(d => d is not null).Select(d => d!.Value).ToHashSet();
}
