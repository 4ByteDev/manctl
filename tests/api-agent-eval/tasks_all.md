# Tasks

For each task say which tool call(s) you would make, with exact parameter names and values. Use the minimum number of calls.
Facts you can rely on are written in the task text.

T1. Notepad window has handle 1234. Click the button named "Save" in it.
T2. A game is running, it has no named elements. Click with the left mouse button at screen point x=640, y=360.
T3. Double-click at screen point x=300, y=200 on a canvas that has no named elements.
T4. Right-click the menu item named "File" in the window with handle 88.
T5. In the window titled "Chrome" type the text "hello world" into the field named "Search".
T6. Press Ctrl+S.
T7. A Windows Save dialog is open. Save the file as C:\out\report.txt.
T8. An installer will open a window titled "Setup Wizard" soon. Wait up to 2 minutes until it appears.
T9. You need to read tiny text. Take a picture of only the area with top-left corner x=100, y=200, size 300 wide and 40 high, and make it 2 times bigger so it is easy to read.
T10. Take a cheap picture of the whole screen at half size.
T11. Run the command `git status` in the folder E:\proj and get its output.
T12. Start the Android emulator: program E:\sdk\emulator.exe with arguments `-avd Pixel6`, and wait until Android has booted.
T13. On the Android phone, tap the button named "Login".
T14. On the Android phone, tap the screen point x=540, y=1200.
T15. Maximize the window with handle 777.
T16. Move the window titled "Calculator" to the top-left corner (0,0) and make it 800 wide and 600 high.
T17. Get all visible text of the window with handle 55 as one string.
T18. Wait up to 10 minutes until the file C:\log.txt contains the text "DONE".
T19. In the window with handle 42, in ONE call: click the field "Name", type "Ivan", press Tab, click the button "OK".
T20. Set the environment variable JAVA_HOME to C:\jdk (only for the current manctl run).
T21. Show the last 100 lines of the Android log.
T22. Copy the file C:\a.apk from this PC to the Android device at /sdcard/a.apk.
T23. Scroll down by 3 notches while the mouse is over the point x=500, y=400.
T24. Which windows are open right now?
T25. Check that the checkbox named "Remember me" in the window with handle 9 is checked.
T26. A program was started earlier with the name "emu". Stop it.
T27. Close the window titled "Untitled - Notepad" politely.
T28. A build program "msbuild" is running. Wait until it exits or does nothing for 60 seconds, but not longer than 30 minutes.

# Tasks (second part)

For each task say which tool call(s) you would make, in order, with exact parameter names and values. Use the minimum number of calls.
Facts you can rely on are written in the task text. If the API has no tool for something, you may NOT invent one: use the closest real tool.

H1. In a game the button Play is drawn on a canvas and has no name. On a screenshot it is at point (812, 455). Press it.
H2. In an ImGui app, drag a slider with the mouse from point (400, 300) to point (520, 300).
H3. Check that somewhere in the window with handle 31 there is the text "Connected". Be cheap in tokens, no pictures.
H4. Close Notepad. Its window is titled "Untitled - Notepad". You do not know its handle.
H5. Wait for the file C:\out\done.flag to appear, at most one and a half minutes.
H6. The window with handle 7 is placed so that its top-left corner is at screen point (200, 100). Click the point inside this window that is 50 pixels right and 60 pixels down from the window's top-left corner.
H7. Run `node server.js` in folder E:\app. It is a server that runs forever; it must stay running while you do other things and look at its output later.
H8. Run `npm run build` in folder E:\app and wait until it finishes (it can take up to 10 minutes).
H9. In the window with handle 12 click the button "Install", then wait until a button "Finish" appears (up to 3 minutes).
H10. Open the Windows "Run" dialog (Win+R).
H11. Put the window with handle 5 on the left half of a 1920x1080 screen.
H12. Look at how the window with handle 55 looks, but save tokens.
H13. Set the environment variable PATH_EXTRA to C:\tools permanently for the current Windows user.
H14. Take a screenshot of the screen of the phone whose serial is R58M1234.
H15. Install the newest built apk from the folder E:\build\apk on the phone.
H16. Press the Back button on the second Android device, its serial is emulator-5556.
H17. Is the text "Подтвердить" on the screen of the Android phone? You do not need a picture.
H18. Read the text that is currently in the clipboard.
H19. In the window with handle 20 scroll down by 5 notches of the mouse wheel; the center of that window is at screen point (960, 540).
H20. You are about to do a series of clicks and the user must not disturb you with their mouse. Turn on protection against that.
H21. Press the Enter key in the window with handle 33.
H22. In the window with handle 14 tick the checkbox "Согласен" and make sure it is now ticked.
H23. Wait until the process setup.exe finishes, at most 20 minutes.
H24. Show me where all monitors are on the desktop.
H25. Zoom into the screen area of size 50x20 at point (10, 10) so that you can read tiny font.
H26. Click the element with automation id "btnOk" in the window titled "Settings".
H27. In the window with handle 3 type the Russian text "Привет" into the first text field (it has no name and its role is Edit).
H28. Remember what program is in front of the user right now.
