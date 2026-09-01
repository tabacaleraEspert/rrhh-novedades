using Microsoft.EntityFrameworkCore;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;
using RRHHNovedades.Web.Services;
using Xunit;

namespace RRHHNovedades.Tests;

/// <summary>
/// Sección Tardanzas (nueva, ago-2026). La tardanza la marca Humand (LATE) y ya está en
/// Novedades; acá se congela la agregación: minutos, orden y sobre todo la RACHA, que
/// cuenta jornadas TRABAJADAS consecutivas llegando tarde (franco/feriado en el medio no
/// corta; un día presente en hora sí).
/// </summary>
public class TardanzasServiceTests
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

    private static NovedadDiaria Dia(int empleadoId, DateOnly fecha, EstadoJornada e, int minutos = 0) =>
        new() { EmpleadoId = empleadoId, Fecha = fecha, Estado = e, MinutosTardanza = minutos };

    private static async Task<TardanzasService> SetupAsync(string db, params NovedadDiaria[] novedades)
    {
        var factory = new InMemoryFactory(db);
        await using var ctx = factory.CreateDbContext();
        ctx.Empleados.AddRange(
            new Empleado { Id = 1, Nombre = "Nadia", Apellido = "Molina", Area = "Producción", EmployeeInternalId = "1", Legajo = "871" },
            new Empleado { Id = 2, Nombre = "Bruno", Apellido = "Paz", Area = "Ventas", EmployeeInternalId = "2", Legajo = "455" });
        ctx.Novedades.AddRange(novedades);
        await ctx.SaveChangesAsync();
        return new TardanzasService(factory);
    }

    // ── Racha (estático, sin DB) ──

    [Fact]
    public void Racha_dia_en_hora_corta_la_racha()
    {
        var r = TardanzasService.RachaMaxima(
        [
            (new DateOnly(2026, 8, 3), true),
            (new DateOnly(2026, 8, 4), true),
            (new DateOnly(2026, 8, 5), false), // llegó en hora: corta
            (new DateOnly(2026, 8, 6), true),
        ]);
        Assert.Equal(2, r);
    }

    [Fact]
    public void Racha_franco_en_el_medio_no_corta()
    {
        // Vie 07/08 tarde, franco s/d (sin fila: no es jornada trabajada), lun 10/08 tarde.
        var r = TardanzasService.RachaMaxima(
        [
            (new DateOnly(2026, 8, 7), true),
            (new DateOnly(2026, 8, 10), true),
        ]);
        Assert.Equal(2, r);
    }

    [Fact]
    public void Racha_desordenada_se_ordena_por_fecha()
    {
        var r = TardanzasService.RachaMaxima(
        [
            (new DateOnly(2026, 8, 6), true),
            (new DateOnly(2026, 8, 4), true),
            (new DateOnly(2026, 8, 5), false),
        ]);
        Assert.Equal(1, r);
    }

    [Fact]
    public void Racha_sin_tardanzas_es_cero()
    {
        Assert.Equal(0, TardanzasService.RachaMaxima(
            [(new DateOnly(2026, 8, 4), false), (new DateOnly(2026, 8, 5), false)]));
    }

    // ── Reporte ──

    [Fact]
    public async Task Reporte_agrega_minutos_y_ordena_por_minutos_total()
    {
        var svc = await SetupAsync(nameof(Reporte_agrega_minutos_y_ordena_por_minutos_total),
            Dia(1, new(2026, 8, 3), EstadoJornada.Tarde, 10),
            Dia(1, new(2026, 8, 4), EstadoJornada.Tarde, 5),
            Dia(2, new(2026, 8, 3), EstadoJornada.Tarde, 40));

        var r = await svc.ReporteAsync(new(2026, 8, 1), new(2026, 8, 31));

        Assert.Equal(2, r.PorEmpleado.Count);
        Assert.Equal("Paz, Bruno", r.PorEmpleado[0].ApellidoNombre); // 40 min > 15 min
        Assert.Equal(40, r.PorEmpleado[0].MinutosTotal);
        Assert.Equal(15, r.PorEmpleado[1].MinutosTotal);
        Assert.Equal(2, r.PorEmpleado[1].Cantidad);
        Assert.Equal(7.5, r.PorEmpleado[1].MinutosPromedio);
    }

    [Fact]
    public async Task Reporte_excluye_a_quien_no_llego_tarde_y_cuenta_fichajes()
    {
        var svc = await SetupAsync(nameof(Reporte_excluye_a_quien_no_llego_tarde_y_cuenta_fichajes),
            Dia(1, new(2026, 8, 3), EstadoJornada.Tarde, 10),
            Dia(2, new(2026, 8, 3), EstadoJornada.Presente),
            Dia(2, new(2026, 8, 4), EstadoJornada.Presente));

        var r = await svc.ReporteAsync(new(2026, 8, 1), new(2026, 8, 31));

        Assert.Single(r.PorEmpleado); // Bruno nunca llegó tarde: no aparece
        Assert.Equal(3, r.JornadasConFichaje); // pero sus presentes sí cuentan en el denominador
    }

    [Fact]
    public async Task Reporte_fuera_de_rango_y_ausencias_no_cuentan()
    {
        var svc = await SetupAsync(nameof(Reporte_fuera_de_rango_y_ausencias_no_cuentan),
            Dia(1, new(2026, 7, 31), EstadoJornada.Tarde, 10),            // fuera del rango
            Dia(1, new(2026, 8, 4), EstadoJornada.AusenteInjustificado)); // no es tardanza

        var r = await svc.ReporteAsync(new(2026, 8, 1), new(2026, 8, 31));

        Assert.Empty(r.PorEmpleado);
    }

    [Fact]
    public async Task Reporte_filtra_por_area()
    {
        var svc = await SetupAsync(nameof(Reporte_filtra_por_area),
            Dia(1, new(2026, 8, 3), EstadoJornada.Tarde, 10),
            Dia(2, new(2026, 8, 3), EstadoJornada.Tarde, 20));

        var r = await svc.ReporteAsync(new(2026, 8, 1), new(2026, 8, 31), area: "Ventas");

        Assert.Single(r.PorEmpleado);
        Assert.Equal("Paz, Bruno", r.PorEmpleado[0].ApellidoNombre);
    }

    [Fact]
    public async Task Racha_en_reporte_presente_corta_franco_no()
    {
        // Lun tarde, mar presente (corta), jue tarde, vie tarde (miércoles sin fila = franco, no corta).
        var svc = await SetupAsync(nameof(Racha_en_reporte_presente_corta_franco_no),
            Dia(1, new(2026, 8, 3), EstadoJornada.Tarde, 5),
            Dia(1, new(2026, 8, 4), EstadoJornada.Presente),
            Dia(1, new(2026, 8, 6), EstadoJornada.Tarde, 5),
            Dia(1, new(2026, 8, 7), EstadoJornada.Tarde, 5));

        var r = await svc.ReporteAsync(new(2026, 8, 1), new(2026, 8, 31));

        Assert.Equal(2, r.PorEmpleado[0].RachaMaxima);
    }
}
