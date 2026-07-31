using System.Text.Json;

namespace RRHHNovedades.Web.Services.Asistente.Herramientas;

/// <summary>
/// Herramienta tipada del asistente. La etiqueta de UI vive en la definición (en Bachi estaba
/// duplicada en mapas aparte y una herramienta nueva obligaba a tocar tres lados).
/// </summary>
public interface IAsistenteTool
{
    string Nombre { get; }

    /// <summary>Descripción para el modelo: qué hace y CUÁNDO usarla.</summary>
    string Descripcion { get; }

    /// <summary>Feedback en el chat mientras ejecuta ("revisando el ausentismo del período…").</summary>
    string Etiqueta { get; }

    /// <summary>JSON Schema del input (objeto raíz con properties/required).</summary>
    string SchemaJson { get; }

    Task<string> EjecutarAsync(JsonElement args, CancellationToken ct = default);
}

/// <summary>
/// Registro de herramientas: definiciones para el modelo + ejecución segura.
/// Los errores vuelven como texto "ERROR: ..." en el tool_result (nunca una excepción que
/// corte el turno): el modelo los lee y se autocorrige o se lo explica al usuario.
/// </summary>
public sealed class AsistenteToolRegistry(IEnumerable<IAsistenteTool> tools, ILogger<AsistenteToolRegistry> logger)
{
    private readonly IReadOnlyList<IAsistenteTool> _tools = tools.ToList();

    public IReadOnlyList<DefinicionTool> Definiciones() =>
        _tools.Select(t => new DefinicionTool(t.Nombre, t.Descripcion, t.SchemaJson)).ToList();

    public string EtiquetaDe(string nombre) =>
        _tools.FirstOrDefault(t => t.Nombre == nombre)?.Etiqueta ?? "consultando los datos…";

    public async Task<string> EjecutarAsync(LlamadaTool llamada, CancellationToken ct = default)
    {
        var tool = _tools.FirstOrDefault(t => t.Nombre == llamada.Nombre);
        if (tool is null)
            return $"ERROR: no existe la herramienta '{llamada.Nombre}'.";

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(llamada.ArgsJson) ? "{}" : llamada.ArgsJson);
            return await tool.EjecutarAsync(doc.RootElement.Clone(), ct);
        }
        catch (OperationCanceledException)
        {
            throw; // el corte por tiempo lo maneja el loop (responde "SIN TIEMPO")
        }
        catch (JsonException)
        {
            return "ERROR: los argumentos no son JSON válido.";
        }
        catch (ArgumentException ex)
        {
            return $"ERROR: {ex.Message}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Herramienta {Tool} falló", llamada.Nombre);
            return $"ERROR: la herramienta falló ({ex.GetType().Name}). Probá con otros argumentos o avisale al usuario.";
        }
    }
}

/// <summary>Lectura de argumentos del modelo: faltantes/ inválidos → ArgumentException con mensaje claro.</summary>
internal static class Args
{
    public static string Texto(JsonElement args, string nombre)
    {
        if (!args.TryGetProperty(nombre, out var v) || v.ValueKind != JsonValueKind.String || v.GetString() is not { Length: > 0 } s)
            throw new ArgumentException($"falta el argumento '{nombre}' (string).");
        return s;
    }

    public static string? TextoOpcional(JsonElement args, string nombre) =>
        args.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    public static int Entero(JsonElement args, string nombre)
    {
        if (!args.TryGetProperty(nombre, out var v) || !v.TryGetInt32(out var i))
            throw new ArgumentException($"falta el argumento '{nombre}' (entero).");
        return i;
    }

    public static int? EnteroOpcional(JsonElement args, string nombre) =>
        args.TryGetProperty(nombre, out var v) && v.TryGetInt32(out var i) ? i : null;

    public static DateOnly Fecha(JsonElement args, string nombre)
    {
        var s = Texto(args, nombre);
        if (!DateOnly.TryParseExact(s, "yyyy-MM-dd", out var f))
            throw new ArgumentException($"'{nombre}' debe ser una fecha yyyy-MM-dd (recibí '{s}').");
        return f;
    }

    public static DateOnly? FechaOpcional(JsonElement args, string nombre)
    {
        var s = TextoOpcional(args, nombre);
        if (s is null) return null;
        if (!DateOnly.TryParseExact(s, "yyyy-MM-dd", out var f))
            throw new ArgumentException($"'{nombre}' debe ser una fecha yyyy-MM-dd (recibí '{s}').");
        return f;
    }

    public static bool Booleano(JsonElement args, string nombre, bool porDefecto) =>
        args.TryGetProperty(nombre, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean() : porDefecto;
}
