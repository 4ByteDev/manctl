# Recovery cases (old-style error answers)

In each case you made a tool call and got an answer. Say the NEXT tool call you would make (the single best one). Use only tools and parameters of the API.

R1. You called: key_press(key="Home")
Answer: An error occurred invoking 'key_press'.

R2. You called: start_process(path="notepad.exe")
Answer: An error occurred invoking 'start_process'.

R3. You called: focus_window(handle=787858)
Answer: An error occurred invoking 'focus_window'.

R4. You called: click(name="Save", windowHandle=198296)
Answer: {"ok":false,"errorCode":"WindowNotFound","message":"No matching window found."}

R5. You called: screenshot(windowHandle=1835874, x=0, y=380, width=993, height=150)
Answer: [InvalidArgument] The crop rectangle doesn't overlap the captured area.

R6. You called: click(name="Playbtn", windowHandle=12)
Answer: {"ok":false,"errorCode":"ElementNotFound","message":"No element matched the selector."}

R7. You called: type_text(text="hi", name="Search", windowHandle=5)
Answer: {"ok":false,"errorCode":"ElementNotFound","message":"No element matched the selector."}

R8. You called: wait_for_window(windowTitle="Setup", timeoutSeconds=60)
Answer: {"matched":false,"elapsedSeconds":60}

R9. You called: android_click(name="Login")
Answer: {"ok":false,"errorCode":"ElementNotFound","message":"No element matched the selector."}

R10. You called: run_command(executable="git status")
Answer: An error occurred invoking 'run_command'.

R11. You called: set_env_var(name="MY_VAR", value="1", scope="global")
Answer: An error occurred invoking 'set_env_var'.

R12. You called: wait_for_file(path="C:\\out\\a.txt", mode="created")
Answer: An error occurred invoking 'wait_for_file'.

R13. You called: start_process(executable="E:\\sdk\\emulator.exe", arguments="-avd Pixel6", waitForType="androidBoot")
Answer: {"ok":false,"errorCode":"Timeout","message":"Readiness check timed out."}
