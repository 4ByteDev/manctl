"""Builds the text an agent really sees from a built Manctl.Server.exe: the MCP 'instructions' from the handshake
plus one card per tool from tools/list (name, description, parameters with type / required / description).

usage: python -I gen_cards.py <Manctl.Server.exe> <out_with_rules.md> <out_without_rules.md>
"""
import json, queue, subprocess, sys, threading

exe, out_with, out_without = sys.argv[1:4]
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

init = rpc("initialize", {"protocolVersion": "2024-11-05", "capabilities": {}, "clientInfo": {"name": "t", "version": "1"}})["result"]
rpc("notifications/initialized", notify=True)
tools = rpc("tools/list", {})["result"]["tools"]
p.kill()

def tname(t):
    if isinstance(t, list): t = [x for x in t if x != "null"]; t = t[0] if len(t) == 1 else "/".join(t)
    return {"integer": "number", "boolean": "true/false", "array": "list"}.get(t, t)

def render_params(props, req, indent="  "):
    lines = []
    for k, v in props.items():
        lines.append(f"{indent}- {k} ({tname(v.get('type', 'any'))}{', required' if k in req else ''}): {v.get('description', '')}")
        items = v.get("items") or {}
        if items.get("properties"):
            lines.append(f"{indent}  each item has:")
            lines += render_params(items["properties"], set(items.get("required", [])), indent + "    ")
    return lines

cards = []
for t in tools:
    sch = t.get("inputSchema", {})
    lines = [f"### {t['name']}", t.get("description", "")]
    props = sch.get("properties", {})
    lines.append("Parameters:" if props else "Parameters: none")
    lines += render_params(props, set(sch.get("required", [])))
    cards.append("\n".join(lines))

instr = init.get("instructions") or ""
body = "\n\n".join(cards)
open(out_with, "w", encoding="utf-8").write("# manctl server instructions\n\n" + instr + "\n\n# Tools\n\n" + body + "\n")
open(out_without, "w", encoding="utf-8").write("# Tools\n\n" + body + "\n")
print("tools:", len(tools), "| instructions chars:", len(instr), "| cards chars:", len(body))
