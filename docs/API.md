<!-- Generated from the running server by tests/api-agent-eval/gen_cards.py. This is exactly what an MCP client/agent receives: the server instructions, then one card per tool. -->

# manctl server instructions

manctl 2.0.0 is a PC-control tool made by 4ByteDev. It controls the user's REAL Windows PC (mouse, keyboard, windows, programs, Android over adb).



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

# Tools

### android_list_devices
Lists the connected Android phones/emulators: serial, state (device = ready; offline/unauthorized = not usable), model.
Parameters: none

### get_ui_tree
Shows what is in a window as a tree of elements (role, name, value, position, enabled, supported actions) — far cheaper than a screenshot. Call it before deciding what to click. Games, canvas and ImGui apps have almost no elements: use screenshot for those. Without windowHandle/windowTitle it looks at the window in front. If the window shows no elements at all the answer is ok:false, errorCode NoElements (not an empty tree).
Parameters:
  - scope (string): "window" (default): the elements of one window. "desktop": the list of top-level windows, one level deep.
  - windowHandle (number): Window handle (number from list_windows).
  - windowTitle (string): Part of the window title (alternative to windowHandle).
  - maxDepth (number): How deep to go into the tree.
  - interactiveOnly (true/false): true (default) = leave out unnamed, non-interactive parts to save space. false = everything.

### click
Clicks an element by name — click(name="Save", windowHandle=1234) — OR a point on the screen — click(x=640, y=360). Use x,y when the target has no name (games, canvas, ImGui apps); x,y are always SCREEN coordinates, even if windowHandle is given. Give either an element (name/role/automationId) or x and y, not both.
Parameters:
  - name (string): Element name (the text you see on it), case-insensitive part of the name.
  - role (string): Element type, e.g. "Button", "Edit", "CheckBox", "ComboBox", "ListItem", "MenuItem".
  - automationId (string): Exact AutomationId of the element, if known.
  - x (number): Screen X to click (use together with y when the target has no name).
  - y (number): Screen Y to click.
  - windowHandle (number): Window handle (number from list_windows). Window: windowHandle (number from list_windows) or windowTitle (part of the title). Omit both to use the window in front.
  - windowTitle (string): Part of the window title (alternative to windowHandle).
  - index (number): If several elements match, pick the Nth (0 = first).
  - button (string): "left" (default), "right" or "middle".
  - doubleClick (true/false): true = double-click.

### key_press
Presses a key or key combination: key_press(keys="ctrl+s"), keys="enter", keys="alt+f4", keys="win+r", keys="tab". The keys go to the window in front (or the windowHandle you give); the answer names that window in resolvedName, so you can see where they went.
Parameters:
  - keys (string, required): Keys joined with '+', e.g. "ctrl+shift+s", "enter", "f5".
  - windowHandle (number): Window handle to bring to the front first (optional).

### set_window_state
Minimizes, maximizes or restores a window: set_window_state(state="maximized", windowHandle=777). state: "minimized", "maximized" or "normal" (restore to the usual size). The answer shows the state and bounds the window really ended up with.
Parameters:
  - state (string, required): "minimized", "maximized" or "normal".
  - windowHandle (number): Window handle (number from list_windows).
  - windowTitle (string): Part of the window title (alternative to windowHandle).

### wait_for_file
Waits (on the server) for a file: wait_for_file(path="C:\log.txt", mode="contains_text", matchText="DONE", timeoutSeconds=600). Use it instead of checking in a loop. Answers matched:true; if it does not happen in time: ok:false, errorCode:"Timeout", matched:false.
Parameters:
  - path (string, required): Full path of the file.
  - mode (string, required): "exists" (wait until it appears), "modified" (until it changes), "contains_text" (until matchText shows up in it) or "deleted" (until it is gone).
  - matchText (string): The text to look for when mode="contains_text" (plain text, not a pattern).
  - timeoutSeconds (number): How long to wait, in seconds (max 1800).
  - pollSeconds (number): How often to re-check, in seconds.

