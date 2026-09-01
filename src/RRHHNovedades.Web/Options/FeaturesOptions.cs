namespace RRHHNovedades.Web.Options;

/// <summary>
/// Flags de las secciones NUEVAS del mega tablero (agregadas ago-2026 sobre el sistema
/// original). Cada sección nueva se puede apagar por config sin deploy: desaparece del
/// menú y su página muestra "sección deshabilitada". Las secciones históricas
/// (Dashboard, Bot, Presentismo, Nocturnidad, Ausentismo) no pasan por acá.
/// </summary>
public class FeaturesOptions
{
    public const string SectionName = "Features";

    public bool Tardanzas { get; set; } = true;
    public bool Vacaciones { get; set; } = true;
    public bool Demografia { get; set; } = true;

    /// <summary>Saldo de vacaciones (días) a partir del cual se marca advertencia (naranja).</summary>
    public int SaldoVacacionesAdvertencia { get; set; } = 21;

    /// <summary>Saldo de vacaciones (días) a partir del cual se marca riesgo (rojo).</summary>
    public int SaldoVacacionesRiesgo { get; set; } = 35;
}
