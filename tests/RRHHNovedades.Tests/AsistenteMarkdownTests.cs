using RRHHNovedades.Web.Services.Asistente;
using Xunit;

namespace RRHHNovedades.Tests;

/// <summary>Render mínimo de Markdown del chat: negrita/código/viñetas, con HTML SIEMPRE escapado antes.</summary>
public class AsistenteMarkdownTests
{
    [Fact]
    public void Negrita_y_codigo()
    {
        Assert.Equal("Hubo <b>132</b> ausencias (<code>tasa 3,4%</code>)",
            AsistenteMarkdown.Render("Hubo **132** ausencias (`tasa 3,4%`)"));
    }

    [Fact]
    public void Vinetas_y_titulos()
    {
        Assert.Equal("<b>Ausentes</b>\n• Díaz, Lucía\n• Paz, Bruno",
            AsistenteMarkdown.Render("### Ausentes\n- Díaz, Lucía\n* Paz, Bruno"));
    }

    [Fact]
    public void Html_de_los_datos_queda_escapado()
    {
        // Un nombre/motivo con HTML jamás se inyecta (los datos vienen de Humand: texto libre).
        var r = AsistenteMarkdown.Render("Motivo: <script>alert(1)</script> y **<b>x</b>**");
        Assert.DoesNotContain("<script>", r);
        Assert.Contains("&lt;script&gt;", r);
        Assert.Contains("<b>&lt;b&gt;x&lt;/b&gt;</b>", r); // la negrita del modelo sí, el HTML del dato no
    }

    [Fact]
    public void Negrita_sin_cierre_queda_literal()
    {
        Assert.Equal("a ** b", AsistenteMarkdown.Render("a ** b"));
    }
}