### type_text
Types text into an element: type_text(text="hello", name="Search", windowHandle=1234). 'text' is what gets typed; 'name' finds the element. With no name/role/automationId it types into whatever has keyboard focus (in the given window); the answer names the window that received it in resolvedName.
Parameters:
  - text (string, required): The text to type.
  - name (string): Element name (the text you see on it), case-insensitive part of the name.
  - role (string): Element type, e.g. "Edit", "Document", "ComboBox".
  - automationId (string): Exact AutomationId of the element, if known.
  - windowHandle (number): Window handle (number from list_windows). Window: windowHandle (number from list_windows) or windowTitle (part of the title). Omit both to use the window in front.
  - windowTitle (string): Part of the window title (alternative to windowHandle).
  - index (number): If several elements match, pick the Nth (0 = first).
  - forceSendKeys (true/false): true = send real key presses even if the element could be set directly.

### wait_for_window
Waits (on the server) until a window appears or closes: wait_for_window(windowTitle="Setup", mode="appears", timeoutSeconds=120). Answers matched:true plus windowHandle for later calls. If it does not happen in time: ok:false, errorCode:"Timeout", matched:false.
Parameters:
  - windowTitle (string): Part of the window title.
  - className (string): Exact window class name, if known (more reliable than the title).
  - mode (string): "appears" (wait until such a window exists) or "closes" (wait until it is gone).
  - timeoutSeconds (number): How long to wait, in seconds (max 1800).
  - pollSeconds (number): How often to re-check, in seconds.

### set_clipboard
Puts text into the clipboard (handy for pasting long text with key_press(keys="ctrl+v")).
Parameters:
  - text (string, required): Text to place on the clipboard.

### set_window_bounds
Moves and/or resizes a window in screen pixels. Give any of x, y (top-left corner) and width, height. Example, left half of a 1920x1080 screen: set_window_bounds(x=0, y=0, width=960, height=1080, windowHandle=5). A maximized or minimized window is restored first. The answer shows the bounds the window really got: windows have minimum/maximum sizes, so it can differ from what you asked, and then ok is false.
Parameters:
  - x (number): New left edge, screen pixels.
  - y (number): New top edge, screen pixels.
  - width (number): New width in pixels.
  - height (number): New height in pixels.
  - windowHandle (number): Window handle (number from list_windows).
  - windowTitle (string): Part of the window title (alternative to windowHandle).

### mouse_move
Moves the mouse pointer to a point on the screen (screen pixels): mouse_move(x=500, y=300). Rarely needed: click(x, y) and scroll(x, y) already move the pointer themselves.
Parameters:
  - x (number, required): Screen X.
  - y (number, required): Screen Y.

### get_foreground_window
Which window is in front right now (same fields as one list_windows entry). A quick check before sending keys or clicks.
Parameters: none

### screenshot
Takes a picture. A picture costs many tokens: prefer get_ui_tree / read text when text is enough, and use this for games, canvas, ImGui apps and visual checks. screenshot() = the whole screen; screenshot(windowHandle=55) = one window; screenshot(x=100, y=200, width=300, height=40) = just that area (screen coordinates, the same as click). scale makes it cheaper or bigger: scale=0.5 is half size, scale=2 enlarges tiny text. To read small text: pick a small area and scale=2. If the picture is one flat color (a black game window, a covered or protected window) a short note says so.
Parameters:
  - windowHandle (number): Window handle (number from list_windows) to photograph only that window.
  - windowTitle (string): Part of the window title (alternative to windowHandle).
  - x (number): Area: left edge in SCREEN pixels (use with y, width, height).
  - y (number): Area: top edge in SCREEN pixels.
  - width (number): Area: width in pixels.
  - height (number): Area: height in pixels.
  - scale (number): Size factor from 0.05 to 8. 1 = original, 0.5 = half (cheap), 2 = twice as big (to read tiny text).
  - format (string): "png" (default, sharp) or "jpeg" (smaller file).
  - quality (number): JPEG quality 1-100 (only for jpeg).

