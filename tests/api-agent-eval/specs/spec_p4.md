# manctl API (tools you can call)

All tools return {ok: true/false, errorCode?, message?, hint?, ...data}. Coordinates are screen pixels.
Window is chosen with windowHandle (number from list_windows) OR windowTitle (part of the title).
Element is chosen with name, role (Button, Edit, CheckBox, ComboBox, ListItem, MenuItem, Text, Window), automationId, index.
Click/mouse x,y are ALWAYS screen coordinates, even when windowHandle is given.
Time is always in seconds. Text/pattern/path values are plain strings.

## Windows
- list_windows() — list visible windows (handle, title, bounds).
- get_foreground_window() — the window in focus.
- focus_window(windowHandle | windowTitle) — bring a window to front.
- wait_for_window(windowTitle, mode="appears"|"closes", timeoutSeconds=60) — wait until a window appears or closes.
- set_window_state(state="minimized"|"maximized"|"normal", windowHandle | windowTitle)
- set_window_bounds(x?, y?, width?, height?, windowHandle | windowTitle) — move and/or resize a window (also for putting it on one half of the screen).
- close_window(windowHandle | windowTitle) — close a window politely.
- list_monitors() — list monitors.

## Looking at a window
- get_ui_tree(windowHandle | windowTitle, maxDepth=10) — element tree of a window as JSON.
- find_element(name?, role?, automationId?, windowHandle?, maxResults=5) — find elements and see their state (enabled, toggled, value). To CHECK one property and get true/false use verify; to see what exists use find_element.
- read_text(windowHandle?) — all visible text of a window as one string.
- wait_for_element(name?, role?, automationId?, windowHandle | windowTitle, timeoutSeconds=10) — wait until an element appears.
- verify(property="name"|"value"|"enabled"|"toggled"|"offscreen", expected? (text; for toggled/enabled/offscreen use "true" or "false"), name?, role?, automationId?, windowHandle | windowTitle) — check a property of an element.
- screenshot(windowHandle?, x?, y?, width?, height?, scale=1, format="png") — picture of the screen, a window, or an area (x,y,width,height in screen coordinates). scale<1 smaller (cheap), scale>1 bigger (to read tiny text). Costs many tokens: prefer read_text/get_ui_tree when text is enough.

## Mouse and keyboard
- click(name? | role? | automationId? | x, y; button="left"|"right"|"middle", doubleClick=false, windowHandle | windowTitle, index=0) — click an element by name, OR a screen point by x,y (use x,y when the thing has no name: games, canvas).
- type_text(text, name?, role?, automationId?, windowHandle | windowTitle, index=0) — type text into an element.
- key_press(keys, windowHandle?) — press keys, e.g. "ctrl+s", "enter".
- mouse_move(x, y)
- scroll(deltaY=0, deltaX=0, x?, y?, windowHandle?) — mouse wheel at screen point x,y (default: where the mouse is). One notch = 120. Up = positive, DOWN = negative (3 notches down = -360). windowHandle (optional) = bring that window to the front first.
- drag(fromX, fromY, toX, toY, button="left")
- run_sequence(steps, windowHandle?, windowTitle?, stopOnError=true, lockInput=true) — run many steps in one call. steps = list of {action: "click"|"type_text"|"key_press"|"wait_for_element"|"verify"|"mouse_move"|"drag"|"scroll", ...same parameters as that tool}. windowHandle/windowTitle given once at the top apply to every step. Example: run_sequence(windowHandle=42, steps=[{action:"click", name:"Name"}, {action:"type_text", text:"Ivan"}, {action:"key_press", keys:"tab"}]). A type_text step without name types into the focused element.
- handle_file_dialog(path, action="open"|"save", timeoutSeconds=15) — fill a Windows Open/Save dialog.
- lock_input() / unlock_input() — block / unblock the user's mouse and keyboard.
- get_clipboard() / set_clipboard(text)

## Programs and commands
Which one? start_process = a program you want to keep running and wait until it is ready (app, game, emulator, installer). run_command = a console command whose output you need, finishes by itself. run_command_async = a console command that runs a long time or forever and whose output you read later.
- start_process( executable, arguments? (ONE string, e.g. "-avd Pixel6"), label? (a short name so you can stop it later; default = file name of the program), workingDirectory?, waitForType="none"|"windowTitle"|"port"|"stdoutPattern"|"androidBoot", waitForValue?, waitForTimeoutSeconds=15, runAsAdmin=false, restartIfRunning=false) — start a program and wait until ready.
- stop_process(label | pid) — label = the label used in start_process — stop a program.
- list_managed_processes()
- wait_for_process_idle(processId | processName, idleThresholdSeconds=30, timeoutSeconds=300) — wait until the process exits or stops using CPU.
- run_command(executable, arguments?, workingDirectory?, timeoutSeconds=30) — run a console program and wait for its output. executable is ONLY the program name ("git"); everything after it goes in arguments (ONE string, "status"). Long jobs: raise timeoutSeconds (max 1800).
- run_command_async(executable, arguments?, workingDirectory?) — start a console program without waiting; returns id.
- get_command_output(id, stdOutOffset=0, stdErrOffset=0) / stop_command(id) / list_commands()
- wait_for_file(path, mode="exists"|"modified"|"contains_text"|"deleted", matchText?, timeoutSeconds=60)
- set_env_var(name, value, scope="process"|"user") / get_env_var(name, scope="process")

## Android (pixels are device pixels; deviceSerial is optional everywhere)
- android_list_devices()
- android_ui_tree(deviceSerial?) / android_find(text, deviceSerial?) — is this text on the phone screen? / android_wait_for_element(name?, role?, automationId?, timeoutSeconds=10)
- android_verify(property, expected?, name?, role?, automationId?)
- android_click(name? | role? | automationId? | x, y; deviceSerial?) — tap an element or a point.
- android_swipe(x1, y1, x2, y2, durationSeconds=0.3)
- android_key_event(keyCode) — e.g. "KEYCODE_BACK"
- android_input_text(text)
- android_screenshot(deviceSerial?)
- android_install_latest_apk(directory, pattern="*.apk", deviceSerial?) — installs the newest matching file
- android_logcat(lines=200, filter?) / android_pull(devicePath, localPath) / android_push(localPath, devicePath)
