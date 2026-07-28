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
/// Regla RRHH (28-jul-2026): un "Franco" de alguien que NO ficha nunca (ventas, oficinas,
/// dirección: sin horario en Humand) en día hábil no feriado es Presente, no Franco.
/// Los fichadores conservan sus francos rotativos; findes y feriados siguen Franco.
/// </summary>
public class PresentePorNoFichajeTests
{
    private static readonly DateOnly Martes = new(2026, 7, 28);
    private static readonly DateOnly Domingo = new(2026, 7, 26);
    private static readonly DateOnly Feriado = new(2026, 7, 9);

    private static JornadaHumand Franco(DateOnly fecha, bool esFeriado = false) =>
        new("E-1", fecha, false, false, [], [], null, null, null, esFeriado);

    [Fact]
    public void No_fichador_en_dia_habil_es_Presente()
    {
        Assert.True(IngestaService.EsPresentePorNoFichaje(Martes, Franco(Martes), [], esFichador: false));
    }

    [Fact]
    public void Fichador_conserva_su_franco_rotativo()
    {
        Assert.False(IngestaService.EsPresentePorNoFichaje(Martes, Franco(Martes), [], esFichador: true));
    }

    [Fact]
    public void Fin_de_semana_sigue_siendo_Franco()
    {
        Assert.False(IngestaService.EsPresentePorNoFichaje(Domingo, Franco(Domingo), [], esFichador: false));
    }

    [Fact]
    public void Feriado_configurado_o_de_Humand_sigue_siendo_Franco()
    {
        Assert.False(IngestaService.EsPresentePorNoFichaje(Feriado, Franco(Feriado), [Feriado], esFichador: false));
        Assert.False(IngestaService.EsPresentePorNoFichaje(Feriado, Franco(Feriado, esFeriado: true), [], esFichador: false));
    }

    [Fact]
    public async Task En_la_ingesta_el_franco_del_no_fichador_queda_Presente_y_el_del_fichador_no()
    {
        var factory = new InMemoryFactory(nameof(En_la_ingesta_el_franco_del_no_fichador_queda_Presente_y_el_del_fichador_no));
        await using (var ctx = factory.CreateDbContext())
        {
            ctx.Empleados.AddRange(
                new Empleado { Id = 1, Nombre = "Juan", Apellido = "Ventas", EmployeeInternalId = "V-1" },
                new Empleado { Id = 2, Nombre = "Rosa", Apellido = "Planta", EmployeeInternalId = "P-1" });
            // Planta fichó hace una semana ⇒ es fichadora; Ventas no fichó nunca.
            ctx.Novedades.Add(new NovedadDiaria { EmpleadoId = 2, Fecha = Martes.AddDays(-7), Estado = EstadoJornada.Presente, HoraEntrada = new TimeOnly(6, 0) });
            await ctx.SaveChangesAsync();
        }

        var humand = Substitute.For<IHumandService>();
        humand.ObtenerJornadasAsync(Arg.Any<IEnumerable<string>>(), Martes, Arg.Any<CancellationToken>()).Returns(
        [
            new JornadaHumand("V-1", Martes, false, false, [], [], null, null, null),
            new JornadaHumand("P-1", Martes, false, false, [], [], null, null, null)
        ]);
        var reloj = Substitute.For<IReloj>();
        reloj.Hoy.Returns(Martes);
        reloj.Ahora.Returns(new DateTimeOffset(Martes.ToDateTime(new TimeOnly(23, 0)), TimeSpan.FromHours(-3)));

        var ingesta = new IngestaService(factory, humand, Options.Create(new AsistenciaOptions()),
            reloj, NullLogger<IngestaService>.Instance);
        await ingesta.SincronizarDiaAsync(Martes);

        await using var check = factory.CreateDbContext();
        Assert.Equal(EstadoJornada.Presente, (await check.Novedades.SingleAsync(n => n.EmpleadoId == 1 && n.Fecha == Martes)).Estado);
        Assert.Equal(EstadoJornada.FrancoNoLaborable, (await check.Novedades.SingleAsync(n => n.EmpleadoId == 2 && n.Fecha == Martes)).Estado);
    }

    private sealed class InMemoryFactory(string dbName) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            var opt = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(dbName).Options;
            return new AppDbContext(opt);
        }
    }
}
