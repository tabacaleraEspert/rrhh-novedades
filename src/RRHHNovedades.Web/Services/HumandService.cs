using Microsoft.Extensions.Options;
using RRHHNovedades.Web.Options;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RRHHNovedades.Web.Services;

/// <summary>
/// Cliente HTTP de la API pública de Humand. Auth Basic, paginación y backoff ante 429.
/// </summary>
public class HumandService : IHumandService
{
    private const int PageLimit = 50; // Humand: el límite máximo permitido es 50.
    private readonly HttpClient _http;
    private readonly ILogger<HumandService> _logger;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public HumandService(HttpClient http, IOptions<HumandOptions> options, ILogger<HumandService> logger)
    {
        var opt = options.Value;
        _http = http;
        _logger = logger;
        _http.BaseAddress = new Uri(opt.BaseUrl.TrimEnd('/') + "/");
        if (!string.IsNullOrWhiteSpace(opt.ApiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", opt.ApiKey);
    }

    public async Task<IReadOnlyList<EmpleadoHumand>> ObtenerEmpleadosAsync(CancellationToken ct = default)
    {
        var result = new List<EmpleadoHumand>();
        int page = 1;
        while (true)
        {
            var resp = await GetAsync($"users?page={page}&limit={PageLimit}", ct);
            using var doc = JsonDocument.Parse(resp);
            var root = doc.RootElement;
            if (!root.TryGetProperty("users", out var users) || users.GetArrayLength() == 0)
                break;

            foreach (var u in users.EnumerateArray())
            {
                var id = Str(u, "employeeInternalId") ?? Str(u, "id");
                if (string.IsNullOrWhiteSpace(id)) continue;
                result.Add(new EmpleadoHumand(
                    id,
                    Str(u, "firstName") ?? string.Empty,
                    Str(u, "lastName") ?? string.Empty,
                    Str(u, "phoneNumber") ?? Str(u, "phone"),
                    Segmentacion(u, "Sector")));
            }

            if (users.GetArrayLength() < PageLimit) break;
            page++;
        }
        _logger.LogInformation("Humand: {Count} empleados obtenidos", result.Count);
        return result;
    }

    public async Task<IReadOnlyList<JornadaHumand>> ObtenerJornadasAsync(
        IEnumerable<string> employeeInternalIds, DateOnly fecha, CancellationToken ct = default)
    {
        var ids = employeeInternalIds.Distinct().ToList();
        var result = new List<JornadaHumand>();
        var f = fecha.ToString("yyyy-MM-dd");

        // employeeIds admite csv; batch para no exceder largo de URL ni rate limit.
        foreach (var batch in ids.Chunk(80))
        {
            int page = 1;
            while (true)
            {
                var idsCsv = string.Join(",", batch);
                var resp = await GetAsync($"time-tracking/day-summaries?employeeIds={Uri.EscapeDataString(idsCsv)}&startDate={f}&endDate={f}&page={page}&limit={PageLimit}", ct);
                using var doc = JsonDocument.Parse(resp);
                var root = doc.RootElement;
                if (!root.TryGetProperty("items", out var items) || items.GetArrayLength() == 0)
                    break;

                foreach (var it in items.EnumerateArray())
                    result.Add(MapJornada(it, fecha));

                // OJO: el totalPages/count de Humand viene mal (siempre 1 / por página).
                // Se pagina mientras la página venga llena.
                if (items.GetArrayLength() < PageLimit) break;
                page++;
            }
        }
        return result;
    }

    private static JornadaHumand MapJornada(JsonElement it, DateOnly fechaDefault)
    {
        var incidences = ReadStringArray(it, "incidences");
        var permisos = new List<string>();
        if (it.TryGetProperty("timeOffRequests", out var tos) && tos.ValueKind == JsonValueKind.Array)
            foreach (var t in tos.EnumerateArray())
                if (Str(t, "name") is { } n) permisos.Add(n);

        TimeOnly? entrada = null, salida = null;
        if (it.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in entries.EnumerateArray())
            {
                var tipo = Str(e, "type");
                var t = ParseTime(Str(e, "time"));
                if (t is null) continue;
                if (tipo == "START") entrada = entrada is null || t < entrada ? t : entrada;
                else if (tipo == "END") salida = salida is null || t > salida ? t : salida;
            }
        }

        TimeOnly? inicioTeorico = null;
        if (it.TryGetProperty("timeSlots", out var slots) && slots.ValueKind == JsonValueKind.Array && slots.GetArrayLength() > 0)
            inicioTeorico = ParseTime(Str(slots[0], "startTime"));

        return new JornadaHumand(
            Str(it, "employeeId") ?? string.Empty,
            ParseDate(Str(it, "referenceDate")) ?? fechaDefault,
            Bool(it, "isWorkday", true),
            Bool(it, "hasSchedule", true),
            incidences,
            permisos,
            entrada, salida, inicioTeorico);
    }

    private async Task<string> GetAsync(string path, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            var resp = await _http.GetAsync(path, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < 4)
            {
                var wait = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // 1,2,4,8s
                _logger.LogWarning("Humand 429, reintentando en {Wait}s", wait.TotalSeconds);
                await Task.Delay(wait, ct);
                continue;
            }
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct);
        }
    }

    // ── helpers JSON ──
    private static string? Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // Lee el ítem de una segmentación por nombre de grupo (ej. "Sector" = área).
    private static string? Segmentacion(JsonElement u, string group)
    {
        if (u.TryGetProperty("segmentations", out var segs) && segs.ValueKind == JsonValueKind.Array)
            foreach (var s in segs.EnumerateArray())
                if (string.Equals(Str(s, "group"), group, StringComparison.OrdinalIgnoreCase))
                    return Str(s, "item");
        return null;
    }

    private static bool Bool(JsonElement e, string prop, bool def) =>
        e.TryGetProperty(prop, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean() : def;

    private static IReadOnlyList<string> ReadStringArray(JsonElement e, string prop)
    {
        var list = new List<string>();
        if (e.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var x in arr.EnumerateArray())
                if (x.ValueKind == JsonValueKind.String && x.GetString() is { } s) list.Add(s);
        return list;
    }

    // TZ explícita: con LocalDateTime la hora dependería de la TZ del servidor (en Azure/UTC
    // las fichadas saldrían corridas 3 horas).
    private static readonly TimeZoneInfo TzArgentina = ResolverTzArgentina();

    private static TimeZoneInfo ResolverTzArgentina()
    {
        foreach (var id in new[] { "America/Argentina/Buenos_Aires", "Argentina Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    private static TimeOnly? ParseTime(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        // Hora "a secas" (ej. "08:00" de timeSlots): es literal, NO se convierte de zona.
        // Si se intentara DateTimeOffset primero, "08:00" se interpretaría con la TZ de la
        // máquina y en un servidor UTC quedaría corrida (lo detectó el CI).
        if (TimeOnly.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var t)) return t;
        // Timestamp completo (ej. "2026-06-09T07:58:00.000-03:00" de entries): convertir a TZ AR.
        if (DateTimeOffset.TryParse(s, out var dto))
            return TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(dto, TzArgentina).DateTime);
        return null;
    }

    private static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParse(s, out var d) ? d : null;
}
