using Microsoft.EntityFrameworkCore;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;
using RRHHNovedades.Web.Services;
using Xunit;

namespace RRHHNovedades.Tests;

/// <summary>
/// Queries que alimentan las herramientas del asistente IA: historial por empleado,
/// tardanzas (no existe reporte fuera del asistente), ausencias por persona y cobertura
/// de datos (anti "mentir por omisión" cuando un período no tiene backfill).
/// </summary>
public class ConsultaAsistenteServiceTests
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

    private static NovedadDiaria Dia(int empleadoId, DateOnly fecha, EstadoJornada e,
        string? motivo = null, bool feriado = false, int minutosTardanza = 0) =>
        new()
        {
            EmpleadoId = empleadoId, Fecha = fecha, Estado = e, MotivoNovedad = motivo,
            EsFeriado = feriado, MinutosTardanza = minutosTardanza
        };

    private static async Task<ConsultaAsistenteService> SetupAsync(string db, params NovedadDiaria[] novedades)
    {
        var factory = new InMemoryFactory(db);
        await using var ctx = factory.CreateDbContext();
        ctx.Empleados.AddRange(
            new Empleado { Id = 1, Nombre = "Nadia", Apellido = "Molina", Area = "Producción", EmployeeInternalId = "1", Legajo = "871" },
            new Empleado { Id = 2, Nombre = "Bruno", Apellido = "Paz", Area = "Ventas", EmployeeInternalId = "2", Legajo = "455" });
        ctx.Novedades.AddRange(novedades);
        await ctx.SaveChangesAsync();
        return new ConsultaAsistenteService(factory);
    }

    // ── Historial ──

    [Fact]
    public async Task Historial_devuelve_los_dias_del_rango_ordenados()
    {
        var svc = await SetupAsync(nameof(Historial_devuelve_los_dias_del_rango_ordenados),
            Dia(1, new(2026, 7, 15), EstadoJornada.AusenteJustificado, "Vacaciones"),
            Dia(1, new(2026, 7, 14), EstadoJornada.Tarde, minutosTardanza: 12),
            Dia(1, new(2026, 7, 20), EstadoJornada.Presente),   // fuera del rango
            Dia(2, new(2026, 7, 15), EstadoJornada.Presente));  // otro empleado

        var h = await svc.HistorialAsync(1, new(2026, 7, 14), new(2026, 7, 16));

        Assert.NotNull(h);
        Assert.Equal("Molina, Nadia", h.ApellidoNombre);
        Assert.Equal(2, h.Dias.Count);
        Assert.Equal(new DateOnly(2026, 7, 14), h.Dias[0].Fecha);
        Assert.Equal(12, h.Dias[0].MinutosTardanza);
        Assert.Equal("Vacaciones", h.Dias[1].Motivo);
    }

    [Fact]
    public async Task Historial_de_empleado_inexistente_devuelve_null()
    {
        var svc = await SetupAsync(nameof(Historial_de_empleado_inexistente_devuelve_null));
        Assert.Null(await svc.HistorialAsync(99, new(2026, 7, 1), new(2026, 7, 31)));
    }

    // ── Tardanzas ──

    [Fact]
    public async Task Tardanzas_agrupa_por_persona_y_suma_minutos()
    {
        var svc = await SetupAsync(nameof(Tardanzas_agrupa_por_persona_y_suma_minutos),
            Dia(1, new(2026, 7, 1), EstadoJornada.Tarde, minutosTardanza: 10),
            Dia(1, new(2026, 7, 3), EstadoJornada.Tarde, minutosTardanza: 25),
            Dia(2, new(2026, 7, 2), EstadoJornada.Tarde, minutosTardanza: 5),
            Dia(2, new(2026, 7, 4), EstadoJornada.Presente),           // presente no cuenta
            Dia(1, new(2026, 7, 5), EstadoJornada.AusenteInjustificado)); // ausencia no cuenta

        var t = await svc.TardanzasAsync(new(2026, 7, 1), new(2026, 7, 31));

        Assert.Equal(2, t.Count);
        Assert.Equal(1, t[0].EmpleadoId); // más minutos primero
        Assert.Equal(2, t[0].Dias);
        Assert.Equal(35, t[0].MinutosTotales);
        Assert.Equal(new DateOnly(2026, 7, 3), t[0].UltimaFecha);
        Assert.Equal(5, t[1].MinutosTotales);
    }

    [Fact]
    public async Task Tardanzas_filtra_por_area_y_por_empleado()
    {
        var svc = await SetupAsync(nameof(Tardanzas_filtra_por_area_y_por_empleado),
            Dia(1, new(2026, 7, 1), EstadoJornada.Tarde, minutosTardanza: 10),
            Dia(2, new(2026, 7, 1), EstadoJornada.Tarde, minutosTardanza: 20));

        var porArea = await svc.TardanzasAsync(new(2026, 7, 1), new(2026, 7, 31), area: "Ventas");
        Assert.Single(porArea);
        Assert.Equal(2, porArea[0].EmpleadoId);

        var porEmpleado = await svc.TardanzasAsync(new(2026, 7, 1), new(2026, 7, 31), empleadoId: 1);
        Assert.Single(porEmpleado);
        Assert.Equal(10, porEmpleado[0].MinutosTotales);
    }

    // ── Ausencias por persona ──

    [Fact]
    public async Task Ausencias_por_persona_separa_tipos_y_excluye_feriados()
    {
        var svc = await SetupAsync(nameof(Ausencias_por_persona_separa_tipos_y_excluye_feriados),
            Dia(1, new(2026, 7, 1), EstadoJornada.AusenteJustificado, "Vacaciones"),
            Dia(1, new(2026, 7, 2), EstadoJornada.AusenteJustificado, "Vacaciones, Lic. por estudio"),
            Dia(1, new(2026, 7, 3), EstadoJornada.AusenteInjustificado),
            Dia(1, new(2026, 7, 9), EstadoJornada.AusenteJustificado, "Vacaciones", feriado: true), // feriado: no cuenta
            Dia(2, new(2026, 7, 1), EstadoJornada.Presente));

        var a = await svc.AusenciasPorPersonaAsync(new(2026, 7, 1), new(2026, 7, 31));

        var fila = Assert.Single(a);
        Assert.Equal(1, fila.EmpleadoId);
        Assert.Equal(2, fila.Justificadas);
        Assert.Equal(1, fila.Injustificadas);
        Assert.Equal(3, fila.Total);
        Assert.Equal(["Lic. por estudio", "Vacaciones"], fila.Motivos); // CSV separado, sin duplicados
    }

    [Fact]
    public async Task Ausencias_por_persona_ordena_por_total_descendente()
    {
        var svc = await SetupAsync(nameof(Ausencias_por_persona_ordena_por_total_descendente),
            Dia(1, new(2026, 7, 1), EstadoJornada.AusenteInjustificado),
            Dia(2, new(2026, 7, 1), EstadoJornada.AusenteInjustificado),
            Dia(2, new(2026, 7, 2), EstadoJornada.AusenteInjustificado));

        var a = await svc.AusenciasPorPersonaAsync(new(2026, 7, 1), new(2026, 7, 31));

        Assert.Equal(2, a.Count);
        Assert.Equal(2, a[0].EmpleadoId);
        Assert.Equal(2, a[0].Total);
    }

    // ── Cobertura ──

    [Fact]
    public async Task Cobertura_detecta_huecos_internos()
    {
        var svc = await SetupAsync(nameof(Cobertura_detecta_huecos_internos),
            Dia(1, new(2026, 7, 1), EstadoJornada.Presente),
            Dia(2, new(2026, 7, 1), EstadoJornada.Presente),  // mismo día: no duplica
            Dia(1, new(2026, 7, 2), EstadoJornada.Presente),
            Dia(1, new(2026, 7, 10), EstadoJornada.Presente),
            Dia(1, new(2026, 7, 11), EstadoJornada.Presente));

        var c = await svc.CoberturaAsync();

        Assert.Equal(new DateOnly(2026, 7, 1), c.PrimeraFecha);
        Assert.Equal(new DateOnly(2026, 7, 11), c.UltimaFecha);
        Assert.Equal(4, c.DiasConDatos);
        var hueco = Assert.Single(c.Huecos);
        Assert.Equal(new DateOnly(2026, 7, 3), hueco.Desde);
        Assert.Equal(new DateOnly(2026, 7, 9), hueco.Hasta);
    }

    [Fact]
    public async Task Cobertura_con_base_vacia_no_explota()
    {
        var svc = await SetupAsync(nameof(Cobertura_con_base_vacia_no_explota));
        var c = await svc.CoberturaAsync();
        Assert.Null(c.PrimeraFecha);
        Assert.Equal(0, c.DiasConDatos);
        Assert.Empty(c.Huecos);
    }
}