### verify
Checks one property of an element and answers ok true/false: verify(property="toggled", expected="true", name="Remember me", windowHandle=9). Cheaper than a screenshot to confirm that a click or typing worked.
Parameters:
  - property (string, required): What to read: "name", "value", "enabled", "toggled" (checkbox on/off) or "offscreen".
  - expected (string): Expected value as text. For toggled/enabled/offscreen use "true" or "false". If omitted, ok just means the property could be read.
  - name (string): Element name (the text you see on it), case-insensitive part of the name.
  - role (string): Element type, e.g. "CheckBox", "Edit", "Button".
  - automationId (string): Exact AutomationId of the element, if known.
  - windowHandle (number): Window handle (number from list_windows).
  - windowTitle (string): Part of the window title (alternative to windowHandle).
  - index (number): If several elements match, pick the Nth (0 = first).

### run_sequence
Does several steps in ONE call (click, type_text, key_press, wait_for_element, verify, mouse_move, drag, scroll) and stops at the first failure. Give the window once at the top and it applies to every step. Example: run_sequence(windowHandle=42, steps=[{action:"click", name:"Name"}, {action:"type_text", text:"Ivan"}, {action:"key_press", keys:"tab"}, {action:"click", name:"OK"}]). In a step, 'name' finds an element and 'text' is what gets typed; a type_text step without name types into the focused field; a click step with x and y clicks that screen point. Blocking the user's mouse/keyboard (lockInput) only works if manctl runs as Administrator: check inputLocked in the answer.
Parameters:
  - steps (list, required): The steps, in order.
    each item has:
      - action (string, required): "click", "type_text", "key_press", "wait_for_element", "verify", "mouse_move", "drag", or "scroll".
      - windowHandle (string/integer): Window handle for this step. Not needed if windowHandle/windowTitle was given once for the whole sequence.
      - windowTitle (string): Part of the window title for this step (alternative to windowHandle).
      - name (string): Element: name (visible text), case-insensitive substring. For click, type_text, wait_for_element, verify.
      - automationId (string): Element: exact AutomationId.
      - role (string): Element: control type, e.g. "Button", "Edit", "CheckBox".
      - index (number): Element: if several match, pick the Nth (0-based).
      - button (string): click/drag: "left" (default), "right" or "middle".
      - doubleClick (true/false): click: send a double-click instead of a single click.
      - text (string): type_text: the text to type. (To find an element use name, not text.)
      - forceSendKeys (true/false): type_text: force real keyboard input even if the element supports direct value-setting.
      - keys (string): key_press: key combo, '+'-separated, e.g. "ctrl+shift+s".
      - timeoutSeconds (number): wait_for_element: max time to wait, in seconds.
      - pollSeconds (number): wait_for_element: how often to re-check, in seconds.
      - property (string): verify: property to read — name, value, enabled, toggled or offscreen.
      - expected (string): verify: expected value as text; for toggled/enabled/offscreen use "true" or "false". If omitted, success just means the property was readable.
      - x (string/integer): click: screen X to click when there is no element name (games, canvas). mouse_move: target X. scroll: X to scroll at.
      - y (string/integer): click: screen Y (see x). mouse_move: target Y. scroll: Y to scroll at.
      - fromX (number): drag: start X.
      - fromY (number): drag: start Y.
      - toX (number): drag: end X.
      - toY (number): drag: end Y.
      - deltaX (number): scroll: horizontal amount, multiples of 120.
      - deltaY (number): scroll: vertical amount, multiples of 120; positive scrolls up, negative down.
  - windowHandle (number): Window handle used by every step that has none of its own.
  - windowTitle (string): Part of the window title used by every step that has none of its own.
  - stopOnError (true/false): true (default) = stop at the first failing step. false = run all steps anyway.
  - lockInput (true/false): true (default) = block the user's own mouse/keyboard while the steps run (needs Administrator, see inputLocked).

