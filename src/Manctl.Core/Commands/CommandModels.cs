namespace Manctl.Core.Commands;

public sealed record CommandResult(bool Success, int? ExitCode, string StdOut, string StdErr, bool TimedOut, string? Error = null, string? ErrorCode = null, string? Hint = null);

public sealed record CommandHandle(string Id, int Pid, string? Error = null, string? ErrorCode = null, string? Hint = null);

public sealed record CommandOutputSnapshot(
    string Id,
    bool Running,
    int? ExitCode,
    string StdOut,
    string StdErr,
    long StdOutOffset,
    long StdErrOffset,
    bool StdOutTruncated,
    bool StdErrTruncated);

public sealed record CommandInfo(string Id, int Pid, string Executable, bool Running, int? ExitCode, DateTimeOffset StartedAtUtc);
