using Microsoft.EntityFrameworkCore;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;
using RRHHNovedades.Web.Services;
using Xunit;

namespace RRHHNovedades.Tests;

/// <summary>
/// Cálculo de nocturnidad: minutos trabajados dentro de la banda 21:00–06:00 según fichadas
/// reales, y redondeo POR NOCHE (fracción ≥ 45 min ⇒ hora completa hacia arriba).
/// </summary>
public class NocturnidadServiceTests
{
    private static TimeOnly T(int h, int m = 0) => new(h, m);

    // ── Minutos en banda nocturna ──

    [Fact]
    public void Turno_noche_22_a_06_cruza_medianoche_son_8_horas()
    {
        Assert.Equal(8 * 60, NocturnidadService.MinutosNocturnos(T(22), T(6)));
    }

    [Fact]
    public void Entrada_antes_de_las_21_solo_cuenta_desde_las_21()
    {
        // 20:00 → 05:00: la banda arranca 21:00 ⇒ 21→05 = 8 h.
        Assert.Equal(8 * 60, NocturnidadService.MinutosNocturnos(T(20), T(5)));
    }

    [Fact]
    public void Turno_tarde_que_termina_22_suma_solo_1_hora()
    {
        Assert.Equal(60, NocturnidadService.MinutosNocturnos(T(14), T(22)));
    }

    [Fact]
    public void Turno_diurno_no_suma_nada()
    {
        Assert.Equal(0, NocturnidadService.MinutosNocturnos(T(8), T(17)));
    }

    [Fact]
    public void Madrugada_del_mismo_dia_cuenta_hasta_las_06()
    {
        // Entrada 02:00, salida 10:00: cuenta 02→06 = 4 h.
        Assert.Equal(4 * 60, NocturnidadService.MinutosNocturnos(T(2), T(10)));
    }

    [Fact]
    public void Sin_salida_no_se_puede_calcular_y_da_0()
    {
        Assert.Equal(0, NocturnidadService.MinutosNocturnos(T(22), null));
    }

    [Fact]
    public void Salida_despues_de_las_06_recorta_en_la_banda()
    {
        // 22:00 → 07:00: cuenta 22→06 = 8 h (la hora 06→07 queda fuera).
        Assert.Equal(8 * 60, NocturnidadService.MinutosNocturnos(T(22), T(7)));
    }

    // ── Redondeo por noche (≥ 45 min ⇒ hora completa) ──

    [Theory]
    [InlineData(8 * 60, 8)]        // exacto
    [InlineData(8 * 60 + 46, 9)]   // 46 min ⇒ redondea arriba
    [InlineData(8 * 60 + 45, 9)]   // 45 min justos ⇒ redondea arriba ("a partir de 45")
    [InlineData(8 * 60 + 44, 8)]   // 44 min ⇒ se descarta
    [InlineData(30, 0)]            // media hora sola no llega a 1 h
    [InlineData(50, 1)]
    [InlineData(0, 0)]
    public void Redondeo_por_noche(int minutos, int esperado)
    {
        Assert.Equal(esperado, NocturnidadService.HorasRedondeadas(minutos));
    }

    [Fact]
    public void Caso_combinado_2110_a_0556_redondea_a_9_horas()
    {
        // 21:10 → 05:56 = 8 h 46 min dentro de la banda ⇒ 9 h.
        var min = NocturnidadService.MinutosNocturnos(T(21, 10), T(5, 56));
        Assert.Equal(8 * 60 + 46, min);
        Assert.Equal(9, NocturnidadService.HorasRedondeadas(min));
    }

    // ── Reporte mensual y desglose (DB en memoria) ──

