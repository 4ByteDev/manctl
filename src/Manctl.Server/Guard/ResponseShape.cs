using System.Text.Json;
using System.Text.Json.Nodes;

namespace Manctl.Server.Guard;

/// <summary>Last line of the response contract. Every tool answer that is an "action or check result" has the same
/// first fields: ok (true/false), then errorCode / message / hint when something is wrong. Older result types call
/// the same things success / error — they are renamed here, in one place, so every tool says it the same way.
/// Data answers (window lists, UI trees, monitor lists, output snapshots) are left alone.</summary>
internal static class ResponseShape
{
    private static readonly string[] ResultMarkers = ["success", "ok", "error", "errorCode", "matched", "status", "totalMatches", "ready"];

    public static string Apply(string text, string? tool)
    {
        if (text.Length == 0 || (text[0] != '{' && text[0] != '[')) return text;

        JsonNode? root;
        try { root = JsonNode.Parse(text); }
        catch (JsonException) { return text; }

        // Data answers (lists, trees): same content, just written in plain readable JSON.
        if (root is not JsonObject src || !ResultMarkers.Any(src.ContainsKey))
            return root?.ToJsonString(ToolErrors.Relaxed) ?? text;

        string? Str(string key) => src.TryGetPropertyValue(key, out var n) && n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        bool? Bool(string key) => src.TryGetPropertyValue(key, out var n) && n is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

        var code = Str("errorCode");
        var message = Str("message") ?? Str("error");
        var okSoFar = Bool("ok") ?? Bool("success") ?? (Str("errorCode") is null && Str("error") is null);
        // adb tools report the reason only in stdErr (console commands have timedOut and keep stdErr as data).
        if (!okSoFar && message is null && !src.ContainsKey("timedOut") && Str("stdErr") is { Length: > 0 } adbErr)
            message = adbErr.Length > 400 ? adbErr[..400] + "..." : adbErr.Trim();
        var ok = Bool("ok") ?? Bool("success") ?? (code is null && Str("error") is null);

        // A failure that has a message but no code still gets one, so "ok:false" always comes with errorCode.
        if (!ok && code is null && message is not null) code = "Unknown";

        // A command that ran but exited non-zero: ok:false, nothing wrong with the call itself.
        string? hint = Str("hint");
        if (hint is null && !ok && code is null && src.TryGetPropertyValue("exitCode", out var ec) && ec is not null)
            hint = $"The command ran but exited with code {ec.ToJsonString()}. Read stdErr and stdOut for the reason.";
        if (hint is null && code is not null)
            hint = ToolErrors.DefaultHint(code, tool);

        var result = new JsonObject { ["ok"] = ok };
        if (code is not null) result["errorCode"] = code;
        if (message is not null) result["message"] = message;
        if (hint is not null) result["hint"] = hint;

        foreach (var (key, value) in src)
        {
            if (key is "ok" or "success" or "error" or "message" or "errorCode" or "hint") continue;
            result[key] = value?.DeepClone();
        }
        return result.ToJsonString(ToolErrors.Relaxed);
    }
}
