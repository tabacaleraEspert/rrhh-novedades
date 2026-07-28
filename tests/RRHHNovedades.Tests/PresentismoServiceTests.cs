using Microsoft.EntityFrameworkCore;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;
using RRHHNovedades.Web.Services;
using Xunit;

namespace RRHHNovedades.Tests;

/// <summary>
/// Planilla de presentismo: buckets por día, mapeo de tipos de Humand y fórmulas de RRHH
/// (total liquidados = trabajados + feriados + licencias pagas; PPP se pierde con 1 injustificada).
/// </summary>
public class PresentismoServiceTests
{
    private static TimeOnly T(int h, int m = 0) => new(h, m);

    // ── Tipos de licencia dinámicos: nombres tal como vienen de Humand ──
    [Fact]
    public void Separa_y_normaliza_los_tipos_del_motivo()
    {
        Assert.Equal(["Vacaciones"], PresentismoService.SepararTipos("Vacaciones"));
        // Motivo combinado y con espacios (así lo guarda la ingesta cuando hay 2 solicitudes).
        Assert.Equal(["Lic. por enfermedad familiar", "Lic. por enfermedad"],
            PresentismoService.SepararTipos("Lic. por enfermedad familiar, Lic. por enfermedad "));
        Assert.Empty(PresentismoService.SepararTipos(null));
    }

    [Theory]
    [InlineData("Licencia sin goce de sueldo", true)]
    [InlineData("Lic. SIN GOCE", true)]
    [InlineData("Vacaciones", false)]
    [InlineData("Lic. por enfermedad", false)]
    public void Sin_goce_se_detecta_por_nombre(string tipo, bool esperado)
    {
        Assert.Equal(esperado, PresentismoService.EsSinGoce(tipo));
    }

    [Fact]
    public void Feriados_de_appsettings_se_parsean_y_los_invalidos_se_ignoran()
    {
        var set = IngestaService.FeriadosConfigurados(["2026-07-09", "basura", "2026-12-25"]);
        Assert.Equal(2, set.Count);
        Assert.Contains(new DateOnly(2026, 7, 9), set);
    }

    // ── Reporte con DB en memoria ──

