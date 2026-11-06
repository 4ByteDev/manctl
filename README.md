# manctl

An [MCP](https://modelcontextprotocol.io) server that lets an AI agent control a real Windows PC (and an Android device over `adb`) —
mouse, keyboard, windows, programs, console commands, clipboard.

by [4ByteDev](https://github.com/4ByteDev)

## Quick start

1. **Build** (Windows, [.NET 8 SDK](https://dotnet.microsoft.com/download)):

   ```powershell
   dotnet build manctl.sln -c Release
   ```

2. **Connect it to your MCP client** — for Claude Code (use the full path to the exe you just built, and restart the client):

   ```powershell
   claude mcp add manctl -- "C:\path\to\manctl\src\Manctl.Server\bin\Release\net8.0-windows\Manctl.Server.exe"
   ```

3. **First call** — ask the agent: *"Use manctl to list my open windows."* It calls `list_windows` and gets back something like:

   ```json
   [ { "handle": 3539666, "title": "Claude", "className": "Chrome_WidgetWin_1", "processId": 4992,
       "bounds": { "x": 153, "y": 47, "width": 1493, "height": 935 },
       "isVisible": true, "isMinimized": false, "isForeground": true } ]
   ```

   (Pure listing tools return the list itself; every action returns `{"ok": …}`.) Pass a window's `handle` as `windowHandle` to the other tools.

   From there: `read_text` / `get_ui_tree` to look, `click` / `type_text` / `key_press` to act, `verify` to check.

Details, the full tool list and the safety notes are below.

## Overview

It is built for agents, including small/weak models: every tool has plain parameter names, every answer has the same
shape (`ok`, and on failure `errorCode`, `message`, `hint`), and a failure tells the agent what to try next instead of
just saying "error".

- Windows 10/11, .NET 8, stdio MCP server (optional loopback HTTP mode)
- 52 tools in 6 groups (listed below)
- Version 2.0.0

> **It acts on your real machine.** Read the [Security](#security) section before connecting it to an agent.

## Why another computer-control server

| Problem agents run into | What manctl does |
|---|---|
| Screenshots cost many tokens | Text-first tools: `read_text`, `get_ui_tree`, `find_element`. Screenshots take an *area* and a `scale`. |
| Games / canvas / ImGui windows have no UI elements | Those tools answer `ok:false, errorCode:"NoElements"` (not an empty "success"), and tell the agent to use a screenshot + `click(x, y)`. |
| Wrong parameter name → useless "An error occurred" | The server checks arguments against each tool's schema first: *"Unknown parameter 'key'. Did you mean 'keys'?"* plus the tool's real parameter list. |
| Windows refuses to bring a window to the front | `focus_window` tries seven ways in turn, verifies the result, and if all fail explains why and what still works without focus. Keys/clicks are **never** sent to the wrong window after a failed focus. |
| "Success" that did nothing | `close_window` is `ok:true` only when the window is really gone; `set_window_bounds` reports the bounds the window really got; a click outside the screen is an error. |
| Agents poll in loops | Server-side waits: `wait_for_window`, `wait_for_element`, `wait_for_file`, `wait_for_process_idle`. |

## Build

Requirements: Windows, [.NET 8 SDK](https://dotnet.microsoft.com/download) (or newer). `adb` on `PATH` only for the Android tools.

```powershell
dotnet build manctl.sln -c Release
# -> src\Manctl.Server\bin\Release\net8.0-windows\Manctl.Server.exe
```

## Connect it to an MCP client

**Claude Code**

```powershell
claude mcp add manctl -- "C:\path\to\Manctl.Server.exe"
```

or in a project `.mcp.json`:

```json
{
  "mcpServers": {
    "manctl": { "command": "C:\\path\\to\\Manctl.Server.exe", "args": [] }
  }
}
```

Restart the client (MCP servers are loaded at start-up). Check it works by asking the agent to call `list_windows`.

**Any other MCP client** — run the exe as a stdio server (`command` = the exe, no arguments).

**HTTP mode (optional)** — `Manctl.Server.exe --http --port 3001`. It listens on `127.0.0.1` only and requires
`Authorization: Bearer <token>`; set the token with the `MANCTL_TOKEN` environment variable, otherwise a random one is
generated and printed to stderr at start-up.

### What the agent is told

On connection the server sends the agent a short rulebook (MCP `instructions`, see `src/Manctl.Server/ServerRules.cs`): look
→ act → check, text before pictures, `click` for elements *and* points, never poll in a loop, same words everywhere
(`name`, `text`, seconds), read `hint` on failure, and — before its first call — to tell the user in one line what manctl is
and who made it.

## Tools

| Group | Tools |
|---|---|
| Windows & monitors | `list_windows`, `get_foreground_window`, `focus_window`, `wait_for_window`, `set_window_state`, `set_window_bounds`, `close_window`, `list_monitors` |
| Elements & screen | `read_text`, `get_ui_tree`, `find_element`, `wait_for_element`, `verify`, `screenshot` |
| Input | `click` (element **or** screen point), `type_text`, `key_press`, `mouse_move`, `scroll`, `drag`, `run_sequence`, `handle_file_dialog`, `lock_input`, `unlock_input`, `get_clipboard`, `set_clipboard` |
| Programs & commands | `start_process`, `stop_process`, `list_managed_processes`, `wait_for_process_idle`, `run_command`, `run_command_async`, `get_command_output`, `stop_command`, `list_commands` |
| Files & environment | `wait_for_file`, `set_env_var`, `get_env_var` |
| Android (adb) | `android_list_devices`, `android_ui_tree`, `android_find`, `android_click`, `android_swipe`, `android_key_event`, `android_input_text`, `android_wait_for_element`, `android_verify`, `android_screenshot`, `android_install_latest_apk`, `android_logcat`, `android_pull`, `android_push` |

Parameter-level documentation: [docs/API.md](docs/API.md) — generated from the running server by `tests/api-agent-eval/gen_cards.py`, i.e. exactly what an agent receives.

### Conventions

- Element: `name` (the visible text, substring match), `role`, `automationId`, `index`. `text` is only ever *what gets typed*.
- Window: `windowHandle` (from `list_windows`) or `windowTitle` (part of the title). Neither → the window in front.
- Coordinates: physical **screen** pixels everywhere (`android_*`: device pixels).
- Time: always seconds (`timeoutSeconds`, `pollSeconds`).
- Answers (listing tools such as `list_windows` return the bare list): `{"ok": true, ...}` or `{"ok": false, "errorCode": "...", "message": "...", "hint": "..."}`. Error codes:
  `ElementNotFound`, `NoElements`, `WindowNotFound`, `ProcessNotFound`, `Timeout`, `AccessDenied`, `InvalidArgument`, `FileNotFound`, `Unknown`.
  A wait that runs out of time is `ok:false, errorCode:"Timeout"`.
- Harmless type slips are accepted (`"1234"` for a number, `"true"` for a boolean).

## Security

manctl gives whatever is connected to it the same control you have at the keyboard. Treat it accordingly.

- **Real input.** Clicks, keys and typing are real OS-level input on your desktop, not a sandbox. Anything an agent can reach
  with mouse and keyboard, it can change or delete.
- **`run_command` / `run_command_async`** run arbitrary console commands as the user that started manctl (`cmd` by default, or
  `powershell`). **`start_process`** starts any program; with `runAsAdmin=true` Windows shows the normal UAC prompt, which only
  the user can approve — manctl cannot bypass it. Run manctl as a normal user, not as Administrator, unless you need the
  features below.
- **`lock_input`** blocks the physical mouse/keyboard so a sequence of clicks cannot be disturbed. It works only if manctl runs
  elevated (Windows' `BlockInput` fails otherwise — the answer says so), and it releases itself after 60 seconds even if the
  agent forgets `unlock_input`. `run_sequence` uses it by default and reports `inputLocked` and a `warning` if it could not.
- **Elevated windows.** A normal manctl cannot focus or send input to windows of programs running as Administrator; the
  error says so.
- **Input goes to the window in front.** Keys/typing without an element go to the foreground window; the answer names the
  window that received them (`resolvedName`) so a wrong target is visible. If a requested window cannot be brought forward,
  nothing is sent.
- **Passwords.** `read_text` never reads password fields.
- **Network.** In stdio mode manctl opens no sockets. HTTP mode is loopback-only and token-protected. The only outgoing
  connection manctl can make is the optional readiness probe of `start_process(waitForType="port")` to the `host:port` you
  give it. There is no telemetry.
- **State on disk.** `%LOCALAPPDATA%\manctl\managed-processes.json` remembers processes started with `start_process`
  (label, pid, lock-file paths) so lock files can be cleaned up after a crash.
- Logs go to stderr only.

Actions an operator should consider dangerous to hand to an unattended agent: `run_command*`, `start_process`
(especially `runAsAdmin`), `stop_process`/`stop_command` (kills), `close_window`, `key_press`/`type_text` (can answer
"Yes" to anything), `set_env_var(scope="user")` (persists), `set_clipboard`, `android_push`/`android_install_latest_apk`.

## Tests

```powershell
python tests\api_v2_test.py src\Manctl.Server\bin\Release\net8.0-windows\Manctl.Server.exe   # 152 checks, ~2 min
python tests\focus_test.py  src\Manctl.Server\bin\Release\net8.0-windows\Manctl.Server.exe   # window-focus ladder
```

They talk to the built server over stdio and open a few small windows of their own (PowerShell + WinForms) which they close
again; they need Python 3 and Windows PowerShell. The Android tools are exercised without a device (argument handling and
failure answers only). `tests/focus_test.py` uses the hook `MANCTL_FOCUS_TEST_SKIP=N` to simulate Windows refusing the first
N focus attempts.

`tests/api-agent-eval/` is a kit for measuring how well a *small AI model* understands the API from its descriptions alone;
see its README.

## Project layout

```
src/Manctl.Core      Windows (UI Automation via FlaUI, Win32, SendInput), processes, commands, Android/adb services
src/Manctl.Server    MCP server: tool definitions (Tools/), argument validation + answer shaping (Guard/), agent rules
tests/               stdio integration tests + the evaluation kit
docs/API.md          the API as an agent sees it
```

## Known limitations

- Windows only (UI Automation + Win32). No OCR: pictures are returned as images, not read.
- Apps that draw their own UI without exposing UI Automation (games, canvas, ImGui, parts of browsers/Electron) expose little
  or nothing to `read_text` / `get_ui_tree`; use `screenshot` and `click(x, y)`.
- `android_input_text` types ASCII only (limit of `adb shell input text`). `android_key_event` is `adb input keyevent` and does
  not reproduce real TV-remote focus behaviour.
- The Android helpers added in 2.0 (`android_find`, `android_logcat`, `android_pull`, `android_push`) have been tested only
  without a connected device so far.
- Real foreground-lock refusals are hard to reproduce; the focus ladder is tested with simulated refusals.

## License

[MIT](LICENSE) © 4ByteDev
