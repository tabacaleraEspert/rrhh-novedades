using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RRHHNovedades.Web.Options;
using RRHHNovedades.Web.Services;
using System.Net;
using System.Text;
using Xunit;

namespace RRHHNovedades.Tests;

/// <summary>
/// Tests del cliente de Humand contra respuestas simuladas con la FORMA REAL de la API
/// (verificada en prod, jun-2026). Congelan los gotchas: limit máx 50, totalPages siempre 1,
/// teléfono en `phoneNumber`, área en la segmentación `Sector`, horas con offset -03:00.
/// </summary>
public class HumandServiceTests
{
    // Handler fake: devuelve respuestas en orden (una por request).
    private sealed class FakeHandler(params (HttpStatusCode Code, string Body)[] responses) : HttpMessageHandler
    {
        private int _i;
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            var (code, body) = responses[Math.Min(_i++, responses.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(code)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private static HumandService Crear(FakeHandler handler) => new(
        new HttpClient(handler),
        Options.Create(new HumandOptions { BaseUrl = "https://fake.local/api/v1", ApiKey = "test" }),
        NullLogger<HumandService>.Instance);

    // Usuario con la forma real: phone=null (el dato vive en phoneNumber) y área en segmentations/Sector.
    private static string UserJson(string id, string nombre = "Juan", string apellido = "Pérez") => $$"""
        {
          "employeeInternalId": "{{id}}", "firstName": "{{nombre}}", "lastName": "{{apellido}}",
          "phone": null, "phoneNumber": "+54115959{{id}}", "department": null,
          "segmentations": [
            { "group": "Sector", "item": "Producción" },
            { "group": "Genero", "item": "Masculino" }
          ],
          "status": "ACTIVE"
        }
        """;

    [Fact]
    public async Task Empleados_mapea_phoneNumber_y_segmentacion_Sector()
    {
        var handler = new FakeHandler(
            (HttpStatusCode.OK, $$"""{ "count": 1, "users": [ {{UserJson("9063")}} ] }"""));
        var svc = Crear(handler);

        var emps = await svc.ObtenerEmpleadosAsync();

        var e = Assert.Single(emps);
        Assert.Equal("9063", e.EmployeeInternalId);
        Assert.Equal("+54115959" + "9063", e.Telefono);   // viene de phoneNumber, NO de phone
        Assert.Equal("Producción", e.Area);                // viene de segmentations/Sector, NO de department
    }

    [Fact]
    public async Task Empleados_pagina_con_limit_50_hasta_pagina_incompleta()
    {
        // Humand rechaza limit>50 (400) → el cliente debe pedir de a 50 y seguir mientras venga llena.
        var page1 = "{ \"count\": 52, \"users\": [" +
                    string.Join(",", Enumerable.Range(1, 50).Select(i => UserJson($"A{i:000}"))) + "] }";
        var page2 = "{ \"count\": 52, \"users\": [" +
                    string.Join(",", Enumerable.Range(51, 2).Select(i => UserJson($"A{i:000}"))) + "] }";
        var handler = new FakeHandler((HttpStatusCode.OK, page1), (HttpStatusCode.OK, page2));
        var svc = Crear(handler);

        var emps = await svc.ObtenerEmpleadosAsync();

        Assert.Equal(52, emps.Count);
        Assert.All(handler.Urls, u => Assert.Contains("limit=50", u));
        Assert.Equal(2, handler.Urls.Count); // página 2 vino incompleta → no pide página 3
    }

    private static string DayJson(string empId, string fecha = "2026-06-09") => $$"""
        {
          "employeeId": "{{empId}}", "referenceDate": "{{fecha}}",
          "isWorkday": true, "hasSchedule": true, "weekday": "TUESDAY",
          "timeSlots": [ { "startTime": "08:00", "endTime": "16:00" } ],
          "entries": [
            { "type": "START", "time": "{{fecha}}T07:58:00.000-03:00" },
            { "type": "END",   "time": "{{fecha}}T16:01:00.000-03:00" }
          ],
          "timeOffRequests": [], "holidays": [], "incidences": []
        }
        """;

    [Fact]
    public async Task Jornadas_ignora_el_totalPages_mentiroso_y_pagina_mientras_venga_llena()
    {
        // Bug REAL de la API: totalPages devuelve SIEMPRE 1 (y count es por página).
        // Si el cliente le creyera, perderíamos empleados (pasó: 147 de 191).
        var ids = Enumerable.Range(1, 60).Select(i => $"E{i:000}").ToList();
        var page1 = "{ \"count\": 50, \"page\": 1, \"totalPages\": 1, \"items\": [" +
                    string.Join(",", ids.Take(50).Select(i => DayJson(i))) + "] }";
        var page2 = "{ \"count\": 10, \"page\": 2, \"totalPages\": 1, \"items\": [" +
                    string.Join(",", ids.Skip(50).Select(i => DayJson(i))) + "] }";
        var handler = new FakeHandler((HttpStatusCode.OK, page1), (HttpStatusCode.OK, page2));
        var svc = Crear(handler);

        var jornadas = await svc.ObtenerJornadasAsync(ids, new DateOnly(2026, 6, 9));

        Assert.Equal(60, jornadas.Count); // con totalPages=1 "creído", serían solo 50
    }

    [Fact]
    public async Task Jornadas_mapea_fichadas_en_hora_argentina_sin_depender_de_la_TZ_del_servidor()
    {
        // La misma hora expresada con offset -03:00 y en UTC (Z) debe dar 07:58 AR en ambos casos.
        var conOffset = DayJson("E1");
        var enUtc = conOffset.Replace("T07:58:00.000-03:00", "T10:58:00.000Z")
                             .Replace("T16:01:00.000-03:00", "T19:01:00.000Z");
        var body1 = $$"""{ "count": 1, "items": [ {{conOffset}} ] }""";
        var body2 = $$"""{ "count": 1, "items": [ {{enUtc}} ] }""";
        var svc1 = Crear(new FakeHandler((HttpStatusCode.OK, body1)));
        var svc2 = Crear(new FakeHandler((HttpStatusCode.OK, body2)));

        var j1 = (await svc1.ObtenerJornadasAsync(["E1"], new DateOnly(2026, 6, 9))).Single();
        var j2 = (await svc2.ObtenerJornadasAsync(["E1"], new DateOnly(2026, 6, 9))).Single();

        Assert.Equal(new TimeOnly(7, 58), j1.HoraEntrada);
        Assert.Equal(new TimeOnly(16, 1), j1.HoraSalida);
        Assert.Equal(j1.HoraEntrada, j2.HoraEntrada);
        Assert.Equal(j1.HoraSalida, j2.HoraSalida);
        Assert.Equal(new TimeOnly(8, 0), j1.InicioTeorico);
    }

    [Fact]
    public async Task Jornadas_mapea_permisos_embebidos_e_incidences()
    {
        // Caso real de vacaciones: sin horario, sin ABSENT, permiso embebido en timeOffRequests.
        var dia = """
            {
              "employeeId": "E1", "referenceDate": "2026-06-09",
              "isWorkday": false, "hasSchedule": false,
              "timeSlots": [], "entries": [],
              "timeOffRequests": [ { "id": 1, "name": "Vacaciones" } ],
              "incidences": []
            }
            """;
        var svc = Crear(new FakeHandler((HttpStatusCode.OK, $$"""{ "count": 1, "items": [ {{dia}} ] }""")));

        var j = (await svc.ObtenerJornadasAsync(["E1"], new DateOnly(2026, 6, 9))).Single();

        Assert.False(j.IsWorkday);
        Assert.False(j.HasSchedule);
        Assert.Equal(["Vacaciones"], j.PermisosDelDia);
        Assert.Empty(j.Incidences);
    }

    [Fact]
    public async Task Reintenta_con_backoff_ante_429()
    {
        var ok = $$"""{ "count": 1, "users": [ {{UserJson("1")}} ] }""";
        var handler = new FakeHandler(
            (HttpStatusCode.TooManyRequests, "{}"),
            (HttpStatusCode.OK, ok));
        var svc = Crear(handler);

        var emps = await svc.ObtenerEmpleadosAsync();

        Assert.Single(emps);
        Assert.Equal(2, handler.Urls.Count); // 429 + reintento exitoso
    }
}
