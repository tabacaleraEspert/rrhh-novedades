using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;

namespace RRHHNovedades.Web.Services.Asistente;

/// <summary>
/// Render mínimo del Markdown que produce el modelo en el chat: negrita, código inline,
/// viñetas y títulos. TODO el texto se escapa como HTML ANTES de aplicar formato (los datos
/// incluyen texto libre de Humand: nunca inyectable). No es un parser Markdown completo
/// a propósito: lo que no se reconoce queda como texto plano visible.
/// </summary>
public static partial class AsistenteMarkdown
{
    [GeneratedRegex(@"\*\*(.+?)\*\*", RegexOptions.Singleline)]
    private static partial Regex Negrita();

    [GeneratedRegex(@"`([^`\n]+)`")]
    private static partial Regex Codigo();

    // Escapa solo lo peligroso (<>&"'), dejando tildes y ñ como texto (no &#237;).
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    public static string Render(string texto)
    {
        var sb = new StringBuilder(texto.Length + 64);
        foreach (var lineaCruda in texto.Replace("\r\n", "\n").Split('\n'))
        {
            var linea = Encoder.Encode(lineaCruda);

            // Títulos ### → línea en negrita; viñetas -/* → bullet visible.
            var trim = linea.TrimStart();
            if (trim.StartsWith("###") || trim.StartsWith("##") || trim.StartsWith('#'))
                linea = $"<b>{trim.TrimStart('#').Trim()}</b>";
            else if (trim.StartsWith("- ") || trim.StartsWith("* "))
                linea = string.Concat(linea.AsSpan(0, linea.Length - trim.Length), "• ", trim.AsSpan(2));

            linea = Negrita().Replace(linea, "<b>$1</b>");
            linea = Codigo().Replace(linea, "<code>$1</code>");
            sb.Append(linea).Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }
}
