namespace RRHHNovedades.Web.Services;

/// <summary>
/// Implementación simulada para desarrollo (Humand:UseMock = true). No pega a la API real.
/// Genera un set fijo de empleados y jornadas variadas para probar el pipeline y el bot.
/// </summary>
public class MockHumandService : IHumandService
{
    private static readonly EmpleadoHumand[] Empleados =
    [
        new("EMP-001", "Juan",   "Pérez",     "+5491111111111", "Producción",
            Status: "ACTIVE", FechaIngreso: new(2012, 3, 1), FechaNacimiento: new(1980, 8, 12), JefeId: "EMP-008", Sexo: "Masculino"),
        new("EMP-002", "Rosa",   "Gómez",     "+5491122222222", "Producción",
            Status: "ACTIVE", FechaIngreso: new(2021, 7, 15), FechaNacimiento: new(1993, 2, 3), JefeId: "EMP-008", Sexo: "Femenino"),
        new("EMP-003", "Mario",  "Sosa",      "+5491133333333", "Logística",
            Status: "ACTIVE", FechaIngreso: new(2018, 1, 10), FechaNacimiento: new(1975, 11, 30), JefeId: "EMP-004", Sexo: "Masculino"),
        new("EMP-004", "Lucía",  "Díaz",      "+5491144444444", "Administración",
            Status: "ACTIVE", FechaIngreso: new(2005, 9, 20), FechaNacimiento: new(1970, 8, 25), Sexo: "Femenino"),
        new("EMP-005", "Pedro",  "Ruiz",      "+5491155555555", "Logística",
            Status: "ACTIVE", FechaIngreso: new(2024, 2, 1), FechaNacimiento: new(1999, 5, 18), JefeId: "EMP-004", Sexo: "Masculino"),
        new("EMP-006", "Sofía",  "Vega",      "+5491166666666", "Ventas",
            Status: "ACTIVE", FechaIngreso: new(2019, 6, 3), FechaNacimiento: new(1988, 12, 1), JefeId: "EMP-004", Sexo: "Femenino"),
        new("EMP-007", "Carla",  "López",     "+5491177777777", "Ventas",
            Status: "ACTIVE", FechaIngreso: new(2022, 10, 11), FechaNacimiento: new(1996, 4, 9), JefeId: "EMP-006", Sexo: "Femenino"),
        new("EMP-008", "Diego",  "Fernández", "+5491188888888", "Producción",
            Status: "ACTIVE", FechaIngreso: new(2010, 4, 5), FechaNacimiento: new(1978, 1, 22), Sexo: "Masculino"),
        new("EMP-009", "Nadia",  "Molina",    "+5491199999999", "Producción", "Turno C Noche", "9001",
            Status: "ACTIVE", FechaIngreso: new(2016, 8, 29), FechaNacimiento: new(1990, 8, 5), JefeId: "EMP-008", Sexo: "Femenino"),
        new("EMP-010", "Bruno",  "Acosta",    "+5491100000000", "Producción", "Turno C Noche", "9002",
            Status: "ACTIVE", FechaIngreso: new(2023, 11, 13), FechaNacimiento: new(2001, 9, 14), JefeId: "EMP-009", Sexo: "Masculino"),
    ];

    public Task<IReadOnlyList<EmpleadoHumand>> ObtenerEmpleadosAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EmpleadoHumand>>(Empleados);

    public Task<IReadOnlyList<SaldoTimeOffHumand>> ObtenerSaldosTimeOffAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SaldoTimeOffHumand>>(
        [
            new("EMP-001", "Vacaciones", 14),
            new("EMP-002", "Vacaciones", 28),   // semáforo naranja (>21)
            new("EMP-003", "Vacaciones", 40),   // semáforo rojo (>35)
            new("EMP-004", "Vacaciones", 0),
            new("EMP-005", "Vacaciones", 7),
            new("EMP-006", "Vacaciones", 21),
            new("EMP-009", "Vacaciones", 3.5),
        ]);

    public Task<IReadOnlyList<SolicitudTimeOffHumand>> ObtenerSolicitudesTimeOffAsync(
        DateOnly desde, DateOnly hasta, CancellationToken ct = default)
    {
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        IReadOnlyList<SolicitudTimeOffHumand> todas =
        [
            new("EMP-004", "Vacaciones", hoy.AddDays(-3), hoy.AddDays(4), "APPROVED", 8),
            new("EMP-005", "Lic. por enfermedad", hoy, hoy.AddDays(1), "APPROVED", 2),
            new("EMP-006", "Vacaciones", hoy.AddDays(10), hoy.AddDays(17), "IN_PROGRESS", 8),
            new("EMP-002", "Días de estudio", hoy.AddDays(20), hoy.AddDays(21), "APPROVED", 2),
        ];
        return Task.FromResult<IReadOnlyList<SolicitudTimeOffHumand>>(
            todas.Where(s => s.Hasta >= desde && s.Desde <= hasta).ToList());
    }

    public Task<IReadOnlyList<JornadaHumand>> ObtenerJornadasAsync(
        IEnumerable<string> employeeInternalIds, DateOnly fecha, CancellationToken ct = default)
    {
        var ids = employeeInternalIds.ToHashSet();
        var list = new List<JornadaHumand>();

        // Distribución de ejemplo: presentes, tardes, ausentes (con/sin permiso), franco.
        JornadaHumand J(string id, TimeOnly? inicioTeorico, string[] inc, string[] permisos,
            TimeOnly? entrada, bool workday = true, bool schedule = true) =>
            new(id, fecha, workday, schedule, inc, permisos, entrada, entrada?.AddHours(8), inicioTeorico);

        var t8 = new TimeOnly(8, 0);
        foreach (var e in Empleados)
        {
            if (!ids.Contains(e.EmployeeInternalId)) continue;
            list.Add(e.EmployeeInternalId switch
            {
                "EMP-001" => J(e.EmployeeInternalId, t8, [], [], new TimeOnly(7, 58)),                       // Presente
                "EMP-002" => J(e.EmployeeInternalId, t8, ["LATE"], [], new TimeOnly(8, 25)),                 // Tarde
                "EMP-003" => J(e.EmployeeInternalId, t8, ["ABSENT"], [], null),                              // Ausente injustificado
                "EMP-004" => J(e.EmployeeInternalId, t8, ["ABSENT"], ["Vacaciones"], null),                 // Justificado
                "EMP-005" => J(e.EmployeeInternalId, t8, ["ABSENT"], ["Certificado médico"], null),         // Justificado
                "EMP-006" => J(e.EmployeeInternalId, new TimeOnly(14, 0), [], [], new TimeOnly(14, 3)),      // Presente (tarde turno)
                "EMP-007" => J(e.EmployeeInternalId, new TimeOnly(14, 0), ["LATE"], [], new TimeOnly(14, 40)),// Tarde (turno tarde)
                "EMP-008" => J(e.EmployeeInternalId, null, [], [], null, workday: false, schedule: false),  // Franco
                "EMP-009" => J(e.EmployeeInternalId, new TimeOnly(22, 0), [], [], new TimeOnly(21, 58)),     // Presente (noche; salida cruza medianoche: 05:58)
                "EMP-010" => J(e.EmployeeInternalId, new TimeOnly(22, 0), ["LATE"], [], new TimeOnly(22, 20)),// Tarde (turno noche)
                _ => J(e.EmployeeInternalId, t8, [], [], t8)
            });
        }
        return Task.FromResult<IReadOnlyList<JornadaHumand>>(list);
    }
}
