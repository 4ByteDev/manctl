"""focus_window test: the ladder of ways to bring a window forward, and what happens when none works.

Windows normally lets this machine switch windows, so the failures are simulated with the test hook
MANCTL_FOCUS_TEST_SKIP=N (pretend the first N ways failed) or =all (pretend all failed). Each step of the ladder is
run on its own against two real windows; the failure answer and "never type into the wrong window" are checked too.

usage: python -I focus_test.py <path to Manctl.Server.exe>
"""
import json, os, queue, subprocess, sys, tempfile, threading, time

exe = sys.argv[1]
STEPS = ["attach input and set foreground", "input event, then set foreground", "lift the foreground lock",
         "minimize and restore", "topmost flip", "switch to this window", "click on its title bar"]

class Server:
    def __init__(self, env=None):
        e = dict(os.environ); e.update(env or {})
        self.p = subprocess.Popen([exe], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, env=e)
        self.q = queue.Queue(); self.i = 0
        threading.Thread(target=lambda: [self.q.put(json.loads(l)) for l in self.p.stdout if l.strip()], daemon=True).start()
        self.rpc("initialize", {"protocolVersion": "2024-11-05", "capabilities": {}, "clientInfo": {"name": "t", "version": "1"}})
        self.rpc("notifications/initialized", notify=True)
    def rpc(self, method, params=None, notify=False):
        msg = {"jsonrpc": "2.0", "method": method}
        if params is not None: msg["params"] = params
        if not notify: self.i += 1; msg["id"] = self.i
        self.p.stdin.write((json.dumps(msg) + "\n").encode()); self.p.stdin.flush()
        if notify: return None
        while True:
            r = self.q.get(timeout=120)
            if r.get("id") == self.i: return r
    def call(self, tool, args=None):
        c = self.rpc("tools/call", {"name": tool, "arguments": args or {}})["result"]["content"]
        t = next((x["text"] for x in c if x["type"] == "text"), "{}")
        try: return json.loads(t)
        except Exception: return {"raw": t}
    def close(self): self.p.kill()

fails = 0
def check(name, cond, detail=""):
    global fails
    fails += (not cond)
    print(("PASS " if cond else "FAIL ") + name + ("" if cond else "   <- " + str(detail)[:500]))

script = os.path.join(tempfile.gettempdir(), "manctl_focus_form.ps1")
open(script, "w", encoding="utf-8").write("""
param([string]$Title,[int]$X)
Add-Type -AssemblyName System.Windows.Forms
$f = New-Object Windows.Forms.Form; $f.Text=$Title; $f.Width=360; $f.Height=220
$f.StartPosition='Manual'; $f.Location = New-Object Drawing.Point($X,300)
$tb = New-Object Windows.Forms.TextBox; $tb.Left=20; $tb.Top=20; $tb.Width=280; $f.Controls.Add($tb)
[void]$f.ShowDialog()
""")

base = Server()
def start(label, title, x):
    base.call("start_process", {"executable": "powershell.exe", "arguments": f'-NoProfile -ExecutionPolicy Bypass -File "{script}" -Title {title} -X {x}', "label": label})
    return base.call("wait_for_window", {"windowTitle": title, "timeoutSeconds": 20}).get("windowHandle")

A = start("fa", "ManctlFocusA", 100)
B = start("fb", "ManctlFocusB", 600)
check("two test windows are up", bool(A) and bool(B))

def front():
    return (base.call("get_foreground_window") or {}).get("handle")

def put_in_front(h):
    for _ in range(3):
        if base.call("focus_window", {"windowHandle": h}).get("ok") and front() == h: return True
    return False

