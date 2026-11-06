using System.ComponentModel;
using Manctl.Core.Processes;
using ModelContextProtocol.Server;

namespace Manctl.Server.Tools;

[McpServerToolType]
public static class ProcessTools
{
    [McpServerTool(Name = "start_process"), Description(
        "Starts a program that should keep running and waits until it is ready: apps, games, installers, the Android emulator. " +
        "Example: start_process(executable=\"E:\\sdk\\emulator.exe\", arguments=\"-avd Pixel6\", waitForType=\"androidBoot\", waitForTimeoutSeconds=240). " +
        "For a console command whose output you need, use run_command instead. " +
        "The program opens on the real visible desktop. If it is already running under the same label, it is returned as is (restartIfRunning=true relaunches it).")]
    public static Task<ManagedProcessInfo> StartProcess(
        ProcessManager manager,
        [Description("Path or name of the program.")] string executable,
        [Description("Arguments as ONE text line, e.g. \"-avd Pixel6\".")] string? arguments = null,
        [Description("A short name for this program so you can stop it later with stop_process(label=...). Default: the program's file name.")] string? label = null,
        [Description("Working folder. Default: manctl's own folder.")] string? workingDirectory = null,
        [Description("How to know it is ready: \"none\" (default, ready at once), \"windowTitle\", \"port\", \"stdoutPattern\" or \"androidBoot\" (Android finished booting).")] string waitForType = "none",
        [Description("Goes with waitForType: part of a window title, or a port / host:port, or a regex for the output, or the device serial for androidBoot (default emulator-5554).")] string? waitForValue = null,
        [Description("How long to wait for ready, in seconds.")] double waitForTimeoutSeconds = 15,
        [Description("Lock files this program owns; they are deleted when it is stopped or after a crash.")] string[]? lockFilePaths = null,
        [Description("true = if it is already running under this label, stop it and start again.")] bool restartIfRunning = false,
        [Description("true = start as Administrator. The user must approve a Windows UAC prompt themselves. Not usable with waitForType=stdoutPattern.")] bool runAsAdmin = false)
        => manager.StartAsync(label ?? DefaultLabel(executable), executable, arguments, workingDirectory, waitForType, waitForValue,
            Units.Ms(waitForTimeoutSeconds), lockFilePaths ?? [], restartIfRunning, runAsAdmin);

    [McpServerTool(Name = "stop_process"), Description(
        "Stops a program started with start_process: stop_process(label=\"emulator\"). Closes it politely first, then kills it (with its child processes) if it does not exit in time. " +
        "Give the label from start_process, or a pid for any other program.")]
    public static Task<StopResult> StopProcess(
        ProcessManager manager,
        [Description("The label used in start_process (or its default, the program's file name without .exe).")] string? label = null,
        [Description("Process id, for programs not started by start_process.")] int? pid = null,
        [Description("true (default) = try to close politely before killing.")] bool graceful = true,
        [Description("How long to wait for the polite close before killing, in seconds.")] double timeoutSeconds = 5)
        => manager.StopAsync(label, pid, graceful, Units.Ms(timeoutSeconds));

    [McpServerTool(Name = "list_managed_processes"), Description("Lists the programs started with start_process and whether each is still running (label, pid, ready).")]
    public static List<ManagedProcessInfo> ListManagedProcesses(ProcessManager manager) => manager.ListManaged();

    private static string DefaultLabel(string executable)
    {
        var file = Path.GetFileNameWithoutExtension(executable.Trim().Trim('"'));
        return string.IsNullOrWhiteSpace(file) ? "process" : file;
    }
}
