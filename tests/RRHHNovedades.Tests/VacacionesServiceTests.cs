using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;
using RRHHNovedades.Web.Options;
using RRHHNovedades.Web.Services;
using Xunit;

namespace RRHHNovedades.Tests;

/// <summary>
/// Sección Vacaciones (nueva, ago-2026): saldos y solicitudes en vivo desde Humand,
/// cruzados con la base local. Congela el semáforo por umbrales y el filtrado de
/// políticas (solo "vacaciones" cuenta como stock).
/// </summary>
public class VacacionesServiceTests
{
    private sealed class InMemoryFactory(string dbName) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            var opt = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(dbName).Options;
            return new AppDbContext(opt);
        }
    }

    private sealed class RelojFijo : IReloj
    {
        public DateTimeOffset Ahora => new(2026, 8, 27, 12, 0, 0, TimeSpan.FromHours(-3));
        public DateOnly Hoy => new(2026, 8, 27);
        public TimeOnly HoraActual => new(12, 0);
        public DateTime EnLocal(DateTime utc) => utc;
    }

    // ── Semáforo (estático) ──

    [Theory]
    [InlineData(0, SaldoSemaforo.Ok)]
    [InlineData(20.5, SaldoSemaforo.Ok)]
    [InlineData(21, SaldoSemaforo.Advertencia)]   // umbral inclusivo
    [InlineData(34.9, SaldoSemaforo.Advertencia)]
    [InlineData(35, SaldoSemaforo.Riesgo)]        // umbral inclusivo
    [InlineData(60, SaldoSemaforo.Riesgo)]
    public void Semaforo_umbral_inclusivo(double dias, SaldoSemaforo esperado) =>
        Assert.Equal(esperado, VacacionesService.Semaforo(dias, 21, 35));

    // ── Reporte ──

    private static async Task<VacacionesService> SetupAsync(
        string db,
        IReadOnlyList<SaldoTimeOffHumand> saldos,
        IReadOnlyList<SolicitudTimeOffHumand> solicitudes)
    {
        var factory = new InMemoryFactory(db);
        await using var ctx = factory.CreateDbContext();
        ctx.Empleados.AddRange(
            new Empleado { Id = 1, Nombre = "Nadia", Apellido = "Molina", Area = "Producción", EmployeeInternalId = "EMP-1", Legajo = "871" },
            new Empleado { Id = 2, Nombre = "Bruno", Apellido = "Paz", Area = "Ventas", EmployeeInternalId = "EMP-2", Legajo = "455" });
        await ctx.SaveChangesAsync();

        var humand = Substitute.For<IHumandService>();
        humand.ObtenerSaldosTimeOffAsync(Arg.Any<CancellationToken>()).Returns(saldos);
        humand.ObtenerSolicitudesTimeOffAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(solicitudes);

        return new VacacionesService(humand, factory, new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new FeaturesOptions()), new RelojFijo());
    }

    [Fact]
    public async Task Saldos_solo_politica_vacaciones_y_orden_descendente()
    {
        var svc = await SetupAsync(nameof(Saldos_solo_politica_vacaciones_y_orden_descendente),
            [
                new("EMP-1", "Vacaciones", 14),
                new("EMP-2", "Vacaciones", 36),
                new("EMP-2", "Días de estudio", 4), // otra política: no es stock de vacaciones
            ],
            []);

        var r = await svc.ReporteAsync(new(2026, 8, 1), new(2026, 8, 31));

        Assert.Equal(2, r.Saldos.Count);
        Assert.Equal("Paz, Bruno", r.Saldos[0].ApellidoNombre);
        Assert.Equal(36, r.Saldos[0].Dias);
        Assert.Equal(SaldoSemaforo.Riesgo, r.Saldos[0].Semaforo);
        Assert.Equal(SaldoSemaforo.Ok, r.Saldos[1].Semaforo);
    }

    [Fact]
    public async Task Saldos_de_un_empleado_desconocido_no_rompen_el_cruce()
    {
        var svc = await SetupAsync(nameof(Saldos_de_un_empleado_desconocido_no_rompen_el_cruce),
            [new("EMP-999", "Vacaciones", 10)],
            []);

        var r = await svc.ReporteAsync(new(2026, 8, 1), new(2026, 8, 31));

        Assert.Single(r.Saldos);
        Assert.Equal("EMP-999", r.Saldos[0].ApellidoNombre); // sin cruce local: muestra el id
        Assert.Null(r.Saldos[0].Area);
    }

    [Fact]
    public async Task Solicitud_en_curso_se_marca_con_hoy()
    {
        var svc = await SetupAsync(nameof(Solicitud_en_curso_se_marca_con_hoy),
            [],
            [
                new("EMP-1", "Vacaciones", new(2026, 8, 25), new(2026, 8, 29), "APPROVED", 5), // hoy 27/08 cae adentro
                new("EMP-2", "Vacaciones", new(2026, 9, 1), new(2026, 9, 5), "APPROVED", 5),
            ]);

        var r = await svc.ReporteAsync(new(2026, 8, 1), new(2026, 9, 30));

        Assert.True(r.Solicitudes.Single(s => s.EmployeeInternalId == "EMP-1").EnCurso);
        Assert.False(r.Solicitudes.Single(s => s.EmployeeInternalId == "EMP-2").EnCurso);
    }

    [Fact]
    public async Task Filtro_de_area_aplica_a_saldos_y_solicitudes()
    {
        var svc = await SetupAsync(nameof(Filtro_de_area_aplica_a_saldos_y_solicitudes),
            [new("EMP-1", "Vacaciones", 10), new("EMP-2", "Vacaciones", 12)],
            [new("EMP-1", "Vacaciones", new(2026, 8, 1), new(2026, 8, 5), "APPROVED", 5)]);

        var r = await svc.ReporteAsync(new(2026, 8, 1), new(2026, 8, 31), area: "Ventas");

        Assert.Single(r.Saldos);
        Assert.Equal("Paz, Bruno", r.Saldos[0].ApellidoNombre);
        Assert.Empty(r.Solicitudes); // la solicitud era de Producción
    }
}
