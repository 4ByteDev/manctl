namespace Manctl.Server.Tools;

/// <summary>Every time value an agent sees is in seconds; the services below still count in milliseconds.</summary>
internal static class Units
{
    public static int Ms(double seconds) => (int)Math.Clamp(Math.Round(seconds * 1000), 0, int.MaxValue);
}
