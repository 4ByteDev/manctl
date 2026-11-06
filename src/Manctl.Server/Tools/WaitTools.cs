using System.ComponentModel;
using Manctl.Core.Processes;
using ModelContextProtocol.Server;

namespace Manctl.Server.Tools;

[McpServerToolType]
public static class WaitTools
{
    [McpServerTool(Name = "wait_for_file"), Description(
        "Waits (on the server) for a file: wait_for_file(path=\"C:\\log.txt\", mode=\"contains_text\", matchText=\"DONE\", timeoutSeconds=600). " +
        "Use it instead of checking in a loop. Answers matched:true; if it does not happen in time: ok:false, errorCode:\"Timeout\", matched:false.")]
    public static Task<WaitForFileResult> WaitForFile(
        [Description("Full path of the file.")] string path,
        [Description("\"exists\" (wait until it appears), \"modified\" (until it changes), \"contains_text\" (until matchText shows up in it) or \"deleted\" (until it is gone).")] string mode,
        [Description("The text to look for when mode=\"contains_text\" (plain text, not a pattern).")] string? matchText = null,
        [Description("How long to wait, in seconds (max 1800).")] double timeoutSeconds = 60,
        [Description("How often to re-check, in seconds.")] double pollSeconds = 0.5)
        => FileWaitService.WaitAsync(path, mode, matchText, (int)Math.Ceiling(timeoutSeconds), Units.Ms(pollSeconds));

    [McpServerTool(Name = "wait_for_process_idle"), Description(
        "Waits until a program exits, or stops using the CPU for idleThresholdSeconds in a row, or the timeout passes: " +
        "wait_for_process_idle(processName=\"msbuild\", idleThresholdSeconds=60, timeoutSeconds=1800). " +
        "status \"exited\" = it finished; \"idle\" = still alive but doing nothing (finished its work or stuck — you decide); \"timeout\"; \"not_found\".")]
    public static Task<ProcessIdleResult> WaitForProcessIdle(
        [Description("Process id to watch (give this or processName).")] int? processId = null,
        [Description("Program name, with or without .exe (give this or processId). If several match, the first is used.")] string? processName = null,
        [Description("How many seconds of no CPU use count as idle.")] int idleThresholdSeconds = 30,
        [Description("Longest wait, in seconds (max 1800).")] double timeoutSeconds = 300,
        [Description("How often to check, in seconds.")] double pollSeconds = 2)
        => ProcessIdleWaitService.WaitAsync(processId, processName, idleThresholdSeconds, (int)Math.Ceiling(timeoutSeconds), Units.Ms(pollSeconds));
}
