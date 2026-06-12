using RRHHNovedades.Web.Models;
using RRHHNovedades.Web.Services;
using Xunit;

namespace RRHHNovedades.Tests;

/// <summary>
/// Casos congelados de la verificación contra datos REALES de Humand (jun-2026).
/// Si Humand cambia su comportamiento o alguien toca el orden de las reglas, esto salta.
/// </summary>
public class ClasificadorJornadaTests
{
    private static readonly DateOnly Fecha = new(2026, 6, 9);

    private static JornadaHumand Jornada(
        bool isWorkday = true, bool hasSchedule = true,
        string[]? incidences = null, string[]? permisos = null,
        TimeOnly? entrada = null, TimeOnly? salida = null, TimeOnly? inicioTeorico = null) =>
        new("EMP-1", Fecha, isWorkday, hasSchedule,
            incidences ?? [], permisos ?? [], entrada, salida, inicioTeorico);

    [Fact]
    public void Ficho_en_horario_es_Presente()
    {
        var (estado, _, _) = IngestaService.Clasificar(
            Jornada(entrada: new TimeOnly(7, 58), inicioTeorico: new TimeOnly(8, 0)));
        Assert.Equal(EstadoJornada.Presente, estado);
    }

    [Fact]
    public void LATE_es_Tarde_con_minutos_calculados()
    {
        var (estado, _, min) = IngestaService.Clasificar(
            Jornada(incidences: ["LATE"], entrada: new TimeOnly(8, 25), inicioTeorico: new TimeOnly(8, 0)));
        Assert.Equal(EstadoJornada.Tarde, estado);
        Assert.Equal(25, min);
    }

    [Fact]
    public void ABSENT_sin_permiso_es_AusenteInjustificado()
    {
        var (estado, motivo, _) = IngestaService.Clasificar(Jornada(incidences: ["ABSENT"]));
        Assert.Equal(EstadoJornada.AusenteInjustificado, estado);
        Assert.Null(motivo);
    }

    [Fact]
    public void Caso_real_vacaciones_Humand_quita_el_horario_y_NO_marca_ABSENT()
    {
        // Caso Miño (verificado en prod): con vacaciones aprobadas, Humand devuelve
        // isWorkday=false, hasSchedule=false, incidences=[] y el permiso embebido.
        // La regla de permiso debe evaluarse ANTES que la de franco, si no cae como Franco
        // y desaparece de los Justificados del parte.
        var (estado, motivo, _) = IngestaService.Clasificar(
            Jornada(isWorkday: false, hasSchedule: false, permisos: ["Vacaciones"]));
        Assert.Equal(EstadoJornada.AusenteJustificado, estado);
        Assert.Equal("Vacaciones", motivo);
    }

    [Fact]
    public void ABSENT_con_permiso_tambien_es_Justificado()
    {
        var (estado, motivo, _) = IngestaService.Clasificar(
            Jornada(incidences: ["ABSENT"], permisos: ["Certificado médico"]));
        Assert.Equal(EstadoJornada.AusenteJustificado, estado);
        Assert.Equal("Certificado médico", motivo);
    }

    [Fact]
    public void Sin_turno_y_sin_permiso_es_Franco()
    {
        var (estado, _, _) = IngestaService.Clasificar(Jornada(isWorkday: false, hasSchedule: false));
        Assert.Equal(EstadoJornada.FrancoNoLaborable, estado);
    }

    [Fact]
    public void Con_permiso_pero_ficho_igual_es_Presente()
    {
        // Trabajó pese al permiso (ej. permiso de medio día): cuenta como presente, con motivo.
        var (estado, motivo, _) = IngestaService.Clasificar(
            Jornada(permisos: ["Salida anticipada"], entrada: new TimeOnly(8, 0)));
        Assert.Equal(EstadoJornada.Presente, estado);
        Assert.Equal("Salida anticipada", motivo);
    }

    [Fact]
    public void Laborable_sin_fichada_sin_ABSENT_y_sin_permiso_es_Injustificado()
    {
        var (estado, _, _) = IngestaService.Clasificar(Jornada());
        Assert.Equal(EstadoJornada.AusenteInjustificado, estado);
    }

    [Fact]
    public void LATE_sin_horario_teorico_es_Tarde_con_cero_minutos()
    {
        var (estado, _, min) = IngestaService.Clasificar(
            Jornada(incidences: ["LATE"], entrada: new TimeOnly(8, 40)));
        Assert.Equal(EstadoJornada.Tarde, estado);
        Assert.Equal(0, min);
    }
}
