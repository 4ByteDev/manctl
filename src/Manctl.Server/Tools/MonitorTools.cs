using System.ComponentModel;
using Manctl.Core.Models;
using Manctl.Core.Win32;
using ModelContextProtocol.Server;

namespace Manctl.Server.Tools;

[McpServerToolType]
public static class MonitorTools
{
    [McpServerTool(Name = "list_monitors"), Description(
        "Lists physical monitors: bounds and work area in physical pixels (same space as get_ui_tree/click), " +
        "per-monitor DPI, and which one is primary. Use this to reason about window placement across a multi-monitor setup.")]
    public static List<MonitorInfo> ListMonitors() => MonitorService.ListMonitors();
}
