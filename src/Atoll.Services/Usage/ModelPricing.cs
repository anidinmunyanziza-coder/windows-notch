using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Atoll.Services.Usage;

/// <summary>Per-token USD rates for one model.</summary>
public sealed record ModelRates(double Prompt, double Completion, double? CacheRead, double? CacheWrite);

/// <summary>
/// API-equivalent pricing for local token counts — a port of the original's
/// ModelPricing / ModelPricingManager, using its bundled OpenRouter-style
/// pricing.json (dollars per token). Log model ids are matched by trying the
/// same candidate keys: provider prefix dropped, date stamp dropped, dotted vs
/// dashed minor versions, tier suffixes and both Claude word orders.
/// </summary>
public static class ModelPricing
{
    private static readonly Lazy<Dictionary<string, ModelRates>> _table = new(Load);

    public static ModelRates? Rates(string modelId)
    {
        foreach (var key in KeyCandidates(modelId))
            if (_table.Value.TryGetValue(key, out var rates)) return rates;
        return null;
    }

    /// <summary>Estimated cost, or null when the model has no usable price.
    /// <paramref name="uncachedInput"/> excludes cache reads and writes.</summary>
    public static double? Cost(string model, long uncachedInput, long output, long cacheRead, long cacheWrite)
    {
        if (Rates(model) is not { } r) return null;
        return uncachedInput * r.Prompt + cacheRead * (r.CacheRead ?? r.Prompt)
               + cacheWrite * (r.CacheWrite ?? r.Prompt) + output * r.Completion;
    }

    public static IReadOnlyList<string> KeyCandidates(string raw)
    {
        var outList = new List<string>();
        void Push(string s)
        {
            if (s.Length == 0) return;
            foreach (var form in new[] { s, "anthropic/" + s, "openai/" + s, "google/" + s })
                if (!outList.Contains(form)) outList.Add(form);
        }

        var s = raw.ToLowerInvariant().Trim();
        var slash = s.LastIndexOf('/');
        if (slash >= 0) s = s[(slash + 1)..];
        Push(s);

        var noDate = Regex.Replace(s, @"-\d{6,}$", "");
        Push(noDate);
        var dashed = noDate.Replace('.', '-');
        Push(dashed);
        var dotted = Regex.Replace(dashed, @"(\d)-(\d)", "$1.$2");
        Push(dotted);
        var untiered = Regex.Replace(dotted, @"-(low|medium|high|thinking)$", "");
        Push(untiered);
        Push(untiered + "-preview");

        // Reconcile family/version word order around the "claude-" stem.
        var families = new[] { "opus", "sonnet", "haiku", "fable" };
        var tokens = dashed.Split('-').ToList();
        if (tokens.Count > 0 && tokens[0] == "claude")
        {
            var famIdx = tokens.FindIndex(t => families.Contains(t));
            if (famIdx > 0)
            {
                var family = tokens[famIdx];
                tokens.RemoveAt(famIdx);
                var version = tokens.Skip(1).ToList();
                if (version.Count > 0)
                {
                    Push(string.Join("-", new[] { "claude", family }.Concat(version)));
                    Push(string.Join("-", new[] { "claude" }.Concat(version).Append(family)));
                    // OpenRouter keys Claude minor versions with a dot: claude-opus-4.1
                    Push(Regex.Replace(string.Join("-", new[] { "claude", family }.Concat(version)), @"(\d)-(\d)", "$1.$2"));
                }
            }
        }
        return outList;
    }

    private static Dictionary<string, ModelRates> Load()
    {
        var table = new Dictionary<string, ModelRates>(StringComparer.Ordinal);
        try
        {
            using var stream = typeof(ModelPricing).Assembly
                .GetManifestResourceStream("Atoll.Services.Usage.pricing.json");
            if (stream is null) return table;
            using var doc = JsonDocument.Parse(stream);
            foreach (var model in doc.RootElement.GetProperty("models").EnumerateArray())
            {
                var id = model.GetProperty("id").GetString()?.ToLowerInvariant();
                if (id is null || !model.TryGetProperty("pricing", out var p)) continue;
                var prompt = Number(p, "prompt");
                var completion = Number(p, "completion");
                // Placeholder rows need both sides to count as priced.
                if (prompt is not > 0 || completion is not > 0) continue;
                table[id] = new ModelRates(prompt.Value, completion.Value, Number(p, "cache_read"), Number(p, "cache_write"));
            }
        }
        catch { /* unpriced: costs show "est. n/a" */ }
        return table;
    }

    private static double? Number(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }
}
