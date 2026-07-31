namespace RRHHNovedades.Web.Services.Asistente;

// ── Modelo de mensajes propio: el resto de la app no conoce el SDK del proveedor ──
// (lección Bachi: el proveedor se cambió una vez en producción; lo único que hubo que
// tocar fue el endpoint porque todo lo demás era agnóstico).

public abstract record MensajeChat;
public sealed record MensajeSystem(string Texto) : MensajeChat;
public sealed record MensajeUsuario(string Texto) : MensajeChat;
public sealed record MensajeAsistente(string Texto) : MensajeChat;

/// <summary>Turno del assistant que pide herramientas (va al historial del loop, nunca al del usuario).</summary>
public sealed record MensajeAsistenteConTools(string? Texto, IReadOnlyList<LlamadaTool> Llamadas) : MensajeChat;

/// <summary>Resultado de una herramienta. TODA llamada pedida necesita el suyo o la API rechaza el request.</summary>
public sealed record MensajeResultadoTool(string LlamadaId, string Resultado) : MensajeChat;

public sealed record LlamadaTool(string Id, string Nombre, string ArgsJson);

/// <summary>Definición de herramienta para el modelo. SchemaJson es el JSON Schema del input.</summary>
public sealed record DefinicionTool(string Nombre, string Descripcion, string SchemaJson);

public sealed record UsoTokens(int Entrada, int Salida, int Cacheados)
{
    public static readonly UsoTokens Cero = new(0, 0, 0);
    public static UsoTokens operator +(UsoTokens a, UsoTokens b) =>
        new(a.Entrada + b.Entrada, a.Salida + b.Salida, a.Cacheados + b.Cacheados);
}

/// <summary>Respuesta de una vuelta no-streaming: texto y/o pedidos de herramientas.</summary>
public sealed record VueltaChat(string? Texto, IReadOnlyList<LlamadaTool> Llamadas, UsoTokens Uso);

// Eventos de la vuelta de cierre (streaming). El consumidor hace switch con default vacío
// (dispatcher sin else): un evento nuevo no rompe a nadie.
public abstract record CierreEvento;
public sealed record CierreTexto(string Delta) : CierreEvento;
public sealed record CierreFin(string TextoCompleto, UsoTokens Uso) : CierreEvento;

public interface IChatProveedor
{
    /// <summary>Vuelta con herramientas habilitadas (no-streaming: la salida es corta).</summary>
    Task<VueltaChat> CompletarAsync(
        IReadOnlyList<MensajeChat> mensajes,
        IReadOnlyList<DefinicionTool> tools,
        int maxTokens,
        CancellationToken ct = default);

    /// <summary>Vuelta de cierre: herramientas declaradas pero bloqueadas (tool_choice none) y
    /// texto streameado. Siempre termina con <see cref="CierreFin"/>.</summary>
    IAsyncEnumerable<CierreEvento> StreamearCierreAsync(
        IReadOnlyList<MensajeChat> mensajes,
        IReadOnlyList<DefinicionTool> tools,
        int maxTokens,
        CancellationToken ct = default);
}
