using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;
using RRHHNovedades.Web.Options;
using RRHHNovedades.Web.Services;
using Xunit;

namespace RRHHNovedades.Tests;

/// <summary>
/// El turno Noche NO se infiere por horario: lo define la segmentación "Turno" de Humand
/// (ej. "Turno C Noche"). Sin ella, un nocturno con inicio 22:00 caería como "Tarde".
/// </summary>
public class IngestaTurnoNocheTests
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

    private static readonly DateOnly Fecha = new(2026, 7, 27);

    private static (IngestaService ingesta, InMemoryFactory factory, IHumandService humand) Setup(string db)
    {
        var factory = new InMemoryFactory(db);
        var humand = Substitute.For<IHumandService>();
        var reloj = Substitute.For<IReloj>();
        reloj.Hoy.Returns(Fecha);
        reloj.Ahora.Returns(new DateTimeOffset(Fecha.ToDateTime(new TimeOnly(23, 0)), TimeSpan.FromHours(-3)));
        var ingesta = new IngestaService(factory, humand, Options.Create(new AsistenciaOptions()),
            reloj, NullLogger<IngestaService>.Instance);
        return (ingesta, factory, humand);
    }

    [Theory]
    [InlineData("Turno C Noche", true)]
    [InlineData("Vigilancia Turno Noche", true)] // ítem real agregado en Humand el 28-jul-2026
    [InlineData("turno c NOCHE", true)]
    [InlineData("Turno A - Cristian", false)]
    [InlineData("Turno B - Derlis", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void Segmentacion_nocturna_se_detecta_por_contener_noche(string? seg, bool esperado)
    {
        Assert.Equal(esperado, IngestaService.EsSegmentacionNocturna(seg));
    }

    [Fact]
    public async Task Empleado_segmentado_noche_queda_con_Turno_Noche_y_su_novedad_tambien()
    {
        var (ingesta, factory, humand) = Setup(nameof(Empleado_segmentado_noche_queda_con_Turno_Noche_y_su_novedad_tambien));
        humand.ObtenerEmpleadosAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new EmpleadoHumand("N-1", "Nadia", "Molina", null, "Producción", "Turno C Noche")
        ]);
        // Inicio teórico 22:00: por horario caería como Tarde (>= corte 13:00) — la segmentación debe ganar.
        humand.ObtenerJornadasAsync(Arg.Any<IEnumerable<string>>(), Fecha, Arg.Any<CancellationToken>()).Returns(
        [
            new JornadaHumand("N-1", Fecha, true, true, [], [], new TimeOnly(22, 5), null, new TimeOnly(22, 0))
        ]);

        await ingesta.SincronizarEmpleadosAsync();
        await ingesta.SincronizarDiaAsync(Fecha);

        await using var ctx = factory.CreateDbContext();
        var emp = await ctx.Empleados.SingleAsync();
        var nov = await ctx.Novedades.SingleAsync();
        Assert.Equal(Turno.Noche, emp.Turno);
        Assert.Equal(Turno.Noche, nov.Turno);
    }

    [Fact]
    public async Task Empleado_que_deja_la_segmentacion_nocturna_vuelve_a_inferencia_por_horario()
    {
        var (ingesta, factory, humand) = Setup(nameof(Empleado_que_deja_la_segmentacion_nocturna_vuelve_a_inferencia_por_horario));
        await using (var ctx = factory.CreateDbContext())
        {
            ctx.Empleados.Add(new Empleado { EmployeeInternalId = "N-1", Nombre = "Nadia", Apellido = "Molina", Turno = Turno.Noche });
            await ctx.SaveChangesAsync();
        }
        humand.ObtenerEmpleadosAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new EmpleadoHumand("N-1", "Nadia", "Molina", null, "Producción", "Turno A - Cristian")
        ]);

        await ingesta.SincronizarEmpleadosAsync();

        await using var check = factory.CreateDbContext();
        var emp = await check.Empleados.SingleAsync();
        Assert.NotEqual(Turno.Noche, emp.Turno);
    }
}
