using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace Manctl.Server.Guard;

/// <summary>Checks a tool call's arguments against the tool's own JSON schema BEFORE the SDK binds them. The SDK
/// itself reduces every binding failure (unknown/missing parameter, wrong type) to the useless
/// "An error occurred invoking 'x'", which is what weak agents kept hitting (key vs keys, path vs executable...).
/// Here the failure is explained in words, with a "did you mean" and the tool's real parameter list.
/// Also forgives harmless type slips first (the number 5 sent as "5", true sent as "true") — see <see cref="Coerce"/>.</summary>
internal static class ArgumentValidator
{
    private sealed record Param(string Name, string[] Types, bool Required, List<Param>? Children);

    // Names agents commonly guess for a parameter that exists under another name (or used to exist in an older
    // manctl). Lists are best guess first; only a name that exists on THIS tool is suggested.
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["key"] = ["keys", "keyCode"],
        ["hotkey"] = ["keys"],
        ["shortcut"] = ["keys"],
        ["handle"] = ["windowHandle"],
        ["hwnd"] = ["windowHandle"],
        ["window"] = ["windowHandle", "windowTitle"],
        ["title"] = ["windowTitle"],
        ["titleContains"] = ["windowTitle"],
        ["path"] = ["executable", "directory"],
        ["file"] = ["executable", "path"],
        ["program"] = ["executable"],
        ["exe"] = ["executable"],
        ["cmd"] = ["command"],
        ["args"] = ["arguments"],
        ["cwd"] = ["workingDirectory"],
        ["elementText"] = ["name"],
        ["element"] = ["name"],
        ["selector"] = ["name"],
        ["label"] = ["name"],
        ["profileName"] = ["label"],
        ["timeout"] = ["timeoutSeconds"],
        ["timeoutMs"] = ["timeoutSeconds"],
        ["waitForTimeoutMs"] = ["waitForTimeoutSeconds"],
        ["pollIntervalMs"] = ["pollSeconds"],
        ["checkIntervalMs"] = ["pollSeconds"],
        ["durationMs"] = ["durationSeconds"],
        ["serial"] = ["deviceSerial"],
        ["device"] = ["deviceSerial"],
        ["id"] = ["pid", "label"],
        ["process"] = ["processName", "processId", "label"],
        ["cropX"] = ["x"],
        ["cropY"] = ["y"],
        ["cropWidth"] = ["width"],
        ["cropHeight"] = ["height"],
        ["region"] = ["x"],
        ["format"] = ["format"],
        ["monitor"] = ["windowHandle"],
    };

    /// <summary>Something a weak agent says that has a better-fitting answer than a spelling guess.</summary>
    private static string? SpecialCase(string toolName, string key, List<Param> props)
    {
        switch (key.ToLowerInvariant())
        {
            case "coordinate" or "coordinates" or "point" or "position" or "pos" or "xy":
                return props.Any(p => p.Name == "x") ? "This tool takes the position as two separate numbers x and y (not one 'coordinate')." : null;
            case "scope" when toolName == "screenshot":
                return "screenshot has no 'scope': pass windowHandle (or windowTitle) for one window, or x, y, width, height for an area; with nothing it takes the whole screen.";
            case "text" when props.All(p => p.Name != "text") && props.Any(p => p.Name == "name"):
                return "To find an element use 'name' (the text you see on it). 'text' is only the text that type_text types.";
        }
        return null;
    }

    /// <returns>null when the call is fine, otherwise a ready-to-send error response.</returns>
    public static CallToolResponse? Check(Tool tool, IReadOnlyDictionary<string, JsonElement>? arguments)
    {
        var props = Parse(tool.InputSchema);
        var problems = new List<string>();
        var hints = new List<string>();

        var values = (arguments ?? new Dictionary<string, JsonElement>()).Select(kv => (kv.Key, kv.Value));
        Validate(tool.Name, "", props, values, problems, hints);

        if (problems.Count == 0) return null;

        hints.Add(Signature(tool.Name, props));
        return ToolErrors.Response("InvalidArgument", string.Join(" ", problems), string.Join(" ", hints.Distinct()), tool.Name);
    }

    private static void Validate(string toolName, string prefix, List<Param> props, IEnumerable<(string Key, JsonElement Value)> values,
                                 List<string> problems, List<string> hints)
    {
        var given = values.ToList();
        var byName = props.ToDictionary(p => p.Name, StringComparer.Ordinal);

        foreach (var (key, _) in given)
        {
            if (byName.ContainsKey(key)) continue;
            problems.Add($"{prefix}Unknown parameter '{key}'.");
            if (SpecialCase(toolName, key, props) is { } special) hints.Add(special);
            else if (Suggest(key, props) is { } guess) hints.Add(guess);
        }

        foreach (var p in props.Where(p => p.Required))
        {
            var has = given.Any(g => g.Key == p.Name && g.Value.ValueKind != JsonValueKind.Null);
            if (!has) problems.Add($"{prefix}Missing required parameter '{p.Name}'.");
        }

        foreach (var (key, value) in given)
        {
            if (!byName.TryGetValue(key, out var p) || value.ValueKind == JsonValueKind.Null) continue;

            if (TypeMismatch(p.Types, value) is { } bad)
            {
                problems.Add($"{prefix}Parameter '{key}' must be {Article(p.Types)}, but got {bad}.");
                continue;
            }

            if (p.Children is null) continue;
            if (value.ValueKind == JsonValueKind.Array)
            {
                var i = 0;
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                        Validate(toolName, $"{key}[{i}]: ", p.Children, item.EnumerateObject().Select(o => (o.Name, o.Value)), problems, hints);
                    else
                        problems.Add($"{prefix}{key}[{i}] must be an object, but got {Describe(item)}.");
                    i++;
                }
            }
            else if (value.ValueKind == JsonValueKind.Object)
            {
                Validate(toolName, $"{key}: ", p.Children, value.EnumerateObject().Select(o => (o.Name, o.Value)), problems, hints);
            }
        }
    }

    /// <summary>Forgives harmless type slips before validation: "123" for a number, "true" for a boolean, 5 for text.
    /// Returns the same dictionary when nothing needed changing.</summary>
    public static IReadOnlyDictionary<string, JsonElement>? Coerce(Tool tool, IReadOnlyDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null || arguments.Count == 0) return arguments;
        var props = Parse(tool.InputSchema);
        var anyChanged = false;
        var result = new Dictionary<string, JsonElement>(arguments.Count);
        foreach (var (key, value) in arguments)
        {
            var p = props.FirstOrDefault(x => x.Name == key);
            if (p is null || value.ValueKind == JsonValueKind.Null) { result[key] = value; continue; }
            var node = JsonNode.Parse(value.GetRawText());
            if (CoerceNode(p, ref node)) { result[key] = JsonSerializer.SerializeToElement(node); anyChanged = true; }
            else result[key] = value;
        }
        return anyChanged ? result : arguments;
    }

    private static bool CoerceNode(Param p, ref JsonNode? node)
    {
        var changed = false;

        if (node is JsonValue v)
        {
            var types = p.Types;
            var wantsNumber = types.Contains("integer") || types.Contains("number");

            if (v.TryGetValue<string>(out var s) && s is not null)
            {
                if (wantsNumber && !types.Contains("string"))
                {
                    var trimmed = s.Trim();
                    if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) { node = JsonValue.Create(l); changed = true; }
                    else if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) { node = JsonValue.Create(d); changed = true; }
                }
                else if (types.Contains("boolean") && !types.Contains("string"))
                {
                    if (s.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)) { node = JsonValue.Create(true); changed = true; }
                    else if (s.Trim().Equals("false", StringComparison.OrdinalIgnoreCase)) { node = JsonValue.Create(false); changed = true; }
                }
            }
            else if (types.Contains("string") && !wantsNumber && !types.Contains("boolean")
                     && (v.GetValueKind() is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False))
            {
                node = JsonValue.Create(v.ToJsonString().Trim('"')); changed = true;
            }
            else if (types.Contains("integer") && !types.Contains("number") && v.GetValueKind() == JsonValueKind.Number
                     && v.TryGetValue<double>(out var dbl) && Math.Abs(dbl % 1) < 1e-9 && !v.TryGetValue<long>(out _))
            {
                node = JsonValue.Create((long)dbl); changed = true; // 5.0 -> 5
            }
        }
        else if (node is JsonArray arr && p.Children is not null)
        {
            foreach (var item in arr)
                if (item is JsonObject o && CoerceObject(p.Children, o)) changed = true;
        }
        else if (node is JsonObject obj && p.Children is not null)
        {
            if (CoerceObject(p.Children, obj)) changed = true;
        }

        return changed;
    }

    private static bool CoerceObject(List<Param> props, JsonObject obj)
    {
        var changed = false;
        foreach (var key in obj.Select(kv => kv.Key).ToList())
        {
            var p = props.FirstOrDefault(x => x.Name == key);
            if (p is null) continue;
            var node = obj[key];
            if (CoerceNode(p, ref node)) { obj[key] = node; changed = true; }
        }
        return changed;
    }

    private static List<Param> Parse(JsonElement schema)
    {
        var list = new List<Param>();
        if (schema.ValueKind != JsonValueKind.Object) return list;
        var required = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
            foreach (var r in req.EnumerateArray()) if (r.GetString() is { } s) required.Add(s);
        if (schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in props.EnumerateObject())
            {
                var types = ReadTypes(prop.Value);
                List<Param>? children = null;
                if (types.Contains("array") && prop.Value.TryGetProperty("items", out var items)
                    && items.ValueKind == JsonValueKind.Object && items.TryGetProperty("properties", out _))
                    children = Parse(items);
                else if (types.Contains("object") && prop.Value.TryGetProperty("properties", out _))
                    children = Parse(prop.Value);
                list.Add(new Param(prop.Name, types, required.Contains(prop.Name), children));
            }
        }
        return list;
    }

    private static string[] ReadTypes(JsonElement propSchema)
    {
        if (!propSchema.TryGetProperty("type", out var t)) return ["any"];
        var types = t.ValueKind switch
        {
            JsonValueKind.String => new[] { t.GetString()! },
            JsonValueKind.Array => t.EnumerateArray().Select(x => x.GetString()).Where(x => x is not null and not "null").Cast<string>().ToArray(),
            _ => ["any"],
        };
        return types.Length == 0 ? ["any"] : types;
    }

    private static string? TypeMismatch(string[] types, JsonElement v)
    {
        if (types.Contains("any")) return null;
        foreach (var t in types)
        {
            var ok = t switch
            {
                "string" => v.ValueKind == JsonValueKind.String,
                "integer" => v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out _),
                "number" => v.ValueKind == JsonValueKind.Number,
                "boolean" => v.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "array" => v.ValueKind == JsonValueKind.Array,
                "object" => v.ValueKind == JsonValueKind.Object,
                _ => true,
            };
            if (ok) return null;
        }
        return Describe(v);
    }

    private static string Describe(JsonElement v)
    {
        var raw = v.ToString();
        if (raw.Length > 40) raw = raw[..40] + "...";
        return v.ValueKind switch
        {
            JsonValueKind.String => $"the text \"{raw}\"",
            JsonValueKind.Number => $"the number {raw}",
            JsonValueKind.True or JsonValueKind.False => $"the boolean {raw}",
            JsonValueKind.Array => "a list",
            JsonValueKind.Object => "an object",
            _ => raw,
        };
    }

    private static string Article(string[] types) => string.Join(" or ", types.Select(t => t switch
    {
        "integer" => "a whole number",
        "number" => "a number",
        "string" => "text",
        "boolean" => "true or false",
        "array" => "a list",
        "object" => "an object",
        _ => t,
    }));

    private static string? Suggest(string key, List<Param> props)
    {
        if (Aliases.TryGetValue(key, out var targets))
        {
            // First preference that exists on THIS tool (the lists are ordered best-guess first).
            var hit = targets.FirstOrDefault(t => props.Any(p => p.Name == t) && t != key);
            if (hit is not null)
                return $"Did you mean '{hit}'?" + (key.EndsWith("Ms", StringComparison.Ordinal) ? " Time is in SECONDS (5000 ms = 5)." : "");
        }

        // Closest real parameter name by edit distance (typos, case, singular/plural).
        var best = props.Select(p => (p.Name, Dist: Levenshtein(key.ToLowerInvariant(), p.Name.ToLowerInvariant())))
            .OrderBy(x => x.Dist).FirstOrDefault();
        if (best.Name is not null && best.Dist <= Math.Max(2, key.Length / 3))
            return $"Did you mean '{best.Name}'?";
        return null;
    }

    private static string Signature(string tool, List<Param> props)
    {
        if (props.Count == 0) return $"{tool} takes no parameters.";
        var sb = new StringBuilder($"{tool} parameters: ");
        sb.Append(string.Join(", ", props.Select(p => $"{p.Name} ({TypeShort(p.Types)}{(p.Required ? ", required" : "")})")));
        sb.Append('.');
        return sb.ToString();
    }

    private static string TypeShort(string[] types)
    {
        var t = types.Contains("string") ? "text" : types[0];
        return t switch { "integer" or "number" => "number", "boolean" => "true/false", "array" => "list", _ => t };
    }

    private static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }
}
