namespace RRHHNovedades.Web.Services;

public record ResultadoEnvio(bool Exito, string? MessageSid, string? Error);

public interface ITwilioService
{
    /// <summary>Envía un mensaje de texto de WhatsApp (modo dev / sin template).</summary>
    Task<ResultadoEnvio> EnviarMensajeAsync(string toNumber, string body, CancellationToken ct = default);

    /// <summary>Envía un mensaje de WhatsApp usando un Content Template (HX...) con variables.</summary>
    Task<ResultadoEnvio> EnviarTemplateAsync(string toNumber, string contentSid, IReadOnlyDictionary<string, string> variables, CancellationToken ct = default);
}
