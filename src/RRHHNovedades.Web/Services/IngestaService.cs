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
            emp.Activo = true;
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Ingesta: {Count} empleados sincronizados", remotos.Count);
        return remotos.Count;
    }

    public async Task<int> SincronizarDiaAsync(DateOnly fecha, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var empleados = await db.Empleados.Where(e => e.Activo).ToListAsync(ct);
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
        int n = 0;

        foreach (var j in jornadas)
        {
            if (!porId.TryGetValue(j.EmployeeInternalId, out var emp)) continue;

            var (estado, motivo, minTarde) = Clasificar(j);
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
            nov.ActualizadoUtc = DateTime.UtcNow;
            n++;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Ingesta: {Count} novedades del {Fecha} actualizadas", n, fecha);
        return n;
    }

    private static (EstadoJornada estado, string? motivo, int minTarde) Clasificar(JornadaHumand j)
    {
        bool absent = j.Incidences.Contains("ABSENT");
        bool late = j.Incidences.Contains("LATE");
        var motivo = j.PermisosDelDia.Count > 0 ? string.Join(", ", j.PermisosDelDia) : null;

        if (!j.IsWorkday || !j.HasSchedule)
            return (EstadoJornada.FrancoNoLaborable, motivo, 0);

        if (absent)
            return j.PermisosDelDia.Count > 0
                ? (EstadoJornada.AusenteJustificado, motivo, 0)
                : (EstadoJornada.AusenteInjustificado, null, 0);

        if (late)
        {
            int min = 0;
            if (j.HoraEntrada is { } he && j.InicioTeorico is { } it && he > it)
                min = (int)(he - it).TotalMinutes;
            return (EstadoJornada.Tarde, motivo, min);
        }

        if (j.HoraEntrada is not null)
            return (EstadoJornada.Presente, motivo, 0);

        // No fichó y sin marca ABSENT explícita.
        return j.PermisosDelDia.Count > 0
            ? (EstadoJornada.AusenteJustificado, motivo, 0)
            : (EstadoJornada.AusenteInjustificado, null, 0);
    }

    private static Turno InferirTurno(JornadaHumand j, Empleado emp, TimeOnly corte)
    {
        var inicio = j.InicioTeorico ?? j.HoraEntrada;
        if (inicio is { } i) return i >= corte ? Turno.Tarde : Turno.Manana;
        return emp.Turno;
    }

    private static TimeOnly? ParseTime(string? s) => TimeOnly.TryParse(s, out var t) ? t : null;
}
