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
/// Empleados dados de BAJA (caso real, sep-2026: FELICE seguía apareciendo en Ausentismo
/// meses después de irse). Dos formas de baja en Humand: status DEACTIVATED, o directamente
/// borrado de /users. Ambas ⇒ Activo = false, ocultos SIEMPRE (sin configuración), sin
/// ingesta diaria ni parte de WhatsApp.
/// </summary>
public class EmpleadosBajaTests
{
    private sealed class InMemoryFactory(string dbName, VisibilidadEmpleados? vis) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            var opt = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;
            return new AppDbContext(opt, vis);
        }
    }

    private static readonly DateOnly Fecha = new(2026, 9, 28);

    private static IngestaService NuevaIngesta(IDbContextFactory<AppDbContext> factory, IHumandService humand)
    {
        var reloj = Substitute.For<IReloj>();
        reloj.Hoy.Returns(Fecha);
        reloj.Ahora.Returns(new DateTimeOffset(Fecha.ToDateTime(new TimeOnly(15, 0)), TimeSpan.FromHours(-3)));
        return new IngestaService(factory, humand, Options.Create(new AsistenciaOptions()), reloj, NullLogger<IngestaService>.Instance);
    }

    [Theory]
    [InlineData("DEACTIVATED", true)]
    [InlineData("deactivated", true)]
    [InlineData("ACTIVE", false)]
    [InlineData("UNCLAIMED", false)] // todavía no reclamó la cuenta, pero es empleado vigente
    [InlineData(null, false)]
    public void Baja_es_solo_status_DEACTIVATED(string? status, bool esperado)
    {
        var r = new EmpleadoHumand("X", "Juan", "Pérez", null, null, Status: status);
        Assert.Equal(esperado, IngestaService.EsBaja(r));
    }

    [Fact]
    public async Task Sync_da_de_baja_al_DEACTIVATED_y_al_borrado_de_Humand_y_reactiva_si_vuelve()
    {
        var factory = new InMemoryFactory(nameof(Sync_da_de_baja_al_DEACTIVATED_y_al_borrado_de_Humand_y_reactiva_si_vuelve), new VisibilidadEmpleados());
        var humand = Substitute.For<IHumandService>();
        var ingesta = NuevaIngesta(factory, humand);

        // Primera sync: los tres vigentes.
        humand.ObtenerEmpleadosAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new EmpleadoHumand("E-1", "Cristian Ariel", "Felice", null, "Producción", Status: "ACTIVE"),
            new EmpleadoHumand("E-2", "Ariel", "Muñoz", null, "Producción", Status: "ACTIVE"),
            new EmpleadoHumand("E-3", "Gabriel", "Bustamante", null, "Producción", Status: "ACTIVE"),
        ]);
        await ingesta.SincronizarEmpleadosAsync();

        // Segunda sync: Muñoz pasa a DEACTIVATED y Felice fue borrado de Humand.
        humand.ObtenerEmpleadosAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new EmpleadoHumand("E-2", "Ariel", "Muñoz", null, "Producción", Status: "DEACTIVATED"),
            new EmpleadoHumand("E-3", "Gabriel", "Bustamante", null, "Producción", Status: "ACTIVE"),
        ]);
        await ingesta.SincronizarEmpleadosAsync();

        await using (var db = factory.CreateDbContext())
        {
            var todos = await db.Empleados.IgnoreQueryFilters().OrderBy(e => e.EmployeeInternalId).ToListAsync();
            Assert.False(todos[0].Activo); // borrado de /users
            Assert.False(todos[1].Activo); // DEACTIVATED
            Assert.True(todos[2].Activo);
        }

        // Tercera sync: Muñoz reaparece ACTIVE (reincorporación) ⇒ vuelve a Activo.
        humand.ObtenerEmpleadosAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new EmpleadoHumand("E-2", "Ariel", "Muñoz", null, "Producción", Status: "ACTIVE"),
            new EmpleadoHumand("E-3", "Gabriel", "Bustamante", null, "Producción", Status: "ACTIVE"),
        ]);
        await ingesta.SincronizarEmpleadosAsync();
        await using (var db2 = factory.CreateDbContext())
            Assert.True((await db2.Empleados.IgnoreQueryFilters().SingleAsync(e => e.EmployeeInternalId == "E-2")).Activo);
    }

    [Fact]
    public async Task Filtro_global_oculta_bajas_siempre_aunque_MostrarBloqueados_este_activado()
    {
        var vis = new VisibilidadEmpleados { MostrarBloqueados = true };
        var factory = new InMemoryFactory(nameof(Filtro_global_oculta_bajas_siempre_aunque_MostrarBloqueados_este_activado), vis);
        await using (var db = factory.CreateDbContext())
        {
            db.Empleados.AddRange(
                new Empleado { Id = 1, Nombre = "Cristian", Apellido = "Felice ⛔", EmployeeInternalId = "1", Bloqueado = true, Activo = false },
                new Empleado { Id = 2, Nombre = "Gabriel", Apellido = "Bustamante", EmployeeInternalId = "2" });
            db.Novedades.AddRange(
                new NovedadDiaria { EmpleadoId = 1, Fecha = Fecha, Turno = Turno.Tarde, Estado = EstadoJornada.AusenteJustificado, MotivoNovedad = "Dia gremial" },
                new NovedadDiaria { EmpleadoId = 2, Fecha = Fecha, Turno = Turno.Tarde, Estado = EstadoJornada.AusenteInjustificado });
            db.LicenciasManuales.Add(new LicenciaManual { EmpleadoId = 1, Desde = Fecha, Motivo = "Reserva de puesto", CreadaPor = "test" });
            await db.SaveChangesAsync();
        }

        await using (var db = factory.CreateDbContext())
        {
            Assert.Equal(1, await db.Empleados.CountAsync());
            Assert.Equal(1, await db.Novedades.CountAsync());
            Assert.Equal(0, await db.LicenciasManuales.CountAsync());
            Assert.Equal(2, await db.Empleados.IgnoreQueryFilters().CountAsync()); // la ingesta sí lo ve
        }
    }

    [Fact]
    public async Task Ingesta_diaria_no_registra_novedades_de_bajas()
    {
        var factory = new InMemoryFactory(nameof(Ingesta_diaria_no_registra_novedades_de_bajas), new VisibilidadEmpleados());
        var humand = Substitute.For<IHumandService>();
        var ingesta = NuevaIngesta(factory, humand);

        await using (var db = factory.CreateDbContext())
        {
            db.Empleados.AddRange(
                new Empleado { Id = 1, Nombre = "Cristian", Apellido = "Felice", EmployeeInternalId = "E-1", Activo = false },
                new Empleado { Id = 2, Nombre = "Gabriel", Apellido = "Bustamante", EmployeeInternalId = "E-2" });
            await db.SaveChangesAsync();
        }
        humand.ObtenerJornadasAsync(Arg.Any<IEnumerable<string>>(), Fecha, Arg.Any<CancellationToken>()).Returns(
        [
            new JornadaHumand("E-1", Fecha, true, true, ["ABSENT"], [], null, null, new TimeOnly(14, 0)),
            new JornadaHumand("E-2", Fecha, true, true, [], [], new TimeOnly(13, 55), null, new TimeOnly(14, 0)),
        ]);

        await ingesta.SincronizarDiaAsync(Fecha);

        await using (var db = factory.CreateDbContext())
        {
            Assert.Equal(1, await db.Novedades.IgnoreQueryFilters().CountAsync(n => n.Fecha == Fecha));
            // Solo se pidieron a Humand las jornadas de los activos.
            await humand.Received(1).ObtenerJornadasAsync(
                Arg.Is<IEnumerable<string>>(ids => ids.Single() == "E-2"), Fecha, Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Parte_de_WhatsApp_excluye_bajas()
    {
        var vis = new VisibilidadEmpleados { MostrarBloqueados = true };
        var factory = new InMemoryFactory(nameof(Parte_de_WhatsApp_excluye_bajas), vis);
        await using (var db = factory.CreateDbContext())
        {
            db.Empleados.AddRange(
                new Empleado { Id = 1, Nombre = "Cristian", Apellido = "Felice", EmployeeInternalId = "1", Activo = false },
                new Empleado { Id = 2, Nombre = "Gabriel Oscar", Apellido = "Bustamante", EmployeeInternalId = "2" });
            db.Novedades.AddRange(
                new NovedadDiaria { EmpleadoId = 1, Fecha = Fecha, Turno = Turno.Tarde, Estado = EstadoJornada.AusenteInjustificado },
                new NovedadDiaria { EmpleadoId = 2, Fecha = Fecha, Turno = Turno.Tarde, Estado = EstadoJornada.Presente });
            await db.SaveChangesAsync();
        }

        var parte = new ParteService(factory, Substitute.For<ITwilioService>(), Options.Create(new TwilioOptions()), NullLogger<ParteService>.Instance);
        var c = await parte.ArmarParteAsync(Fecha, Turno.Tarde);

        Assert.DoesNotContain("Felice", c.Completo);
        Assert.Contains("Presentes: 1", c.Completo);
        Assert.Contains("Ausentes: 0", c.Completo);
    }
}
