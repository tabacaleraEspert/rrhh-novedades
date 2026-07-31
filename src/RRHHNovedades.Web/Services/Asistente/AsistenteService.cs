using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using RRHHNovedades.Web.Options;
using RRHHNovedades.Web.Services.Asistente.Herramientas;

namespace RRHHNovedades.Web.Services.Asistente;

// Eventos que consume la UI. El switch del consumidor lleva default vacío (dispatcher sin
// else, como el cliente de Bachi): un evento nuevo no rompe a nadie.
public abstract record AsistenteEvento;
public sealed record EventoTexto(string Delta) : AsistenteEvento;
public sealed record EventoHerramienta(string Etiqueta) : AsistenteEvento;
public sealed record EventoError(string Mensaje) : AsistenteEvento;
public sealed record EventoFin(string TextoCompleto, int DuracionMs, decimal CostoUsd) : AsistenteEvento;

/// <summary>
/// El loop de un turno del asistente: guardas → abrir turno → vueltas de herramientas
/// (no-streaming) → vuelta de cierre (streaming, tool_choice none) → cerrar turno SIEMPRE.
/// Lecciones Bachi cableadas: el cierre nunca permite tools (sin eso: 51 s y 0 caracteres),
/// toda tool_call recibe su tool_result (o "SIN TIEMPO"), presupuesto de cierre generoso
/// (incluye tokens de razonamiento), nunca burbuja vacía, rate limit falla-cerrada.
/// </summary>
public sealed class AsistenteService(
    IChatProveedor proveedor,
    AsistenteToolRegistry registry,
    PromptBuilder prompts,
    AsistenteEstado estado,
    IRegistroTurnosService registro,
    IReloj reloj,
    IOptions<AsistenteOptions> options,
    IOptions<HumandOptions> humand,
    ILogger<AsistenteService> logger)
{
    private const string MensajeSinRespuesta =
        "No pude generar una respuesta. Probá reformular la pregunta o achicar el período.";

    public async IAsyncEnumerable<AsistenteEvento> PreguntarAsync(
        string pregunta,
        int usuarioId,
        string nombreUsuario,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var opt = options.Value;

        // ── Guardas, en orden y todas antes de gastar un token ──
        if (!opt.Habilitado)
        {
            yield return new EventoError("El asistente no está configurado (falta la API key). El resto del tablero funciona normal.");
            yield break;
        }

        pregunta = pregunta.Trim();
        if (pregunta.Length == 0) yield break;
        if (pregunta.Length > opt.MaxCharsMensaje) pregunta = pregunta[..opt.MaxCharsMensaje];

        var recientes = await registro.ConsultasRecientesAsync(usuarioId, ct);
        if (recientes is null)
        {
            // Falla CERRADA: si no se puede contar, no se responde.
            yield return new EventoError("No pude verificar el límite de consultas. Probá de nuevo en un momento.");
            yield break;
        }
        if (recientes >= opt.RateLimitCantidad)
        {
            yield return new EventoError($"Llegaste al límite de {opt.RateLimitCantidad} consultas en {opt.RateLimitVentanaMinutos} minutos. Esperá un poco y volvé a probar.");
            yield break;
        }

        // El turno se abre ANTES de llamar al modelo: la fila existe para el rate limit.
        var turnoId = await registro.AbrirAsync(usuarioId, pregunta, ct);

        var nombrePila = nombreUsuario.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? nombreUsuario;
        var mensajes = new List<MensajeChat>
        {
            new MensajeSystem(prompts.PromptEstable()),   // byte-idéntico: prefijo cacheado
            new MensajeSystem(prompts.PromptVolatil(nombrePila, reloj.Hoy, humand.Value.UseMock)),
        };
        mensajes.AddRange(estado.MensajesParaModelo());
        mensajes.Add(new MensajeUsuario(pregunta));

        estado.AgregarUsuario(pregunta);

        var sw = Stopwatch.StartNew();
        var tools = registry.Definiciones();
        var usoTotal = UsoTokens.Cero;
        var vueltas = 0;
        string textoFinal = "";
        string? errorTurno = null;

        // Timeout propio (no hay corte de plataforma como en Vercel, pero una pregunta abierta
        // no puede colgar el circuito). Linked al ct del circuito.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(opt.TimeoutSegundos));

        try
        {
            // ── Vueltas con herramientas (no-streaming: la salida útil son tool calls) ──
            while (vueltas < opt.MaxVueltas - 1)
            {
                vueltas++;
                VueltaChat vuelta;
                try
                {
                    vuelta = await proveedor.CompletarAsync(mensajes, tools, opt.MaxTokensVuelta, cts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    errorTurno = "timeout";
                    break; // al cierre con lo que haya
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Asistente: falló la vuelta {Vuelta} del turno {TurnoId}", vueltas, turnoId);
                    errorTurno = $"proveedor: {ex.Message}";
                    break;
                }

                usoTotal += vuelta.Uso;

                if (vuelta.Llamadas.Count == 0)
                {
                    // Respondió directo sin herramientas: ese texto es la respuesta final.
                    if (!string.IsNullOrWhiteSpace(vuelta.Texto))
                    {
                        textoFinal = vuelta.Texto;
                        yield return new EventoTexto(textoFinal);
                    }
                    break;
                }

                mensajes.Add(new MensajeAsistenteConTools(vuelta.Texto, vuelta.Llamadas));

                foreach (var llamada in vuelta.Llamadas)
                {
                    yield return new EventoHerramienta(registry.EtiquetaDe(llamada.Nombre));

                    string resultado;
                    var swTool = Stopwatch.StartNew();
                    if (sw.Elapsed.TotalSeconds > opt.SegundosSinHerramientas)
                    {
                        // Toda tool_call necesita su tool_result: si no hay tiempo, se responde
                        // esto en vez de saltearla (la API rechaza el request si falta una).
                        resultado = "SIN TIEMPO: no se ejecutó esta consulta. Respondé con lo que tengas y aclará qué quedó pendiente.";
                    }
                    else
                    {
                        try
                        {
                            resultado = await registry.EjecutarAsync(llamada, cts.Token);
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                        {
                            resultado = "SIN TIEMPO: la consulta se cortó por tiempo. Respondé con lo que tengas y aclará qué quedó pendiente.";
                        }
                    }
                    mensajes.Add(new MensajeResultadoTool(llamada.Id, resultado));
                    await registro.RegistrarHerramientaAsync(turnoId, llamada.Nombre, llamada.ArgsJson, (int)swTool.ElapsedMilliseconds, CancellationToken.None);
                }
            }

            // ── Vuelta de cierre: streaming, herramientas bloqueadas ──
            // Se corre siempre que no haya respuesta directa. Si el presupuesto ya se agotó
            // (timeout), el cierre recibe una prórroga corta propia: mejor una respuesta
            // parcial que una burbuja de error (el equivalente del MS_PARA_CERRAR de Bachi).
            if (textoFinal.Length == 0)
            {
                vueltas++;
                using var ctsCierre = cts.IsCancellationRequested
                    ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                    : null;
                ctsCierre?.CancelAfter(TimeSpan.FromSeconds(20));
                var tokenCierre = ctsCierre?.Token ?? cts.Token;

                if (errorTurno == "timeout")
                    mensajes.Add(new MensajeSystem("Se acabó el tiempo de consulta. Respondé AHORA con lo que tengas y aclarás qué quedó pendiente."));

                var enumerador = proveedor.StreamearCierreAsync(mensajes, tools, opt.MaxTokensCierre, tokenCierre)
                    .GetAsyncEnumerator(CancellationToken.None);
                try
                {
                    while (true)
                    {
                        CierreEvento? ev;
                        try
                        {
                            if (!await enumerador.MoveNextAsync()) break;
                            ev = enumerador.Current;
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                        {
                            errorTurno ??= "timeout_cierre";
                            break;
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Asistente: falló el cierre del turno {TurnoId}", turnoId);
                            errorTurno ??= $"cierre: {ex.Message}";
                            break;
                        }

                        switch (ev)
                        {
                            case CierreTexto t:
                                yield return new EventoTexto(t.Delta);
                                break;
                            case CierreFin fin:
                                textoFinal = fin.TextoCompleto;
                                usoTotal += fin.Uso;
                                break;
                            default:
                                break; // evento nuevo: se ignora sin romper
                        }
                    }
                }
                finally
                {
                    await enumerador.DisposeAsync();
                }
            }

            // ── Red final: nunca burbuja vacía ──
            if (string.IsNullOrWhiteSpace(textoFinal))
            {
                textoFinal = errorTurno is null
                    ? MensajeSinRespuesta
                    : "Se cortó la consulta antes de terminar. Probá con una pregunta más acotada.";
                yield return new EventoTexto(textoFinal);
                errorTurno ??= "sin_respuesta";
            }

            sw.Stop();
            var costo = registro.CalcularCosto(usoTotal);
            estado.AgregarAsistente(textoFinal, $"{sw.Elapsed.TotalSeconds:0.0} s · US$ {costo:0.000}");
            yield return new EventoFin(textoFinal, (int)sw.ElapsedMilliseconds, costo);
        }
        finally
        {
            // El turno se cierra SIEMPRE, incluso si el circuito se desconectó a mitad de camino.
            await registro.CerrarAsync(
                turnoId,
                ok: errorTurno is null,
                error: errorTurno,
                respuesta: textoFinal.Length > 0 ? textoFinal : null,
                vueltas,
                (int)sw.ElapsedMilliseconds,
                usoTotal,
                CancellationToken.None);
        }
    }
}
