using RRHHNovedades.Web.Services;
using Xunit;

namespace RRHHNovedades.Tests;

public class ParteSchedulerTests
{
    [Fact]
    public void FechasResyncRetro_va_de_ayer_hacia_atras_sin_incluir_hoy()
    {
        var hoy = new DateOnly(2026, 8, 26);
        var fechas = ParteScheduler.FechasResyncRetro(hoy, 15).ToList();

        Assert.Equal(15, fechas.Count);
        Assert.Equal(new DateOnly(2026, 8, 25), fechas[0]);
        Assert.Equal(new DateOnly(2026, 8, 11), fechas[^1]);
        Assert.DoesNotContain(hoy, fechas);
    }

    [Fact]
    public void FechasResyncRetro_cero_dias_no_sincroniza_nada()
    {
        Assert.Empty(ParteScheduler.FechasResyncRetro(new DateOnly(2026, 8, 26), 0));
    }
}