    private sealed class InMemoryFactory(string dbName) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            var opt = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(dbName).Options;
            return new AppDbContext(opt);
        }
    }

    private static async Task<PresentismoService> SetupAsync(string db)
    {
        var factory = new InMemoryFactory(db);
        await using var ctx = factory.CreateDbContext();
        ctx.Empleados.Add(new Empleado { Id = 1, Nombre = "Nadia", Apellido = "Molina", Area = "Producción", EmployeeInternalId = "1", Legajo = "871" });

        NovedadDiaria Dia(int d, EstadoJornada e, string? motivo = null, bool feriado = false, TimeOnly? ent = null, TimeOnly? sal = null) =>
            new() { EmpleadoId = 1, Fecha = new DateOnly(2026, 7, d), Estado = e, MotivoNovedad = motivo, EsFeriado = feriado, HoraEntrada = ent, HoraSalida = sal };

        ctx.Novedades.AddRange(
            Dia(1, EstadoJornada.Presente, ent: T(22), sal: T(6)),   // trabajado (nocturno: 8 hs)
            Dia(2, EstadoJornada.Tarde, ent: T(8, 30), sal: T(17)),  // trabajado
            Dia(9, EstadoJornada.FrancoNoLaborable, feriado: true),  // feriado
            Dia(10, EstadoJornada.AusenteInjustificado),             // injustificada
            Dia(13, EstadoJornada.AusenteJustificado, "Vacaciones"),
            Dia(14, EstadoJornada.AusenteJustificado, "Vacaciones"),
            Dia(15, EstadoJornada.AusenteJustificado, "Lic. por enfermedad "),
            Dia(20, EstadoJornada.FrancoNoLaborable));               // franco: no cuenta
        await ctx.SaveChangesAsync();

        return new PresentismoService(factory, new NocturnidadService(factory));
    }

    [Fact]
    public async Task Fila_completa_con_columnas_dinamicas_ppp_y_totales()
    {
        var svc = await SetupAsync(nameof(Fila_completa_con_columnas_dinamicas_ppp_y_totales));
        var reporte = await svc.ReporteMensualAsync(2026, 7);
        var fila = Assert.Single(reporte.Filas);

        // Columnas dinámicas: los tipos tal como están cargados en Humand, normalizados.
        Assert.Equal(["Lic. por enfermedad", "Vacaciones"], reporte.TiposLicencia);
        Assert.Equal("871", fila.Legajo);
        Assert.Equal(30 - 1 - 4, fila.Trabajados);             // base 30 − feriado − 4 ausencias (fichadas no importan)
        Assert.Equal(1, fila.Feriados);
        Assert.Equal(1, fila.Injustificadas);
        Assert.Equal(2, fila.Licencias["Vacaciones"]);
        Assert.Equal(1, fila.Licencias["Lic. por enfermedad"]);
        Assert.Equal(8, fila.HorasNocturnas);                  // la noche del 1/7 (22→06)
        Assert.Equal("DESCONTAR", fila.Ppp);                   // tuvo 1 injustificada
        Assert.Equal(4, fila.TotalInasistencia);               // 1 injust + 2 vac + 1 enf
        Assert.Equal(30 - 1, fila.TotalLiquidados);            // base 30 − injustificada (justificadas se pagan)
        Assert.Contains("Vacaciones 13/07 al 14/07", fila.Observacion);
        Assert.Contains("Injustificada 10/07", fila.Observacion);
    }

    [Fact]
    public async Task Sin_injustificadas_el_ppp_es_Si()
    {
        var factory = new InMemoryFactory(nameof(Sin_injustificadas_el_ppp_es_Si));
        await using (var ctx = factory.CreateDbContext())
        {
            ctx.Empleados.Add(new Empleado { Id = 1, Nombre = "A", Apellido = "B", EmployeeInternalId = "1" });
            ctx.Novedades.Add(new NovedadDiaria { EmpleadoId = 1, Fecha = new(2026, 7, 1), Estado = EstadoJornada.Presente });
            await ctx.SaveChangesAsync();
        }
        var svc = new PresentismoService(factory, new NocturnidadService(factory));

        var fila = Assert.Single((await svc.ReporteMensualAsync(2026, 7)).Filas);
        Assert.Equal("Si", fila.Ppp);
        Assert.Equal(30, fila.Trabajados);       // sin ausencias ni feriados: base completa
        Assert.Equal(30, fila.TotalLiquidados);
        Assert.Equal(0, fila.TotalInasistencia);
    }

    [Fact]
    public async Task Excel_respeta_el_formato_de_la_planilla_de_rrhh()
    {
        var svc = await SetupAsync(nameof(Excel_respeta_el_formato_de_la_planilla_de_rrhh));
        var bytes = await svc.ExcelMensualAsync(2026, 7);

        using var ms = new MemoryStream(bytes);
        using var wb = new ClosedXML.Excel.XLWorkbook(ms);
        var ws = wb.Worksheet("07-2026");
        // Columnas: 5 fijas + 2 tipos dinámicos (enfermedad, vacaciones) + 5 fijas = 12.
        Assert.Equal("LEGAJOS", ws.Cell(1, 1).GetString());
        Assert.Equal("LIC. POR ENFERMEDAD", ws.Cell(1, 6).GetString());
        Assert.Equal("VACACIONES", ws.Cell(1, 7).GetString());
        Assert.Equal("TOTAL DIAS LIQUIDADOS", ws.Cell(1, 12).GetString());
        Assert.Equal("871", ws.Cell(2, 1).GetString());
        Assert.Equal(25, ws.Cell(2, 3).GetValue<int>());       // trabajados = 30 − 1 feriado − 4 ausencias
        Assert.Equal(2, ws.Cell(2, 7).GetValue<int>());        // vacaciones
        Assert.Equal("DESCONTAR", ws.Cell(2, 10).GetString()); // PPP
        Assert.Equal(29, ws.Cell(2, 12).GetValue<int>());      // días liquidados = 30 − 1 injustificada
    }
}
