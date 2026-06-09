namespace RRHHNovedades.Web.Services;

/// <summary>
/// Implementación simulada para desarrollo (Humand:UseMock = true). No pega a la API real.
/// Genera un set fijo de empleados y jornadas variadas para probar el pipeline y el bot.
/// </summary>
public class MockHumandService : IHumandService
{
    private static readonly EmpleadoHumand[] Empleados =
    [
        new("EMP-001", "Juan",   "Pérez",     "+5491111111111", "Producción"),
        new("EMP-002", "Rosa",   "Gómez",     "+5491122222222", "Producción"),
        new("EMP-003", "Mario",  "Sosa",      "+5491133333333", "Logística"),
        new("EMP-004", "Lucía",  "Díaz",      "+5491144444444", "Administración"),
        new("EMP-005", "Pedro",  "Ruiz",      "+5491155555555", "Logística"),
        new("EMP-006", "Sofía",  "Vega",      "+5491166666666", "Ventas"),
        new("EMP-007", "Carla",  "López",     "+5491177777777", "Ventas"),
        new("EMP-008", "Diego",  "Fernández", "+5491188888888", "Producción"),
    ];

    public Task<IReadOnlyList<EmpleadoHumand>> ObtenerEmpleadosAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EmpleadoHumand>>(Empleados);

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
                _ => J(e.EmployeeInternalId, t8, [], [], t8)
            });
        }
        return Task.FromResult<IReadOnlyList<JornadaHumand>>(list);
    }
}
