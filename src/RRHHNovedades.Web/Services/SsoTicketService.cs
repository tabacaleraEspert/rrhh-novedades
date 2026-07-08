using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;
using RRHHNovedades.Web.Options;
using System.Text;

namespace RRHHNovedades.Web.Services;

/// <summary>
/// Autologin (SSO) desde el Command Center: valida el ticket JWT de un solo uso y devuelve
/// el usuario correspondiente. Contrato del ticket: HS256 con secret compartido, claims
/// dni / aud / iat / exp (vida corta) / jti (único, se quema aunque el login falle).
/// </summary>
public interface ISsoTicketService
{
    /// <summary>
    /// Valida el ticket, consume (quema) su jti y devuelve el usuario si todo está OK.
    /// Devuelve null ante CUALQUIER fallo — el motivo se loguea pero no se expone al cliente,
    /// que siempre recibe un 401 genérico.
    /// </summary>
    Task<Usuario?> ValidarYConsumirAsync(string ticket, CancellationToken ct = default);
}

public class SsoTicketService(
    IDbContextFactory<AppDbContext> dbFactory,
    IOptions<SsoOptions> options,
    ILogger<SsoTicketService> logger) : ISsoTicketService
{
    // Un secret HMAC más corto que el hash (32 bytes) es fuerza-brutable; mejor no arrancar el SSO.
    private const int MinSecretChars = 32;

    public async Task<Usuario?> ValidarYConsumirAsync(string ticket, CancellationToken ct = default)
    {
        var opt = options.Value;
        if (string.IsNullOrWhiteSpace(ticket)) return null;
        if (opt.SharedSecret.Length < MinSecretChars)
        {
            logger.LogWarning("SSO: intento de login con SSO no configurado (SharedSecret ausente o corto)");
            return null;
        }

        // 1) Validación criptográfica: firma HS256 (algoritmo fijo para evitar confusión alg),
        //    audience y vencimiento con tolerancia de reloj.
        var handler = new JsonWebTokenHandler { MapInboundClaims = false };
        var result = await handler.ValidateTokenAsync(ticket, new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = true,
            ValidAudience = opt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opt.SharedSecret)),
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(opt.ClockSkewSegundos),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        });
        if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt)
        {
            logger.LogWarning("SSO: ticket rechazado en validación (firma/aud/exp): {Motivo}",
                result.Exception?.GetType().Name ?? "desconocido");
            return null;
        }

        // 2) Reglas del contrato: jti presente y acotado, iat presente, vida corta, dni presente.
        var jti = jwt.Id;
        if (string.IsNullOrEmpty(jti) || jti.Length > 64)
        {
            logger.LogWarning("SSO: ticket sin jti o jti inválido");
            return null;
        }
        if (jwt.IssuedAt == default)
        {
            logger.LogWarning("SSO: ticket sin iat");
            return null;
        }
        if ((jwt.ValidTo - jwt.IssuedAt).TotalSeconds > opt.VidaMaximaSegundos)
        {
            logger.LogWarning("SSO: ticket con vida mayor a {Max}s — mal emitido", opt.VidaMaximaSegundos);
            return null;
        }
        if (!jwt.TryGetPayloadValue<string>("dni", out var dni) || string.IsNullOrWhiteSpace(dni))
        {
            logger.LogWarning("SSO: ticket sin claim dni");
            return null;
        }

        // 3) Quemar el jti ANTES de buscar el usuario: un solo uso incluso si el login falla.
        //    El insert con PK es atómico: ante una carrera, el segundo SaveChanges tira
        //    DbUpdateException (PK duplicada) y el ticket se rechaza.
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Purga oportunista de jti vencidos (volumen trivial: tickets de ~60s de vida).
        // Query + RemoveRange y no ExecuteDelete: el provider InMemory de los tests no lo soporta.
        var vencidos = await db.SsoTicketsUsados.Where(t => t.ExpiraUtc < DateTime.UtcNow).ToListAsync(ct);
        if (vencidos.Count > 0) db.SsoTicketsUsados.RemoveRange(vencidos);

        db.SsoTicketsUsados.Add(new SsoTicketUsado
        {
            Jti = jti,
            ExpiraUtc = jwt.ValidTo.AddSeconds(opt.ClockSkewSegundos)
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        // Npgsql envuelve la PK duplicada en DbUpdateException (23505); el provider InMemory de los
        // tests deja escapar la ArgumentException del diccionario interno. Ambas = jti ya usado.
        catch (Exception ex) when (ex is DbUpdateException or ArgumentException)
        {
            logger.LogWarning("SSO: jti repetido — posible replay");
            return null;
        }

        // 4) Lookup del usuario por DNI. El jti ya quedó quemado pase lo que pase.
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Dni == dni && u.Activo, ct);
        if (usuario is null)
        {
            logger.LogWarning("SSO: ticket válido pero sin usuario activo para el DNI recibido");
            return null;
        }

        logger.LogInformation("SSO: login OK para {Email}", usuario.Email);
        return usuario;
    }
}
