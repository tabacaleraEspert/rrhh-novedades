namespace RRHHNovedades.Web.Models;

/// <summary>Teléfono del listado al que el bot envía los partes diarios (equipo RRHH).</summary>
public class DestinatarioParte
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    /// <summary>Número en formato E.164, ej. "+5491122334455". Se normaliza a "whatsapp:" al enviar.</summary>
    public string Telefono { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
}
