"""Real answers when a window cannot be brought forward (simulated with MANCTL_FOCUS_TEST_SKIP=all), for checking that a weak
agent follows the hint instead of giving up. Opens two small test windows and closes them again.

usage: python -I gen_focus_cases.py <Manctl.Server.exe> <out_new.md> <out_old.md> <out_meta.json>
"""
import json, os, queue, subprocess, sys, tempfile, threading

exe, out_new, out_old, out_meta = sys.argv[1:5]

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
    def text(self, tool, args):
        c = self.rpc("tools/call", {"name": tool, "arguments": args})["result"]["content"]
        return next((x["text"] for x in c if x["type"] == "text"), "")
    def close(self): self.p.kill()

script = os.path.join(tempfile.gettempdir(), "manctl_focus_form_gen.ps1")
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
    base.text("start_process", {"executable": "powershell.exe", "arguments": f'-NoProfile -ExecutionPolicy Bypass -File "{script}" -Title {title} -X {x}', "label": label})
    return json.loads(base.text("wait_for_window", {"windowTitle": title, "timeoutSeconds": 20}))["windowHandle"]
A = start("ga", "ManctlGenA", 100)
B = start("gb", "ManctlGenB", 600)
base.text("focus_window", {"windowHandle": B})

s = Server({"MANCTL_FOCUS_TEST_SKIP": "all"})
cases = [("FC1", "focus_window", {"windowHandle": A}), ("FC2", "key_press", {"keys": "ctrl+s", "windowHandle": A}),
         ("FC3", "type_text", {"text": "hello", "windowHandle": A})]
head = ["# Cases (real answers from manctl)", "", "You called a tool and got the answer shown. Decide what you do next: make another tool call, or give up and tell the user.", ""]
new_lines, old_lines = list(head), list(head)
for cid, tool, args in cases:
    ans = s.text(tool, args)
    new_lines += [f"{cid}. You called: {tool}({json.dumps(args)})", f"Answer: {ans}", ""]
    # what the same situation looked like before: focus_window returned a bare boolean
    old_ans = "false" if tool == "focus_window" else ans
    old_lines += [f"{cid}. You called: {tool}({json.dumps(args)})", f"Answer: {old_ans}", ""]
open(out_new, "w", encoding="utf-8").write("\n".join(new_lines))
open(out_old, "w", encoding="utf-8").write("\n".join(old_lines))
json.dump({"A": A, "B": B}, open(out_meta, "w"))
s.close()
base.text("stop_process", {"label": "ga", "graceful": False}); base.text("stop_process", {"label": "gb", "graceful": False})
base.close(); os.remove(script)
print("ok", A, B)