    private sealed class InMemoryFactory(string dbName) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            var opt = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(dbName).Options;
            return new AppDbContext(opt);
        }
    }

    private static async Task<NocturnidadService> SetupMesAsync(string db)
    {
        var factory = new InMemoryFactory(db);
        await using var ctx = factory.CreateDbContext();
        ctx.Empleados.AddRange(
            new Empleado { Id = 1, Nombre = "Nadia", Apellido = "Molina", Area = "Producción", EmployeeInternalId = "1", Turno = Turno.Noche, Legajo = "1262" },
            new Empleado { Id = 2, Nombre = "Sofía", Apellido = "Vega", Area = "Ventas", EmployeeInternalId = "2" });
        ctx.Novedades.AddRange(
            // Molina: 3 noches dentro del período de JULIO (26-jun → 25-jul inclusive),
            // 1 sin salida (no computable) y 1 del 26-jul (ya pertenece a AGOSTO).
            new NovedadDiaria { EmpleadoId = 1, Fecha = new(2026, 6, 26), HoraEntrada = T(22), HoraSalida = T(6) },     // borde: entra en julio
            new NovedadDiaria { EmpleadoId = 1, Fecha = new(2026, 7, 1), HoraEntrada = T(22), HoraSalida = T(6) },
            new NovedadDiaria { EmpleadoId = 1, Fecha = new(2026, 7, 2), HoraEntrada = T(21, 10), HoraSalida = T(5, 56) },
            new NovedadDiaria { EmpleadoId = 1, Fecha = new(2026, 7, 3), HoraEntrada = T(22), HoraSalida = null },
            new NovedadDiaria { EmpleadoId = 1, Fecha = new(2026, 7, 26), HoraEntrada = T(22), HoraSalida = T(6) },     // borde: ya es agosto
            // Vega: turno tarde que pisa 1 h la banda.
            new NovedadDiaria { EmpleadoId = 2, Fecha = new(2026, 7, 10), HoraEntrada = T(14), HoraSalida = T(22) },
            // Diurno puro: no aparece.
            new NovedadDiaria { EmpleadoId = 2, Fecha = new(2026, 7, 11), HoraEntrada = T(8), HoraSalida = T(17) });
        await ctx.SaveChangesAsync();
        return new NocturnidadService(factory);
    }

    [Theory]
    [InlineData(2026, 7, "2026-06-26", "2026-07-26")]  // julio = 26-jun → 25-jul inclusive
    [InlineData(2026, 1, "2025-12-26", "2026-01-26")]  // enero cruza el año
    [InlineData(2026, 12, "2026-11-26", "2026-12-26")]
    public void Periodo_de_liquidacion_va_del_26_anterior_al_25_inclusive(int anio, int mes, string desde, string hastaExcl)
    {
        var (d, h) = NocturnidadService.PeriodoLiquidacion(anio, mes);
        Assert.Equal(DateOnly.Parse(desde), d);
        Assert.Equal(DateOnly.Parse(hastaExcl), h); // exclusivo ⇒ incluye hasta el 25
    }

    [Fact]
    public async Task Reporte_mensual_acumula_por_empleado_solo_el_periodo_de_liquidacion()
    {
        var svc = await SetupMesAsync(nameof(Reporte_mensual_acumula_por_empleado_solo_el_periodo_de_liquidacion));
        var filas = await svc.ReporteMensualAsync(2026, 7);

        Assert.Equal(2, filas.Count);
        var molina = filas.Single(f => f.ApellidoNombre.StartsWith("Molina"));
        Assert.Equal(3, molina.Noches);               // 26-jun entra; la sin salida y la del 26-jul no
        Assert.Equal(8 + 8 + 9, molina.HorasNocturnas); // 8h + 8h + 8h46 redondeada a 9
        var vega = filas.Single(f => f.ApellidoNombre.StartsWith("Vega"));
        Assert.Equal(1, vega.Noches);
        Assert.Equal(1, vega.HorasNocturnas);
    }

    [Fact]
    public async Task La_noche_del_26_pasa_al_mes_siguiente()
    {
        var svc = await SetupMesAsync(nameof(La_noche_del_26_pasa_al_mes_siguiente));
        var agosto = await svc.ReporteMensualAsync(2026, 8);

        var molina = Assert.Single(agosto);
        Assert.Equal(1, molina.Noches); // la del 26-jul
        Assert.Equal(8, molina.HorasNocturnas);
    }

    [Fact]
    public async Task Excel_del_periodo_trae_resumen_con_totales_y_detalle_por_noche()
    {
        var svc = await SetupMesAsync(nameof(Excel_del_periodo_trae_resumen_con_totales_y_detalle_por_noche));
        var bytes = await svc.ExcelMensualAsync(2026, 7);

        using var ms = new MemoryStream(bytes);
        using var wb = new ClosedXML.Excel.XLWorkbook(ms);
        var res = wb.Worksheet("Resumen");
        Assert.Equal("1262", res.Cell(5, 1).GetString());          // legajo
        Assert.Equal("Molina, Nadia", res.Cell(5, 2).GetString()); // ordenado por apellido
        Assert.Equal(3, res.Cell(5, 4).GetValue<int>());           // noches del período
        Assert.Equal(25, res.Cell(5, 5).GetValue<int>());          // 8+8+9
        Assert.Equal("Vega, Sofía", res.Cell(6, 2).GetString());
        Assert.Equal("—", res.Cell(6, 1).GetString());             // sin legajo cargado
        var det = wb.Worksheet("Detalle");
        Assert.Equal(4, det.RowsUsed().Count() - 1);               // 3 noches Molina + 1 Vega (sin cabecera)
        Assert.Equal("1262", det.Cell(2, 1).GetString());
    }

    [Fact]
    public async Task Detalle_mensual_lista_cada_noche_con_sus_horas()
    {
        var svc = await SetupMesAsync(nameof(Detalle_mensual_lista_cada_noche_con_sus_horas));
        var noches = await svc.DetalleMensualAsync(1, 2026, 7);

        Assert.Equal(3, noches.Count);
        Assert.Equal(new DateOnly(2026, 6, 26), noches[0].Fecha);
        Assert.Equal(new DateOnly(2026, 7, 1), noches[1].Fecha);
        Assert.Equal(8, noches[1].Horas);
        Assert.Equal(new DateOnly(2026, 7, 2), noches[2].Fecha);
        Assert.Equal(9, noches[2].Horas);
    }
}
