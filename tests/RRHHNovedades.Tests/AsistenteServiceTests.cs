using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RRHHNovedades.Web.Options;
using RRHHNovedades.Web.Services;
using RRHHNovedades.Web.Services.Asistente;
using RRHHNovedades.Web.Services.Asistente.Herramientas;
using Xunit;

namespace RRHHNovedades.Tests;

/// <summary>
/// El loop del asistente con un proveedor de chat guionado (sin API real): guardas en orden,
/// rate limit falla-cerrada, toda tool_call recibe su tool_result, cierre con herramientas
/// bloqueadas al agotar vueltas, nunca burbuja vacía y cierre del turno SIEMPRE.
/// </summary>
public class AsistenteServiceTests
{
    // ── Proveedor guionado ──

    private sealed class ProveedorGuionado : IChatProveedor
    {
        private readonly Queue<VueltaChat> _vueltas = new();
        public string? TextoCierre { get; set; } = "Respuesta final.";
        public List<IReadOnlyList<MensajeChat>> RequestsCompletar { get; } = [];
        public List<IReadOnlyList<MensajeChat>> RequestsCierre { get; } = [];

        public void EncolarVuelta(VueltaChat v) => _vueltas.Enqueue(v);

        public Task<VueltaChat> CompletarAsync(IReadOnlyList<MensajeChat> mensajes, IReadOnlyList<DefinicionTool> tools, int maxTokens, CancellationToken ct = default)
        {
            RequestsCompletar.Add(mensajes.ToList());
            if (_vueltas.Count == 0)
                throw new InvalidOperationException("el guion no esperaba otra vuelta");
            return Task.FromResult(_vueltas.Dequeue());
        }

        public async IAsyncEnumerable<CierreEvento> StreamearCierreAsync(IReadOnlyList<MensajeChat> mensajes, IReadOnlyList<DefinicionTool> tools, int maxTokens, [EnumeratorCancellation] CancellationToken ct = default)
        {
            RequestsCierre.Add(mensajes.ToList());
            await Task.Yield();
            if (TextoCierre is { Length: > 0 })
            {
                yield return new CierreTexto(TextoCierre);
                yield return new CierreFin(TextoCierre, new UsoTokens(100, 20, 50));
            }
            else
            {
                yield return new CierreFin("", new UsoTokens(100, 0, 50));
            }
        }
    }

    private sealed class ToolEco : IAsistenteTool
    {
        public List<string> Ejecuciones { get; } = [];
        public string Nombre => "eco";
        public string Descripcion => "test";
        public string Etiqueta => "haciendo eco…";
        public string SchemaJson => """{"type":"object","properties":{}}""";
        public Task<string> EjecutarAsync(JsonElement args, CancellationToken ct = default)
        {
            Ejecuciones.Add(args.GetRawText());
            return Task.FromResult("resultado-eco");
        }
    }

    private sealed class RelojFijo : IReloj
    {
        public DateTimeOffset Ahora => new(2026, 7, 30, 12, 0, 0, TimeSpan.FromHours(-3));
        public DateOnly Hoy => new(2026, 7, 30);
        public TimeOnly HoraActual => new(12, 0);
        public DateTime EnLocal(DateTime utc) => utc;
    }

    private static (AsistenteService svc, ProveedorGuionado prov, ToolEco tool, IRegistroTurnosService reg) Armar(
        string apiKey = "sk-test", int? consultasRecientes = 0)
    {
        var opt = Microsoft.Extensions.Options.Options.Create(new AsistenteOptions { ApiKey = apiKey });
        var humand = Microsoft.Extensions.Options.Options.Create(new HumandOptions());
        var reloj = new RelojFijo();

        var prov = new ProveedorGuionado();
        var tool = new ToolEco();
        var registry = new AsistenteToolRegistry([tool], NullLogger<AsistenteToolRegistry>.Instance);

        var env = Substitute.For<IWebHostEnvironment>();
        env.ContentRootPath.Returns(Path.Combine(Path.GetTempPath(), "no-existe-" + Guid.NewGuid()));
        var prompts = new PromptBuilder(env, NullLogger<PromptBuilder>.Instance);

        var estado = new AsistenteEstado(reloj, opt);

        var reg = Substitute.For<IRegistroTurnosService>();
        reg.ConsultasRecientesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(consultasRecientes);
        reg.AbrirAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(7);
        reg.CalcularCosto(Arg.Any<UsoTokens>()).Returns(0.01m);

        var svc = new AsistenteService(prov, registry, prompts, estado, reg, reloj, opt, humand,
            NullLogger<AsistenteService>.Instance);
        return (svc, prov, tool, reg);
    }

