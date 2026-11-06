"""Real answers for the 'empty result / refusal' situations: a window with no UI elements (like a game or ImGui window),
a window that will not close, a point outside the window, a window that cannot get that small.
It opens its own small test windows with PowerShell and closes them again.

usage: python -I gen_recovery2.py <Manctl.Server.exe> <out_cases.md> <out_meta.json>
"""
import json, os, queue, subprocess, sys, tempfile, threading, time

exe, out_md, out_meta = sys.argv[1:4]
p = subprocess.Popen([exe], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
q = queue.Queue()
threading.Thread(target=lambda: [q.put(json.loads(l)) for l in p.stdout if l.strip()], daemon=True).start()
_id = 0
def rpc(method, params=None, notify=False):
    global _id
    msg = {"jsonrpc": "2.0", "method": method}
    if params is not None: msg["params"] = params
    if not notify: _id += 1; msg["id"] = _id
    p.stdin.write((json.dumps(msg) + "\n").encode()); p.stdin.flush()
    if notify: return None
    while True:
        r = q.get(timeout=90)
        if r.get("id") == _id: return r
def call(tool, args):
    c = rpc("tools/call", {"name": tool, "arguments": args})["result"]["content"][0]
    return c.get("text") if c["type"] == "text" else "[an image]"

rpc("initialize", {"protocolVersion": "2024-11-05", "capabilities": {}, "clientInfo": {"name": "t", "version": "1"}})
rpc("notifications/initialized", notify=True)

script = os.path.join(tempfile.gettempdir(), "manctl_gen_form.ps1")
open(script, "w", encoding="utf-8").write("""
param([string]$Title,[switch]$Stubborn)
Add-Type -AssemblyName System.Windows.Forms
$f = New-Object Windows.Forms.Form; $f.Text=$Title; $f.Width=400; $f.Height=300; $f.BackColor=[Drawing.Color]::Black
$f.StartPosition='Manual'; $f.Location = New-Object Drawing.Point(700,300)
if ($Stubborn) { $f.Add_FormClosing({ $_.Cancel = $true }) }
[void]$f.ShowDialog()
""")
def start(label, title, flag=""):
    call("start_process", {"executable": "powershell.exe", "arguments": f'-NoProfile -ExecutionPolicy Bypass -File "{script}" -Title {title} {flag}', "label": label})
    return json.loads(call("wait_for_window", {"windowTitle": title, "timeoutSeconds": 20}))["windowHandle"]

H = start("gf1", "ManctlGenEmpty")
H2 = start("gf2", "ManctlGenStubborn", "-Stubborn")
B = next(w["bounds"] for w in json.loads(call("list_windows", {})) if w["handle"] == H)

cases = [
    ("RN1", "get_ui_tree", {"windowHandle": H}),
    ("RN2", "find_element", {"name": "Play", "windowHandle": H}),
    ("RN3", "click", {"name": "Play", "windowHandle": H}),
    ("RN4", "wait_for_element", {"name": "Play", "windowHandle": H, "timeoutSeconds": 1}),
    ("RN5", "close_window", {"windowHandle": H2}),
    ("RN6", "click", {"x": 50, "y": 60, "windowHandle": H}),
    ("RN7", "set_window_bounds", {"width": 10, "height": 10, "windowHandle": H}),
]
lines = ["# Recovery cases 2 (real answers from manctl)", "",
         "In each case you made a tool call and got the answer shown. Say the NEXT tool call you would make (the single best one).", ""]
for cid, tool, args in cases:
    lines += [f"{cid}. You called: {tool}({json.dumps(args, ensure_ascii=False)})", f"Answer: {call(tool, args)}", ""]
open(out_md, "w", encoding="utf-8").write("\n".join(lines))
json.dump({"handle": H, "stubborn": H2, "bounds": B}, open(out_meta, "w"))
call("stop_process", {"label": "gf1", "graceful": False}); call("stop_process", {"label": "gf2", "graceful": False})
os.remove(script)
p.kill()
print("cases:", len(cases), "| window", H, B)
