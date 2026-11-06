using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Manctl.Core.Models;
using ModelContextProtocol.Protocol;

namespace Manctl.Server.Guard;

/// <summary>One error shape for everything a tool can say "no" with: {ok:false, errorCode, tool?, message, hint}.
/// The hint is the part that matters for a weak agent — it says what to do next, not just what went wrong.</summary>
public static class ToolErrors
{
    /// <summary>Plain readable JSON: apostrophes, quotes and non-Latin letters stay as they are instead of ' / Н noise,
    /// which costs tokens and confuses small models.</summary>
    public static readonly JsonSerializerOptions Relaxed = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static CallToolResponse Response(string code, string message, string? hint = null, string? tool = null) => new()
    {
        IsError = true,
        Content = [new Content { Type = "text", Text = Json(code, message, hint, tool) }],
    };

    public static string Json(string code, string message, string? hint = null, string? tool = null)
    {
        var o = new JsonObject { ["ok"] = false, ["errorCode"] = code };
        if (tool is not null) o["tool"] = tool;
        o["message"] = message;
        o["hint"] = hint ?? DefaultHint(code, tool);
        return o.ToJsonString(Relaxed);
    }

    /// <summary>Generic next-step advice per error code. It names real tools and parameters, so it must be kept in
    /// sync whenever a tool is renamed or removed.</summary>
    public static string DefaultHint(string? code, string? tool = null) => code switch
    {
        ErrorCodes.WindowNotFound =>
            "No window matched. The handle may be old (the window was closed or restarted). Call list_windows for current handles, or pass windowTitle (part of the title) instead.",
        ErrorCodes.ElementNotFound =>
            "Nothing matched in that window. Call find_element or get_ui_tree to see the real names and roles (name/text is a substring match; role is e.g. Button, Edit, CheckBox). " +
            "If the target has no UI elements (game, canvas, ImGui), element tools cannot see it: take a screenshot to find the point, then click(x, y) with screen coordinates.",
        ErrorCodes.NoElements =>
            "This window shows no UI elements to the system (typical for games, canvas and ImGui windows, or a screen still loading), so name/role searches and get_ui_tree cannot see anything. " +
            "An empty result here does NOT mean the window is empty. Take a screenshot (an area, scale 0.5-2) to see it and use click(x, y) with screen coordinates.",
        ErrorCodes.ProcessNotFound =>
            "No such process. Use list_managed_processes for programs started with start_process, or check the process name / pid.",
        ErrorCodes.Timeout =>
            "It did not happen in time. Use a bigger timeout, or look at the current state first (list_windows, get_foreground_window, get_ui_tree).",
        ErrorCodes.AccessDenied =>
            "Windows refused the action. Input blocking (lock_input) needs manctl to run as Administrator; protected windows (UAC prompts) cannot be controlled at all.",
        ErrorCodes.FileNotFound =>
            "File or folder not found. Use full absolute paths and check that it exists.",
        ErrorCodes.InvalidArgument =>
            "Check parameter names and values against the tool's schema.",
        _ =>
            "Unexpected failure. Check the preconditions (window visible? device connected and unlocked?), then try again. If it keeps happening it is a manctl bug.",
    };
}
