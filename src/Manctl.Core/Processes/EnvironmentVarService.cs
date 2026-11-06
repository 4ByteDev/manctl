namespace Manctl.Core.Processes;

public sealed record EnvVarResult(bool Ok, string Scope, string? Error = null);

/// <summary>scope="process" affects manctl's own process and is inherited by every child process it spawns from
/// now on (run_command/run_command_async/start_process all go through Process.Start in this same process) — no
/// extra plumbing needed, that's how environment inheritance already works. scope="user" writes to the user's
/// registry (same mechanism as `setx`) — persists across restarts but does NOT retroactively affect the current
/// manctl process or anything already running.</summary>
public static class EnvironmentVarService
{
    public static EnvVarResult Set(string name, string? value, string scope)
    {
        if (!TryParseScope(scope, out var target))
            return new EnvVarResult(false, scope, $"Unknown scope '{scope}'. Use \"process\" or \"user\".");

        try
        {
            Environment.SetEnvironmentVariable(name, string.IsNullOrEmpty(value) ? null : value, target);
            return new EnvVarResult(true, scope);
        }
        catch (Exception ex)
        {
            return new EnvVarResult(false, scope, ex.Message);
        }
    }

    public static string? Get(string name, string scope) =>
        TryParseScope(scope, out var target) ? Environment.GetEnvironmentVariable(name, target) : null;

    private static bool TryParseScope(string scope, out EnvironmentVariableTarget target)
    {
        switch (scope.ToLowerInvariant())
        {
            case "process": target = EnvironmentVariableTarget.Process; return true;
            case "user": target = EnvironmentVariableTarget.User; return true;
            default: target = EnvironmentVariableTarget.Process; return false;
        }
    }
}
