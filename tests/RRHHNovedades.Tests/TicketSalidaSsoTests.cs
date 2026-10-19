using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RRHHNovedades.Web.Services;
using System.Text;
using Xunit;

namespace RRHHNovedades.Tests;

/// <summary>
/// Ticket de salida hacia Sueldos RRHH: tiene que cumplir el contrato que valida Sueldos
/// (HS256, aud sueldos-rrhh, dni string, jti hex, exp - iat ≤ 300, next interno).
/// </summary>
public class TicketSalidaSsoTests
{
    private const string Secret = "secreto-de-test-con-mas-de-32-chars-0123456789";

    private static async Task<TokenValidationResult> Validar(string ticket, string secret = Secret, string aud = "sueldos-rrhh")
        => await new JsonWebTokenHandler().ValidateTokenAsync(ticket, new TokenValidationParameters
        {
            ValidAudience = aud,
            ValidateIssuer = false,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.Zero,
        });

    [Fact]
    public async Task Cumple_el_contrato_de_Sueldos()
    {
        var ahora = DateTime.UtcNow;
        var r = await Validar(TicketSalidaSso.Crear(Secret, "sueldos-rrhh", "41700043", ahora, nombre: "Davor", rol: "Admin"));
        Assert.True(r.IsValid);
        var t = (JsonWebToken)r.SecurityToken;
        Assert.Equal("41700043", t.GetClaim("dni").Value);
        Assert.Matches("^[0-9a-f]{32}$", t.GetClaim("jti").Value);
        Assert.Equal("/", t.GetClaim("next").Value);
        Assert.Equal("Davor", t.GetClaim("nombre").Value);
        Assert.Equal("Admin", t.GetClaim("rol").Value);
        Assert.Equal("HS256", t.Alg);
        Assert.Equal(TicketSalidaSso.VidaSegundos, (t.ValidTo - t.IssuedAt).TotalSeconds);
    }

    [Fact]
    public async Task Cada_ticket_tiene_jti_distinto()
    {
        var ahora = DateTime.UtcNow;
        var a = new JsonWebToken(TicketSalidaSso.Crear(Secret, "sueldos-rrhh", "1", ahora)).GetClaim("jti").Value;
        var b = new JsonWebToken(TicketSalidaSso.Crear(Secret, "sueldos-rrhh", "1", ahora)).GetClaim("jti").Value;
        Assert.NotEqual(a, b);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Otro_secreto_u_otra_aud_no_validan()
    {
        var ticket = TicketSalidaSso.Crear(Secret, "sueldos-rrhh", "41700043", DateTime.UtcNow);
        Assert.False((await Validar(ticket, secret: "otro-secreto-distinto-de-mas-de-32-chars-xx")).IsValid);
        Assert.False((await Validar(ticket, aud: "rrhh-novedades")).IsValid);
    }

    [Fact]
    public void Secreto_corto_se_rechaza()
        => Assert.Throws<ArgumentException>(() => TicketSalidaSso.Crear("corto", "sueldos-rrhh", "1", DateTime.UtcNow));
}
