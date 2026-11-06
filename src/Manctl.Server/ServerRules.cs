namespace Manctl.Server;

/// <summary>The short rulebook every agent that connects to manctl receives in the MCP handshake ("instructions").
/// It is written for the weakest model: ten plain rules, no exceptions to the exceptions. If a rule here stops being
/// true (a tool renamed, a default changed), fix it here first — agents trust this text over their own guesses.</summary>
internal static class ServerRules
{
    /// <summary>Version shown to the user in the introduction line (read from the assembly, so it can never go stale).</summary>
    private static readonly string Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?";

    public static readonly string Text = $$"""
        manctl {{Version}} is a PC-control tool made by 4ByteDev. It controls the user's REAL Windows PC (mouse, keyboard, windows, programs, Android over adb).

        Before anything else: the first time you use manctl in a conversation, tell the user in one short line, in the user's language, what it is and who made it (for example: "I am using manctl, a PC-control tool by 4ByteDev: it can click, type, open programs and run commands on your computer."). Say it once, before your first manctl call.

        Rules:

        1. Look, act, check. First list_windows / read_text / get_ui_tree / find_element, then act, then verify (or a small screenshot).
        2. Text before pictures. read_text (all text of a window; contains=... to check for a word), get_ui_tree and find_element are cheap; screenshot costs many tokens. Use screenshot only when there are no elements (games, canvas, ImGui) or you must see pixels, and then take just an area (x, y, width, height) and/or scale below 1. If a tool answers errorCode NoElements, the window shows no elements at all: that says nothing about what is on screen, so take a screenshot.
        3. To click use click: click(name="Save") for an element, click(x=640, y=360) for a point. x,y are always SCREEN coordinates, even if windowHandle is given. Do not use drag to click.
        4. Never poll in a loop. To wait use wait_for_window / wait_for_element / wait_for_file / wait_for_process_idle.
        5. Get a window's windowHandle once from list_windows and reuse it. windowTitle (part of the title) works when the handle is unknown. With neither, the window in front is used.
        6. Several steps in a row: run_sequence (give the window once at the top).
        7. Same words everywhere: name = how to find an element (the text on it), role, automationId, index; text = only what type_text types; time is always in SECONDS (timeoutSeconds, pollSeconds).
        8. Every action answers {ok, ...}. If ok is false read errorCode, message and hint, fix the call, and do not repeat it blindly. A wait that ran out of time is ok:false, errorCode "Timeout". Keys and typed text go to the window in front: the answer names it (resolvedName), check it. A failure is a cue to try the next step in its hint: tell the user it cannot be done only after those options are used up (e.g. if focus_window fails, click(name), type_text(name) and read_text still work without the window in front).
        9. Programs: start_process for apps/games/servers that keep running (label them to stop later); run_command for a console command whose output you need; run_command_async for one that runs long or forever.
        10. This acts on a real machine. Do not touch windows you did not open, and do not run destructive commands without being asked.
        """;
}
