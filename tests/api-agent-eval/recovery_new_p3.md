# Recovery cases (new-style error answers)

In each case you made a tool call and got an answer. Say the NEXT tool call you would make (the single best one). Use only tools and parameters of the API.

R1. You called: key_press(key="Home")
Answer: {"ok":false,"errorCode":"InvalidArgument","message":"Unknown parameter 'key'.","hint":"key_press parameters: keys (required, e.g. \"ctrl+s\"), windowHandle (optional). Did you mean 'keys'?"}

R2. You called: start_process(path="notepad.exe")
Answer: {"ok":false,"errorCode":"InvalidArgument","message":"Unknown parameter 'path'. Missing required parameter 'executable'.","hint":"start_process parameters: executable (required), arguments, label, workingDirectory, waitForType, waitForValue, waitForTimeoutSeconds, runAsAdmin, restartIfRunning. Did you mean 'executable'?"}

R3. You called: focus_window(handle=787858)
Answer: {"ok":false,"errorCode":"InvalidArgument","message":"Unknown parameter 'handle'.","hint":"focus_window parameters: windowHandle or windowTitle. Did you mean 'windowHandle'?"}

R4. You called: click(name="Save", windowHandle=198296)
Answer: {"ok":false,"errorCode":"WindowNotFound","message":"No window with handle 198296.","hint":"The window was closed or the handle is old. Call list_windows to get current handles, or pass windowTitle instead."}

R5. You called: screenshot(windowHandle=1835874, x=0, y=380, width=993, height=150)
Answer: {"ok":false,"errorCode":"InvalidArgument","message":"Area (0,380,993,150) is outside the window. The window covers screen area x=820..1813, y=40..559.","hint":"x,y are SCREEN coordinates, not window coordinates. To get the part that starts 380 pixels below the window top use x=820, y=420."}

R6. You called: click(name="Playbtn", windowHandle=12)
Answer: {"ok":false,"errorCode":"ElementNotFound","message":"No element named 'Playbtn' in window 12.","hint":"Closest names: 'Play', 'Playlist'. If the thing has no name (game, canvas), use click(x, y) with screen coordinates."}

R7. You called: type_text(text="hi", name="Search", windowHandle=5)
Answer: {"ok":false,"errorCode":"ElementNotFound","message":"No element named 'Search' in window 5.","hint":"Elements with role Edit in this window: 'Find', 'Replace'. Use find_element or get_ui_tree to see more."}

R8. You called: wait_for_window(windowTitle="Setup", timeoutSeconds=60)
Answer: {"ok":true,"matched":false,"elapsedSeconds":60,"hint":"Timed out: no window with 'Setup' in the title appeared. Call list_windows to see what is open, or wait longer with a bigger timeoutSeconds."}

R9. You called: android_click(name="Login")
Answer: {"ok":false,"errorCode":"ElementNotFound","message":"No element named 'Login' on the Android screen.","hint":"Use android_find(text) or android_ui_tree to see what is on the screen."}

R10. You called: run_command(executable="git status")
Answer: {"ok":false,"errorCode":"FileNotFound","message":"Program 'git status' was not found.","hint":"executable is only the program name. Put the rest in arguments: executable=\"git\", arguments=\"status\"."}

R11. You called: set_env_var(name="MY_VAR", value="1", scope="global")
Answer: {"ok":false,"errorCode":"InvalidArgument","message":"Unknown scope 'global'.","hint":"scope must be \"process\" or \"user\"."}

R12. You called: wait_for_file(path="C:\\out\\a.txt", mode="created")
Answer: {"ok":false,"errorCode":"InvalidArgument","message":"Unknown mode 'created'.","hint":"mode must be one of: exists, modified, contains_text, deleted."}

R13. You called: start_process(executable="E:\\sdk\\emulator.exe", arguments="-avd Pixel6", waitForType="androidBoot")
Answer: {"ok":false,"errorCode":"Timeout","message":"Android did not finish booting in 15 seconds. The emulator process is running.","hint":"Booting can take minutes. Call start_process again with the same arguments and waitForTimeoutSeconds=240; do not set restartIfRunning, so the running emulator is kept."}
