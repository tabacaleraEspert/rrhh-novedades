namespace RRHHNovedades.Web.Options;

public class AsistenciaOptions
{
    public const string SectionName = "Asistencia";

    /// <summary>Zona horaria para cortes y comparaciones. Default Argentina.</summary>
    public string TimeZone { get; set; } = "America/Argentina/Buenos_Aires";

    /// <summary>Hora de envío del parte del turno mañana (HH:mm). Parametrizable.</summary>
    public string HoraParteManana { get; set; } = "07:00";

    /// <summary>Hora de envío del parte del turno tarde (HH:mm). Parametrizable.</summary>
    public string HoraParteTarde { get; set; } = "14:00";

    /// <summary>Hora límite para separar turno mañana de turno tarde según el inicio de jornada.</summary>
    public string CorteTurnoTarde { get; set; } = "13:00";

    /// <summary>Tolerancia de tardanza en minutos (informativo; el LATE lo marca Humand).</summary>
    public int ToleranciaTardanzaMin { get; set; } = 10;
}
