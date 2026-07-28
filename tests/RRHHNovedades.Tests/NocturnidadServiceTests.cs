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
}
