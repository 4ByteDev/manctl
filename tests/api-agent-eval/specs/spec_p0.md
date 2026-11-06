# manctl API (tools you can call)

All tools return {ok: true/false, errorCode?, message?, hint?, ...data}. Coordinates are screen pixels.
Window is chosen with windowHandle (number from list_windows) OR windowTitle (part of the title).
Element is chosen with name, role, automationId, index.

## Windows
- list_windows() — list visible windows (handle, title, bounds).
- get_foreground_window() — the window in focus.
- focus_window(windowHandle | windowTitle) — bring a window to front.
- wait_for_window(windowTitle, mode="appears"|"closes", timeoutSeconds=60) — wait until a window appears or closes.
- set_window_state(state="minimized"|"maximized"|"normal", windowHandle | windowTitle)
- set_window_bounds(x?, y?, width?, height?, windowHandle | windowTitle) — move and/or resize a window.
- close_window(windowHandle | windowTitle) — close a window politely.
- list_monitors() — list monitors.

## Looking at a window
- get_ui_tree(windowHandle | windowTitle, maxDepth=10) — element tree of a window as JSON.
- find_element(name?, role?, automationId?, windowHandle?, maxResults=5) — find elements.
- read_text(windowHandle?) — all visible text of a window as one string.
- wait_for_element(name?, role?, automationId?, windowHandle | windowTitle, timeoutSeconds=10) — wait until an element appears.
- verify(property="name"|"value"|"enabled"|"toggled"|"offscreen", expected?, name?, role?, automationId?, windowHandle | windowTitle) — check a property of an element.
- screenshot(windowHandle?, x?, y?, width?, height?, scale=1, format="png") — picture of the screen, a window, or an area (x,y,width,height). scale<1 smaller, scale>1 bigger.

## Mouse and keyboard
- click(name? | role? | automationId? | x, y; button="left"|"right"|"middle", doubleClick=false, windowHandle | windowTitle, index=0) — click an element by name or a point by x,y.
- type_text(text, name?, role?, automationId?, windowHandle | windowTitle, index=0) — type text into an element.
- key_press(keys, windowHandle?) — press keys, e.g. "ctrl+s", "enter".
- mouse_move(x, y)
- scroll(deltaY=0, deltaX=0, x?, y?) — mouse wheel; one notch = 120; positive = up.
- drag(fromX, fromY, toX, toY, button="left")
- run_sequence(steps, stopOnError=true, lockInput=true) — run many steps in one call. steps = list of {action: "click"|"type_text"|"key_press"|"wait_for_element"|"verify"|"mouse_move"|"drag"|"scroll", ...same parameters as that tool}.
- handle_file_dialog(path, action="open"|"save", timeoutSeconds=15) — fill a Windows Open/Save dialog.
- lock_input() / unlock_input() — block / unblock the user's mouse and keyboard.
- get_clipboard() / set_clipboard(text)

## Programs and commands
- start_process(profileName, executable, arguments?, workingDirectory?, waitForType="none"|"windowTitle"|"port"|"stdoutPattern"|"androidBoot", waitForValue?, waitForTimeoutSeconds=15, runAsAdmin=false, restartIfRunning=false) — start a program and wait until ready.
- stop_process(profileName | pid) — stop a program.
- list_managed_processes()
- wait_for_process_idle(processId | processName, idleThresholdSeconds=30, timeoutSeconds=300) — wait until the process exits or stops using CPU.
- run_command(executable, arguments?, workingDirectory?, timeoutSeconds=30) — run a console program and wait for its output.
- run_command_async(executable, arguments?, workingDirectory?) — start a console program without waiting; returns id.
- get_command_output(id, stdOutOffset=0, stdErrOffset=0) / stop_command(id) / list_commands()
- wait_for_file(path, mode="exists"|"modified"|"contains_text"|"deleted", matchText?, timeoutSeconds=60)
- set_env_var(name, value, scope="process"|"user") / get_env_var(name, scope="process")

## Android (pixels are device pixels; deviceSerial is optional everywhere)
- android_list_devices()
- android_ui_tree(deviceSerial?) / android_find(text, deviceSerial?) / android_wait_for_element(name?, role?, automationId?, timeoutSeconds=10)
- android_verify(property, expected?, name?, role?, automationId?)
- android_click(name? | role? | automationId? | x, y; deviceSerial?) — tap an element or a point.
- android_swipe(x1, y1, x2, y2, durationSeconds=0.3)
- android_key_event(keyCode) — e.g. "KEYCODE_BACK"
- android_input_text(text)
- android_screenshot(deviceSerial?)
- android_install_latest_apk(directory, pattern)
- android_logcat(lines=200, filter?) / android_pull(devicePath, localPath) / android_push(localPath, devicePath)