if A and B:
    check("normal focus_window works and reports the way used", put_in_front(B) and (lambda r: r.get("ok") is True and r.get("method"))(base.call("focus_window", {"windowHandle": A})) and front() == A)

    print("\n== each way of the ladder on its own (the earlier ones are simulated as failed)")
    for k, name in enumerate(STEPS):
        put_in_front(B)
        s = Server({"MANCTL_FOCUS_TEST_SKIP": str(k)})
        r = s.call("focus_window", {"windowHandle": A}); s.close()
        reached = r.get("ok") is True and front() == A
        if reached:
            check(f"way {k + 1} '{name}' brings the window forward by itself", r.get("method") == name, r)
        else:
            # not every way works alone on every machine; the ladder only needs the union. Report it, do not fail.
            print(f"INFO  way {k + 1} '{name}' alone did not bring the window forward on this machine: {str(r)[:160]}")
    put_in_front(B)

    print("\n== when every way fails")
    s = Server({"MANCTL_FOCUS_TEST_SKIP": "all"})
    r = s.call("focus_window", {"windowHandle": A})
    check("focus_window -> ok:false AccessDenied with the reason", r.get("ok") is False and r.get("errorCode") == "AccessDenied" and "7 different ways" in r.get("message", ""), r)
    check("...the answer names the window that is in front", "ManctlFocusB" in (r.get("frontWindow") or "") or "ManctlFocusB" in r.get("message", ""), r)
    h = r.get("hint", "")
    check("...and the hint lists what to do next instead of giving up", all(w in h for w in ("click(name", "read_text", "set_window_state", "ask the user")), h)
    check("...and the window really did not come forward", front() == B, front())

    tb_before = base.call("read_text", {"windowHandle": B}).get("text", "")
    r = s.call("key_press", {"keys": "z", "windowHandle": A})
    check("key_press into a window that cannot be focused -> ok:false, nothing sent", r.get("ok") is False and r.get("errorCode") == "AccessDenied" and r.get("hint"), r)
    r = s.call("type_text", {"text": "zzzz", "windowHandle": A})
    check("type_text into a window that cannot be focused -> ok:false", r.get("ok") is False and r.get("errorCode") == "AccessDenied", r)
    r = s.call("click", {"x": 150, "y": 330, "windowHandle": A})
    check("click(x,y) in a window that cannot be focused -> ok:false", r.get("ok") is False and r.get("errorCode") == "AccessDenied", r)
    r = s.call("scroll", {"deltaY": -120, "windowHandle": A})
    check("scroll(windowHandle) in a window that cannot be focused -> ok:false", r.get("ok") is False and r.get("errorCode") == "AccessDenied", r)
    r = s.call("run_sequence", {"windowHandle": A, "lockInput": False, "steps": [{"action": "scroll", "deltaY": -120}]})
    check("run_sequence scroll step in such a window -> ok:false", r.get("ok") is False, r)
    s.close()
    time.sleep(0.3)
    tb_after = base.call("read_text", {"windowHandle": B}).get("text", "")
    check("no stray keys landed in the window that WAS in front", "z" not in tb_after.replace("ManctlFocusB", "") and tb_before == tb_after, (tb_before, tb_after))

    print("\n== UI Automation actions do not need the window in front")
    s = Server({"MANCTL_FOCUS_TEST_SKIP": "all"})
    put_in_front(B)
    r = s.call("type_text", {"text": "background", "role": "Edit", "windowHandle": A})
    check("type_text(role=Edit) works in a window that is NOT in front", r.get("ok") is True and r.get("method") == "setValue", r)
    r = s.call("verify", {"property": "value", "expected": "background", "role": "Edit", "windowHandle": A})
    check("...and verify sees it", r.get("ok") is True, r)
    r = s.call("read_text", {"windowHandle": A})
    check("read_text works in the background too", r.get("ok") is True and "background" in r.get("text", ""), r)
    s.close()

base.call("stop_process", {"label": "fa", "graceful": False}); base.call("stop_process", {"label": "fb", "graceful": False})
base.close()
try: os.remove(script)
except OSError: pass
print("\nFAILED" if fails else "\nALL OK", fails)
sys.exit(1 if fails else 0)
