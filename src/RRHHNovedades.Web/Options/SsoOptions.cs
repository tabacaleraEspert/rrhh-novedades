namespace RRHHNovedades.Web.Options;

/// <summary>Autologin (SSO) desde el Command Center de Espert vía ticket JWT de un solo uso.</summary>
public class SsoOptions
{
    public const string SectionName = "Sso";

    /// <summary>
    /// Secreto HS256 compartido con el Command Center (Key Vault: Sso--SharedSecret).
    /// Mínimo 32 caracteres; vacío = SSO deshabilitado.
    /// </summary>
    public string SharedSecret { get; set; } = string.Empty;

    /// <summary>Audience esperada en el ticket; cualquier otra se rechaza.</summary>
    public string Audience { get; set; } = "rrhh-novedades";

    /// <summary>Vida máxima aceptada (exp - iat); más largo se considera ticket mal emitido.</summary>
    public int VidaMaximaSegundos { get; set; } = 300;

    /// <summary>Tolerancia de desfase de reloj contra el emisor.</summary>
    public int ClockSkewSegundos { get; set; } = 10;
}
