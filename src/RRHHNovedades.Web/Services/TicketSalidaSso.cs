using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text;

namespace RRHHNovedades.Web.Services;

/// <summary>
/// Ticket SSO de SALIDA hacia Sueldos RRHH: a Sueldos se entra solo desde Novedades, ya logueado
/// con el usuario de esta sesión. Contrato: HS256, aud = app destino, dni, iat/exp (120 s), jti hex
/// de un solo uso y next. El destino valida firma/aud/jti y decide quién entra por su tabla de
/// usuarios (DNI).
/// </summary>
public static class TicketSalidaSso
{
    public const int VidaSegundos = 120;

    public static string Crear(string secreto, string audience, string dni, DateTime ahoraUtc, string next = "/")
    {
        if (Encoding.UTF8.GetByteCount(secreto) < 32)
            throw new ArgumentException("El secreto SSO debe tener al menos 32 caracteres.", nameof(secreto));
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Audience = audience,
            IssuedAt = ahoraUtc,
            NotBefore = ahoraUtc,
            Expires = ahoraUtc.AddSeconds(VidaSegundos),
            Claims = new Dictionary<string, object>
            {
                ["dni"] = dni,
                ["jti"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
                ["next"] = next,
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secreto)), SecurityAlgorithms.HmacSha256),
        });
    }
}
