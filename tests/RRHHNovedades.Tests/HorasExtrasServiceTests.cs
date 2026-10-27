using Microsoft.EntityFrameworkCore;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;
using RRHHNovedades.Web.Services;
using Xunit;

namespace RRHHNovedades.Tests;

/// <summary>
/// Horas extras y refrigerio: NO se calculan acá (las calcula Humand con sus políticas de marcaje);
/// el servicio acumula por período de liquidación (26 → 25) lo que guardó la ingesta.
/// Valores tomados de jornadas reales (oct-2026): lun-vie 3,89 h extra ⇒ 0,89 de refrigerio;
/// sábado sin horario 6,13 h ⇒ 5 al 50 + 1,14 al 100 y refrigerio = toda la jornada.
/// </summary>
public class HorasExtrasServiceTests
{
    private static TimeOnly T(int h, int m = 0) => new(h, m);

    private sealed class InMemoryFactory(string dbName) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            var opt = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(dbName).Options;
            return new AppDbContext(opt);
        }
    }

    private static async Task<HorasExtrasService> SetupAsync(string db)
    {
        // Prefijo: la base en memoria es global al proceso y NocturnidadServiceTests usa los mismos nombres de test.
        var factory = new InMemoryFactory("HorasExtras_" + db);
        await using var ctx = factory.CreateDbContext();
        ctx.Empleados.AddRange(
            new Empleado { Id = 1, Nombre = "Juan", Apellido = "Pérez", Area = "Producción", EmployeeInternalId = "1", Legajo = "1001" },
            new Empleado { Id = 2, Nombre = "Rosa", Apellido = "Gómez", Area = "Logística", EmployeeInternalId = "2" },
            new Empleado { Id = 3, Nombre = "Mario", Apellido = "Sosa", Area = "Producción", EmployeeInternalId = "3" });
        ctx.Novedades.AddRange(
            // Pérez: período OCTUBRE = 26-sep → 25-oct inclusive.
            new NovedadDiaria { EmpleadoId = 1, Fecha = new(2026, 9, 26), HoraEntrada = T(10, 59), HoraSalida = T(17, 8), HorasExtras50 = 5, HorasExtras100 = 1.14, HorasRefrigerio = 6.14 }, // sábado, borde: entra en octubre
            new NovedadDiaria { EmpleadoId = 1, Fecha = new(2026, 9, 28), HoraEntrada = T(9, 7), HoraSalida = T(21), HorasExtras50 = 3.89, HorasRefrigerio = 0.89 },
            new NovedadDiaria { EmpleadoId = 1, Fecha = new(2026, 9, 29), HoraEntrada = T(9), HoraSalida = T(17), Estado = EstadoJornada.Presente },                     // sin extras
            new NovedadDiaria { EmpleadoId = 1, Fecha = new(2026, 10, 1), HoraEntrada = T(12, 41), HoraSalida = T(22, 47), HorasExtras50 = 2.1 },                       // < 3 extras ⇒ sin refrigerio
            new NovedadDiaria { EmpleadoId = 1, Fecha = new(2026, 10, 26), HoraEntrada = T(9), HoraSalida = T(21), HorasExtras50 = 4, HorasRefrigerio = 1 },           // borde: ya es noviembre
            // Gómez: solo refrigerio (caso raro pero posible), sin extras.
            new NovedadDiaria { EmpleadoId = 2, Fecha = new(2026, 10, 5), HoraEntrada = T(8), HoraSalida = T(17), HorasRefrigerio = 0.5 },
            // Sosa: nada en el período.
            new NovedadDiaria { EmpleadoId = 3, Fecha = new(2026, 10, 5), HoraEntrada = T(8), HoraSalida = T(16) });
        await ctx.SaveChangesAsync();
        return new HorasExtrasService(factory);
    }

    [Fact]
    public async Task Reporte_mensual_acumula_por_empleado_solo_el_periodo_de_liquidacion()
    {
        var svc = await SetupAsync(nameof(Reporte_mensual_acumula_por_empleado_solo_el_periodo_de_liquidacion));
        var filas = await svc.ReporteMensualAsync(2026, 10);

        Assert.Equal(3, filas.Count); // Sosa aparece en 0 (todo el personal del período)
        var perez = filas[0];          // ordenado por total de extras
        Assert.Equal("1001", perez.Legajo);
        Assert.Equal("Pérez, Juan", perez.ApellidoNombre);
        Assert.Equal(3, perez.DiasConExtras);                 // 26-sep, 28-sep, 1-oct (el 26-oct es noviembre)
        Assert.Equal(10.99, perez.Extras50, 2);               // 5 + 3,89 + 2,1
        Assert.Equal(1.14, perez.Extras100, 2);
        Assert.Equal(12.13, perez.TotalExtras, 2);
        Assert.Equal(11, perez.Extras50Liq);                  // por día: 5 + 4 (3,89 = 3h53) + 2 (2,1 = 2h06)
        Assert.Equal(1, perez.Extras100Liq);
        Assert.Equal(7.03, perez.Refrigerio, 2);              // 6,14 + 0,89
        Assert.Equal(2, perez.DiasConRefrigerio);

        var gomez = filas[1];
        Assert.Equal(0, gomez.DiasConExtras);
        Assert.Equal(0, gomez.TotalExtras);
        Assert.Equal(0.5, gomez.Refrigerio, 2);
        Assert.Equal(1, gomez.DiasConRefrigerio);

        var sosa = filas[2];
        Assert.Equal("Sosa, Mario", sosa.ApellidoNombre);
        Assert.Equal(0, sosa.DiasConExtras);
        Assert.Equal(0, sosa.TotalExtras);
        Assert.Equal(0, sosa.TotalExtrasLiq);
        Assert.Equal(0, sosa.Refrigerio);
    }

    [Fact]
    public async Task El_dia_26_pasa_al_mes_siguiente()
    {
        var svc = await SetupAsync(nameof(El_dia_26_pasa_al_mes_siguiente));
        var noviembre = await svc.ReporteMensualAsync(2026, 11);

        var perez = Assert.Single(noviembre);
        Assert.Equal(1, perez.DiasConExtras);
        Assert.Equal(4, perez.Extras50, 2);
        Assert.Equal(1, perez.Refrigerio, 2);
    }

    [Fact]
    public async Task Detalle_mensual_lista_solo_dias_con_horas_en_orden()
    {
        var svc = await SetupAsync(nameof(Detalle_mensual_lista_solo_dias_con_horas_en_orden));
        var dias = await svc.DetalleMensualAsync(1, 2026, 10);

        Assert.Equal(3, dias.Count);
        Assert.Equal(new DateOnly(2026, 9, 26), dias[0].Fecha);
        Assert.Equal(T(10, 59), dias[0].Entrada);
        Assert.Equal(1.14, dias[0].Extras100, 2);
        Assert.Equal(new DateOnly(2026, 9, 28), dias[1].Fecha);
        Assert.Equal(0.89, dias[1].Refrigerio, 2);
        Assert.Equal(new DateOnly(2026, 10, 1), dias[2].Fecha);
        Assert.Equal(0, dias[2].Refrigerio);
    }

    [Fact]
    public async Task Detalle_con_dias_en_cero_cubre_todo_el_periodo()
    {
        var svc = await SetupAsync(nameof(Detalle_con_dias_en_cero_cubre_todo_el_periodo));
        var dias = await svc.DetalleMensualAsync(1, 2026, 10, incluirDiasEnCero: true);

        Assert.Equal(30, dias.Count); // 26-sep → 25-oct inclusive
        Assert.Equal(new DateOnly(2026, 9, 26), dias[0].Fecha);
        Assert.Equal(new DateOnly(2026, 10, 25), dias[^1].Fecha);

        // Día sin sincronizar: 0 y sin fichada.
        var sinDatos = dias.Single(d => d.Fecha == new DateOnly(2026, 9, 27));
        Assert.False(sinDatos.TieneHoras);
        Assert.Null(sinDatos.Entrada);

        // Trabajó su horario sin extras: se ve la fichada, en 0.
        var normal = dias.Single(d => d.Fecha == new DateOnly(2026, 9, 29));
        Assert.False(normal.TieneHoras);
        Assert.Equal(T(9), normal.Entrada);
        Assert.Equal(T(17), normal.Salida);

        Assert.Equal(3, dias.Count(d => d.TieneHoras));
    }

    [Fact]
    public async Task Excel_del_periodo_trae_resumen_con_totales_y_detalle_por_dia()
    {
        var svc = await SetupAsync(nameof(Excel_del_periodo_trae_resumen_con_totales_y_detalle_por_dia));
        var bytes = await svc.ExcelMensualAsync(2026, 10);

        using var ms = new MemoryStream(bytes);
        using var wb = new ClosedXML.Excel.XLWorkbook(ms);
        var res = wb.Worksheet("Resumen");
        Assert.Equal("—", res.Cell(5, 1).GetString());              // Gómez sin legajo (ordenado por apellido)
        Assert.Equal("Gómez, Rosa", res.Cell(5, 2).GetString());
        Assert.Equal("1001", res.Cell(6, 1).GetString());
        Assert.Equal("Pérez, Juan", res.Cell(6, 2).GetString());
        Assert.Equal(3, res.Cell(6, 4).GetValue<int>());             // días con extras
        Assert.Equal(10.99, res.Cell(6, 5).GetValue<double>(), 2);   // HE 50
        Assert.Equal(1.14, res.Cell(6, 6).GetValue<double>(), 2);    // HE 100
        Assert.Equal(12.13, res.Cell(6, 7).GetValue<double>(), 2);   // total
        Assert.Equal(11, res.Cell(6, 8).GetValue<double>());         // HE 50 liq.: 5 + 4 + 2
        Assert.Equal(1, res.Cell(6, 9).GetValue<double>());          // HE 100 liq.: 1,14 ⇒ 1
        Assert.Equal(12, res.Cell(6, 10).GetValue<double>());        // total liq.
        Assert.Equal(7.03, res.Cell(6, 11).GetValue<double>(), 2);   // refrigerio
        Assert.Equal(2, res.Cell(6, 12).GetValue<int>());            // días con refrigerio
        Assert.Equal("Sosa, Mario", res.Cell(7, 2).GetString());       // sin horas: figura en 0
        Assert.Equal(0, res.Cell(7, 7).GetValue<double>());
        Assert.Equal("TOTAL", res.Cell(8, 2).GetString());

        var det = wb.Worksheet("Detalle");
        Assert.Equal(4, det.RowsUsed().Count() - 1);                 // 1 Gómez + 3 Pérez (sin cabecera)
        Assert.Equal("Gómez, Rosa", det.Cell(2, 2).GetString());
        Assert.Equal("10:59", det.Cell(3, 5).GetString());           // primera fila de Pérez: 26-sep
        Assert.Equal(5, det.Cell(3, 9).GetValue<double>());          // HE 50 liq.
        Assert.Equal(1, det.Cell(3, 10).GetValue<double>());         // HE 100 liq.
        Assert.Equal(6.14, det.Cell(3, 11).GetValue<double>(), 2);
    }

    [Fact]
    public async Task Excel_respeta_el_filtro_de_area()
    {
        var svc = await SetupAsync(nameof(Excel_respeta_el_filtro_de_area));
        var bytes = await svc.ExcelMensualAsync(2026, 10, area: "Logística");

        using var ms = new MemoryStream(bytes);
        using var wb = new ClosedXML.Excel.XLWorkbook(ms);
        var res = wb.Worksheet("Resumen");
        Assert.Equal("Gómez, Rosa", res.Cell(5, 2).GetString());
        Assert.Equal("TOTAL", res.Cell(6, 2).GetString());
        Assert.Contains("Logística", res.Cell(1, 1).GetString());
    }

    // Regla RRHH (27-oct-2026): las extras van desde el fin del horario; Humand les descuenta la
    // tardanza y acá se la devuelve. Casos reales de Romero Christian.
    [Theory]
    [InlineData(8, 14, 0, 1.76, 2)]   // 30-sep: entra 08:14, sale 18:00 ⇒ Humand 1,76; RRHH 2
    [InlineData(8, 7, 0, 2.73, 3)]    // 5-oct: entra 08:07, sale 18:50 ⇒ Humand 2,73; RRHH 3
    [InlineData(8, 0, 0, 4.8, 5)]     // puntual: no cambia
    [InlineData(7, 55, 0, 1.76, 1)]   // llegó antes: no se toca lo de Humand
    [InlineData(8, 30, 0, 0, 0)]      // tarde y sin extras en Humand: no se inventan extras
    public void Extras_devuelve_la_tardanza_que_Humand_descuenta(int h, int m, int s, double humand50, double liqEsperado)
    {
        var n = new NovedadDiaria { HoraEntrada = new TimeOnly(h, m, s), HoraSalida = T(18), InicioTeorico = T(8), HorasExtras50 = humand50 };
        var (e50, e100) = HorasExtrasService.Extras(n);
        Assert.Equal(liqEsperado, HorasExtrasService.Redondear(e50));
        Assert.Equal(0, e100);
    }

    [Fact]
    public void Extras_sin_horario_teorico_quedan_como_Humand()
    {
        var n = new NovedadDiaria { HoraEntrada = T(8, 14), HoraSalida = T(18), HorasExtras50 = 1.76 };
        Assert.Equal(1.76, HorasExtrasService.Extras(n).E50, 3);
    }

    // Regla RRHH (oct-2026): por día, 48 min o más de fracción suman una hora; menos se descarta.
    // Casos reales de Derlis Von Brand (20 y 21-oct) + bordes.
    [Theory]
    [InlineData(4.3, 4)]    // 4h18
    [InlineData(4.56, 4)]   // 4h34
    [InlineData(3.89, 4)]   // 3h53
    [InlineData(4.8, 5)]    // 4h48 justo en el borde
    [InlineData(4.79, 5)]   // 4h47m24s: el minuto empezado cuenta ⇒ 48
    [InlineData(4.78, 4)]   // 4h46m48s ⇒ 47
    [InlineData(6.79, 7)]   // caso real Vazquez sáb 3-oct: fichada 06:12 → 13:00 = 6h48
    [InlineData(0.5, 0)]    // 30 min
    [InlineData(0.8, 1)]    // 48 min
    [InlineData(2.1, 2)]
    [InlineData(5, 5)]
    [InlineData(0, 0)]
    public void Redondeo_de_48_minutos_por_dia(double horas, double esperado) =>
        Assert.Equal(esperado, HorasExtrasService.Redondear(horas));
}
