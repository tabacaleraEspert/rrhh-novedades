namespace RRHHNovedades.Web.Options;

public class TwilioOptions
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    /// <summary>Número WhatsApp emisor (mismo de ChatbotCobros), formato "whatsapp:+...".</summary>
    public string WhatsAppNumber { get; set; } = string.Empty;
    /// <summary>ContentSid (HX...) del template del parte. Vacío = se envía como texto plano (dev).</summary>
    public string ContentSidParte { get; set; } = string.Empty;
}
