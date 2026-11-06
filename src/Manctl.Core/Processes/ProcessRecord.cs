using System.Text.Json.Serialization;
using Manctl.Core.Models;

namespace Manctl.Core.Processes;

public sealed record ProcessRecord(
    string ProfileName,
    int Pid,
    string Executable,
    string? Arguments,
    string[] LockFilePaths,
    DateTimeOffset StartedAtUtc);

public sealed record ManagedProcessInfo(
    [property: JsonPropertyName("label")] string ProfileName,
    int Pid,
    string Executable,
    bool IsAlive,
    bool Ready,
    string? ReadySignal,
    DateTimeOffset StartedAtUtc,
    string? Error = null,
    string? ErrorCode = null);

public sealed record StopResult(
    bool Success,
    int Pid,
    int? ExitCode,
    string[] LockFilesCleaned,
    string? Error = null,
    string? ErrorCode = null);
