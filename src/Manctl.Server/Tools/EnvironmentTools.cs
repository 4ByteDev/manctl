using System.ComponentModel;
using Manctl.Core.Models;
using Manctl.Core.Processes;
using ModelContextProtocol.Server;

namespace Manctl.Server.Tools;

[McpServerToolType]
public static class EnvironmentTools
{
    [McpServerTool(Name = "set_env_var"), Description(
        "Sets an environment variable for the programs started afterwards by run_command, run_command_async and start_process. " +
        "scope=\"process\" (default): only while manctl runs. scope=\"user\": permanently for the Windows user, but it does not change programs already running. " +
        "An empty value deletes the variable. Example: set_env_var(name=\"JAVA_HOME\", value=\"C:\\jdk\").")]
    public static object SetEnvVar(
        [Description("Variable name.")] string name,
        [Description("Value to set. Empty or omitted deletes the variable.")] string? value = null,
        [Description("\"process\" (default) or \"user\".")] string scope = "process")
    {
        var r = EnvironmentVarService.Set(name, value, scope);
        return r.Ok ? r : new OkResult(false, ErrorCodes.InvalidArgument, r.Error, "scope must be \"process\" or \"user\".");
    }

    [McpServerTool(Name = "get_env_var"), Description("Reads an environment variable. Answer: {ok:true, value:\"...\"}; value is null if it is not set. Use scope=\"user\" to read the permanent per-user value.")]
    public static ValueResult GetEnvVar(
        [Description("Variable name.")] string name,
        [Description("\"process\" (default) or \"user\".")] string scope = "process")
        => new(true, EnvironmentVarService.Get(name, scope));
}