### android_install_latest_apk
Installs the newest file matching a pattern from a folder (handy when the build number is in the file name): android_install_latest_apk(directory="E:\\build\\apk") installs the newest *.apk there.
Parameters:
  - directory (string, required): Folder to look in.
  - pattern (string): File name pattern, e.g. "app-*-debug.apk". Default "*.apk".
  - reinstall (true/false): true (default) = replace an existing install, keeping its data.
  - timeoutSeconds (number): How long to wait for the install, in seconds.
  - deviceSerial (string): Device serial from android_list_devices, e.g. "emulator-5554". Leave out if only one device is connected.

### handle_file_dialog
Fills in a Windows Open/Save file dialog and presses its button: handle_file_dialog(path="C:\\out\\report.txt", action="save"). Waits for the dialog to appear if it is not open yet.
Parameters:
  - path (string, required): Full path of the file to open or save.
  - action (string, required): "open" or "save": which button to press.
  - timeoutSeconds (number): How long to wait for the dialog to appear, in seconds.

### lock_input
Blocks the real user's physical mouse/keyboard input so a click/type_text chain can't be interrupted by the user touching the mouse or keyboard mid-sequence. Auto-releases after 60 seconds even if unlock_input is never called — a real input block is never left unbounded. run_sequence already does this itself (lockInput=true by default); call this directly only when doing several separate click/type_text calls outside run_sequence that need the whole episode protected. REQUIRES manctl to be running elevated (as Administrator) — Windows' BlockInput API fails with ACCESS_DENIED for a non-elevated process. If ok:false, the block did NOT engage; automation will still proceed unprotected unless you restart manctl elevated.
Parameters: none

### start_process
Starts a program that should keep running and waits until it is ready: apps, games, installers, the Android emulator. Example: start_process(executable="E:\sdk\emulator.exe", arguments="-avd Pixel6", waitForType="androidBoot", waitForTimeoutSeconds=240). For a console command whose output you need, use run_command instead. The program opens on the real visible desktop. If it is already running under the same label, it is returned as is (restartIfRunning=true relaunches it).
Parameters:
  - executable (string, required): Path or name of the program.
  - arguments (string): Arguments as ONE text line, e.g. "-avd Pixel6".
  - label (string): A short name for this program so you can stop it later with stop_process(label=...). Default: the program's file name.
  - workingDirectory (string): Working folder. Default: manctl's own folder.
  - waitForType (string): How to know it is ready: "none" (default, ready at once), "windowTitle", "port", "stdoutPattern" or "androidBoot" (Android finished booting).
  - waitForValue (string): Goes with waitForType: part of a window title, or a port / host:port, or a regex for the output, or the device serial for androidBoot (default emulator-5554).
  - waitForTimeoutSeconds (number): How long to wait for ready, in seconds.
  - lockFilePaths (list): Lock files this program owns; they are deleted when it is stopped or after a crash.
  - restartIfRunning (true/false): true = if it is already running under this label, stop it and start again.
  - runAsAdmin (true/false): true = start as Administrator. The user must approve a Windows UAC prompt themselves. Not usable with waitForType=stdoutPattern.

### unlock_input
Releases a lock_input block. Safe to call even if nothing is locked.
Parameters: none

### android_verify
Checks one property of an Android element and answers ok true/false: android_verify(property="toggled", expected="true", name="Remember me"). Properties: "name", "value", "enabled", "toggled" (checked). "offscreen" is not available on Android.
Parameters:
  - property (string, required): What to read: "name", "value", "enabled" or "toggled".
  - expected (string): Expected value as text. For toggled/enabled use "true" or "false".
  - name (string): Element text or description, case-insensitive part of it.
  - role (string): Element type, e.g. "CheckBox", "Switch", "Button".
  - automationId (string): Exact resource id of the element.
  - index (number): If several elements match, pick the Nth (0 = first).
  - deviceSerial (string): Device serial from android_list_devices, e.g. "emulator-5554". Leave out if only one device is connected.

### android_find
Is this text on the Android screen, and where? android_find(text="Login") answers found true/false, matchCount and each match's role and device point (x, y) (texts and content descriptions). Cheaper than android_ui_tree. Several matches: tap the one you want with android_click(x, y). If the screen exposes no elements (black/secure screen, game) you get ok:false, errorCode NoElements — take android_screenshot instead.
Parameters:
  - text (string, required): Text to look for, case-insensitive part of it.
  - deviceSerial (string): Device serial from android_list_devices, e.g. "emulator-5554". Leave out if only one device is connected.

