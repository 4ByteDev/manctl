using Manctl.Core.Models;
using static Manctl.Core.Win32.NativeMethods;

namespace Manctl.Core.Win32;

/// <summary>Real OS-level mouse/keyboard input via SendInput — not emulation inside a sandboxed process.</summary>
public static class InputSimulator
{
    public static void MoveMouseTo(int x, int y)
    {
        var (nx, ny) = ToAbsolute(x, y);
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = nx,
                    dy = ny,
                    dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE,
                }
            }
        };
        Send(input);
    }

    public static void Click(int x, int y, MouseButton button, bool doubleClick = false)
    {
        MoveMouseTo(x, y);
        var (down, up) = button switch
        {
            MouseButton.Left => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP),
            MouseButton.Right => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
            MouseButton.Middle => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
            _ => throw new ArgumentOutOfRangeException(nameof(button)),
        };

        var clicks = doubleClick ? 2 : 1;
        for (var i = 0; i < clicks; i++)
        {
            SendMouseEvent(down);
            SendMouseEvent(up);
        }
    }

    public static void MouseDown(int x, int y, MouseButton button)
    {
        MoveMouseTo(x, y);
        SendMouseEvent(button switch
        {
            MouseButton.Left => MOUSEEVENTF_LEFTDOWN,
            MouseButton.Right => MOUSEEVENTF_RIGHTDOWN,
            MouseButton.Middle => MOUSEEVENTF_MIDDLEDOWN,
            _ => throw new ArgumentOutOfRangeException(nameof(button)),
        });
    }

    public static void MouseUp(int x, int y, MouseButton button)
    {
        MoveMouseTo(x, y);
        SendMouseEvent(button switch
        {
            MouseButton.Left => MOUSEEVENTF_LEFTUP,
            MouseButton.Right => MOUSEEVENTF_RIGHTUP,
            MouseButton.Middle => MOUSEEVENTF_MIDDLEUP,
            _ => throw new ArgumentOutOfRangeException(nameof(button)),
        });
    }

    /// <summary>Presses down at the start point, moves through intermediate steps (many apps only recognize a drag
    /// if they see movement between down and up, not a teleport), then releases at the end point.</summary>
    public static void Drag(int fromX, int fromY, int toX, int toY, MouseButton button = MouseButton.Left, int steps = 12, int stepDelayMs = 10)
    {
        MouseDown(fromX, fromY, button);

        for (var i = 1; i <= steps; i++)
        {
            var t = (double)i / steps;
            var x = fromX + (int)Math.Round((toX - fromX) * t);
            var y = fromY + (int)Math.Round((toY - fromY) * t);
            MoveMouseTo(x, y);
            Thread.Sleep(stepDelayMs);
        }

        MouseUp(toX, toY, button);
    }

    public static void Scroll(int deltaY, int deltaX = 0)
    {
        if (deltaY != 0)
        {
            Send(new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_WHEEL, mouseData = unchecked((uint)deltaY) } }
            });
        }
        if (deltaX != 0)
        {
            Send(new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_HWHEEL, mouseData = unchecked((uint)deltaX) } }
            });
        }
    }

    /// <summary>Types arbitrary Unicode text via KEYEVENTF_UNICODE — no keyboard-layout/VK mapping needed, works for any script.</summary>
    public static void TypeUnicodeText(string text)
    {
        foreach (var ch in text)
        {
            SendUnicodeChar(ch, keyUp: false);
            SendUnicodeChar(ch, keyUp: true);
        }
    }

    /// <summary>Sends a key combo like "ctrl+shift+s" using real virtual-key codes (required for modifier chords).</summary>
    public static void SendKeyChord(string chordSpec)
    {
        var parts = chordSpec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var vkCodes = parts.Select(ResolveVirtualKey).ToList();

        foreach (var vk in vkCodes)
            SendKeyEvent(vk, keyUp: false);

        for (var i = vkCodes.Count - 1; i >= 0; i--)
            SendKeyEvent(vkCodes[i], keyUp: true);
    }

    private static void SendMouseEvent(uint flags) =>
        Send(new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags } } });

    private static void SendKeyEvent(ushort vk, bool keyUp) =>
        Send(new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = keyUp ? KEYEVENTF_KEYUP : 0 } }
        });

    private static void SendUnicodeChar(char ch, bool keyUp) =>
        Send(new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wScan = ch,
                    dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0),
                }
            }
        });

    private static void Send(params INPUT[] inputs)
    {
        var size = System.Runtime.InteropServices.Marshal.SizeOf<INPUT>();
        var sent = SendInput((uint)inputs.Length, inputs, size);
        if (sent != inputs.Length)
            throw new InvalidOperationException($"SendInput only accepted {sent}/{inputs.Length} events (Win32 error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}).");
    }

    private static (int x, int y) ToAbsolute(int x, int y)
    {
        var vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        var nx = (int)Math.Round((x - vx) * 65535.0 / Math.Max(1, vw - 1));
        var ny = (int)Math.Round((y - vy) * 65535.0 / Math.Max(1, vh - 1));
        return (nx, ny);
    }

    private static readonly Dictionary<string, ushort> KeyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = 0x11, ["control"] = 0x11,
        ["shift"] = 0x10,
        ["alt"] = 0x12, ["menu"] = 0x12,
        ["win"] = 0x5B, ["windows"] = 0x5B,
        ["enter"] = 0x0D, ["return"] = 0x0D,
        ["esc"] = 0x1B, ["escape"] = 0x1B,
        ["tab"] = 0x09,
        ["space"] = 0x20,
        ["backspace"] = 0x08,
        ["delete"] = 0x2E, ["del"] = 0x2E,
        ["home"] = 0x24,
        ["end"] = 0x23,
        ["pageup"] = 0x21,
        ["pagedown"] = 0x22,
        ["up"] = 0x26,
        ["down"] = 0x28,
        ["left"] = 0x25,
        ["right"] = 0x27,
        ["f1"] = 0x70, ["f2"] = 0x71, ["f3"] = 0x72, ["f4"] = 0x73,
        ["f5"] = 0x74, ["f6"] = 0x75, ["f7"] = 0x76, ["f8"] = 0x77,
        ["f9"] = 0x78, ["f10"] = 0x79, ["f11"] = 0x7A, ["f12"] = 0x7B,
    };

    private static ushort ResolveVirtualKey(string token)
    {
        if (KeyMap.TryGetValue(token, out var vk))
            return vk;

        if (token.Length == 1)
        {
            var c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
                return c;
        }

        throw new ArgumentException($"Unknown key token '{token}'.");
    }
}
