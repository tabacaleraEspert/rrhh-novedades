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

public class ParteServiceTests
{
    // DbContextFactory en memoria para los tests.
    private sealed class InMemoryFactory(string dbName) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            var opt = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(dbName).Options;
            return new AppDbContext(opt);
        }
    }

    private static async Task<(IParteService parte, InMemoryFactory factory)> SetupAsync(string db)
    {
        var factory = new InMemoryFactory(db);
        await using var ctx = factory.CreateDbContext();

        var emps = new[]
        {
            new Empleado { Id = 1, Nombre = "Juan",  Apellido = "Pérez", EmployeeInternalId = "1" },
            new Empleado { Id = 2, Nombre = "Rosa",  Apellido = "Gómez", EmployeeInternalId = "2" },
            new Empleado { Id = 3, Nombre = "Mario", Apellido = "Sosa",  EmployeeInternalId = "3" },
            new Empleado { Id = 4, Nombre = "Lucía", Apellido = "Díaz",  EmployeeInternalId = "4" },
        };
        ctx.Empleados.AddRange(emps);

        var hoy = new DateOnly(2026, 6, 9);
        ctx.Novedades.AddRange(
            new NovedadDiaria { EmpleadoId = 1, Fecha = hoy, Turno = Turno.Manana, Estado = EstadoJornada.Presente },
            new NovedadDiaria { EmpleadoId = 2, Fecha = hoy, Turno = Turno.Manana, Estado = EstadoJornada.Tarde },
            new NovedadDiaria { EmpleadoId = 3, Fecha = hoy, Turno = Turno.Manana, Estado = EstadoJornada.AusenteInjustificado },
            new NovedadDiaria { EmpleadoId = 4, Fecha = hoy, Turno = Turno.Manana, Estado = EstadoJornada.AusenteJustificado, MotivoNovedad = "Vacaciones" }
        );
        await ctx.SaveChangesAsync();

        var twilio = Substitute.For<ITwilioService>();
        var twOpt = Options.Create(new TwilioOptions());
        var parte = new ParteService(factory, twilio, twOpt, NullLogger<ParteService>.Instance);
        return (parte, factory);
    }

    [Fact]
    public async Task Parte_incluye_conteos_y_nombres_correctos()
    {
        var (parte, _) = await SetupAsync(nameof(Parte_incluye_conteos_y_nombres_correctos));
        var c = await parte.ArmarParteAsync(new DateOnly(2026, 6, 9), Turno.Manana);

        Assert.Contains("Turno Mañana", c.Encabezado);
        Assert.Contains("Presentes: 1", c.Cuerpo);
        Assert.Contains("Tardanzas: 1 (Gómez, Rosa)", c.Cuerpo);
        Assert.Contains("Ausentes: 1 (Sosa, Mario)", c.Cuerpo);
        Assert.Contains("Justificados: 1 (Díaz, Lucía)", c.Cuerpo);
    }

    [Fact]
    public async Task Parte_de_turno_sin_novedades_da_ceros()
    {
        var (parte, _) = await SetupAsync(nameof(Parte_de_turno_sin_novedades_da_ceros));
        var c = await parte.ArmarParteAsync(new DateOnly(2026, 6, 9), Turno.Tarde);

        Assert.Contains("Presentes: 0", c.Cuerpo);
        Assert.Contains("Ausentes: 0", c.Cuerpo);
    }

    [Fact]
    public async Task Variables_del_template_mapean_1_a_5()
    {
        var (parte, _) = await SetupAsync(nameof(Variables_del_template_mapean_1_a_5));
        var v = (await parte.ArmarParteAsync(new DateOnly(2026, 6, 9), Turno.Manana)).Variables;

        Assert.Equal(5, v.Count);
        Assert.Equal("Turno Mañana · 09/06/2026", v["1"]);
        Assert.Equal("1", v["2"]);                              // presentes (número)
        Assert.Equal("1 (Gómez, Rosa)", v["3"]);                // tardanzas
        Assert.Equal("1 (Sosa, Mario)", v["4"]);                // ausentes
        Assert.Equal("1 (Díaz, Lucía)", v["5"]);                // justificados
    }

    [Fact]
    public async Task Variable_de_lista_vacia_es_cero_no_vacio()
    {
        // WhatsApp rechaza variables vacías → debe ser "0", nunca "".
        var (parte, _) = await SetupAsync(nameof(Variable_de_lista_vacia_es_cero_no_vacio));
        var v = (await parte.ArmarParteAsync(new DateOnly(2026, 6, 9), Turno.Tarde)).Variables;

        Assert.Equal("0", v["3"]);
        Assert.Equal("0", v["4"]);
        Assert.Equal("0", v["5"]);
        Assert.DoesNotContain(v.Values, string.IsNullOrEmpty);
    }
}
