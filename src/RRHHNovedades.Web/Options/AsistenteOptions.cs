namespace RRHHNovedades.Web.Options;

/// <summary>
/// Asistente IA de consultas (chat sobre los datos del tablero, API de OpenAI).
/// Sin ApiKey el asistente queda deshabilitado y el resto del tablero funciona normal.
/// </summary>
public class AsistenteOptions
{
    public const string SectionName = "Asistente";

    public string ApiKey { get; set; } = string.Empty;
    public string Modelo { get; set; } = "gpt-5.2";

    /// <summary>Vueltas máximas del loop de herramientas; la última es siempre de cierre (sin tools).</summary>
    public int MaxVueltas { get; set; } = 4;

    /// <summary>Presupuesto total de un turno. Pasado este tiempo se corta con lo que haya.</summary>
    public int TimeoutSegundos { get; set; } = 90;

    /// <summary>Pasado este umbral no se ejecutan más herramientas (se responde "SIN TIEMPO").</summary>
    public int SegundosSinHerramientas { get; set; } = 70;

    public int MaxMensajesHistorial { get; set; } = 24;
    public int MaxCharsMensaje { get; set; } = 4000;

    /// <summary>Rate limit por usuario contando filas de AsistenteTurnos (falla cerrada).</summary>
    public int RateLimitCantidad { get; set; } = 20;
    public int RateLimitVentanaMinutos { get; set; } = 5;

    /// <summary>Tope de compactación de resultados de herramientas.</summary>
    public int MaxFilasResultado { get; set; } = 150;
    public int MaxCharsResultado { get; set; } = 12000;

    /// <summary>Presupuesto de la vuelta de cierre. Incluye tokens de razonamiento del modelo:
    /// con un valor chico el modelo "piensa" todo el presupuesto y no escribe nada.</summary>
    public int MaxTokensCierre { get; set; } = 4000;

    /// <summary>Presupuesto de las vueltas con herramientas (la salida es corta: tool calls).</summary>
    public int MaxTokensVuelta { get; set; } = 2000;

    // Tarifas por millón de tokens (USD) para calcular el costo al cerrar el turno.
    // Si se cambia el modelo, actualizar acá; el histórico guarda el costo ya calculado.
    public decimal PrecioInputPorMTok { get; set; } = 1.75m;
    public decimal PrecioOutputPorMTok { get; set; } = 14m;
    public decimal PrecioCachePorMTok { get; set; } = 0.175m;

    public bool Habilitado => !string.IsNullOrWhiteSpace(ApiKey);
}
