using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HashKeyChain.Configuration;
using Microsoft.Extensions.Options;

namespace HashKeyChain.Services.Analysis;

/// <summary>
/// Thin REST client for Azure AI Content Understanding. Submits a document to a
/// custom analyzer, polls the async operation to completion, and returns the flat
/// map of extracted fields. Kept deliberately small and dependency-free so it can
/// back both the contract auto-fill and the shipping-document cross-check.
/// </summary>
public sealed class ContentUnderstandingClient
{
    private readonly ContentUnderstandingOptions _options;
    private readonly ILogger<ContentUnderstandingClient> _logger;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    public ContentUnderstandingClient(
        IOptions<ContentUnderstandingOptions> options,
        ILogger<ContentUnderstandingClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    /// <summary>
    /// Analyzes the document bytes with the configured analyzer. Returns null on any
    /// failure so callers can fall back safely.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, CuFieldValue>?> AnalyzeAsync(byte[] content, CancellationToken ct = default)
    {
        if (!_options.IsConfigured || content.Length == 0)
            return null;

        try
        {
            var endpoint = _options.Endpoint.TrimEnd('/');
            var analyzeUrl =
                $"{endpoint}/contentunderstanding/analyzers/{_options.AnalyzerId}:analyze?api-version={_options.ApiVersion}";

            var body = JsonSerializer.Serialize(new
            {
                inputs = new[] { new { data = Convert.ToBase64String(content) } }
            });

            using var req = new HttpRequestMessage(HttpMethod.Post, analyzeUrl)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("Ocp-Apim-Subscription-Key", _options.ApiKey);

            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Content Understanding analyze returned {Status}.", (int)resp.StatusCode);
                return null;
            }

            var opLocation = resp.Headers.TryGetValues("Operation-Location", out var vals)
                ? vals.FirstOrDefault()
                : null;
            if (string.IsNullOrWhiteSpace(opLocation))
                return null;

            // Poll the async operation.
            for (var attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);

                using var pollReq = new HttpRequestMessage(HttpMethod.Get, opLocation);
                pollReq.Headers.Add("Ocp-Apim-Subscription-Key", _options.ApiKey);
                using var pollResp = await Http.SendAsync(pollReq, ct);
                if (!pollResp.IsSuccessStatusCode)
                    return null;

                using var doc = JsonDocument.Parse(await pollResp.Content.ReadAsStringAsync(ct));
                var status = doc.RootElement.TryGetProperty("status", out var st) ? st.GetString() : null;

                if (string.Equals(status, "Succeeded", StringComparison.OrdinalIgnoreCase))
                    return ParseFields(doc.RootElement);

                if (string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Content Understanding analyze failed for document.");
                    return null;
                }
            }

            _logger.LogWarning("Content Understanding analyze timed out.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Content Understanding analyze threw; falling back.");
            return null;
        }
    }

    private static IReadOnlyDictionary<string, CuFieldValue> ParseFields(JsonElement root)
    {
        var result = new Dictionary<string, CuFieldValue>(StringComparer.OrdinalIgnoreCase);

        if (!root.TryGetProperty("result", out var res) ||
            !res.TryGetProperty("contents", out var contents) ||
            contents.ValueKind != JsonValueKind.Array ||
            contents.GetArrayLength() == 0)
            return result;

        var first = contents[0];
        if (!first.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var field in fields.EnumerateObject())
        {
            var v = field.Value;
            string? str = null;
            decimal? num = null;
            DateTime? date = null;

            if (v.TryGetProperty("valueString", out var vs) && vs.ValueKind == JsonValueKind.String)
                str = vs.GetString();
            if (v.TryGetProperty("valueNumber", out var vn) && vn.ValueKind == JsonValueKind.Number)
                num = vn.GetDecimal();
            if (v.TryGetProperty("valueDate", out var vd) && vd.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(vd.GetString(), out var d))
                date = d;

            result[field.Name] = new CuFieldValue(Clean(str), num, date);
        }

        return result;
    }

    private static string? Clean(string? s)
    {
        var t = s?.Trim();
        return string.IsNullOrEmpty(t) || string.Equals(t, "null", StringComparison.OrdinalIgnoreCase) ? null : t;
    }
}

/// <summary>A single extracted field value from Content Understanding.</summary>
public sealed record CuFieldValue(string? String, decimal? Number, DateTime? Date)
{
    public string? Text => String;
    public decimal? AsNumber => Number ?? (decimal.TryParse(String, out var n) ? n : null);
    public DateTime? AsDate => Date ?? (DateTime.TryParse(String, out var d) ? d : null);
}
