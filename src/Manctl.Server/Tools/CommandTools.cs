using System.ComponentModel;
using System.Text;
using Manctl.Core.Commands;
using Manctl.Core.Models;
using ModelContextProtocol.Server;

namespace Manctl.Server.Tools;

[McpServerToolType]
public static class CommandTools
{
    private const string HowToPickCommand =
        "Say what to run in ONE of two ways: command=\"git status\" (a whole line, run through the shell) OR executable=\"git\" with arguments=\"status\".";

    [McpServerTool(Name = "run_command"), Description(
        "Runs a console command, waits until it finishes and returns its output: run_command(command=\"git status\", workingDirectory=\"E:\\\\proj\"). " +
        "Or split it: executable=\"git\", arguments=\"status\". For long jobs (builds, analysis) raise timeoutSeconds (max 1800). " +
        "If it does not finish in time it is killed (timedOut:true). Use run_command_async for something that runs a long time or forever.")]
    public static Task<CommandResult> RunCommand(
        CommandExecutionService commands,
        [Description("A whole command line, e.g. \"npm run build\". Runs through the shell (cmd by default). Use this OR executable.")] string? command = null,
        [Description("Only the program name or path, e.g. \"git\". The rest goes in arguments. Use this OR command.")] string? executable = null,
        [Description("Arguments as ONE text line, e.g. \"status\" (with executable).")] string? arguments = null,
        [Description("Folder to run in. Default: manctl's own folder.")] string? workingDirectory = null,
        [Description("With command: \"cmd\" (default) or \"powershell\".")] string shell = "cmd",
        [Description("How long to wait before killing it, in seconds (max 1800).")] double timeoutSeconds = 30)
    {
        var plan = Plan(command, executable, arguments, shell);
        if (plan.Error is not null)
            return Task.FromResult(new CommandResult(false, null, "", "", false, Error: plan.Error, ErrorCode: ErrorCodes.InvalidArgument, Hint: plan.Hint));
        return commands.RunSyncAsync(plan.Executable!, plan.Arguments, workingDirectory, Units.Ms(timeoutSeconds));
    }

    [McpServerTool(Name = "run_command_async"), Description(
        "Starts a console command and does not wait: run_command_async(command=\"node server.js\", workingDirectory=\"E:\\\\app\"). " +
        "Returns an id. Read the output later with get_command_output(id) and end it with stop_command(id). " +
        "For a program with a window that you want to see, use start_process.")]
    public static CommandHandle RunCommandAsync(
        CommandExecutionService commands,
        [Description("A whole command line, e.g. \"node server.js\". Runs through the shell (cmd by default). Use this OR executable.")] string? command = null,
        [Description("Only the program name or path. The rest goes in arguments. Use this OR command.")] string? executable = null,
        [Description("Arguments as ONE text line (with executable).")] string? arguments = null,
        [Description("Folder to run in. Default: manctl's own folder.")] string? workingDirectory = null,
        [Description("With command: \"cmd\" (default) or \"powershell\".")] string shell = "cmd")
    {
        var plan = Plan(command, executable, arguments, shell);
        if (plan.Error is not null)
            return new CommandHandle("", 0, Error: plan.Error, ErrorCode: ErrorCodes.InvalidArgument, Hint: plan.Hint);
        return commands.Start(plan.Executable!, plan.Arguments, workingDirectory);
    }

    [McpServerTool(Name = "get_command_output"), Description(
        "Gets the output of a command started with run_command_async. Pass back stdOutOffset/stdErrOffset from the previous answer to receive only what is new.")]
    public static object GetCommandOutput(
        CommandExecutionService commands,
        [Description("The id returned by run_command_async.")] string id,
        [Description("Only stdout after this offset (use the stdOutOffset of the previous answer; 0 = everything).")] long stdOutOffset = 0,
        [Description("Only stderr after this offset (0 = everything).")] long stdErrOffset = 0)
    {
        var snapshot = commands.GetOutput(id, stdOutOffset, stdErrOffset);
        return snapshot is null
            ? new OkResult(false, ErrorCodes.InvalidArgument, $"No command with id '{id}' (it was never started, or already stopped and cleaned up).", "Use list_commands to see the ids that exist.")
            : snapshot;
    }

    [McpServerTool(Name = "stop_command"), Description("Ends a command started with run_command_async and returns the output it produced so far.")]
    public static CommandResult StopCommand(
        CommandExecutionService commands,
        [Description("The id returned by run_command_async.")] string id,
        [Description("true (default) = try to close politely before killing.")] bool graceful = true,
        [Description("How long to wait for the polite close before killing, in seconds.")] double timeoutSeconds = 3)
        => commands.Stop(id, graceful, Units.Ms(timeoutSeconds));

    [McpServerTool(Name = "list_commands"), Description("Lists the commands started with run_command_async: id, whether still running, exit code.")]
    public static List<CommandInfo> ListCommands(CommandExecutionService commands) => commands.List();

    private sealed record CommandPlan(string? Executable, string? Arguments, string? Error, string? Hint);

    private static CommandPlan Plan(string? command, string? executable, string? arguments, string shell)
    {
        var hasCommand = !string.IsNullOrWhiteSpace(command);
        var hasExe = !string.IsNullOrWhiteSpace(executable);

        if (hasCommand && (hasExe || arguments is not null))
            return new CommandPlan(null, null, "Give command OR executable (+arguments), not both.", HowToPickCommand);
        if (!hasCommand && !hasExe)
            return new CommandPlan(null, null, "Nothing to run.", HowToPickCommand);

        if (hasCommand)
        {
            switch (shell.Trim().ToLowerInvariant())
            {
                case "cmd":
                    // /s /c "<line>" : /s makes cmd strip exactly the outer pair of quotes, so quotes inside survive.
                    return new CommandPlan("cmd.exe", $"/s /c \"{command}\"", null, null);
                case "powershell" or "pwsh":
                    // EncodedCommand sidesteps every quoting problem between the agent's text and PowerShell.
                    var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command!));
                    return new CommandPlan("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}", null, null);
                default:
                    return new CommandPlan(null, null, $"Unknown shell '{shell}'.", "shell must be \"cmd\" or \"powershell\".");
            }
        }

        // "git status" given as executable is the classic mistake: the program name has a space and no such file exists.
        var exe = executable!.Trim();
        if (arguments is null && exe.Any(char.IsWhiteSpace) && !File.Exists(exe.Trim('"')))
        {
            var parts = exe.Split(' ', 2);
            return new CommandPlan(null, null, $"executable '{exe}' looks like a whole command line, not a program name.",
                $"Use command=\"{exe}\" (the whole line), or split it: executable=\"{parts[0]}\", arguments=\"{(parts.Length > 1 ? parts[1] : "")}\".");
        }

        return new CommandPlan(exe, arguments, null, null);
    }
}
