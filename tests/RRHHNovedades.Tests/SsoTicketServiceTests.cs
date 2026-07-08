using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;
using RRHHNovedades.Web.Options;
using RRHHNovedades.Web.Services;
using System.Text;
using Xunit;

namespace RRHHNovedades.Tests;

/// <summary>
/// Contrato del ticket SSO del Command Center: HS256 con secret compartido, aud fija,
/// vida corta (exp-iat ≤ 300s), jti único de un solo uso que se quema aunque el login falle.
/// Todo fallo devuelve null (el endpoint responde 401 genérico).
/// </summary>
public class SsoTicketServiceTests
{
    // 64+ chars: alcanza para firmar HS512 en el test de algoritmo no permitido.
    private const string Secret = "secreto-de-test-con-mas-de-64-chars-0123456789-abcdefghijklmnopqrstuvwxyz";
    private const string DniValido = "30111222";
    private const string Audience = "rrhh-novedades";

    // DbContextFactory en memoria para los tests.
    private sealed class InMemoryFactory(string dbName) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(dbName).Options;
            return new AppDbContext(options);
        }

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(CreateDbContext());
    }

    private static async Task<(ISsoTicketService sso, InMemoryFactory factory)> SetupAsync(
        string db, string secret = Secret)
    {
        var factory = new InMemoryFactory(db);
        await using (var ctx = await factory.CreateDbContextAsync())
        {
            ctx.Usuarios.Add(new Usuario
            {
                Nombre = "Usuaria SSO",
                Email = "sso@tabacaleraespert.com",
                PasswordHash = "x",
                Rol = Roles.RRHH,
                Activo = true,
                Dni = DniValido
            });
            ctx.Usuarios.Add(new Usuario
            {
                Nombre = "Inactivo",
                Email = "inactivo@tabacaleraespert.com",
                PasswordHash = "x",
                Rol = Roles.RRHH,
                Activo = false,
                Dni = "20999888"
            });
            await ctx.SaveChangesAsync();
        }
        var sso = new SsoTicketService(factory,
            Microsoft.Extensions.Options.Options.Create(new SsoOptions { SharedSecret = secret }),
            NullLogger<SsoTicketService>.Instance);
        return (sso, factory);
    }

    /// <summary>Emite un ticket como lo haría el Command Center; cada parámetro permite romper una regla.</summary>
    private static string CrearTicket(
        string dni = DniValido, string? aud = Audience, string? jti = null,
        int vidaSegundos = 60, DateTime? iat = null,
        string secret = Secret, string alg = SecurityAlgorithms.HmacSha256)
    {
        var emitido = iat ?? DateTime.UtcNow;
        var claims = new Dictionary<string, object> { ["dni"] = dni };
        if (jti != "") claims["jti"] = jti ?? Guid.NewGuid().ToString("N");
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Audience = aud,
            IssuedAt = emitido,
            NotBefore = emitido,
            Expires = emitido.AddSeconds(vidaSegundos),
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), alg)
        });
    }

    [Fact]
    public async Task Ticket_valido_devuelve_el_usuario()
    {
        var (sso, _) = await SetupAsync(nameof(Ticket_valido_devuelve_el_usuario));
        var usuario = await sso.ValidarYConsumirAsync(CrearTicket());
        Assert.NotNull(usuario);
        Assert.Equal("sso@tabacaleraespert.com", usuario!.Email);
    }

    [Fact]
    public async Task Firma_con_otro_secret_rechaza()
    {
        var (sso, _) = await SetupAsync(nameof(Firma_con_otro_secret_rechaza));
        var ticket = CrearTicket(secret: "otro-secret-igual-de-largo-que-el-valido-abcdef");
        Assert.Null(await sso.ValidarYConsumirAsync(ticket));
    }

    [Fact]
    public async Task Alg_none_rechaza()
    {
        var (sso, _) = await SetupAsync(nameof(Alg_none_rechaza));
        // Token sin firma armado a mano (un handler se niega a emitirlo).
        static string B64(string json) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(json));
        var ahora = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = B64("""{"alg":"none","typ":"JWT"}""");
        var payload = B64($$"""{"dni":"{{DniValido}}","aud":"{{Audience}}","iat":{{ahora}},"exp":{{ahora + 60}},"jti":"{{Guid.NewGuid():N}}"}""");
        Assert.Null(await sso.ValidarYConsumirAsync($"{header}.{payload}."));
    }

    [Fact]
    public async Task Alg_distinto_de_HS256_rechaza()
    {
        var (sso, _) = await SetupAsync(nameof(Alg_distinto_de_HS256_rechaza));
        var ticket = CrearTicket(alg: SecurityAlgorithms.HmacSha512);
        Assert.Null(await sso.ValidarYConsumirAsync(ticket));
    }

    [Fact]
    public async Task Exp_vencido_fuera_del_skew_rechaza()
    {
        var (sso, _) = await SetupAsync(nameof(Exp_vencido_fuera_del_skew_rechaza));
        var ticket = CrearTicket(iat: DateTime.UtcNow.AddSeconds(-120), vidaSegundos: 60); // venció hace 60s
        Assert.Null(await sso.ValidarYConsumirAsync(ticket));
    }

    [Fact]
    public async Task Exp_vencido_hace_5s_acepta_por_clock_skew()
    {
        var (sso, _) = await SetupAsync(nameof(Exp_vencido_hace_5s_acepta_por_clock_skew));
        var ticket = CrearTicket(iat: DateTime.UtcNow.AddSeconds(-65), vidaSegundos: 60); // venció hace ~5s, skew 10s
        Assert.NotNull(await sso.ValidarYConsumirAsync(ticket));
    }

    [Fact]
    public async Task Vida_mayor_a_300s_rechaza_como_mal_emitido()
    {
        var (sso, _) = await SetupAsync(nameof(Vida_mayor_a_300s_rechaza_como_mal_emitido));
        var ticket = CrearTicket(vidaSegundos: 600); // vigente, pero emitido con vida excesiva
        Assert.Null(await sso.ValidarYConsumirAsync(ticket));
    }

    [Fact]
    public async Task Aud_incorrecta_rechaza()
    {
        var (sso, _) = await SetupAsync(nameof(Aud_incorrecta_rechaza));
        Assert.Null(await sso.ValidarYConsumirAsync(CrearTicket(aud: "trade-visit-tool")));
    }

    [Fact]
    public async Task Sin_jti_rechaza()
    {
        var (sso, _) = await SetupAsync(nameof(Sin_jti_rechaza));
        Assert.Null(await sso.ValidarYConsumirAsync(CrearTicket(jti: "")));
    }

    [Fact]
    public async Task Jti_mas_largo_que_64_rechaza()
    {
        var (sso, _) = await SetupAsync(nameof(Jti_mas_largo_que_64_rechaza));
        Assert.Null(await sso.ValidarYConsumirAsync(CrearTicket(jti: new string('a', 65))));
    }

    [Fact]
    public async Task Jti_repetido_rechaza_el_segundo_uso()
    {
        var (sso, _) = await SetupAsync(nameof(Jti_repetido_rechaza_el_segundo_uso));
        var ticket = CrearTicket();
        Assert.NotNull(await sso.ValidarYConsumirAsync(ticket));
        Assert.Null(await sso.ValidarYConsumirAsync(ticket)); // replay
    }

    [Fact]
    public async Task Jti_se_quema_aunque_el_dni_no_exista()
    {
        var (sso, _) = await SetupAsync(nameof(Jti_se_quema_aunque_el_dni_no_exista));
        var jti = Guid.NewGuid().ToString("N");
        Assert.Null(await sso.ValidarYConsumirAsync(CrearTicket(dni: "99999999", jti: jti)));
        // Mismo jti con dni válido: el jti ya quedó consumido por el intento fallido.
        Assert.Null(await sso.ValidarYConsumirAsync(CrearTicket(jti: jti)));
    }

    [Fact]
    public async Task Dni_inexistente_rechaza()
    {
        var (sso, _) = await SetupAsync(nameof(Dni_inexistente_rechaza));
        Assert.Null(await sso.ValidarYConsumirAsync(CrearTicket(dni: "99999999")));
    }

    [Fact]
    public async Task Usuario_inactivo_rechaza()
    {
        var (sso, _) = await SetupAsync(nameof(Usuario_inactivo_rechaza));
        Assert.Null(await sso.ValidarYConsumirAsync(CrearTicket(dni: "20999888")));
    }

    [Fact]
    public async Task Sin_secret_configurado_rechaza()
    {
        var (sso, _) = await SetupAsync(nameof(Sin_secret_configurado_rechaza), secret: "");
        Assert.Null(await sso.ValidarYConsumirAsync(CrearTicket()));
    }

    [Fact]
    public async Task Purga_jtis_vencidos_en_validaciones_posteriores()
    {
        var (sso, factory) = await SetupAsync(nameof(Purga_jtis_vencidos_en_validaciones_posteriores));
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.SsoTicketsUsados.Add(new SsoTicketUsado { Jti = "viejo", ExpiraUtc = DateTime.UtcNow.AddMinutes(-5) });
            await db.SaveChangesAsync();
        }
        Assert.NotNull(await sso.ValidarYConsumirAsync(CrearTicket()));
        await using var check = await factory.CreateDbContextAsync();
        Assert.False(await check.SsoTicketsUsados.AnyAsync(t => t.Jti == "viejo"));
        Assert.Equal(1, await check.SsoTicketsUsados.CountAsync()); // solo el jti recién consumido
    }
}
