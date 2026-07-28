namespace RRHHNovedades.Web.Services;

/// <summary>Empleado tal como lo necesitamos de Humand (`/users`).</summary>
public record EmpleadoHumand(
    string EmployeeInternalId,
    string Nombre,
    string Apellido,
    string? Telefono,
    string? Area,
    string? SegTurno = null,  // Ítem de la segmentación "Turno" (ej. "Turno C Noche"); null si no tiene.
    string? Legajo = null);   // Campo personalizado "Legajo" de Humand.

/// <summary>Resumen de jornada de un empleado en un día (`/time-tracking/day-summaries`).</summary>
public record JornadaHumand(
    string EmployeeInternalId,
    DateOnly Fecha,
    bool IsWorkday,
    bool HasSchedule,
    IReadOnlyList<string> Incidences,
    IReadOnlyList<string> PermisosDelDia,
    TimeOnly? HoraEntrada,
    TimeOnly? HoraSalida,
    TimeOnly? InicioTeorico,
    bool EsFeriado = false); // array `holidays` del day-summary no vacío

/// <summary>
/// Integración con Humand (plataforma de RRHH) — fuente de empleados y novedades de asistencia.
/// Ver docs/humand/ENDPOINTS-RELEVANTES.md.
/// </summary>
public interface IHumandService
{
    Task<IReadOnlyList<EmpleadoHumand>> ObtenerEmpleadosAsync(CancellationToken ct = default);

    Task<IReadOnlyList<JornadaHumand>> ObtenerJornadasAsync(
        IEnumerable<string> employeeInternalIds, DateOnly fecha, CancellationToken ct = default);
}
