using System.Text.Json;
using System.Text.Json.Serialization;

namespace RRHHNovedades.Web.Services.Asistente;

/// <summary>
/// Serializa resultados de herramientas para el modelo. El historial se reenvía completo en
/// cada vuelta del loop, así que un resultado gordo se paga N veces: las listas van en formato
/// columnar ({"columnas":[...],"filas":[[...]]}, −51 % de tokens medido en Bachi) y con tope
/// de filas/caracteres. Números y fechas SIEMPRE en cultura invariante (es-AR usa coma decimal
/// y rompería el JSON que lee el modelo).
/// Deuda heredada consciente de Bachi: las columnas salen de la PRIMERA fila; con filas
/// heterogéneas lo que no esté en la primera se pierde. Acá las filas son records homogéneos.
/// </summary>
public static class Compactador
{
    /// <summary>Nota que acompaña un resultado vacío cuando "vacío" puede significar "sin datos cargados".</summary>
    public const string NotaVacio =
        "SIN_RESULTADOS: la consulta no devolvió filas. Puede significar que no hubo casos O que el período no tiene datos sincronizados — verificá la cobertura antes de afirmar que no hubo.";

    private static readonly JsonSerializerOptions Opciones = new()
    {
        Converters = { new JsonStringEnumConverter(), new DateOnlyConverter(), new TimeOnlyConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Objeto suelto (resúmenes, fichas): JSON directo, sin forma columnar.</summary>
    public static string Objeto<T>(T valor) => JsonSerializer.Serialize(valor, Opciones);

    /// <summary>Lista → JSON columnar con tope de filas y de caracteres.</summary>
    public static string Lista<T>(IReadOnlyList<T> filas, int maxFilas = 150, int maxChars = 12000, string? notaVacio = NotaVacio)
    {
        if (filas.Count == 0) return notaVacio ?? "SIN_RESULTADOS";

        var elementos = filas.Select(f => JsonSerializer.SerializeToElement(f, Opciones)).ToList();
        var columnas = elementos[0].EnumerateObject().Select(p => p.Name).ToList();

        var corte = Math.Min(filas.Count, maxFilas);
        string json;
        while (true)
        {
            var cuerpo = new
            {
                columnas,
                filas = elementos.Take(corte)
                    .Select(e => columnas.Select(c => e.TryGetProperty(c, out var v) ? v : default).ToList())
                    .ToList(),
                nota = corte < filas.Count ? $"[truncado a {corte} filas de {filas.Count}]" : null,
            };
            json = JsonSerializer.Serialize(cuerpo, Opciones);
            // Piso corte > 1: una sola fila gigante puede superar el tope igual (deuda conocida).
            if (json.Length <= maxChars || corte <= 1) break;
            corte = Math.Max(1, corte / 2);
        }
        return json;
    }

    private sealed class DateOnlyConverter : JsonConverter<DateOnly>
    {
        public override DateOnly Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => DateOnly.Parse(r.GetString()!);
        public override void Write(Utf8JsonWriter w, DateOnly v, JsonSerializerOptions o) => w.WriteStringValue(v.ToString("yyyy-MM-dd"));
    }

    private sealed class TimeOnlyConverter : JsonConverter<TimeOnly>
    {
        public override TimeOnly Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => TimeOnly.Parse(r.GetString()!);
        public override void Write(Utf8JsonWriter w, TimeOnly v, JsonSerializerOptions o) => w.WriteStringValue(v.ToString("HH:mm"));
    }
}