### run_command_async
Starts a console command and does not wait: run_command_async(command="node server.js", workingDirectory="E:\\app"). Returns an id. Read the output later with get_command_output(id) and end it with stop_command(id). For a program with a window that you want to see, use start_process.
Parameters:
  - command (string): A whole command line, e.g. "node server.js". Runs through the shell (cmd by default). Use this OR executable.
  - executable (string): Only the program name or path. The rest goes in arguments. Use this OR command.
  - arguments (string): Arguments as ONE text line (with executable).
  - workingDirectory (string): Folder to run in. Default: manctl's own folder.
  - shell (string): With command: "cmd" (default) or "powershell".

### android_ui_tree
Shows what is on the Android screen as a tree of elements (same format as get_ui_tree): name, role, resource id, position in device pixels, enabled, supported actions. Much cheaper than android_screenshot. Call it before tapping.
Parameters:
  - interactiveOnly (true/false): true (default) = leave out unnamed, non-interactive layout wrappers to save space. false = everything.
  - deviceSerial (string): Device serial from android_list_devices, e.g. "emulator-5554". Leave out if only one device is connected.

### focus_window
Brings a window to the front (restores it if minimized): focus_window(windowHandle=1234) or focus_window(windowTitle="Notepad"). Windows often refuses to switch windows for a background program (it only flashes the taskbar button), so manctl tries up to seven ways in turn and checks each time. ok:true means the window really is in front now (method says how). If all ways fail you get ok:false with the reason and what to try next — follow the hint before telling the user it cannot be done.
Parameters:
  - windowHandle (number): Window handle (number from list_windows).
  - windowTitle (string): Part of the window title (alternative to windowHandle).

### scroll
Turns the mouse wheel. One notch = 120. UP is positive, DOWN is negative: 3 notches down = deltaY=-360. Give x and y to scroll over a specific point, e.g. scroll(deltaY=-360, x=500, y=400); otherwise it scrolls where the pointer already is.
Parameters:
  - deltaY (number): Vertical amount in multiples of 120. Positive = up, negative = down.
  - deltaX (number): Horizontal amount in multiples of 120. Positive = right.
  - x (number): Screen X to scroll at (optional).
  - y (number): Screen Y to scroll at (optional).
  - windowHandle (number): Window handle to bring to the front first (optional).

### android_screenshot
Takes a picture of the Android screen. Costs many tokens: prefer android_ui_tree when text is enough. android_screenshot() = whole screen; android_screenshot(x=0, y=1000, width=1080, height=300) = just that area (device pixels); scale=0.5 makes it half size (cheap), scale=2 enlarges tiny text.
Parameters:
  - x (number): Area: left edge in device pixels (use with y, width, height).
  - y (number): Area: top edge in device pixels.
  - width (number): Area: width in pixels.
  - height (number): Area: height in pixels.
  - scale (number): Size factor from 0.05 to 8. 1 = original, 0.5 = half (cheap), 2 = twice as big.
  - format (string): "png" (default) or "jpeg" (smaller).
  - quality (number): JPEG quality 1-100 (only for jpeg).
  - deviceSerial (string): Device serial from android_list_devices, e.g. "emulator-5554". Leave out if only one device is connected.

### find_element
Looks for elements by name, role or automationId and returns only the matches (with their state: enabled, value, position): find_element(name="Remember me", role="CheckBox", windowHandle=9). Cheaper than get_ui_tree when you want one thing. totalMatches tells you if there were more than maxResults. Nothing found is ok:false: ElementNotFound (the window is readable, the thing is not there) or NoElements (the window shows no elements at all).
Parameters:
  - name (string): Element name (the text you see on it), case-insensitive part of the name.
  - role (string): Element type, e.g. "Button", "Edit", "CheckBox", "ComboBox", "ListItem", "MenuItem", "Text".
  - automationId (string): Exact AutomationId, if known (more reliable than the name).
  - windowHandle (number): Window handle (number from list_windows). Omit both window fields to search all visible windows (slower).
  - windowTitle (string): Part of the window title (alternative to windowHandle).
  - maxResults (number): At most this many matches are returned.

