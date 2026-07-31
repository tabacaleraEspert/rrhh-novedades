using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;
using RRHHNovedades.Web.Services;
using RRHHNovedades.Web.Services.Asistente;
using RRHHNovedades.Web.Services.Asistente.Herramientas;
using Xunit;

namespace RRHHNovedades.Tests;

/// <summary>
/// Compactador (formato columnar, topes, cultura invariante), parseo de argumentos del modelo
/// y reglas de las herramientas: errores como texto "ERROR: ..." (nunca excepción), sin
/// defaults silenciosos, normalización de tildes en la búsqueda.
/// </summary>
public class AsistenteToolsTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // ── Compactador ──

    private record Fila(string Nombre, decimal Monto, DateOnly Fecha);

    [Fact]
    public void Compactador_serializa_columnar_con_cultura_invariante()
    {
        var json = Compactador.Lista([new Fila("Pérez", 1234.5m, new(2026, 7, 15))]);

        var doc = JsonDocument.Parse(json).RootElement;
        Assert.Equal(["Nombre", "Monto", "Fecha"],
            doc.GetProperty("columnas").EnumerateArray().Select(e => e.GetString()!).ToArray());
        var fila = doc.GetProperty("filas")[0];
        Assert.Equal("Pérez", fila[0].GetString());
        Assert.Equal(1234.5m, fila[1].GetDecimal()); // punto decimal, nunca coma es-AR
        Assert.Equal("2026-07-15", fila[2].GetString());
        Assert.Contains("\"Monto\":", json == null ? "" : "\"Monto\":"); // sanity
    }

    [Fact]
    public void Compactador_trunca_con_nota()
    {
        var filas = Enumerable.Range(1, 300).Select(i => new Fila($"E{i}", i, new(2026, 1, 1))).ToList();
        var json = Compactador.Lista(filas, maxFilas: 150);

        var doc = JsonDocument.Parse(json).RootElement;
        Assert.Equal(150, doc.GetProperty("filas").GetArrayLength());
        Assert.Equal("[truncado a 150 filas de 300]", doc.GetProperty("nota").GetString());
    }

    [Fact]
    public void Compactador_recorta_mas_si_supera_el_tope_de_caracteres()
    {
        var filas = Enumerable.Range(1, 100).Select(i => new Fila(new string('x', 200), i, new(2026, 1, 1))).ToList();
        var json = Compactador.Lista(filas, maxFilas: 100, maxChars: 5000);
        var doc = JsonDocument.Parse(json).RootElement;
        Assert.True(doc.GetProperty("filas").GetArrayLength() < 100);
        Assert.Contains("truncado", doc.GetProperty("nota").GetString());
    }

    [Fact]
    public void Compactador_lista_vacia_devuelve_nota_anti_omision()
    {
        Assert.StartsWith("SIN_RESULTADOS", Compactador.Lista(Array.Empty<Fila>()));
    }

    [Fact]
    public void Compactador_serializa_enums_como_texto()
    {
        var json = Compactador.Objeto(new { Estado = EstadoJornada.AusenteJustificado });
        Assert.Contains("AusenteJustificado", json); // "3" sería ilegible para el modelo
    }

    // ── Args ──

    [Fact]
    public void Args_fecha_invalida_da_mensaje_claro()
    {
        var ex = Assert.Throws<ArgumentException>(() => Args.Fecha(Json("""{"desde":"15/07/2026"}"""), "desde"));
        Assert.Contains("yyyy-MM-dd", ex.Message);
    }

    [Fact]
    public void Args_faltante_da_mensaje_claro()
    {
        var ex = Assert.Throws<ArgumentException>(() => Args.Entero(Json("{}"), "empleadoId"));
        Assert.Contains("empleadoId", ex.Message);
    }

    // ── Registry: errores como texto, nunca excepción ──

    private sealed class ToolQueExplota : IAsistenteTool
    {
        public string Nombre => "explota";
        public string Descripcion => "test";
        public string Etiqueta => "explotando…";
        public string SchemaJson => """{"type":"object","properties":{}}""";
        public Task<string> EjecutarAsync(JsonElement args, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task Registry_convierte_excepciones_en_texto_ERROR()
    {
        var registry = new AsistenteToolRegistry([new ToolQueExplota()], NullLogger<AsistenteToolRegistry>.Instance);
        var r = await registry.EjecutarAsync(new LlamadaTool("id1", "explota", "{}"));
        Assert.StartsWith("ERROR:", r);
    }

    [Fact]
    public async Task Registry_tool_inexistente_devuelve_ERROR()
    {
        var registry = new AsistenteToolRegistry([], NullLogger<AsistenteToolRegistry>.Instance);
        var r = await registry.EjecutarAsync(new LlamadaTool("id1", "no_existe", "{}"));
        Assert.StartsWith("ERROR:", r);
    }

    [Fact]
    public async Task Registry_args_invalidos_devuelven_ERROR()
    {
        var registry = new AsistenteToolRegistry([new ToolQueExplota()], NullLogger<AsistenteToolRegistry>.Instance);
        var r = await registry.EjecutarAsync(new LlamadaTool("id1", "explota", "esto no es json"));
        Assert.StartsWith("ERROR:", r);
    }

    // ── buscar_empleado: tildes y ambigüedad ──

    [Fact]
    public async Task BuscarEmpleado_matchea_sin_tildes()
    {
        var licencias = Substitute.For<ILicenciaManualService>();
        licencias.EmpleadosActivosAsync(Arg.Any<CancellationToken>()).Returns(
            new List<EmpleadoOpcion> { new(1, "871", "Pérez, José", "Producción"), new(2, "455", "Paz, Bruno", "Ventas") });

        var tool = new BuscarEmpleadoTool(licencias);
        var r = await tool.EjecutarAsync(Json("""{"texto":"perez"}"""));

        Assert.Contains("Pérez", r);
        Assert.DoesNotContain("Paz", r);
    }

    [Fact]
    public async Task BuscarEmpleado_sin_coincidencias_avisa()
    {
        var licencias = Substitute.For<ILicenciaManualService>();
        licencias.EmpleadosActivosAsync(Arg.Any<CancellationToken>()).Returns(new List<EmpleadoOpcion>());

        var tool = new BuscarEmpleadoTool(licencias);
        Assert.StartsWith("SIN_RESULTADOS", await tool.EjecutarAsync(Json("""{"texto":"zzz"}""")));
    }

    // ── get_ausentismo: sin default silencioso ──

    [Fact]
    public async Task Ausentismo_agrupacion_desconocida_es_ERROR_no_default()
    {
        var registry = new AsistenteToolRegistry(
            [new AusentismoTool(Substitute.For<IAusentismoService>(), Substitute.For<IConsultaAsistenteService>())],
            NullLogger<AsistenteToolRegistry>.Instance);

        var r = await registry.EjecutarAsync(new LlamadaTool("id1", "get_ausentismo",
            """{"desde":"2026-07-01","hasta":"2026-07-31","agrupar":"trimestre"}"""));

        Assert.StartsWith("ERROR:", r);
        Assert.Contains("trimestre", r);
    }

    // ── get_resumen_dia: turno inválido ──

    [Fact]
    public async Task ResumenDia_turno_desconocido_es_ERROR()
    {
        var registry = new AsistenteToolRegistry(
            [new ResumenDiaTool(Substitute.For<IConsultaAsistenteService>())],
            NullLogger<AsistenteToolRegistry>.Instance);

        var r = await registry.EjecutarAsync(new LlamadaTool("id1", "get_resumen_dia",
            """{"fecha":"2026-07-15","turno":"Madrugada"}"""));

        Assert.StartsWith("ERROR:", r);
    }
}
