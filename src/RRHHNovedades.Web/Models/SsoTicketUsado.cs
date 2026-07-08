namespace RRHHNovedades.Web.Models;

/// <summary>
/// Jti de tickets SSO ya consumidos (un solo uso). Un ticket se quema aunque el login falle;
/// las filas vencidas se purgan oportunistamente en cada validación.
/// </summary>
public class SsoTicketUsado
{
    public string Jti { get; set; } = string.Empty; // PK, máx 64

    /// <summary>Vencimiento del ticket (exp + skew), en UTC — no usar IReloj acá: Npgsql exige Kind.Utc.</summary>
    public DateTime ExpiraUtc { get; set; }
}
