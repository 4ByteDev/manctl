namespace Manctl.Core.Models;

/// <summary>Physical screen pixels — same space used by SendInput, GetWindowRect and UIA bounding rects once the process is per-monitor DPI aware.</summary>
public sealed record Rect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;
}

public sealed record WindowInfo(
    long Handle,
    string Title,
    string ClassName,
    int ProcessId,
    Rect Bounds,
    bool IsVisible,
    bool IsMinimized,
    bool IsForeground);

public sealed class UiNode
{
    public required string Role { get; init; }
    public string? Name { get; init; }
    public string? Value { get; init; }
    public string? AutomationId { get; init; }
    public string? ClassName { get; init; }
    public required Rect BoundingBox { get; init; }
    public bool IsEnabled { get; init; } = true;
    public string[] Patterns { get; init; } = [];
    public List<UiNode> Children { get; init; } = [];
}

public sealed class ElementSelector
{
    /// <summary>Scope to a specific top-level window. If neither this nor WindowTitle is set, the current foreground window is used.</summary>
    public long? WindowHandle { get; init; }

    /// <summary>Case-insensitive substring match against window title, used to pick the scope window.</summary>
    public string? WindowTitle { get; init; }

    /// <summary>Exact AutomationId match — fastest, most reliable when available.</summary>
    public string? AutomationId { get; init; }

    /// <summary>Case-insensitive substring match against the element's Name/text.</summary>
    public string? Text { get; init; }

    /// <summary>UIA control type name, e.g. "Button", "Edit", "CheckBox".</summary>
    public string? Role { get; init; }

    /// <summary>When multiple elements match, pick the Nth (0-based).</summary>
    public int Index { get; init; } = 0;

    /// <summary>True when at least one of name/role/automationId was given. Window fields alone do not select an element.</summary>
    public bool HasElementCriteria => !string.IsNullOrEmpty(AutomationId) || !string.IsNullOrEmpty(Role) || !string.IsNullOrEmpty(Text);
}

public enum MouseButton { Left, Right, Middle }

/// <summary>Fixed vocabulary for the ErrorCode field carried by every operation that can fail — a model checking
/// one consistent field name/value set is more reliable than parsing a free-text message. Error/message text
/// stays alongside for human logs/debugging; ErrorCode is additive, never a replacement for it. Populated only
/// for genuine operational failures (couldn't resolve/find/execute) — NOT for an expected "didn't happen within
/// the timeout" outcome that a tool's own dedicated field (Matched/TimedOut/Status) already communicates cleanly,
/// and NOT for pure getters where "no value" is itself a legitimate answer (get_clipboard, get_env_var).</summary>
public static class ErrorCodes
{
    public const string ElementNotFound = "ElementNotFound";
    public const string WindowNotFound = "WindowNotFound";
    public const string ProcessNotFound = "ProcessNotFound";
    public const string Timeout = "Timeout";
    public const string AccessDenied = "AccessDenied";
    public const string InvalidArgument = "InvalidArgument";
    public const string FileNotFound = "FileNotFound";
    /// <summary>The window exists but exposes no UI elements to read or click (games, canvas, ImGui, protected windows).
    /// Not the same as "nothing matched": an empty answer from such a window says nothing about what is on screen.</summary>
    public const string NoElements = "NoElements";
    public const string Unknown = "Unknown";
}

/// <summary>Generic error shape for the handful of tools that don't already have their own richer result record
/// to attach ErrorCode/Error to.</summary>
public sealed record ErrorResult(string ErrorCode, string Error);

public sealed record ActionResult(
    bool Success,
    string Method,
    string? Error = null,
    string? ResolvedRole = null,
    string? ResolvedName = null,
    Rect? ResolvedBoundingBox = null,
    string? ErrorCode = null,
    string? Hint = null);

public sealed record VerifyResult(bool Success, string Property, string? Expected, string? Actual, string? ErrorCode = null, string? Error = null);

public sealed record MonitorInfo(Rect Bounds, Rect WorkArea, bool IsPrimary, int DpiX, int DpiY, string DeviceName);

public sealed record WindowWaitResult(bool Matched, double ElapsedSeconds, long? WindowHandle, string? Title, string? Error = null, string? ErrorCode = null, string? Hint = null);

public sealed record OkResult(bool Ok, string? ErrorCode = null, string? Error = null, string? Hint = null);

/// <summary>Answer of a window change (state, bounds, close) that also says what the window looks like afterwards,
/// so the caller does not have to trust that the Windows call did what was asked.</summary>
public sealed record WindowChangeResult(bool Ok, string? ErrorCode = null, string? Error = null, string? Hint = null, Rect? Bounds = null, string? State = null);

/// <summary>focus_window answer: which way finally brought the window forward, or why none did.</summary>
public sealed record FocusResult(bool Ok, string? ErrorCode = null, string? Error = null, string? Hint = null, string? Method = null, string? FrontWindow = null);

/// <summary>Getter answer: the value (null when there is none) inside the same {ok,...} envelope as everything else.</summary>
public sealed record ValueResult(bool Ok, string? Value);