### android_wait_for_element
Waits (on the server) until an element appears on the Android screen: android_wait_for_element(name="Finish", timeoutSeconds=60). Each check is a full screen dump, so do not poll faster than every 0.8 seconds.
Parameters:
  - name (string): Element text or description, case-insensitive part of it.
  - role (string): Element type, e.g. "Button", "EditText", "TextView".
  - automationId (string): Exact resource id of the element.
  - index (number): If several elements match, pick the Nth (0 = first).
  - timeoutSeconds (number): How long to wait, in seconds.
  - pollSeconds (number): How often to re-check, in seconds (minimum 0.3).
  - deviceSerial (string): Device serial from android_list_devices, e.g. "emulator-5554". Leave out if only one device is connected.

### android_input_text
Types text into the focused Android field. Only plain ASCII works (no Cyrillic, CJK or emoji) — that is an Android limit of 'input text'.
Parameters:
  - text (string, required): Text to type (ASCII only).
  - deviceSerial (string): Device serial from android_list_devices, e.g. "emulator-5554". Leave out if only one device is connected.

### get_env_var
Reads an environment variable. Answer: {ok:true, value:"..."}; value is null if it is not set. Use scope="user" to read the permanent per-user value.
Parameters:
  - name (string, required): Variable name.
  - scope (string): "process" (default) or "user".

### android_logcat
Reads the latest lines of the Android log: android_logcat(lines=100), only errors: level="E", only lines with a word: filter="MyApp". To see what one action causes: android_logcat(clear=true), do the action, then android_logcat() again.
Parameters:
  - lines (number): How many of the latest lines (1-5000).
  - filter (string): Only lines containing this text (case-insensitive), e.g. an app or tag name.
  - level (string): Lowest level to show: V, D, I, W, E or F (E = errors and worse).
  - clear (true/false): true = empty the log buffer instead of reading it.
  - deviceSerial (string): Device serial from android_list_devices, e.g. "emulator-5554". Leave out if only one device is connected.

### list_windows
Lists the visible windows: handle (number to reuse in other tools), title, class, process id, position and size, minimized/foreground. Start here when you do not know a window's handle.
Parameters: none

### set_env_var
Sets an environment variable for the programs started afterwards by run_command, run_command_async and start_process. scope="process" (default): only while manctl runs. scope="user": permanently for the Windows user, but it does not change programs already running. An empty value deletes the variable. Example: set_env_var(name="JAVA_HOME", value="C:\jdk").
Parameters:
  - name (string, required): Variable name.
  - value (string): Value to set. Empty or omitted deletes the variable.
  - scope (string): "process" (default) or "user".

### get_command_output
Gets the output of a command started with run_command_async. Pass back stdOutOffset/stdErrOffset from the previous answer to receive only what is new.
Parameters:
  - id (string, required): The id returned by run_command_async.
  - stdOutOffset (number): Only stdout after this offset (use the stdOutOffset of the previous answer; 0 = everything).
  - stdErrOffset (number): Only stderr after this offset (0 = everything).

### drag
Presses the mouse button at one screen point, moves to another and releases: drag(fromX=400, fromY=300, toX=520, toY=300). For sliders, moving things, selecting. To simply click a point use click(x, y), not drag.
Parameters:
  - fromX (number, required): Start X (screen).
  - fromY (number, required): Start Y (screen).
  - toX (number, required): End X (screen).
  - toY (number, required): End Y (screen).
  - button (string): "left" (default), "right" or "middle".

