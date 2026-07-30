using Microsoft.EntityFrameworkCore;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;
using RRHHNovedades.Web.Services;
using Xunit;

namespace RRHHNovedades.Tests;

/// <summary>
/// Reporte de ausentismo por rango: buckets día/semana/mes, % justificadas vs injustificadas
/// sobre el total de ausencias y tasa sobre jornadas evaluables. Reglas congeladas con RRHH
/// (jul-2026): feriado nunca cuenta como ausencia; semanas lunes-domingo recortadas al rango.
/// </summary>
public class AusentismoServiceTests
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

    /// <summary>"Hoy" congelado al 30/07/2026 (el día del caso real que originó la regla de futuras).</summary>
    private sealed class RelojFijo : IReloj
    {
        public DateTimeOffset Ahora => new(2026, 7, 30, 12, 0, 0, TimeSpan.FromHours(-3));
        public DateOnly Hoy => new(2026, 7, 30);
        public TimeOnly HoraActual => new(12, 0);
        public DateTime EnLocal(DateTime utc) => utc;
    }

    private static NovedadDiaria Dia(int empleadoId, DateOnly fecha, EstadoJornada e, string? motivo = null, bool feriado = false) =>
        new() { EmpleadoId = empleadoId, Fecha = fecha, Estado = e, MotivoNovedad = motivo, EsFeriado = feriado };

    private static async Task<AusentismoService> SetupAsync(string db, params NovedadDiaria[] novedades)
    {
        var factory = new InMemoryFactory(db);
        await using var ctx = factory.CreateDbContext();
        ctx.Empleados.AddRange(
            new Empleado { Id = 1, Nombre = "Nadia", Apellido = "Molina", Area = "Producción", EmployeeInternalId = "1", Legajo = "871" },
            new Empleado { Id = 2, Nombre = "Bruno", Apellido = "Paz", Area = "Ventas", EmployeeInternalId = "2", Legajo = "455" });
        ctx.Novedades.AddRange(novedades);
        await ctx.SaveChangesAsync();
        return new AusentismoService(factory, new RelojFijo());
    }

    // ── Buckets ──

    [Fact]
    public async Task Semana_que_cruza_meses_es_un_solo_bucket_semanal_pero_los_meses_la_reparten()
    {
        // Mar 30/06 y mié 01/07/2026: misma semana (lunes 29/06), meses distintos.
        var svc = await SetupAsync(nameof(Semana_que_cruza_meses_es_un_solo_bucket_semanal_pero_los_meses_la_reparten),
            Dia(1, new(2026, 6, 30), EstadoJornada.AusenteInjustificado),
            Dia(1, new(2026, 7, 1), EstadoJornada.AusenteJustificado, "Vacaciones"));

        var r = await svc.ReporteAsync(new(2026, 6, 1), new(2026, 7, 31));

        var semana = Assert.Single(r.PorSemana, s => s.Total > 0);
        Assert.Equal("Sem 29/06 al 05/07", semana.Etiqueta);
        Assert.Equal(2, semana.Total);

        Assert.Equal(2, r.PorMes.Count);
        Assert.Equal(1, r.PorMes[0].Total); // junio: la injustificada del 30/06
        Assert.Equal(1, r.PorMes[0].Injustificadas);
        Assert.Equal(1, r.PorMes[1].Total); // julio: la licencia del 01/07
        Assert.Equal(1, r.PorMes[1].Justificadas);
        Assert.Equal("Junio 2026", r.PorMes[0].Etiqueta);
        Assert.Equal("Julio 2026", r.PorMes[1].Etiqueta);
    }

    [Fact]
    public async Task Semanas_se_recortan_al_borde_del_rango()
    {
        var svc = await SetupAsync(nameof(Semanas_se_recortan_al_borde_del_rango),
            Dia(1, new(2026, 7, 30), EstadoJornada.AusenteInjustificado));

        // 01/06/2026 es lunes (arranque limpio); la última semana 27/07-02/08 se recorta al 31/07.
        var r = await svc.ReporteAsync(new(2026, 6, 1), new(2026, 7, 31));

        Assert.Equal(new DateOnly(2026, 6, 1), r.PorSemana[0].Desde);
        var ultima = r.PorSemana[^1];
        Assert.Equal(new DateOnly(2026, 7, 27), ultima.Desde);
        Assert.Equal(new DateOnly(2026, 7, 31), ultima.Hasta);
        Assert.Equal("Sem 27/07 al 31/07", ultima.Etiqueta); // la etiqueta NUNCA muestra días de afuera del rango
        Assert.Equal(1, ultima.Total);

        // El rango completo queda cubierto sin huecos: 61 días.
        Assert.Equal(61, r.PorDia.Count);
        Assert.Equal(61, r.PorSemana.Sum(s => s.Hasta.DayNumber - s.Desde.DayNumber + 1));
    }

    [Fact]
    public async Task Dia_sin_ausencias_aparece_con_cero_y_sin_division_por_cero()
    {
        var svc = await SetupAsync(nameof(Dia_sin_ausencias_aparece_con_cero_y_sin_division_por_cero),
            Dia(1, new(2026, 7, 1), EstadoJornada.Presente));

        var r = await svc.ReporteAsync(new(2026, 7, 1), new(2026, 7, 2));

        Assert.Equal(2, r.PorDia.Count);
        var d1 = r.PorDia[0];
        Assert.Equal(0, d1.Total);
        Assert.Equal(0, d1.PctJustificadas);
        Assert.Equal(0, d1.PctInjustificadas);
        Assert.Equal(0, d1.TasaAusentismo);
        Assert.Equal(1, d1.JornadasEvaluables);   // el presente cuenta como jornada
        Assert.Equal(0, r.PorDia[1].JornadasEvaluables); // día sin novedades
    }

    // ── Reglas de clasificación ──

    [Fact]
    public async Task Feriado_no_cuenta_como_ausencia_aunque_el_estado_sea_ausente()
    {
        var svc = await SetupAsync(nameof(Feriado_no_cuenta_como_ausencia_aunque_el_estado_sea_ausente),
            Dia(1, new(2026, 7, 9), EstadoJornada.AusenteInjustificado, feriado: true),
            Dia(2, new(2026, 7, 10), EstadoJornada.AusenteInjustificado));

        var r = await svc.ReporteAsync(new(2026, 7, 1), new(2026, 7, 31));

        var det = Assert.Single(r.Detalle);
        Assert.Equal(new DateOnly(2026, 7, 10), det.Fecha);
        Assert.Equal(1, r.PorMes[0].Total);
    }

    [Fact]
    public async Task Licencia_multiple_cuenta_una_ausencia_con_el_primer_tipo()
    {
        var svc = await SetupAsync(nameof(Licencia_multiple_cuenta_una_ausencia_con_el_primer_tipo),
            Dia(1, new(2026, 6, 10), EstadoJornada.AusenteJustificado, "Vacaciones, Lic. por enfermedad"),
            Dia(2, new(2026, 6, 10), EstadoJornada.AusenteJustificado)); // permiso sin motivo → "Licencia"

        var r = await svc.ReporteAsync(new(2026, 6, 1), new(2026, 6, 30));

        Assert.Equal(2, r.Detalle.Count);
        Assert.Equal("Vacaciones", r.Detalle.Single(d => d.EmpleadoId == 1).Motivo);
        Assert.Equal("Licencia", r.Detalle.Single(d => d.EmpleadoId == 2).Motivo);
        Assert.Equal(2, r.PorMes[0].Justificadas);
        Assert.Equal(0, r.PorMes[0].Injustificadas);
    }

    // ── Porcentajes y tasa ──

    [Fact]
    public async Task Porcentajes_sobre_ausencias_y_tasa_sobre_jornadas_evaluables()
    {
        var f = new DateOnly(2026, 7, 6);
        var novedades = new List<NovedadDiaria>
        {
            Dia(1, f, EstadoJornada.AusenteJustificado, "Vacaciones"),
            Dia(1, f.AddDays(1), EstadoJornada.AusenteJustificado, "Vacaciones"),
            Dia(2, f, EstadoJornada.AusenteJustificado, "Lic. por enfermedad"),
            Dia(2, f.AddDays(1), EstadoJornada.AusenteInjustificado),
        };
        // 6 jornadas evaluables extra (presentes/tardes) repartidas en los 2 días.
        for (int i = 0; i < 3; i++)
        {
            novedades.Add(Dia(1, f.AddDays(2 + i), EstadoJornada.Presente));    // fuera de los días medidos abajo
            novedades.Add(Dia(2, f.AddDays(2 + i), EstadoJornada.FrancoNoLaborable)); // franco: nunca evaluable
        }
        var svc = await SetupAsync(nameof(Porcentajes_sobre_ausencias_y_tasa_sobre_jornadas_evaluables), [.. novedades]);

        var r = await svc.ReporteAsync(f, f.AddDays(1));

        var sem = Assert.Single(r.PorSemana);
        Assert.Equal(3, sem.Justificadas);
        Assert.Equal(1, sem.Injustificadas);
        Assert.Equal(4, sem.Total);
        Assert.Equal(0.75, sem.PctJustificadas);
        Assert.Equal(0.25, sem.PctInjustificadas);
        Assert.Equal(4, sem.JornadasEvaluables); // las 4 ausencias son a la vez jornadas evaluables
        Assert.Equal(1.0, sem.TasaAusentismo);
    }

    [Fact]
    public async Task Filtro_de_area_afecta_detalle_agregados_y_denominador()
    {
        var f = new DateOnly(2026, 7, 6);
        var svc = await SetupAsync(nameof(Filtro_de_area_afecta_detalle_agregados_y_denominador),
            Dia(1, f, EstadoJornada.AusenteInjustificado),      // Producción
            Dia(2, f, EstadoJornada.Presente));                 // Ventas

        var r = await svc.ReporteAsync(f, f, area: "Ventas");

        Assert.Empty(r.Detalle);
        Assert.Equal(0, r.PorDia[0].Total);
        Assert.Equal(1, r.PorDia[0].JornadasEvaluables); // solo el presente de Ventas
    }

    [Fact]
    public async Task Semana_recortada_por_el_desde_tampoco_muestra_dias_de_afuera()
    {
        // Rango "solo julio": la semana del lunes 29/06 se etiqueta desde el 01/07.
        var svc = await SetupAsync(nameof(Semana_recortada_por_el_desde_tampoco_muestra_dias_de_afuera),
            Dia(1, new(2026, 7, 1), EstadoJornada.AusenteInjustificado));

        var r = await svc.ReporteAsync(new(2026, 7, 1), new(2026, 7, 31));

        Assert.Equal("Sem 01/07 al 05/07", r.PorSemana[0].Etiqueta);
        Assert.Equal(new DateOnly(2026, 7, 1), r.PorSemana[0].Desde);
    }

    // ── Fechas futuras: licencias ya pedidas en Humand ──

    [Fact]
    public async Task Licencia_futura_cuenta_como_justificada_y_se_marca_programada()
    {
        // Hoy (RelojFijo) = 30/07/2026. Vacaciones pedidas para el 03/08: cuentan y quedan Futura.
        var svc = await SetupAsync(nameof(Licencia_futura_cuenta_como_justificada_y_se_marca_programada),
            Dia(1, new(2026, 7, 29), EstadoJornada.AusenteJustificado, "Vacaciones"),
            Dia(1, new(2026, 8, 3), EstadoJornada.AusenteJustificado, "Vacaciones"));

        var r = await svc.ReporteAsync(new(2026, 7, 1), new(2026, 8, 31));

        Assert.Equal(2, r.Detalle.Count);
        Assert.False(r.Detalle.Single(d => d.Fecha == new DateOnly(2026, 7, 29)).Futura);
        Assert.True(r.Detalle.Single(d => d.Fecha == new DateOnly(2026, 8, 3)).Futura);
        Assert.Equal(1, r.PorMes.Single(m => m.Etiqueta == "Agosto 2026").Justificadas);
    }

    [Fact]
    public async Task Pendientes_engordan_el_denominador_de_la_tasa_pero_no_son_ausencia()
    {
        // Día futuro típico: 1 licencia programada + 3 pendientes → tasa 25%, no 100%.
        var f = new DateOnly(2026, 8, 3);
        var svc = await SetupAsync(nameof(Pendientes_engordan_el_denominador_de_la_tasa_pero_no_son_ausencia),
            Dia(1, f, EstadoJornada.AusenteJustificado, "Vacaciones"),
            Dia(2, f, EstadoJornada.Pendiente),
            Dia(2, f.AddDays(1), EstadoJornada.Pendiente),
            Dia(1, f.AddDays(1), EstadoJornada.Pendiente));

        var r = await svc.ReporteAsync(f, f.AddDays(1));

        var sem = Assert.Single(r.PorSemana);
        Assert.Equal(1, sem.Total);
        Assert.Equal(4, sem.JornadasEvaluables);
        Assert.Equal(0.25, sem.TasaAusentismo);
    }

    // ── Excel ──

    [Fact]
    public async Task Excel_tiene_resumen_y_detalle_con_la_data_cruda()
    {
        var svc = await SetupAsync(nameof(Excel_tiene_resumen_y_detalle_con_la_data_cruda),
            Dia(1, new(2026, 7, 6), EstadoJornada.AusenteJustificado, "Vacaciones"),
            Dia(2, new(2026, 7, 6), EstadoJornada.AusenteInjustificado));

        var bytes = await svc.ExcelAsync(new(2026, 7, 6), new(2026, 7, 7));

        using var ms = new MemoryStream(bytes);
        using var wb = new ClosedXML.Excel.XLWorkbook(ms);

        var res = wb.Worksheet("Resumen");
        Assert.StartsWith("Ausentismo 06/07/2026 al 07/07/2026", res.Cell(1, 1).GetString());
        Assert.Equal("POR MES", res.Cell(3, 1).GetString());

        var det = wb.Worksheet("Detalle");
        Assert.Equal("Fecha", det.Cell(1, 1).GetString());
        Assert.Equal("Molina, Nadia", det.Cell(2, 3).GetString());
        Assert.Equal("Justificada", det.Cell(2, 5).GetString());
        Assert.Equal("Vacaciones", det.Cell(2, 6).GetString());
        Assert.Equal("Paz, Bruno", det.Cell(3, 3).GetString());
        Assert.Equal("Injustificada", det.Cell(3, 5).GetString());
        Assert.Equal("—", det.Cell(3, 6).GetString());
    }
}
