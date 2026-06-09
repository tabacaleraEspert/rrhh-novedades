using Microsoft.Extensions.Options;
using RRHHNovedades.Web.Options;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace RRHHNovedades.Web.Services;

/// <summary>
/// Envío outbound de WhatsApp vía Twilio (reutiliza la cuenta de ChatbotCobros).
/// Si no hay credenciales configuradas, loguea el mensaje en vez de enviar (modo desarrollo).
/// </summary>
public class TwilioService : ITwilioService
{
    private readonly TwilioOptions _opt;
    private readonly ILogger<TwilioService> _logger;
    private readonly bool _enabled;

    public TwilioService(IOptions<TwilioOptions> options, ILogger<TwilioService> logger)
    {
        _opt = options.Value;
        _logger = logger;
        _enabled = !string.IsNullOrWhiteSpace(_opt.AccountSid) && !string.IsNullOrWhiteSpace(_opt.AuthToken);
        if (_enabled)
            TwilioClient.Init(_opt.AccountSid, _opt.AuthToken);
    }

    public async Task<ResultadoEnvio> EnviarMensajeAsync(string toNumber, string body, CancellationToken ct = default)
    {
        if (!_enabled)
        {
            _logger.LogInformation("[Twilio DEV] a {To}:\n{Body}", toNumber, body);
            return new ResultadoEnvio(true, "DEV-" + Guid.NewGuid().ToString("N")[..8], null);
        }
        try
        {
            var msg = await MessageResource.CreateAsync(
                body: body,
                from: new PhoneNumber(_opt.WhatsAppNumber),
                to: new PhoneNumber(Normalizar(toNumber)));
            return new ResultadoEnvio(true, msg.Sid, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enviando WhatsApp a {To}", toNumber);
            return new ResultadoEnvio(false, null, ex.Message);
        }
    }

    public async Task<ResultadoEnvio> EnviarTemplateAsync(string toNumber, string contentSid, IDictionary<string, string> variables, CancellationToken ct = default)
    {
        if (!_enabled)
        {
            _logger.LogInformation("[Twilio DEV] template {Sid} a {To}: {Vars}",
                contentSid, toNumber, System.Text.Json.JsonSerializer.Serialize(variables));
            return new ResultadoEnvio(true, "DEV-" + Guid.NewGuid().ToString("N")[..8], null);
        }
        try
        {
            var msg = await MessageResource.CreateAsync(
                from: new PhoneNumber(_opt.WhatsAppNumber),
                to: new PhoneNumber(Normalizar(toNumber)),
                contentSid: contentSid,
                contentVariables: System.Text.Json.JsonSerializer.Serialize(variables));
            return new ResultadoEnvio(true, msg.Sid, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enviando template a {To}", toNumber);
            return new ResultadoEnvio(false, null, ex.Message);
        }
    }

    private static string Normalizar(string numero) =>
        numero.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase) ? numero : $"whatsapp:{numero}";
}
