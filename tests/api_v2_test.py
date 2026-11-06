"""manctl v2 API test, over stdio (raw MCP JSON-RPC) against a built Manctl.Server.exe.

Part A  every wrong call that agents really made (from old transcripts) must now get a readable error + hint
Part B  harmless type slips are forgiven
Part C  one common answer shape {ok, errorCode?, message?, hint?} for action tools
Part D  a live run on a small WinForms test window of our own (edits, clicks, resizes, screenshots, closes)
Part E  empty results must never look like success: windows with no elements, off-screen points, refusals, early deaths
Part F  read_text (order, duplicates, passwords, NoElements) and the Android helpers (without a device)

usage: python -I api_v2_test.py <path to Manctl.Server.exe>   (add -v to print every answer)
"""
import base64, json, queue, struct, subprocess, sys, threading, time

exe = sys.argv[1]
VERBOSE = "-v" in sys.argv
p = subprocess.Popen([exe], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
q = queue.Queue()
threading.Thread(target=lambda: [q.put(json.loads(l)) for l in p.stdout if l.strip()], daemon=True).start()
_id = 0

def rpc(method, params=None, notify=False):
    global _id
    msg = {"jsonrpc": "2.0", "method": method}
    if params is not None: msg["params"] = params
    if not notify:
        _id += 1; msg["id"] = _id
    p.stdin.write((json.dumps(msg) + "\n").encode()); p.stdin.flush()
    if notify: return None
    while True:
        r = q.get(timeout=120)
        if r.get("id") == _id: return r

def call(tool, args=None):
    r = rpc("tools/call", {"name": tool, "arguments": args or {}})
    if "error" in r: return {"_rpc_error": r["error"], "text": json.dumps(r["error"])}
    res = r["result"]; c = res.get("content", [])
    out = {"isError": res.get("isError"), "types": [x.get("type") for x in c]}
    if c and c[0].get("type") == "text":
        out["text"] = c[0]["text"]
        try: out["json"] = json.loads(c[0]["text"])
        except Exception: pass
    elif c and c[0].get("type") == "image":
        out["mime"] = c[0].get("mimeType"); out["b64"] = c[0].get("data")
    return out

def png_size(b64):
    raw = base64.b64decode(b64[:200])
    return struct.unpack(">II", raw[16:24]) if raw[:8] == b"\x89PNG\r\n\x1a\n" else None

fails = 0
def check(name, cond, detail=""):
    global fails
    fails += (not cond)
    print(("PASS " if cond else "FAIL ") + name + ("" if cond else "   <- " + str(detail)[:400]))

init = rpc("initialize", {"protocolVersion": "2024-11-05", "capabilities": {}, "clientInfo": {"name": "t", "version": "1"}})
rpc("notifications/initialized", notify=True)
instructions = init["result"].get("instructions") or ""
check("server hands the rules to every agent (instructions)", "Look, act, check" in instructions and "SECONDS" in instructions, instructions[:80])
check("rules are short enough to always read", len(instructions) < 3000, len(instructions))
check("the rules tell the agent to introduce the tool and its author", "4ByteDev" in instructions and "Before anything else" in instructions, instructions[:200])
tools = {t["name"]: t for t in rpc("tools/list", {})["result"]["tools"]}
check("52 tools registered", len(tools) == 52, len(tools))
for gone in ("android_tap", "move_window", "resize_window", "minimize_window", "maximize_window", "restore_window"):
    check(f"{gone} is gone", gone not in tools)
for here in ("set_window_state", "set_window_bounds", "read_text"):
    check(f"{here} exists", here in tools)

def bad(tool, args, needles, name=None, code="InvalidArgument"):
    r = call(tool, args); o = r.get("json") or {}
    blob = (o.get("message", "") + " " + o.get("hint", ""))
    ok = (o.get("ok") is False and o.get("errorCode") == code and o.get("hint")
          and all(n in blob for n in needles) and "An error occurred invoking" not in (r.get("text") or ""))
    check(name or f"{tool}{json.dumps(args)[:60]}", ok, r.get("text"))
    if VERBOSE: print("      ", (r.get("text") or "")[:500])

print("\n== A. wrong calls get a readable answer")
bad("key_press", {"key": "Home"}, ["'key'", "keys"])
bad("start_process", {"path": "notepad.exe"}, ["'path'", "executable"])
bad("start_process", {"executable": "notepad.exe", "profileName": "x"}, ["'profileName'", "label"])
bad("focus_window", {"handle": "787858"}, ["'handle'", "windowHandle"])
bad("mouse_move", {"windowHandle": "1901766", "coordinate": "[825, 747]"}, ["'coordinate'", "x and y"])
bad("click", {"text": "Save"}, ["'text'", "name"])
bad("type_text", {"text": "x", "elementText": "Edit"}, ["'elementText'", "name"])
bad("wait_for_element", {"name": "x", "timeoutMs": 5000}, ["'timeoutMs'", "timeoutSeconds", "SECONDS"])
bad("screenshot", {"scope": "window", "windowHandle": 1}, ["'scope'", "windowHandle"])
bad("screenshot", {"cropX": 0, "cropY": 0, "cropWidth": 5, "cropHeight": 5}, ["'cropX'", "'x'"])
bad("verify", {}, ["Missing required parameter 'property'"])
bad("get_clipboard", {"x": 1}, ["Unknown parameter 'x'", "takes no parameters"])
bad("run_sequence", {"steps": [{"action": "click", "elementText": "OK"}]}, ["steps[0]", "'elementText'", "name"])
bad("run_sequence", {"steps": [{"name": "OK"}]}, ["steps[0]", "Missing required parameter 'action'"])
bad("click", {}, ["Say what to click"])
bad("click", {"name": "A", "x": 1, "y": 2}, ["not both"])
bad("click", {"x": 5}, ["both x and y"])
bad("screenshot", {"x": 0, "y": 0}, ["all four"])
bad("screenshot", {"scale": 100}, ["scale must be"])
bad("run_command", {}, ["Nothing to run"])
bad("run_command", {"executable": "git status"}, ["whole command line", "command=\"git status\""])
bad("run_command", {"command": "echo hi", "executable": "cmd"}, ["not both"])
bad("set_window_state", {"state": "huge", "windowHandle": 1}, ["Unknown state"])
bad("set_window_bounds", {"windowHandle": 1}, ["Nothing to change"])
bad("focus_window", {}, ["Say which window"])
bad("focus_window", {"windowHandle": 99999999}, ["No matching window"], code="WindowNotFound")
bad("screenshot", {"windowHandle": 99999999}, ["No matching window"], code="WindowNotFound")
bad("screenshot", {"x": 90000, "y": 0, "width": 10, "height": 10}, ["outside the picture", "SCREEN coordinates"])
bad("wait_for_file", {"path": "C:\\nope\\a.txt", "mode": "created", "timeoutSeconds": 1}, ["Unknown mode"])
bad("set_env_var", {"name": "MANCTL_T", "value": "1", "scope": "global"}, ["Unknown scope", "process"])
bad("start_process", {"executable": "nonexistent_zzz.exe", "label": "zz"}, ["nonexistent_zzz"], code="FileNotFound")

print("\n== B. harmless slips are forgiven")
r = call("focus_window", {"windowHandle": "99999999"})   # numeric text for a number
check("windowHandle as text is accepted", (r.get("json") or {}).get("errorCode") == "WindowNotFound", r.get("text"))
r = call("get_env_var", {"name": 5})                      # number for text
check("number for a text parameter is accepted", (r.get("json") or {}).get("ok") is True, r.get("text"))
r = call("run_sequence", {"steps": [{"action": "mouse_move", "x": "10", "y": "10"}], "lockInput": "false"})
check("text numbers/booleans inside steps are accepted", (r.get("json") or {}).get("ok") is True, r.get("text"))

print("\n== C. common answer shape")
r = call("get_clipboard"); o = r.get("json") or {}
check("get_clipboard -> {ok, value}", o.get("ok") is True and "value" in o or o.get("ok") is True, r.get("text"))
r = call("set_env_var", {"name": "MANCTL_V2_T", "value": "abc"}); check("set_env_var ok", (r.get("json") or {}).get("ok") is True, r.get("text"))
r = call("get_env_var", {"name": "MANCTL_V2_T"}); check("get_env_var value", (r.get("json") or {}).get("value") == "abc", r.get("text"))
r = call("mouse_move", {"x": 5, "y": 5}); check("mouse_move -> ok:true", (r.get("json") or {}).get("ok") is True, r.get("text"))
r = call("scroll", {"deltaY": -120, "x": 5, "y": 5}); check("scroll at point -> ok:true", (r.get("json") or {}).get("ok") is True, r.get("text"))
r = call("scroll", {"x": 5}); check("scroll with only x -> InvalidArgument", (r.get("json") or {}).get("errorCode") == "InvalidArgument", r.get("text"))
r = call("run_command", {"command": "echo hi there"}); o = r.get("json") or {}
check("run_command(command) -> ok + output", o.get("ok") is True and "hi there" in o.get("stdOut", ""), r.get("text"))
r = call("run_command", {"command": "echo \"quoted  text\" & echo second"}); o = r.get("json") or {}
check("run_command(command) keeps quotes", o.get("ok") is True and "quoted  text" in o.get("stdOut", "") and "second" in o.get("stdOut", ""), r.get("text"))
r = call("run_command", {"command": "Write-Output ('a'+'b')", "shell": "powershell"}); o = r.get("json") or {}
check("run_command(powershell)", o.get("ok") is True and "ab" in o.get("stdOut", ""), r.get("text"))
r = call("run_command", {"executable": "cmd", "arguments": "/c echo split"}); o = r.get("json") or {}
check("run_command(executable+arguments)", o.get("ok") is True and "split" in o.get("stdOut", ""), r.get("text"))
r = call("run_command", {"command": "exit 3"}); o = r.get("json") or {}
check("non-zero exit -> ok:false + exitCode + hint", o.get("ok") is False and o.get("exitCode") == 3 and "exited with code 3" in o.get("hint", ""), r.get("text"))
r = call("run_command", {"command": "ping -n 4 127.0.0.1 > nul", "timeoutSeconds": 1}); o = r.get("json") or {}
check("timeoutSeconds is seconds (timedOut)", o.get("timedOut") is True and o.get("errorCode") == "Timeout", r.get("text"))
r = call("start_process", {"executable": "cmd.exe", "arguments": "/c ping -n 30 127.0.0.1 > nul", "label": "pingtest"}); o = r.get("json") or {}
check("start_process(label)", o.get("label") == "pingtest" and o.get("pid", 0) > 0, r.get("text"))
r = call("start_process", {"executable": "cmd.exe", "arguments": "/c ping -n 30 127.0.0.1 > nul", "label": "pingtest"}); o = r.get("json") or {}
check("start_process again -> already running, not an error", o.get("ok") is True and "already running" in (o.get("readySignal") or ""), r.get("text"))
r = call("list_managed_processes"); check("list shows label", "pingtest" in (r.get("text") or ""), r.get("text"))
r = call("stop_process", {"label": "pingtest", "graceful": False}); check("stop_process(label)", (r.get("json") or {}).get("ok") is True, r.get("text"))
r = call("start_process", {"executable": "C:\\Windows\\System32\\cmd.exe", "arguments": "/c exit", "waitForTimeoutSeconds": 2}); o = r.get("json") or {}
check("start_process default label = file name", o.get("label") == "cmd", r.get("text"))
call("stop_process", {"label": "cmd"})
r = call("wait_for_window", {"windowTitle": "no-such-window-zzz", "timeoutSeconds": 0.5, "pollSeconds": 0.2}); o = r.get("json") or {}
check("wait_for_window seconds/pollSeconds -> ok:false Timeout, matched:false", o.get("matched") is False and o.get("ok") is False and o.get("errorCode") == "Timeout" and o.get("hint"), r.get("text"))
r = call("wait_for_file", {"path": "C:\\nope\\x", "mode": "exists", "timeoutSeconds": 0.5, "pollSeconds": 0.2}); o = r.get("json") or {}
check("wait_for_file timeout -> ok:false Timeout, matched:false", o.get("matched") is False and o.get("ok") is False and o.get("errorCode") == "Timeout", r.get("text"))
r = call("list_monitors"); check("list_monitors still a list", (r.get("text") or "").startswith("["), r.get("text"))
r = call("list_windows"); wins = r.get("json") or []
check("list_windows still a list", isinstance(wins, list) and len(wins) > 0, r.get("text"))
r = call("get_foreground_window"); check("get_foreground_window has handle", "handle" in (r.get("json") or {}), r.get("text"))
r = call("android_list_devices"); check("android_list_devices answers", r.get("isError") is not True, r.get("text"))

print("\n== D. live: a small WinForms test window (our own process, killed at the end)")
import os, tempfile
script = os.path.join(tempfile.gettempdir(), "manctl_test_form.ps1")
open(script, "w", encoding="utf-8").write("""
Add-Type -AssemblyName System.Windows.Forms
$f = New-Object Windows.Forms.Form; $f.Text='ManctlTestForm'; $f.Width=520; $f.Height=360
$f.StartPosition='Manual'; $f.Location = New-Object Drawing.Point(300,200)
$tb = New-Object Windows.Forms.TextBox; $tb.Left=20; $tb.Top=20; $tb.Width=300
$cb = New-Object Windows.Forms.CheckBox; $cb.Text='Remember me'; $cb.Left=20; $cb.Top=60; $cb.Width=200
$btn = New-Object Windows.Forms.Button; $btn.Text='OK'; $btn.Left=20; $btn.Top=100
$lbl = New-Object Windows.Forms.Label; $lbl.Text='waiting'; $lbl.Left=20; $lbl.Top=150; $lbl.Width=300
$btn.Add_Click({ $lbl.Text = 'clicked:' + $tb.Text })
$f.Controls.AddRange(@($tb,$cb,$btn,$lbl)); [void]$f.ShowDialog()
""")
r = call("start_process", {"executable": "powershell.exe", "arguments": f'-NoProfile -ExecutionPolicy Bypass -File "{script}"', "label": "form"})
check("test form process started", (r.get("json") or {}).get("pid", 0) > 0, r.get("text"))
r = call("wait_for_window", {"windowTitle": "ManctlTestForm", "timeoutSeconds": 20}); o = r.get("json") or {}
check("wait_for_window finds it and gives the handle", o.get("matched") is True and o.get("ok") is True and o.get("windowHandle"), r.get("text"))
h = o.get("windowHandle")
if h:
    r = call("type_text", {"text": "Ivan", "role": "Edit", "windowHandle": h}); check("type_text(role=Edit)", (r.get("json") or {}).get("ok") is True, r.get("text"))
    r = call("verify", {"property": "value", "expected": "Ivan", "role": "Edit", "windowHandle": h}); check("verify value", (r.get("json") or {}).get("ok") is True, r.get("text"))
    r = call("verify", {"property": "toggled", "expected": "false", "name": "Remember me", "windowHandle": h}); check("checkbox starts unchecked (toggled=false)", (r.get("json") or {}).get("ok") is True, r.get("text"))
    r = call("click", {"name": "Remember me", "role": "CheckBox", "windowHandle": h}); check("click(name) toggles the checkbox", (r.get("json") or {}).get("ok") is True, r.get("text"))
    r = call("verify", {"property": "toggled", "expected": "true", "name": "Remember me", "windowHandle": h}); check("toggled=true after click", (r.get("json") or {}).get("ok") is True, r.get("text"))
    r = call("verify", {"property": "toggled", "expected": "checked", "name": "Remember me", "windowHandle": h}); check("expected=checked also means true", (r.get("json") or {}).get("ok") is True, r.get("text"))
    r = call("click", {"name": "OK", "role": "Button", "windowHandle": h}); check("click(name=OK)", (r.get("json") or {}).get("ok") is True, r.get("text"))
    r = call("wait_for_element", {"name": "clicked:Ivan", "windowHandle": h, "timeoutSeconds": 5}); check("wait_for_element sees the result", (r.get("json") or {}).get("ok") is True, r.get("text"))
    # click at a point: the centre of the OK button, found with find_element
    r = call("type_text", {"text": "Anna", "role": "Edit", "windowHandle": h})
    fe = (call("find_element", {"name": "OK", "role": "Button", "windowHandle": h}).get("json") or {})
    box = (fe.get("matches") or [{}])[0].get("boundingBox") or {}
    check("find_element gives a position", bool(box), fe)
    if box:
        r = call("click", {"x": box["x"] + box["width"] // 2, "y": box["y"] + box["height"] // 2, "windowHandle": h})
        check("click(x, y) on the OK button", (r.get("json") or {}).get("ok") is True, r.get("text"))
        r = call("wait_for_element", {"name": "clicked:Anna", "windowHandle": h, "timeoutSeconds": 5}); check("point click really pressed the button", (r.get("json") or {}).get("ok") is True, r.get("text"))
    r = call("type_text", {"text": " Smith", "windowHandle": h}); check("type_text with no element -> focused field", (r.get("json") or {}).get("ok") is True, r.get("text"))
    r = call("find_element", {"role": "Edit", "windowTitle": "ManctlTestForm"}); check("find_element by windowTitle", (r.get("json") or {}).get("totalMatches", 0) >= 1, r.get("text"))
    r = call("run_sequence", {"windowHandle": h, "lockInput": False, "steps": [
        {"action": "click", "name": "Remember me"}, {"action": "verify", "property": "toggled", "expected": "false", "name": "Remember me"},
        {"action": "type_text", "text": "Zed", "role": "Edit"}, {"action": "click", "name": "OK"},
        {"action": "wait_for_element", "name": "clicked:Zed", "timeoutSeconds": 5}, {"action": "scroll", "deltaY": -120, "x": 400, "y": 300}]})
    check("run_sequence: top-level window + name + seconds", (r.get("json") or {}).get("ok") is True, r.get("text"))
    r = call("run_sequence", {"windowHandle": h, "lockInput": False, "steps": [{"action": "click"}]})
    check("run_sequence step without target -> ok:false and a message", (r.get("json") or {}).get("ok") is False and "needs an element" in (r.get("text") or ""), r.get("text"))
    r = call("set_window_state", {"state": "maximized", "windowHandle": h}); check("set_window_state maximized (and it really is)", (r.get("json") or {}).get("ok") is True and (r.get("json") or {}).get("state") == "maximized", r.get("text"))
    time.sleep(0.5)
    r = call("set_window_bounds", {"x": 100, "y": 100, "width": 700, "height": 500, "windowHandle": h}); check("set_window_bounds", (r.get("json") or {}).get("ok") is True, r.get("text"))
    time.sleep(0.5)
    w2 = next((w for w in call("list_windows").get("json") or [] if w["handle"] == h), {})
    b = w2.get("bounds", {})
    check("window is where we put it", (b.get("x"), b.get("y"), b.get("width"), b.get("height")) == (100, 100, 700, 500), b)
    full = call("screenshot", {"windowHandle": h}); half = call("screenshot", {"windowHandle": h, "scale": 0.5})
    fs, hs = png_size(full.get("b64", "")), png_size(half.get("b64", ""))
    check("window screenshot size", fs == (700, 500), fs)
    check("scale=0.5 halves the picture", hs == (350, 250), hs)
    area = call("screenshot", {"x": 110, "y": 110, "width": 100, "height": 40, "scale": 2}); a = png_size(area.get("b64", ""))
    check("area + scale=2 -> 200x80", a == (200, 80), a)
    jp = call("screenshot", {"windowHandle": h, "format": "jpeg", "quality": 50})
    check("jpeg format", jp.get("mime") == "image/jpeg" and 0 < len(jp.get("b64", "")), (jp.get("mime"), len(jp.get("b64", ""))))
    r = call("key_press", {"keys": "ctrl+a", "windowHandle": h}); check("key_press ctrl+a", (r.get("json") or {}).get("ok") is True, r.get("text"))
    r = call("focus_window", {"windowTitle": "ManctlTestForm"}); check("focus_window by title", (r.get("json") or {}).get("ok") is True, r.get("text"))
    r = call("close_window", {"windowTitle": "ManctlTestForm"}); check("close_window by title (waits until it is really gone)", (r.get("json") or {}).get("ok") is True and (r.get("json") or {}).get("state") == "closed", r.get("text"))
    time.sleep(1)
    r = call("wait_for_window", {"windowTitle": "ManctlTestForm", "mode": "closes", "timeoutSeconds": 5})
    check("window really closed", (r.get("json") or {}).get("matched") is True or "ManctlTestForm" not in json.dumps(call("list_windows").get("json")), r.get("text"))
call("stop_process", {"label": "form", "graceful": False})
try: os.remove(script)
except OSError: pass

print("\n== E. an empty result must never look like success")
script2 = os.path.join(tempfile.gettempdir(), "manctl_test_form2.ps1")
open(script2, "w", encoding="utf-8").write("""
param([string]$Title='ManctlEmptyForm',[switch]$Black,[switch]$Stubborn)
Add-Type -AssemblyName System.Windows.Forms
$f = New-Object Windows.Forms.Form; $f.Text=$Title; $f.Width=400; $f.Height=300
$f.StartPosition='Manual'; $f.Location = New-Object Drawing.Point(700,300)
if ($Black) { $f.BackColor = [Drawing.Color]::Black }
if ($Stubborn) { $f.Add_FormClosing({ $_.Cancel = $true }) }
[void]$f.ShowDialog()
""")
def start_form(label, title, *flags):
    call("start_process", {"executable": "powershell.exe", "arguments": f'-NoProfile -ExecutionPolicy Bypass -File "{script2}" -Title {title} ' + " ".join(flags), "label": label})
    return (call("wait_for_window", {"windowTitle": title, "timeoutSeconds": 20}).get("json") or {}).get("windowHandle")

def j(r): return r.get("json") or {}

eh = start_form("emptyform", "ManctlEmptyForm", "-Black")
check("empty (black) test window is up", bool(eh))
if eh:
    o = j(call("get_ui_tree", {"windowHandle": eh}))
    check("get_ui_tree on a window with no elements -> ok:false NoElements (not an empty tree)", o.get("ok") is False and o.get("errorCode") == "NoElements" and "screenshot" in o.get("hint", "") and (o.get("window") or {}).get("title") == "ManctlEmptyForm", o)
    o = j(call("find_element", {"name": "OK", "windowHandle": eh}))
    check("find_element in such a window -> NoElements", o.get("ok") is False and o.get("errorCode") == "NoElements", o)
    o = j(call("click", {"name": "OK", "windowHandle": eh}))
    check("click(name) in such a window -> NoElements", o.get("ok") is False and o.get("errorCode") == "NoElements", o)
    o = j(call("verify", {"property": "name", "name": "x", "windowHandle": eh}))
    check("verify in such a window -> NoElements", o.get("ok") is False and o.get("errorCode") == "NoElements", o)
    o = j(call("wait_for_element", {"name": "x", "windowHandle": eh, "timeoutSeconds": 0.5}))
    check("wait_for_element in such a window -> NoElements, not a plain Timeout", o.get("ok") is False and o.get("errorCode") == "NoElements", o)
    wb = next((w["bounds"] for w in call("list_windows").get("json") or [] if w["handle"] == eh), {"x": 700, "y": 300})
    r = call("screenshot", {"windowHandle": eh, "x": wb["x"] + 30, "y": wb["y"] + 80, "width": 100, "height": 100})
    check("screenshot of a one-color area carries a note", r.get("types", [None])[0] == "image" and "text" in r.get("types", []), r.get("types"))
    r = call("key_press", {"keys": "shift", "windowHandle": eh}); o = j(r)
    check("key_press says which window received the keys", o.get("ok") is True and o.get("resolvedName") == "ManctlEmptyForm", r.get("text"))
    r = call("type_text", {"text": "", "windowHandle": eh}); o = j(r)
    check("type_text into focus says which window got it", o.get("ok") is True and o.get("resolvedName") == "ManctlEmptyForm", r.get("text"))
    o = j(call("set_window_state", {"state": "minimized", "windowHandle": eh}))
    check("set_window_state minimized -> ok + state", o.get("ok") is True and o.get("state") == "minimized", o)
    o = j(call("screenshot", {"windowHandle": eh}))
    check("screenshot of a minimized window -> clear error", o.get("ok") is False and "minimized" in o.get("message", "") and "set_window_state" in o.get("hint", ""), o)
    o = j(call("set_window_state", {"state": "normal", "windowHandle": eh}))
    check("set_window_state normal -> ok + state + bounds", o.get("ok") is True and o.get("state") == "normal" and o.get("bounds"), o)
    o = j(call("set_window_bounds", {"width": 10, "height": 10, "windowHandle": eh}))
    check("tiny size the window refuses -> ok:false and the REAL bounds", o.get("ok") is False and "did not end up" in o.get("message", "") and o.get("bounds", {}).get("width", 0) > 10, o)
    o = j(call("click", {"x": 5, "y": 5, "windowHandle": eh}))
    check("click(x,y) outside the named window -> explains screen coordinates", o.get("ok") is False and o.get("errorCode") == "InvalidArgument" and "SCREEN coordinates" in o.get("message", ""), o)
    o = j(call("close_window", {"windowHandle": eh}))
    check("close_window -> ok only when really gone", o.get("ok") is True and o.get("state") == "closed", o)
call("stop_process", {"label": "emptyform", "graceful": False})

sh = start_form("stubborn", "ManctlStubbornForm", "-Stubborn")
check("stubborn test window is up", bool(sh))
if sh:
    o = j(call("close_window", {"windowHandle": sh}))
    check("window that refuses to close -> ok:false Timeout 'still open'", o.get("ok") is False and o.get("errorCode") == "Timeout" and "still open" in o.get("message", ""), o)
call("stop_process", {"label": "stubborn", "graceful": False})

print("\n   coordinates must be on a real screen")
for tool, args in [("click", {"x": 99999, "y": 5}), ("mouse_move", {"x": -99999, "y": 5}), ("drag", {"fromX": 5, "fromY": 5, "toX": 99999, "toY": 5}), ("scroll", {"deltaY": -120, "x": 99999, "y": 5})]:
    o = j(call(tool, args))
    check(f"{tool} off-screen -> InvalidArgument", o.get("ok") is False and o.get("errorCode") == "InvalidArgument" and "outside the screen" in o.get("message", ""), o)
o = j(call("run_sequence", {"steps": [{"action": "mouse_move", "x": 99999, "y": 5}], "lockInput": False}))
check("run_sequence mouse_move off-screen -> ok:false", o.get("ok") is False, o)

print("\n   waits and starts that were too eager to say ok")
o = j(call("wait_for_window", {"windowTitle": "no-such-window-zzz", "mode": "closes", "timeoutSeconds": 1}))
check("wait for a window to close that never existed -> ok:true but with a hint", o.get("ok") is True and "nothing to wait for" in o.get("hint", ""), o)
o = j(call("wait_for_file", {"path": "C:\\nope\\never.txt", "mode": "deleted", "timeoutSeconds": 1}))
check("wait for deletion of a path that never existed -> hint", o.get("matched") is True and "did not exist" in o.get("hint", ""), o)
o = j(call("wait_for_file", {"path": tempfile.gettempdir(), "mode": "exists", "timeoutSeconds": 2}))
check("a folder counts for mode=exists", o.get("matched") is True and o.get("ok") is True, o)
o = j(call("start_process", {"executable": "cmd.exe", "arguments": "/c exit 3", "label": "diesnow"}))
check("a program that dies at once with an error -> ok:false", o.get("ok") is False and "exited at once with code 3" in o.get("message", ""), o)
o = j(call("start_process", {"executable": "cmd.exe", "arguments": "/c exit 0", "label": "endsnow"}))
check("a program that ended cleanly at once -> ok:true plus a note", o.get("ok") is True and "already finished" in (o.get("readySignal") or ""), o)
call("stop_process", {"label": "diesnow"}); call("stop_process", {"label": "endsnow"})
o = j(call("android_input_text", {"text": "привет"}))
check("android_input_text with non-ASCII -> ok:false (not a silent drop)", o.get("ok") is False and "ASCII" in (o.get("message") or ""), o)
o = j(call("run_sequence", {"steps": [{"action": "mouse_move", "x": 5, "y": 5}]}))
check("run_sequence says when the user's input was NOT blocked", o.get("ok") is True and (o.get("inputLocked") is True or "NOT blocked" in o.get("warning", "")), o)
o = j(call("get_ui_tree", {"windowTitle": "no-such-window-zzz"}))
check("get_ui_tree for a missing window -> WindowNotFound", o.get("ok") is False and o.get("errorCode") == "WindowNotFound", o)
o = j(call("click", {"name": "x", "windowTitle": "no-such-window-zzz"}))
check("click(name) in a missing window -> WindowNotFound (not ElementNotFound)", o.get("ok") is False and o.get("errorCode") == "WindowNotFound", o)
try: os.remove(script2)
except OSError: pass

print("\n== F. read_text and the Android helpers")
script3 = os.path.join(tempfile.gettempdir(), "manctl_test_form3.ps1")
open(script3, "w", encoding="utf-8").write("""
Add-Type -AssemblyName System.Windows.Forms
$f = New-Object Windows.Forms.Form; $f.Text='ManctlTextForm'; $f.Width=520; $f.Height=360
$f.StartPosition='Manual'; $f.Location = New-Object Drawing.Point(300,200)
$lbl = New-Object Windows.Forms.Label; $lbl.Text='Status: ready'; $lbl.Left=20; $lbl.Top=20; $lbl.Width=300
$tb = New-Object Windows.Forms.TextBox; $tb.Text='hello world'; $tb.Left=20; $tb.Top=50; $tb.Width=300
$pw = New-Object Windows.Forms.TextBox; $pw.UseSystemPasswordChar=$true; $pw.Text='secret'; $pw.Left=20; $pw.Top=80; $pw.Width=300
$cb = New-Object Windows.Forms.CheckBox; $cb.Text='Remember me'; $cb.Checked=$true; $cb.Left=20; $cb.Top=110; $cb.Width=200
$b1 = New-Object Windows.Forms.Button; $b1.Text='OK'; $b1.Left=20; $b1.Top=150
$b2 = New-Object Windows.Forms.Button; $b2.Text='OK'; $b2.Left=220; $b2.Top=150
$b3 = New-Object Windows.Forms.Button; $b3.Text='Cancel'; $b3.Left=20; $b3.Top=200
$b1.Add_Click({ $lbl.Text = 'Status: first' }); $b2.Add_Click({ $lbl.Text = 'Status: second' })
$f.Controls.AddRange(@($lbl,$tb,$pw,$cb,$b1,$b2,$b3)); [void]$f.ShowDialog()
""")
call("start_process", {"executable": "powershell.exe", "arguments": f'-NoProfile -ExecutionPolicy Bypass -File "{script3}"', "label": "textform"})
th = j(call("wait_for_window", {"windowTitle": "ManctlTextForm", "timeoutSeconds": 20})).get("windowHandle")
check("text test window is up", bool(th))
if th:
    time.sleep(0.7)
    r = call("read_text", {"windowHandle": th}); o = j(r); t = o.get("text") or ""
    check("read_text gives ok + text", o.get("ok") is True and o.get("window") == "ManctlTextForm" and o.get("chars", 0) > 10, r.get("text"))
    check("labels, field value and buttons are all there", all(w in t for w in ("Status: ready", "hello world", "Cancel")), t)
    check("checkbox state is shown as [x]", "[x] Remember me" in t, t)
    check("password field content is never read", "secret" not in t and "password" in t.lower(), t)
    check("two OK buttons stay two (no dedup)", t.count("OK") == 2, t)
    check("reading order top to bottom", t.index("Status: ready") < t.index("Remember me") < t.index("Cancel"), t)
    o = j(call("read_text", {"windowHandle": th, "contains": "OK"}))
    ms = o.get("matches") or []
    check("contains='OK' -> found, 2 matches with different x", o.get("ok") is True and o.get("found") is True and o.get("matchCount") == 2 and len(ms) == 2 and ms[0]["x"] != ms[1]["x"], o)
    check("several matches -> hint says how to pick one", "click(x, y)" in o.get("hint", ""), o)
    o = j(call("read_text", {"windowHandle": th, "contains": "remember"}))
    check("contains is case-insensitive, one match needs no hint", o.get("found") is True and o.get("matchCount") == 1 and not o.get("hint"), o)
    o = j(call("read_text", {"windowHandle": th, "contains": "no-such-text-zzz"}))
    check("text not there -> ok:false ElementNotFound found:false + caution", o.get("ok") is False and o.get("errorCode") == "ElementNotFound" and o.get("found") is False and "not proof" in o.get("hint", ""), o)
    # the duplicate problem end to end: press the SECOND OK by its point
    second = sorted((j(call("read_text", {"windowHandle": th, "contains": "OK"})).get("matches") or []), key=lambda m: m["x"])[-1]
    o = j(call("click", {"x": second["x"], "y": second["y"], "windowHandle": th}))
    check("click(x, y) of the second match presses the second button", o.get("ok") is True, o)
    time.sleep(0.4)
    o = j(call("read_text", {"windowHandle": th, "contains": "Status: second"}))
    check("...and the window really shows 'Status: second'", o.get("found") is True, o)
    o = j(call("read_text", {"windowTitle": "ManctlTextForm", "maxChars": 200}))
    check("read_text by windowTitle", o.get("ok") is True and o.get("truncated") is False, o)
call("stop_process", {"label": "textform", "graceful": False})
try: os.remove(script3)
except OSError: pass

o = j(call("read_text", {"windowTitle": "no-such-window-zzz"}))
check("read_text for a missing window -> WindowNotFound", o.get("ok") is False and o.get("errorCode") == "WindowNotFound", o)
script4 = os.path.join(tempfile.gettempdir(), "manctl_test_form4.ps1")
open(script4, "w", encoding="utf-8").write("""
Add-Type -AssemblyName System.Windows.Forms
$f = New-Object Windows.Forms.Form; $f.Text='ManctlBlankForm'; $f.Width=400; $f.Height=300; $f.BackColor=[Drawing.Color]::Black
$f.StartPosition='Manual'; $f.Location = New-Object Drawing.Point(700,300)
[void]$f.ShowDialog()
""")
call("start_process", {"executable": "powershell.exe", "arguments": f'-NoProfile -ExecutionPolicy Bypass -File "{script4}"', "label": "blankform"})
bh = j(call("wait_for_window", {"windowTitle": "ManctlBlankForm", "timeoutSeconds": 20})).get("windowHandle")
if bh:
    o = j(call("read_text", {"windowHandle": bh}))
    check("read_text of a window with no elements -> NoElements, not an empty success", o.get("ok") is False and o.get("errorCode") == "NoElements" and "screenshot" in o.get("hint", ""), o)
    o = j(call("read_text", {"windowHandle": bh, "contains": "Play"}))
    check("read_text(contains) in such a window -> NoElements, not 'not found'", o.get("ok") is False and o.get("errorCode") == "NoElements", o)
call("stop_process", {"label": "blankform", "graceful": False})
try: os.remove(script4)
except OSError: pass

print("\n   Android helpers (no device is connected here: argument handling and failure answers only)")
devices = j({"json": None}) or {}
dev_list = call("android_list_devices").get("json") or []
has_device = any(d.get("state") == "device" for d in dev_list) if isinstance(dev_list, list) else False
o = j(call("android_push", {"localPath": "C:\\definitely\\not\\here.bin", "devicePath": "/sdcard/x.bin"}))
check("android_push of a missing local file -> FileNotFound", o.get("ok") is False and o.get("errorCode") == "FileNotFound", o)
o = j(call("android_logcat", {"level": "Q"}))
check("android_logcat bad level -> InvalidArgument", o.get("ok") is False and o.get("errorCode") == "InvalidArgument", o)
if not has_device:
    o = j(call("android_find", {"text": "Login"}))
    check("android_find without a device -> ok:false with the reason", o.get("ok") is False and o.get("errorCode") == "Unknown" and "android_list_devices" in o.get("hint", ""), o)
    o = j(call("android_logcat", {"lines": 10}))
    check("android_logcat without a device -> ok:false with a message", o.get("ok") is False and o.get("message"), o)
    o = j(call("android_pull", {"devicePath": "/sdcard/x.bin", "localPath": os.path.join(tempfile.gettempdir(), "manctl_pull_test", "x.bin")}))
    check("android_pull without a device -> ok:false with a message", o.get("ok") is False and o.get("message"), o)
    o = j(call("android_pull", {"devicePath": "files/a.json", "localPath": os.path.join(tempfile.gettempdir(), "manctl_pull_test", "a.json"), "app": "com.example.none"}))
    check("android_pull(app) without a device -> ok:false with a message", o.get("ok") is False and o.get("message"), o)
else:
    print("INFO  a device is connected: live Android checks are not part of this script")

p.kill()
print("\nFAILED" if fails else "\nALL OK", fails)
sys.exit(1 if fails else 0)
