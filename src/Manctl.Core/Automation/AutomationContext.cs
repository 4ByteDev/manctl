using FlaUI.UIA3;

namespace Manctl.Core.Automation;

/// <summary>Single long-lived UIA3Automation instance for the whole server process. UIA/COM calls are not
/// thread-safe, so every caller must take <see cref="Gate"/> before touching Automation or any AutomationElement.</summary>
public sealed class AutomationContext : IDisposable
{
    public UIA3Automation Automation { get; } = new();
    public object Gate { get; } = new();

    public void Dispose() => Automation.Dispose();
}
