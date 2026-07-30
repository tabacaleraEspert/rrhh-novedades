using Microsoft.EntityFrameworkCore;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;
using RRHHNovedades.Web.Services;
using Xunit;

namespace RRHHNovedades.Tests;

/// <summary>
/// Licencias manuales de RRHH (ej. "reserva de puesto"): justifican retroactivamente las
/// injustificadas/pendientes del período, marcan EsManual, y el borrado revierte exactamente
/// esos días (pasado → injustificada, futuro → pendiente) sin tocar lo que justificó Humand.
/// </summary>
public class LicenciaManualServiceTests
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
        public DateTimeOffset Ahora => new(2026, 7, 30, 12, 0, 0, TimeSpan.FromHours(-3));
        public DateOnly Hoy => new(2026, 7, 30);
        public TimeOnly HoraActual => new(12, 0);
        public DateTime EnLocal(DateTime utc) => utc;
    }

    private static NovedadDiaria Dia(DateOnly fecha, EstadoJornada e, string? motivo = null) =>
        new() { EmpleadoId = 1, Fecha = fecha, Estado = e, MotivoNovedad = motivo };

    private static async Task<(LicenciaManualService Svc, InMemoryFactory Factory)> SetupAsync(string db, params NovedadDiaria[] novedades)
    {
        var factory = new InMemoryFactory(db);
        await using var ctx = factory.CreateDbContext();
        ctx.Empleados.Add(new Empleado { Id = 1, Nombre = "Nadia", Apellido = "Molina", Area = "Producción", EmployeeInternalId = "1", Legajo = "871" });
        ctx.Novedades.AddRange(novedades);
        await ctx.SaveChangesAsync();
        return (new LicenciaManualService(factory, new RelojFijo()), factory);
    }

    [Fact]
    public async Task Crear_justifica_retroactivo_injustificadas_y_pendientes_pero_no_presentes_ni_humand()
    {
        var (svc, factory) = await SetupAsync(nameof(Crear_justifica_retroactivo_injustificadas_y_pendientes_pero_no_presentes_ni_humand),
            Dia(new(2026, 7, 20), EstadoJornada.AusenteInjustificado),
            Dia(new(2026, 7, 21), EstadoJornada.Presente),
            Dia(new(2026, 7, 22), EstadoJornada.AusenteJustificado, "Vacaciones"), // de Humand: no se pisa
            Dia(new(2026, 8, 3), EstadoJornada.Pendiente),                          // futuro ya sincronizado
            Dia(new(2026, 7, 10), EstadoJornada.AusenteInjustificado));             // antes del desde: afuera

        var retro = await svc.CrearAsync(1, new(2026, 7, 15), null, "Reserva de puesto", "Davor");

        Assert.Equal(2, retro); // 20/07 injustificada + 03/08 pendiente
        await using var ctx = factory.CreateDbContext();
        var porFecha = await ctx.Novedades.ToDictionaryAsync(n => n.Fecha);
        Assert.Equal(EstadoJornada.AusenteJustificado, porFecha[new(2026, 7, 20)].Estado);
        Assert.Equal("Reserva de puesto", porFecha[new(2026, 7, 20)].MotivoNovedad);
        Assert.True(porFecha[new(2026, 7, 20)].EsManual);
        Assert.Equal(EstadoJornada.Presente, porFecha[new(2026, 7, 21)].Estado);
        Assert.Equal("Vacaciones", porFecha[new(2026, 7, 22)].MotivoNovedad);
        Assert.False(porFecha[new(2026, 7, 22)].EsManual);
        Assert.Equal(EstadoJornada.AusenteJustificado, porFecha[new(2026, 8, 3)].Estado);
        Assert.Equal(EstadoJornada.AusenteInjustificado, porFecha[new(2026, 7, 10)].Estado); // fuera del rango
    }

    [Fact]
    public async Task Eliminar_revierte_solo_lo_manual_pasado_a_injustificada_y_futuro_a_pendiente()
    {
        var (svc, factory) = await SetupAsync(nameof(Eliminar_revierte_solo_lo_manual_pasado_a_injustificada_y_futuro_a_pendiente),
            Dia(new(2026, 7, 20), EstadoJornada.AusenteInjustificado),
            Dia(new(2026, 8, 3), EstadoJornada.Pendiente),
            Dia(new(2026, 7, 22), EstadoJornada.AusenteJustificado, "Vacaciones")); // Humand, intacta

        await svc.CrearAsync(1, new(2026, 7, 15), null, "Reserva de puesto", "Davor");
        int id;
        await using (var ctx = factory.CreateDbContext())
            id = (await ctx.LicenciasManuales.SingleAsync()).Id;

        var revertidos = await svc.EliminarAsync(id);

        Assert.Equal(2, revertidos);
        await using var ctx2 = factory.CreateDbContext();
        var porFecha = await ctx2.Novedades.ToDictionaryAsync(n => n.Fecha);
        Assert.Equal(EstadoJornada.AusenteInjustificado, porFecha[new(2026, 7, 20)].Estado); // pasado
        Assert.Null(porFecha[new(2026, 7, 20)].MotivoNovedad);
        Assert.Equal(EstadoJornada.Pendiente, porFecha[new(2026, 8, 3)].Estado);             // futuro
        Assert.Equal("Vacaciones", porFecha[new(2026, 7, 22)].MotivoNovedad);                // Humand intacta
        Assert.Empty(await ctx2.LicenciasManuales.ToListAsync());
    }

    [Fact]
    public async Task Motivos_usados_quedan_como_opciones_repetibles()
    {
        var (svc, _) = await SetupAsync(nameof(Motivos_usados_quedan_como_opciones_repetibles));

        await svc.CrearAsync(1, new(2026, 7, 1), new(2026, 7, 5), "Reserva de puesto", "Davor");
        await svc.CrearAsync(1, new(2026, 7, 10), new(2026, 7, 12), "Acuerdo gremial", "Davor");
        await svc.CrearAsync(1, new(2026, 7, 20), null, "Reserva de puesto", "Davor"); // repetido: 1 sola vez

        Assert.Equal(["Acuerdo gremial", "Reserva de puesto"], await svc.MotivosAsync());
    }

    [Fact]
    public async Task Hasta_acotado_no_justifica_despues_del_fin()
    {
        var (svc, factory) = await SetupAsync(nameof(Hasta_acotado_no_justifica_despues_del_fin),
            Dia(new(2026, 7, 20), EstadoJornada.AusenteInjustificado),
            Dia(new(2026, 7, 25), EstadoJornada.AusenteInjustificado));

        var retro = await svc.CrearAsync(1, new(2026, 7, 18), new(2026, 7, 22), "Reserva de puesto", "Davor");

        Assert.Equal(1, retro);
        await using var ctx = factory.CreateDbContext();
        Assert.Equal(EstadoJornada.AusenteInjustificado,
            (await ctx.Novedades.SingleAsync(n => n.Fecha == new DateOnly(2026, 7, 25))).Estado);
    }

    [Fact]
    public async Task La_ingesta_aplica_la_licencia_vigente_del_dia()
    {
        // El overlay de la ingesta usa el mismo criterio: injustificada/pendiente + licencia vigente
        // ⇒ justificada manual. Se prueba la condición de vigencia con la que filtra la ingesta.
        var (svc, factory) = await SetupAsync(nameof(La_ingesta_aplica_la_licencia_vigente_del_dia));
        await svc.CrearAsync(1, new(2026, 7, 15), null, "Reserva de puesto", "Davor");

        await using var ctx = factory.CreateDbContext();
        var fecha = new DateOnly(2026, 8, 10);
        var vigentes = await ctx.LicenciasManuales
            .Where(l => l.Desde <= fecha && (l.Hasta == null || l.Hasta >= fecha))
            .ToListAsync();
        Assert.Single(vigentes); // sin Hasta: sigue vigente a futuro

        var fueraDeRango = new DateOnly(2026, 7, 1);
        Assert.Empty(await ctx.LicenciasManuales
            .Where(l => l.Desde <= fueraDeRango && (l.Hasta == null || l.Hasta >= fueraDeRango))
            .ToListAsync());
    }
}
