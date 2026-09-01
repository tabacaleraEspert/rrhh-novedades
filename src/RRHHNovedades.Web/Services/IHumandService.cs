namespace RRHHNovedades.Web.Services;

/// <summary>Empleado tal como lo necesitamos de Humand (`/users`).</summary>
public record EmpleadoHumand(
    string EmployeeInternalId,
    string Nombre,
    string Apellido,
    string? Telefono,
    string? Area,
    string? SegTurno = null,  // Ítem de la segmentación "Turno" (ej. "Turno C Noche"); null si no tiene.
    string? Legajo = null,    // Campo personalizado "Legajo" de Humand.
    // ── Campos para Demografía (nueva, ago-2026). Todos opcionales: si Humand no
    //    los trae, el bloque correspondiente de la página no se muestra. ──
    string? Status = null,           // ACTIVE | DEACTIVATED | UNCLAIMED
    DateOnly? FechaIngreso = null,   // hiringDate
    DateOnly? FechaNacimiento = null,// birthdate
    string? JefeId = null,           // relationships: employeeInternalId del jefe (BOSS)
    string? Sexo = null);            // campo personalizado "Sexo"/"Género" si existe

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

/// <summary>Saldo de un empleado en una política de time-off (`/time-off/balances`).</summary>
public record SaldoTimeOffHumand(
    string EmployeeInternalId,
    string Politica,      // nombre de la política (ej. "Vacaciones")
    double Saldo);        // días disponibles (currentBalance)

/// <summary>Solicitud de licencia/vacaciones (`/time-off/requests`).</summary>
public record SolicitudTimeOffHumand(
    string EmployeeInternalId,
    string Politica,
    DateOnly Desde,
    DateOnly Hasta,
    string Estado,        // APPROVED | IN_PROGRESS | REJECTED | CANCELLED
    double Dias);

/// <summary>
/// Integración con Humand (plataforma de RRHH) — fuente de empleados y novedades de asistencia.
/// Ver docs/humand/ENDPOINTS-RELEVANTES.md.
/// </summary>
public interface IHumandService
{
    Task<IReadOnlyList<EmpleadoHumand>> ObtenerEmpleadosAsync(CancellationToken ct = default);

    Task<IReadOnlyList<JornadaHumand>> ObtenerJornadasAsync(
        IEnumerable<string> employeeInternalIds, DateOnly fecha, CancellationToken ct = default);

    /// <summary>Saldos de time-off de toda la organización (sección Vacaciones).</summary>
    Task<IReadOnlyList<SaldoTimeOffHumand>> ObtenerSaldosTimeOffAsync(CancellationToken ct = default);

    /// <summary>Solicitudes de time-off del rango (sección Vacaciones). Estados APPROVED e IN_PROGRESS.</summary>
    Task<IReadOnlyList<SolicitudTimeOffHumand>> ObtenerSolicitudesTimeOffAsync(
        DateOnly desde, DateOnly hasta, CancellationToken ct = default);
}
