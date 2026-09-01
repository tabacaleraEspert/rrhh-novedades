using RRHHNovedades.Web.Services;
using Xunit;

namespace RRHHNovedades.Tests;

/// <summary>
/// Sección Demografía (nueva, ago-2026): cálculo puro sobre los users de Humand.
/// Congela: filtro de activos, agrupaciones, buckets de antigüedad/edad, cumpleaños
/// del mes y esquema de jefes por relationships.
/// </summary>
public class DemografiaServiceTests
{
    private static readonly DateOnly Hoy = new(2026, 8, 27);

    private static EmpleadoHumand Emp(string id, string nombre = "N", string apellido = "A",
        string? area = null, string? segTurno = null, string? status = "ACTIVE",
        DateOnly? ingreso = null, DateOnly? nacimiento = null, string? jefe = null, string? sexo = null) =>
        new(id, nombre, apellido, null, area, segTurno, null,
            Status: status, FechaIngreso: ingreso, FechaNacimiento: nacimiento, JefeId: jefe, Sexo: sexo);

    [Fact]
    public void Solo_activos_cuentan_y_sin_status_se_asume_activo()
    {
        var r = DemografiaService.Calcular(
        [
            Emp("1"),
            Emp("2", status: null),          // sin status: activo
            Emp("3", status: "DEACTIVATED"),
            Emp("4", status: "UNCLAIMED"),
        ], Hoy);

        Assert.Equal(2, r.Activos);
    }

    [Fact]
    public void Sectores_ordenados_por_cantidad_y_sin_sector_agrupa()
    {
        var r = DemografiaService.Calcular(
        [
            Emp("1", area: "Producción"), Emp("2", area: "Producción"),
            Emp("3", area: "Ventas"), Emp("4", area: null),
        ], Hoy);

        Assert.Equal("Producción", r.PorSector[0].Etiqueta);
        Assert.Equal(2, r.PorSector[0].Cantidad);
        Assert.Contains(r.PorSector, d => d.Etiqueta == "Sin sector" && d.Cantidad == 1);
    }

    [Theory]
    [InlineData("Turno C Noche", "Noche")]
    [InlineData("01 - Turno Mañana 06 a 15", "Mañana")]
    [InlineData("Turno TARDE", "Tarde")]
    [InlineData(null, "Sin segmentación")]
    [InlineData("Turno A", "Turno A")] // nombre propio sin franja: tal cual
    public void Turno_se_deriva_del_nombre_de_la_segmentacion(string? seg, string esperado) =>
        Assert.Equal(esperado, DemografiaService.TurnoDe(Emp("1", segTurno: seg)));

    [Fact]
    public void Sexo_solo_aparece_si_hay_dato()
    {
        var sin = DemografiaService.Calcular([Emp("1"), Emp("2")], Hoy);
        Assert.Empty(sin.PorSexo);

        var con = DemografiaService.Calcular(
            [Emp("1", sexo: "Femenino"), Emp("2", sexo: "Femenino"), Emp("3", sexo: "Masculino"), Emp("4")], Hoy);
        Assert.Equal(2, con.PorSexo.Count);
        Assert.Equal("Femenino", con.PorSexo[0].Etiqueta); // mayoría primero
    }

    [Fact]
    public void Antiguedad_promedio_y_buckets_ignoran_a_quien_no_tiene_fecha()
    {
        var r = DemografiaService.Calcular(
        [
            Emp("1", ingreso: Hoy.AddYears(-2)),  // 1-3 años
            Emp("2", ingreso: Hoy.AddYears(-12)), // 10-20
            Emp("3"),                              // sin fecha
        ], Hoy);

        Assert.Equal(7.0, r.AntiguedadPromedioAnios);
        Assert.Equal(1, r.SinFechaIngreso);
        Assert.Contains(r.AntiguedadBuckets, b => b.Etiqueta == "1-3 años" && b.Cantidad == 1);
        Assert.Contains(r.AntiguedadBuckets, b => b.Etiqueta == "10-20 años" && b.Cantidad == 1);
        Assert.DoesNotContain(r.AntiguedadBuckets, b => b.Cantidad == 0); // buckets vacíos no salen
    }

    [Fact]
    public void Cumples_del_mes_con_edad_que_cumple()
    {
        var r = DemografiaService.Calcular(
        [
            Emp("1", nombre: "Nadia", apellido: "Molina", nacimiento: new(1990, 8, 5)),  // agosto
            Emp("2", nombre: "Bruno", apellido: "Paz", nacimiento: new(1985, 12, 25)),   // otro mes
        ], Hoy); // hoy = 27/08/2026

        var c = Assert.Single(r.CumplesDelMes);
        Assert.Equal(5, c.Dia);
        Assert.Equal("Molina, Nadia", c.ApellidoNombre);
        Assert.Equal(36, c.CumpleAnios); // 2026 - 1990
    }

    [Fact]
    public void Jefes_agrupa_reportes_y_ordena_por_cantidad()
    {
        var r = DemografiaService.Calcular(
        [
            Emp("J1", nombre: "Diego", apellido: "Fernández", area: "Producción"),
            Emp("J2", nombre: "Lucía", apellido: "Díaz", area: "Administración"),
            Emp("1", nombre: "Juan", apellido: "Pérez", jefe: "J1"),
            Emp("2", nombre: "Rosa", apellido: "Gómez", jefe: "J1"),
            Emp("3", nombre: "Pedro", apellido: "Ruiz", jefe: "J2"),
        ], Hoy);

        Assert.Equal(2, r.Jefes.Count);
        Assert.Equal("Fernández, Diego", r.Jefes[0].JefeNombre);
        Assert.Equal(2, r.Jefes[0].Cantidad);
        Assert.Equal(["Gómez, Rosa", "Pérez, Juan"], r.Jefes[0].Reportes); // alfabético
    }

    [Fact]
    public void Jefe_desconocido_muestra_el_id_sin_romper()
    {
        var r = DemografiaService.Calcular([Emp("1", jefe: "NO-EXISTE")], Hoy);

        var j = Assert.Single(r.Jefes);
        Assert.Equal("NO-EXISTE", j.JefeNombre);
        Assert.Null(j.JefeArea);
    }

    [Fact]
    public void Buckets_limite_superior_exclusivo()
    {
        var b = DemografiaService.Buckets([0.5, 1.0, 2.9, 3.0],
            [(1, "< 1"), (3, "1-3"), (double.MaxValue, "3+")]);

        Assert.Equal(1, b.Single(x => x.Etiqueta == "< 1").Cantidad);
        Assert.Equal(2, b.Single(x => x.Etiqueta == "1-3").Cantidad); // 1.0 entra acá, 3.0 no
        Assert.Equal(1, b.Single(x => x.Etiqueta == "3+").Cantidad);
    }
}
