using Manctl.Core.Models;
using Manctl.Core.Win32;

namespace Manctl.Core.Automation;

/// <summary>Answer of get_ui_tree: the tree, or why there is none.</summary>
public sealed record TreeResult(UiNode? Tree, string? ErrorCode, string? Error, WindowInfo? Window);

public static class UiTreeService
{
    /// <summary>Like GetTree, but a missing window and a window with no usable elements (game, canvas, ImGui — only a
    /// title bar) are reported as errors instead of as an empty-looking success.</summary>
    public static TreeResult GetTreeChecked(AutomationContext ctx, string scope, long? windowHandle, string? windowTitle, int maxDepth, bool interactiveOnly)
    {
        if (string.Equals(scope, "desktop", StringComparison.OrdinalIgnoreCase))
            return new TreeResult(GetTree(ctx, scope, windowHandle, windowTitle, maxDepth, interactiveOnly), null, null, null);

        lock (ctx.Gate)
        {
            var window = WindowManager.FindWindow(windowHandle, windowTitle);
            if (window is null)
            {
                var what = windowHandle is { } h ? $"handle {h}" : windowTitle is { } t ? $"'{t}' in the title" : "(the window in front)";
                return new TreeResult(null, ErrorCodes.WindowNotFound, $"No window with {what}.", null);
            }

            var root = ctx.Automation.FromHandle(new IntPtr(window.Handle));
            if (root is null)
                return new TreeResult(null, ErrorCodes.WindowNotFound, "Could not access that window.", window);

            if (!UiTreeBuilder.HasMeaningfulElements(root))
                return new TreeResult(null, ErrorCodes.NoElements,
                    $"Window '{window.Title}' exposes no UI elements (it is probably a game, canvas or ImGui window): there is nothing to list, and this says nothing about what is drawn in it.",
                    window);

            return new TreeResult(UiTreeBuilder.Build(root, maxDepth, interactiveOnly), null, null, window);
        }
    }

    /// <summary>scope="window": tree rooted at a specific window (by handle/title, or the foreground window
    /// if neither is given). scope="desktop": one level deep over top-level windows only.</summary>
    public static UiNode? GetTree(AutomationContext ctx, string scope, long? windowHandle, string? windowTitle, int maxDepth, bool interactiveOnly)
    {
        lock (ctx.Gate)
        {
            if (string.Equals(scope, "desktop", StringComparison.OrdinalIgnoreCase))
            {
                var desktop = ctx.Automation.GetDesktop();
                return UiTreeBuilder.Build(desktop, maxDepth: 1, interactiveOnly: false);
            }

            var window = WindowManager.FindWindow(windowHandle, windowTitle);
            if (window is null)
                return null;

            var root = ctx.Automation.FromHandle(new IntPtr(window.Handle));
            return root is null ? null : UiTreeBuilder.Build(root, maxDepth, interactiveOnly);
        }
    }
}
