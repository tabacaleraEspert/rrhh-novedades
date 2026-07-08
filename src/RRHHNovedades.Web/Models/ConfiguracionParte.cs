namespace RRHHNovedades.Web.Models;

/// <summary>
/// Configuración editable del bot (fila única, Id=1). Hoy: horarios de los 2 partes diarios.
/// Editable desde Configuración sin redeploy; el scheduler la lee en cada tick.
/// </summary>
public class ConfiguracionParte
{
    public int Id { get; set; }

    /// <summary>Hora de envío del parte de la mañana (HH:mm).</summary>
    public string HoraParteManana { get; set; } = "07:00";

    /// <summary>Hora de envío del parte de la tarde (HH:mm).</summary>
    public string HoraParteTarde { get; set; } = "14:00";
}
