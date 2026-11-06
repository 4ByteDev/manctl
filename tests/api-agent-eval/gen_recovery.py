"""Makes the 'you called X, got this real answer, what next?' cases by calling a built Manctl.Server.exe for real.

usage: python -I gen_recovery.py <Manctl.Server.exe> <out_cases.md> <out_meta.json>
"""
import json, queue, subprocess, sys, threading

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
        r = q.get(timeout=60)
        if r.get("id") == _id: return r
def call(tool, args):
    r = rpc("tools/call", {"name": tool, "arguments": args})
    c = r["result"]["content"][0]
    return c.get("text") if c["type"] == "text" else "[an image]"

rpc("initialize", {"protocolVersion": "2024-11-05", "capabilities": {}, "clientInfo": {"name": "t", "version": "1"}})
rpc("notifications/initialized", notify=True)
wins = json.loads(call("list_windows", {}))
win = next(w for w in wins if w["bounds"]["width"] > 300 and w["bounds"]["height"] > 300)
H, B = win["handle"], win["bounds"]

cases = [
    ("RR1", "key_press", {"key": "Home"}),
    ("RR2", "click", {"text": "Save", "windowHandle": H}),
    ("RR3", "wait_for_element", {"name": "Finish", "timeoutMs": 5000}),
    ("RR4", "screenshot", {"scope": "window", "windowHandle": H}),
    ("RR5", "run_command", {"executable": "git status"}),
    ("RR6", "start_process", {"path": "notepad.exe"}),
    ("RR7", "focus_window", {"handle": 787858}),
    ("RR8", "mouse_move", {"coordinate": [10, 20]}),
    ("RR9", "screenshot", {"windowHandle": H, "x": B["x"] + B["width"] + 300, "y": B["y"] + 100, "width": 300, "height": 100}),
    ("RR10", "click", {"name": "Playbtn", "windowHandle": H}),
    ("RR11", "wait_for_window", {"windowTitle": "no-such-window-zzz", "timeoutSeconds": 1}),
    ("RR12", "set_window_state", {"state": "huge", "windowHandle": H}),
    ("RR13", "click", {"name": "OK", "x": 1, "y": 2}),
    ("RR14", "run_sequence", {"windowHandle": H, "steps": [{"action": "click", "elementText": "OK"}]}),
    ("RR15", "type_text", {"text": "x", "role": "Edit", "windowHandle": 99999999}),
]
lines = ["# Recovery cases (real answers from manctl)", "",
         "In each case you made a tool call and got the answer shown. Say the NEXT tool call you would make (the single best one).", ""]
for cid, tool, args in cases:
    ans = call(tool, args)
    lines += [f"{cid}. You called: {tool}({json.dumps(args, ensure_ascii=False)})", f"Answer: {ans}", ""]
open(out_md, "w", encoding="utf-8").write("\n".join(lines))
json.dump({"handle": H, "bounds": B}, open(out_meta, "w"))
p.kill()
print("cases:", len(cases), "| window", H, B)