### wait_for_element
Waits (on the server) until an element appears — use this instead of calling get_ui_tree/find_element in a loop. Example: wait_for_element(name="Finish", windowHandle=12, timeoutSeconds=180).
Parameters:
  - name (string): Element name (the text you see on it), case-insensitive part of the name.
  - role (string): Element type, e.g. "Button", "Edit", "CheckBox", "ComboBox", "ListItem", "MenuItem", "Text".
  - automationId (string): Exact AutomationId of the element, if known.
  - windowHandle (number): Window handle (number from list_windows). Window: windowHandle (number from list_windows) or windowTitle (part of the title). Omit both to use the window in front.
  - windowTitle (string): Part of the window title (alternative to windowHandle).
  - index (number): If several elements match, pick the Nth (0 = first).
  - timeoutSeconds (number): How long to wait, in seconds.
  - pollSeconds (number): How often to re-check, in seconds.

### list_managed_processes
Lists the programs started with start_process and whether each is still running (label, pid, ready).
Parameters: none

### list_commands
Lists the commands started with run_command_async: id, whether still running, exit code.
Parameters: none

### stop_process
Stops a program started with start_process: stop_process(label="emulator"). Closes it politely first, then kills it (with its child processes) if it does not exit in time. Give the label from start_process, or a pid for any other program.
Parameters:
  - label (string): The label used in start_process (or its default, the program's file name without .exe).
  - pid (number): Process id, for programs not started by start_process.
  - graceful (true/false): true (default) = try to close politely before killing.
  - timeoutSeconds (number): How long to wait for the polite close before killing, in seconds.

### list_monitors
Lists physical monitors: bounds and work area in physical pixels (same space as get_ui_tree/click), per-monitor DPI, and which one is primary. Use this to reason about window placement across a multi-monitor setup.
Parameters: none

### get_clipboard
Reads the text in the clipboard. Answer: {ok:true, value:"..."}; value is null if the clipboard is empty or holds something that is not text.
Parameters: none

### stop_command
Ends a command started with run_command_async and returns the output it produced so far.
Parameters:
  - id (string, required): The id returned by run_command_async.
  - graceful (true/false): true (default) = try to close politely before killing.
  - timeoutSeconds (number): How long to wait for the polite close before killing, in seconds.

### wait_for_process_idle
Waits until a program exits, or stops using the CPU for idleThresholdSeconds in a row, or the timeout passes: wait_for_process_idle(processName="msbuild", idleThresholdSeconds=60, timeoutSeconds=1800). status "exited" = it finished; "idle" = still alive but doing nothing (finished its work or stuck — you decide); "timeout"; "not_found".
Parameters:
  - processId (number): Process id to watch (give this or processName).
  - processName (string): Program name, with or without .exe (give this or processId). If several match, the first is used.
  - idleThresholdSeconds (number): How many seconds of no CPU use count as idle.
  - timeoutSeconds (number): Longest wait, in seconds (max 1800).
  - pollSeconds (number): How often to check, in seconds.

### run_command
Runs a console command, waits until it finishes and returns its output: run_command(command="git status", workingDirectory="E:\\proj"). Or split it: executable="git", arguments="status". For long jobs (builds, analysis) raise timeoutSeconds (max 1800). If it does not finish in time it is killed (timedOut:true). Use run_command_async for something that runs a long time or forever.
Parameters:
  - command (string): A whole command line, e.g. "npm run build". Runs through the shell (cmd by default). Use this OR executable.
  - executable (string): Only the program name or path, e.g. "git". The rest goes in arguments. Use this OR command.
  - arguments (string): Arguments as ONE text line, e.g. "status" (with executable).
  - workingDirectory (string): Folder to run in. Default: manctl's own folder.
  - shell (string): With command: "cmd" (default) or "powershell".
  - timeoutSeconds (number): How long to wait before killing it, in seconds (max 1800).

### android_push
Copies a file or folder FROM this PC TO the Android device: android_push(localPath="C:\\a.apk", devicePath="/sdcard/a.apk"). Use /sdcard/ or /storage/emulated/0/ for plain files; to install an app use android_install_latest_apk.
Parameters:
  - localPath (string, required): Full path of the file or folder on this PC.
  - devicePath (string, required): Full path on the device, e.g. /sdcard/Download/a.apk.
  - deviceSerial (string): Device serial from android_list_devices, e.g. "emulator-5554". Leave out if only one device is connected.

### close_window
Closes a window politely (like the X button): close_window(windowTitle="Untitled - Notepad"). ok is true only if the window is really gone a moment later. If it is still there (a 'save changes?' question, or the program refuses) you get ok:false. This is not a kill: to end a program use stop_process.
Parameters:
  - windowHandle (number): Window handle (number from list_windows).
  - windowTitle (string): Part of the window title (alternative to windowHandle).

### android_key_event
Presses an Android key: android_key_event(keyCode="KEYCODE_BACK"). Others: KEYCODE_HOME, KEYCODE_ENTER, KEYCODE_DPAD_DOWN, KEYCODE_DPAD_CENTER, or a number. Note: this is adb 'input keyevent' — it does not reproduce real TV remote focus behavior exactly.
Parameters:
  - keyCode (string, required): Key name such as "KEYCODE_BACK", or its number.
  - deviceSerial (string): Device serial from android_list_devices, e.g. "emulator-5554". Leave out if only one device is connected.

### android_swipe
Swipes/drags a finger from one device point to another: android_swipe(x1=540, y1=1600, x2=540, y2=600) scrolls a list down. A longer durationSeconds is a slow drag, a short one a flick.
Parameters:
  - x1 (number, required): Start X (device pixels).
  - y1 (number, required): Start Y.
  - x2 (number, required): End X.
  - y2 (number, required): End Y.
  - durationSeconds (number): How long the swipe lasts, in seconds.
  - deviceSerial (string): Device serial from android_list_devices, e.g. "emulator-5554". Leave out if only one device is connected.

### android_click
Taps an element by name — android_click(name="Login") — OR a point on the device screen — android_click(x=540, y=1200). Use x,y only when the target has no name; x,y are device pixels (same as android_ui_tree). Give either an element or x and y, not both.
Parameters:
  - name (string): Element text or description, case-insensitive part of it.
  - role (string): Element type, e.g. "Button", "EditText", "TextView".
  - automationId (string): Exact resource id of the element.
  - x (number): Device X to tap (use together with y).
  - y (number): Device Y to tap.
  - index (number): If several elements match, pick the Nth (0 = first).
  - deviceSerial (string): Device serial from android_list_devices, e.g. "emulator-5554". Leave out if only one device is connected.

### read_text
Reads the text a window shows. The answer has one "text" string, a line per visual row, top to bottom: labels, buttons, field contents, menu and list items, document text (checkboxes as [x] / [ ]). The cheap way to 'see' a window: read_text(windowHandle=55). Repeated texts stay repeated (two OK buttons = two lines). Password fields are never read. To check for a word use contains: read_text(windowHandle=31, contains="Connected") answers found true/false, matchCount and each match's role and screen point (x, y). If the window shows no elements at all (game, canvas, ImGui) you get ok:false, errorCode NoElements: that does NOT mean the window is empty, take a screenshot.
Parameters:
  - windowHandle (number): Window handle (number from list_windows). Omit both window fields to read the window in front.
  - windowTitle (string): Part of the window title (alternative to windowHandle).
  - contains (string): Only look for this text (case-insensitive part of a text) and return the matches with their x, y instead of the whole text.
  - maxChars (number): Longest text to return, in characters.

### android_pull
Copies a file or folder FROM the Android device to this PC: android_pull(devicePath="/sdcard/Download/a.png", localPath="E:\\out\\a.png"). For a private file of your own debuggable app add app="com.example.app" (uses run-as), e.g. devicePath="files/configs/main.json".
Parameters:
  - devicePath (string, required): Full path on the device, e.g. /sdcard/Download/a.png (with app: path inside the app folder).
  - localPath (string, required): Where to save it on this PC (missing folders are created).
  - app (string): Package name of a debuggable app, to read its private files with run-as.
  - deviceSerial (string): Device serial from android_list_devices, e.g. "emulator-5554". Leave out if only one device is connected.
