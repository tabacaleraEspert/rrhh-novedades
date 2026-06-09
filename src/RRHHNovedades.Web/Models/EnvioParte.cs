namespace RRHHNovedades.Web.Models;

/// <summary>Registro de cada parte enviado (auditoría básica del bot).</summary>
public class EnvioParte
{
    public int Id { get; set; }
    public DateOnly Fecha { get; set; }
    public Turno Turno { get; set; }
    public string Telefono { get; set; } = string.Empty;
    public string? MessageSid { get; set; }
    public bool Exito { get; set; }
    public string? Error { get; set; }
    /// <summary>Texto del cuerpo enviado (para trazabilidad).</summary>
    public string? Cuerpo { get; set; }
    public DateTime EnviadoUtc { get; set; }
}
