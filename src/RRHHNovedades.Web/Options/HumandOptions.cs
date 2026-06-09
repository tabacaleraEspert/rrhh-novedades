namespace RRHHNovedades.Web.Options;

public class HumandOptions
{
    public const string SectionName = "Humand";

    public string BaseUrl { get; set; } = "https://api-prod.humand.co/public/api/v1";
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Si es true, usa datos simulados en vez de pegarle a la API real (desarrollo).</summary>
    public bool UseMock { get; set; }

    /// <summary>Opcional: nombre de la segmentación cuyo ítem habilita el seguimiento (ej. "Turno"). Vacío = todos.</summary>
    public string? SegmentacionFiltro { get; set; }
}
