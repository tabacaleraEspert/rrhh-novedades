using Microsoft.Extensions.Options;
using RRHHNovedades.Web.Options;

namespace RRHHNovedades.Web.Services.Asistente;

/// <summary>Burbuja del chat para la UI.</summary>
public sealed record Burbuja(bool EsUsuario, string Texto, string? Pie = null);

/// <summary>
/// Historial de conversación del circuito (scoped: vive y muere con la conexión de Blazor).
/// Semántica heredada de Bachi: TTL duro de 1 h desde el PRIMER mensaje (no se renueva, para
/// que una charla vieja no contamine respuestas nuevas), tope de mensajes enviados al modelo,
/// y los turnos intermedios del loop (tool calls/results) NUNCA entran acá — solo el texto final.
/// </summary>
public sealed class AsistenteEstado(IReloj reloj, IOptions<AsistenteOptions> options)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    private readonly List<Burbuja> _burbujas = [];
    private DateTimeOffset? _inicio;

    public IReadOnlyList<Burbuja> Burbujas
    {
        get { ExpirarSiCorresponde(); return _burbujas; }
    }

    public void AgregarUsuario(string texto) => Agregar(new Burbuja(true, texto));

    public void AgregarAsistente(string texto, string? pie = null) => Agregar(new Burbuja(false, texto, pie));

    private void Agregar(Burbuja b)
    {
        ExpirarSiCorresponde();
        _inicio ??= reloj.Ahora;
        _burbujas.Add(b);
    }

    /// <summary>Historial para el modelo: los últimos N mensajes, cada uno recortado.</summary>
    public IReadOnlyList<MensajeChat> MensajesParaModelo()
    {
        ExpirarSiCorresponde();
        var max = options.Value.MaxMensajesHistorial;
        var maxChars = options.Value.MaxCharsMensaje;
        return _burbujas
            .Skip(Math.Max(0, _burbujas.Count - max))
            .Select(b =>
            {
                var texto = b.Texto.Length > maxChars ? b.Texto[..maxChars] : b.Texto;
                return b.EsUsuario ? (MensajeChat)new MensajeUsuario(texto) : new MensajeAsistente(texto);
            })
            .ToList();
    }

    public void Reiniciar()
    {
        _burbujas.Clear();
        _inicio = null;
    }

    /// <summary>Minutos hasta que la conversación expira (para el aviso de la UI). Null si está vacía.</summary>
    public int? MinutosRestantes()
    {
        ExpirarSiCorresponde();
        if (_inicio is null) return null;
        var restante = Ttl - (reloj.Ahora - _inicio.Value);
        return Math.Max(0, (int)restante.TotalMinutes);
    }

    private void ExpirarSiCorresponde()
    {
        if (_inicio is not null && reloj.Ahora - _inicio.Value > Ttl)
        {
            _burbujas.Clear();
            _inicio = null;
        }
    }
}