    private static async Task<List<AsistenteEvento>> Correr(AsistenteService svc, string pregunta = "¿quién faltó?")
    {
        var eventos = new List<AsistenteEvento>();
        await foreach (var e in svc.PreguntarAsync(pregunta, usuarioId: 1, nombreUsuario: "Ana Prueba"))
            eventos.Add(e);
        return eventos;
    }

    // ── Guardas ──

    [Fact]
    public async Task Sin_api_key_devuelve_error_y_no_llama_al_proveedor()
    {
        var (svc, prov, _, reg) = Armar(apiKey: "");
        var eventos = await Correr(svc);

        Assert.IsType<EventoError>(Assert.Single(eventos));
        Assert.Empty(prov.RequestsCompletar);
        await reg.DidNotReceive().AbrirAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rate_limit_falla_cerrada_cuando_no_se_puede_contar()
    {
        var (svc, prov, _, _) = Armar(consultasRecientes: null);
        var eventos = await Correr(svc);

        Assert.IsType<EventoError>(Assert.Single(eventos));
        Assert.Empty(prov.RequestsCompletar);
    }

    [Fact]
    public async Task Rate_limit_alcanzado_rechaza()
    {
        var (svc, prov, _, _) = Armar(consultasRecientes: 20);
        var eventos = await Correr(svc);

        var err = Assert.IsType<EventoError>(Assert.Single(eventos));
        Assert.Contains("límite", err.Mensaje);
        Assert.Empty(prov.RequestsCompletar);
    }

    // ── Loop ──

    [Fact]
    public async Task Tool_call_se_ejecuta_y_su_resultado_vuelve_al_modelo()
    {
        var (svc, prov, tool, _) = Armar();
        prov.EncolarVuelta(new VueltaChat(null, [new LlamadaTool("c1", "eco", "{}")], new UsoTokens(10, 5, 0)));
        prov.EncolarVuelta(new VueltaChat("Faltaron 2 personas.", [], new UsoTokens(10, 5, 0)));

        var eventos = await Correr(svc);

        Assert.Single(tool.Ejecuciones);
        Assert.Contains(eventos, e => e is EventoHerramienta h && h.Etiqueta == "haciendo eco…");
        Assert.Contains(eventos, e => e is EventoTexto t && t.Delta.Contains("Faltaron 2"));

        // El segundo request lleva el turno del assistant con las tool calls Y su tool_result.
        var segundo = prov.RequestsCompletar[1];
        Assert.Contains(segundo, m => m is MensajeAsistenteConTools);
        var resultado = Assert.IsType<MensajeResultadoTool>(segundo.Last(m => m is MensajeResultadoTool));
        Assert.Equal("c1", resultado.LlamadaId);
        Assert.Equal("resultado-eco", resultado.Resultado);
    }

    [Fact]
    public async Task Al_agotar_vueltas_va_al_cierre_con_herramientas_bloqueadas()
    {
        var (svc, prov, _, _) = Armar();
        // MaxVueltas=4 ⇒ 3 vueltas con tools; el modelo pide herramientas siempre.
        for (int i = 0; i < 3; i++)
            prov.EncolarVuelta(new VueltaChat(null, [new LlamadaTool($"c{i}", "eco", "{}")], new UsoTokens(10, 5, 0)));

        var eventos = await Correr(svc);

        Assert.Single(prov.RequestsCierre); // el cierre SIEMPRE llega
        var fin = Assert.IsType<EventoFin>(eventos.Last());
        Assert.Equal("Respuesta final.", fin.TextoCompleto);
    }

    [Fact]
    public async Task Turno_sin_texto_nunca_deja_burbuja_vacia()
    {
        var (svc, prov, _, reg) = Armar();
        prov.EncolarVuelta(new VueltaChat(null, [new LlamadaTool("c1", "eco", "{}")], new UsoTokens(10, 5, 0)));
        prov.EncolarVuelta(new VueltaChat(null, [], new UsoTokens(10, 5, 0))); // ni texto ni tools
        prov.TextoCierre = null; // y el cierre tampoco escribe

        var eventos = await Correr(svc);

        var fin = Assert.IsType<EventoFin>(eventos.Last());
        Assert.False(string.IsNullOrWhiteSpace(fin.TextoCompleto));
        await reg.Received(1).CerrarAsync(7, ok: false, Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<UsoTokens>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task El_turno_se_abre_antes_y_se_cierra_siempre_con_el_uso_sumado()
    {
        var (svc, prov, _, reg) = Armar();
        prov.EncolarVuelta(new VueltaChat(null, [new LlamadaTool("c1", "eco", "{}")], new UsoTokens(100, 10, 40)));
        prov.EncolarVuelta(new VueltaChat("Listo.", [], new UsoTokens(200, 20, 150)));

        await Correr(svc);

        await reg.Received(1).AbrirAsync(1, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await reg.Received(1).CerrarAsync(7, ok: true, null, "Listo.", 2, Arg.Any<int>(),
            new UsoTokens(300, 30, 190), Arg.Any<CancellationToken>());
        await reg.Received(1).RegistrarHerramientaAsync(7, "eco", "{}", Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Error_del_proveedor_termina_en_mensaje_y_turno_cerrado_con_error()
    {
        var (svc, prov, _, reg) = Armar();
        prov.TextoCierre = null; // sin vueltas encoladas: CompletarAsync explota; el cierre tampoco escribe

        var eventos = await Correr(svc);

        var fin = Assert.IsType<EventoFin>(eventos.Last());
        Assert.False(string.IsNullOrWhiteSpace(fin.TextoCompleto));
        await reg.Received(1).CerrarAsync(7, ok: false, Arg.Is<string?>(e => e != null),
            Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<UsoTokens>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task El_historial_del_usuario_solo_guarda_texto_final_no_turnos_intermedios()
    {
        var (svc, prov, _, _) = Armar();
        prov.EncolarVuelta(new VueltaChat(null, [new LlamadaTool("c1", "eco", "{}")], new UsoTokens(10, 5, 0)));
        prov.EncolarVuelta(new VueltaChat("Todo bien.", [], new UsoTokens(10, 5, 0)));
        await Correr(svc);

        // Segunda pregunta: el historial que ve el modelo tiene user+assistant, sin tool calls.
        prov.EncolarVuelta(new VueltaChat("Segunda.", [], new UsoTokens(10, 5, 0)));
        await Correr(svc, "¿y ayer?");

        var ultimo = prov.RequestsCompletar.Last();
        Assert.DoesNotContain(ultimo, m => m is MensajeAsistenteConTools or MensajeResultadoTool);
        Assert.Contains(ultimo, m => m is MensajeAsistente a && a.Texto == "Todo bien.");
    }

    // ── Prompt estable: precondición del caché ──

    [Fact]
    public async Task El_bloque_estable_es_byte_identico_entre_requests()
    {
        var (svc, prov, _, _) = Armar();
        prov.EncolarVuelta(new VueltaChat("Uno.", [], new UsoTokens(10, 5, 0)));
        await Correr(svc);
        prov.EncolarVuelta(new VueltaChat("Dos.", [], new UsoTokens(10, 5, 0)));
        await Correr(svc, "otra");

        var estable1 = Assert.IsType<MensajeSystem>(prov.RequestsCompletar[0][0]).Texto;
        var estable2 = Assert.IsType<MensajeSystem>(prov.RequestsCompletar[1][0]).Texto;
        Assert.Same(estable1, estable2); // misma referencia: imposible que difiera un byte
    }
}
