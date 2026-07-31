using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using RRHHNovedades.Web.Options;

namespace RRHHNovedades.Web.Services.Asistente;

/// <summary>
/// Implementación del proveedor de chat sobre la API de OpenAI (SDK oficial).
/// Todo lo específico del vendor vive acá: mapeo de mensajes, tool calls fragmentadas
/// en streaming, prompt caching (clave + retención 24 h) y lectura de usage.
/// </summary>
public sealed class OpenAIProveedor(IOptions<AsistenteOptions> options) : IChatProveedor
{
    private readonly AsistenteOptions _opt = options.Value;
    private ChatClient? _client;

    private ChatClient Client => _client ??= new ChatClient(_opt.Modelo, _opt.ApiKey);

    public async Task<VueltaChat> CompletarAsync(
        IReadOnlyList<MensajeChat> mensajes,
        IReadOnlyList<DefinicionTool> tools,
        int maxTokens,
        CancellationToken ct = default)
    {
        var opciones = ArmarOpciones(tools, maxTokens, permitirTools: true);
        ChatCompletion c = await Client.CompleteChatAsync(Mapear(mensajes), opciones, ct);

        var llamadas = c.ToolCalls
            .Select(t => new LlamadaTool(t.Id, t.FunctionName, t.FunctionArguments.ToString()))
            .ToList();
        var texto = string.Concat(c.Content.Select(p => p.Text));

        return new VueltaChat(texto.Length == 0 ? null : texto, llamadas, MapearUso(c.Usage));
    }

    public async IAsyncEnumerable<CierreEvento> StreamearCierreAsync(
        IReadOnlyList<MensajeChat> mensajes,
        IReadOnlyList<DefinicionTool> tools,
        int maxTokens,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Cierre: tools declaradas (mismo prefijo de prompt → mismo caché) pero bloqueadas.
        // Sin esto el modelo sigue pidiendo herramientas en vez de escribir (lección pagada).
        var opciones = ArmarOpciones(tools, maxTokens, permitirTools: false);

        var texto = new StringBuilder();
        var uso = UsoTokens.Cero;

        await foreach (var update in Client.CompleteChatStreamingAsync(Mapear(mensajes), opciones, ct))
        {
            foreach (var parte in update.ContentUpdate)
            {
                if (parte.Text is { Length: > 0 } delta)
                {
                    texto.Append(delta);
                    yield return new CierreTexto(delta);
                }
            }
            if (update.Usage is not null)
                uso = MapearUso(update.Usage);
        }

        yield return new CierreFin(texto.ToString(), uso);
    }

    private ChatCompletionOptions ArmarOpciones(IReadOnlyList<DefinicionTool> tools, int maxTokens, bool permitirTools)
    {
        var opciones = new ChatCompletionOptions
        {
            MaxOutputTokenCount = maxTokens,
        };
        // prompt_cache_key/retention no están tipados en Chat Completions (SDK 2.12): van por el
        // escape hatch de serialización. Clave estable → requests del mismo prefijo caen en el
        // mismo nodo de caché; retención 24 h evita re-escrituras con uso esporádico.
#pragma warning disable SCME0001, OPENAI001
        opciones.Patch.Set("$.prompt_cache_key"u8, $"rrhh-{_opt.Modelo}-base");
        opciones.Patch.Set("$.prompt_cache_retention"u8, "24h");
#pragma warning restore SCME0001, OPENAI001
        // NO setear ReasoningEffort: con function tools la API lo rechaza (400, lección pagada).
        foreach (var t in tools)
            opciones.Tools.Add(ChatTool.CreateFunctionTool(t.Nombre, t.Descripcion, BinaryData.FromString(t.SchemaJson)));
        if (!permitirTools)
            opciones.ToolChoice = ChatToolChoice.CreateNoneChoice();
        return opciones;
    }

    private static UsoTokens MapearUso(ChatTokenUsage? u) => u is null
        ? UsoTokens.Cero
        : new UsoTokens(u.InputTokenCount, u.OutputTokenCount, u.InputTokenDetails?.CachedTokenCount ?? 0);

    private static List<ChatMessage> Mapear(IReadOnlyList<MensajeChat> mensajes)
    {
        var res = new List<ChatMessage>(mensajes.Count);
        foreach (var m in mensajes)
        {
            switch (m)
            {
                case MensajeSystem s:
                    res.Add(new SystemChatMessage(s.Texto));
                    break;
                case MensajeUsuario u:
                    res.Add(new UserChatMessage(u.Texto));
                    break;
                case MensajeAsistente a:
                    res.Add(new AssistantChatMessage(a.Texto));
                    break;
                case MensajeAsistenteConTools at:
                {
                    var llamadas = at.Llamadas.Select(l =>
                        ChatToolCall.CreateFunctionToolCall(l.Id, l.Nombre, BinaryData.FromString(l.ArgsJson)));
                    var msg = new AssistantChatMessage(llamadas);
                    if (!string.IsNullOrEmpty(at.Texto))
                        msg.Content.Add(ChatMessageContentPart.CreateTextPart(at.Texto));
                    res.Add(msg);
                    break;
                }
                case MensajeResultadoTool r:
                    res.Add(new ToolChatMessage(r.LlamadaId, r.Resultado));
                    break;
            }
        }
        return res;
    }
}
