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
/// Bug real (backfill 26-ago-2026): un empleado con DOS licencias manuales cuyos rangos se
/// superponen en la misma fecha rompía la ingesta con "An item with the same key has already
/// been added" al armar el diccionario por empleado. Debe tolerarse: gana la de Desde más reciente.
/// </summary>
public class IngestaLicenciasSuperpuestasTests
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

    private static readonly DateOnly Fecha = new(2026, 7, 1);

    [Fact]
    public async Task Dos_licencias_manuales_superpuestas_no_rompen_y_gana_la_mas_reciente()
    {
        var factory = new InMemoryFactory(nameof(Dos_licencias_manuales_superpuestas_no_rompen_y_gana_la_mas_reciente));
        var humand = Substitute.For<IHumandService>();
        var reloj = Substitute.For<IReloj>();
        reloj.Hoy.Returns(new DateOnly(2026, 8, 26));
        reloj.Ahora.Returns(new DateTimeOffset(2026, 8, 26, 5, 0, 0, TimeSpan.FromHours(-3)));

        await using (var ctx = factory.CreateDbContext())
        {
            var emp = new Empleado { EmployeeInternalId = "E-1", Nombre = "Juan", Apellido = "Perez", Activo = true };
            ctx.Empleados.Add(emp);
            await ctx.SaveChangesAsync();
            ctx.LicenciasManuales.Add(new LicenciaManual { EmpleadoId = emp.Id, Desde = new DateOnly(2026, 6, 1), Hasta = null, Motivo = "Reserva de puesto" });
            ctx.LicenciasManuales.Add(new LicenciaManual { EmpleadoId = emp.Id, Desde = new DateOnly(2026, 6, 20), Hasta = new DateOnly(2026, 7, 10), Motivo = "Licencia gremial" });
            await ctx.SaveChangesAsync();
        }

        // Día laborable, sin fichada y sin permiso en Humand ⇒ caería injustificado ⇒ aplica la manual.
        humand.ObtenerJornadasAsync(Arg.Any<IEnumerable<string>>(), Fecha, Arg.Any<CancellationToken>()).Returns(
        [
            new JornadaHumand("E-1", Fecha, true, true, [], [], null, null, new TimeOnly(8, 0))
        ]);

        var ingesta = new IngestaService(factory, humand, Options.Create(new AsistenciaOptions()),
            reloj, NullLogger<IngestaService>.Instance);
        await ingesta.SincronizarDiaAsync(Fecha);

        await using var check = factory.CreateDbContext();
        var nov = await check.Novedades.SingleAsync();
        Assert.Equal(EstadoJornada.AusenteJustificado, nov.Estado);
        Assert.True(nov.EsManual);
        Assert.Equal("Licencia gremial", nov.MotivoNovedad); // Desde más reciente gana
    }
}
