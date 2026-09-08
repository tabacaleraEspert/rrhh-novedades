using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
/// Empleados "bloqueados": RRHH les pone ⛔ al nombre en Humand (caso real, sep-2026: "FELICE ⛔").
/// Regla: nunca salen en el parte de WhatsApp; en el resto de la app se muestran u ocultan según
/// ConfiguracionParte.MostrarBloqueados (filtro global del DbContext).
/// </summary>
public class EmpleadosBloqueadosTests
{
    private sealed class InMemoryFactory(string dbName, VisibilidadEmpleados? vis) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            var opt = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;
            return new AppDbContext(opt, vis);
        }
    }

    private static readonly DateOnly Fecha = new(2026, 9, 3);

    [Theory]
    [InlineData("FELICE ⛔", "CRISTIAN ARIEL", true)]
    [InlineData("FELICE", "CRISTIAN ⛔", true)]
    [InlineData("🚫 PÉREZ", "JUAN", true)]
    [InlineData("PÉREZ", "JUAN", false)]
    [InlineData("", "", false)]
    public void Marca_de_bloqueo_se_detecta_en_nombre_o_apellido(string apellido, string nombre, bool esperado)
    {
        var r = new EmpleadoHumand("X", nombre, apellido, null, null);
        Assert.Equal(esperado, IngestaService.EsBloqueado(r));
    }

    [Fact]
    public async Task Sync_marca_y_desmarca_Bloqueado_y_sigue_ingestando_su_dia()
    {
        var vis = new VisibilidadEmpleados { MostrarBloqueados = false };
        var factory = new InMemoryFactory(nameof(Sync_marca_y_desmarca_Bloqueado_y_sigue_ingestando_su_dia), vis);
        var humand = Substitute.For<IHumandService>();
        var reloj = Substitute.For<IReloj>();
        reloj.Hoy.Returns(Fecha);
        reloj.Ahora.Returns(new DateTimeOffset(Fecha.ToDateTime(new TimeOnly(15, 0)), TimeSpan.FromHours(-3)));
        var ingesta = new IngestaService(factory, humand, Options.Create(new AsistenciaOptions()), reloj, NullLogger<IngestaService>.Instance);

        humand.ObtenerEmpleadosAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new EmpleadoHumand("E-1", "Cristian Ariel", "Felice ⛔", null, "Producción"),
            new EmpleadoHumand("E-2", "Gabriel", "Bustamante", null, "Producción"),
        ]);
        humand.ObtenerJornadasAsync(Arg.Any<IEnumerable<string>>(), Fecha, Arg.Any<CancellationToken>()).Returns(
        [
            new JornadaHumand("E-1", Fecha, true, true, ["ABSENT"], [], null, null, new TimeOnly(14, 0)),
            new JornadaHumand("E-2", Fecha, true, true, [], [], new TimeOnly(13, 55), null, new TimeOnly(14, 0)),
        ]);

        await ingesta.SincronizarEmpleadosAsync();
        await ingesta.SincronizarDiaAsync(Fecha);

        await using (var db = factory.CreateDbContext())
        {
            var todos = await db.Empleados.IgnoreQueryFilters().OrderBy(e => e.EmployeeInternalId).ToListAsync();
            Assert.True(todos[0].Bloqueado);
            Assert.False(todos[1].Bloqueado);
            // La ingesta sigue registrando el día del bloqueado (para poder mostrarlo si se pide).
            Assert.Equal(2, await db.Novedades.IgnoreQueryFilters().CountAsync(n => n.Fecha == Fecha));
        }

        // Segunda sync: sin marca, ni se duplica ni queda bloqueado.
        humand.ObtenerEmpleadosAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new EmpleadoHumand("E-1", "Cristian Ariel", "Felice", null, "Producción"),
            new EmpleadoHumand("E-2", "Gabriel", "Bustamante", null, "Producción"),
        ]);
        await ingesta.SincronizarEmpleadosAsync();
        await using (var db = factory.CreateDbContext())
        {
            Assert.Equal(2, await db.Empleados.IgnoreQueryFilters().CountAsync());
            Assert.False((await db.Empleados.IgnoreQueryFilters().SingleAsync(e => e.EmployeeInternalId == "E-1")).Bloqueado);
        }
    }

    [Fact]
    public async Task Filtro_global_oculta_o_muestra_bloqueados_segun_configuracion()
    {
        var vis = new VisibilidadEmpleados { MostrarBloqueados = false };
        var factory = new InMemoryFactory(nameof(Filtro_global_oculta_o_muestra_bloqueados_segun_configuracion), vis);
        await using (var db = factory.CreateDbContext())
        {
            db.Empleados.AddRange(
                new Empleado { Id = 1, Nombre = "Cristian", Apellido = "Felice ⛔", EmployeeInternalId = "1", Bloqueado = true },
                new Empleado { Id = 2, Nombre = "Gabriel", Apellido = "Bustamante", EmployeeInternalId = "2" });
            db.Novedades.AddRange(
                new NovedadDiaria { EmpleadoId = 1, Fecha = Fecha, Turno = Turno.Tarde, Estado = EstadoJornada.AusenteInjustificado },
                new NovedadDiaria { EmpleadoId = 2, Fecha = Fecha, Turno = Turno.Tarde, Estado = EstadoJornada.Presente });
            db.LicenciasManuales.Add(new LicenciaManual { EmpleadoId = 1, Desde = Fecha, Motivo = "Reserva de puesto", CreadaPor = "test" });
            await db.SaveChangesAsync();
        }

        await using (var db = factory.CreateDbContext())
        {
            Assert.Equal(1, await db.Empleados.CountAsync());
            Assert.Equal(1, await db.Novedades.CountAsync());
            Assert.Equal(0, await db.LicenciasManuales.CountAsync());
            Assert.Equal(2, await db.Empleados.IgnoreQueryFilters().CountAsync());
        }

        vis.MostrarBloqueados = true; // cambia en caliente, sin recrear nada
        await using (var db = factory.CreateDbContext())
        {
            Assert.Equal(2, await db.Empleados.CountAsync());
            Assert.Equal(2, await db.Novedades.CountAsync());
            Assert.Equal(1, await db.LicenciasManuales.CountAsync());
        }
    }

    [Fact]
    public async Task DbContextFactory_de_DI_inyecta_la_visibilidad_al_contexto()
    {
        // Mismo cableado que ServiceCollectionExtensions: AddDbContextFactory + singleton VisibilidadEmpleados.
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseInMemoryDatabase(nameof(DbContextFactory_de_DI_inyecta_la_visibilidad_al_contexto)));
        services.AddSingleton<VisibilidadEmpleados>();
        await using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var vis = sp.GetRequiredService<VisibilidadEmpleados>();

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Empleados.Add(new Empleado { Id = 1, Nombre = "X", Apellido = "Y ⛔", EmployeeInternalId = "1", Bloqueado = true });
            await db.SaveChangesAsync();
        }
        vis.MostrarBloqueados = false;
        await using (var db = await factory.CreateDbContextAsync())
            Assert.Equal(0, await db.Empleados.CountAsync());
        vis.MostrarBloqueados = true;
        await using (var db = await factory.CreateDbContextAsync())
            Assert.Equal(1, await db.Empleados.CountAsync());
    }

    [Fact]
    public async Task Parte_de_WhatsApp_excluye_bloqueados_aunque_se_muestren_en_la_app()
    {
        var vis = new VisibilidadEmpleados { MostrarBloqueados = true };
        var factory = new InMemoryFactory(nameof(Parte_de_WhatsApp_excluye_bloqueados_aunque_se_muestren_en_la_app), vis);
        await using (var db = factory.CreateDbContext())
        {
            db.Empleados.AddRange(
                new Empleado { Id = 1, Nombre = "Cristian Ariel", Apellido = "Felice ⛔", EmployeeInternalId = "1", Bloqueado = true },
                new Empleado { Id = 2, Nombre = "Gabriel Oscar", Apellido = "Bustamante", EmployeeInternalId = "2" },
                new Empleado { Id = 3, Nombre = "Matias", Apellido = "Bocchieri", EmployeeInternalId = "3" },
                new Empleado { Id = 4, Nombre = "Pedro", Apellido = "Bloqueado ⛔", EmployeeInternalId = "4", Bloqueado = true });
            db.Novedades.AddRange(
                new NovedadDiaria { EmpleadoId = 1, Fecha = Fecha, Turno = Turno.Tarde, Estado = EstadoJornada.AusenteInjustificado },
                new NovedadDiaria { EmpleadoId = 2, Fecha = Fecha, Turno = Turno.Tarde, Estado = EstadoJornada.AusenteJustificado, MotivoNovedad = "Vacaciones" },
                new NovedadDiaria { EmpleadoId = 3, Fecha = Fecha, Turno = Turno.Tarde, Estado = EstadoJornada.Presente },
                new NovedadDiaria { EmpleadoId = 4, Fecha = Fecha, Turno = Turno.Tarde, Estado = EstadoJornada.Presente });
            await db.SaveChangesAsync();
        }

        var parte = new ParteService(factory, Substitute.For<ITwilioService>(), Options.Create(new TwilioOptions()), NullLogger<ParteService>.Instance);
        var c = await parte.ArmarParteAsync(Fecha, Turno.Tarde);

        Assert.DoesNotContain("Felice", c.Completo);
        Assert.DoesNotContain("⛔", c.Completo);
        Assert.Contains("Presentes: 1", c.Completo);        // el bloqueado presente tampoco cuenta
        Assert.Contains("Ausentes: 0", c.Completo);
        Assert.Contains("Justificados: 1 (Bustamante, Gabriel Oscar)", c.Completo);
        Assert.Equal("0", c.Variables["4"]);

        vis.MostrarBloqueados = false; // con la app ocultándolos el resultado es el mismo
        var c2 = await parte.ArmarParteAsync(Fecha, Turno.Tarde);
        Assert.Equal(c.Completo, c2.Completo);
    }
}
