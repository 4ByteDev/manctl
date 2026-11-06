"""Grades answers to recovery_real.md. usage: python -I grade_real_rec.py <meta.json> <ans1.json> [ans2.json ...]"""
import json, sys
meta = json.load(open(sys.argv[1], encoding="utf-8"))
H, B = meta["handle"], meta["bounds"]
def low(x): return str(x).lower()
def num(x):
    try: return float(x)
    except Exception: return None
def first(cs): return cs[0] if cs else {"tool": None, "args": {}}
def A(c): return c.get("args", {}) or {}
C = {}
def R(t):
    def d(f): C[t] = f; return f
    return d
@R("RR1")
def _(cs): c = first(cs); return c["tool"] == "key_press" and low(A(c).get("keys")) == "home"
@R("RR2")
def _(cs): c = first(cs); return c["tool"] == "click" and low(A(c).get("name")) == "save" and "text" not in A(c)
@R("RR3")
def _(cs): c = first(cs); a = A(c); return c["tool"] == "wait_for_element" and low(a.get("name")) == "finish" and num(a.get("timeoutSeconds")) == 5 and "timeoutMs" not in a
@R("RR4")
def _(cs): c = first(cs); a = A(c); return c["tool"] == "screenshot" and num(a.get("windowHandle")) == H and "scope" not in a
@R("RR5")
def _(cs): c = first(cs); a = A(c); return c["tool"] == "run_command" and (low(a.get("command")) == "git status" or (low(a.get("executable")) == "git" and low(a.get("arguments")) == "status"))
@R("RR6")
def _(cs): c = first(cs); a = A(c); return c["tool"] == "start_process" and low(a.get("executable")) == "notepad.exe" and "path" not in a
@R("RR7")
def _(cs): c = first(cs); a = A(c); return c["tool"] == "focus_window" and num(a.get("windowHandle")) == 787858 and "handle" not in a
@R("RR8")
def _(cs): c = first(cs); a = A(c); return c["tool"] == "mouse_move" and num(a.get("x")) == 10 and num(a.get("y")) == 20
@R("RR9")
def _(cs):
    c = first(cs); a = A(c)
    if c["tool"] != "screenshot": return False
    if "x" not in a and "y" not in a: return num(a.get("windowHandle")) == H   # whole window first: the safe way to find the right area
    return num(a.get("x")) is not None and B["x"] <= num(a["x"]) < B["x"] + B["width"] and num(a.get("y")) is not None and B["y"] <= num(a["y"]) < B["y"] + B["height"]
@R("RR10")
def _(cs): c = first(cs); return c["tool"] in ("find_element", "get_ui_tree", "screenshot", "read_text") or (c["tool"] == "click" and low(A(c).get("name")) == "play")
@R("RR11")
def _(cs): c = first(cs); return c["tool"] == "list_windows" or (c["tool"] == "wait_for_window" and (num(A(c).get("timeoutSeconds")) or 0) > 1)
@R("RR12")
def _(cs): c = first(cs); return c["tool"] == "set_window_state" and low(A(c).get("state")) in ("minimized", "maximized", "normal")
@R("RR13")
def _(cs):
    c = first(cs); a = A(c); has_el = any(k in a for k in ("name", "role", "automationId")); has_pt = "x" in a or "y" in a
    return c["tool"] == "click" and has_el != has_pt
@R("RR14")
def _(cs):
    c = first(cs); a = A(c)
    return c["tool"] == "run_sequence" and isinstance(a.get("steps"), list) and a["steps"] and all("elementText" not in s and low(s.get("name")) == "ok" for s in a["steps"])
@R("RR15")
def _(cs): c = first(cs); a = A(c); return c["tool"] in ("list_windows", "get_foreground_window") or (c["tool"] == "type_text" and "windowTitle" in a and num(a.get("windowHandle")) != 99999999)
tot = {t: 0 for t in C}; n = 0
for f in sys.argv[2:]:
    ans = json.load(open(f, encoding="utf-8")); n += 1; fails = []
    for t, fn in C.items():
        cs = next((e.get("calls", []) for e in ans if e.get("task") == t), [])
        try: ok = bool(fn(cs))
        except Exception: ok = False
        tot[t] += ok
        if not ok: fails.append(t)
    print(f.split("\\")[-1], len(C) - len(fails), "/", len(C), "fails", fails)
print("failing:", {t: f"{v}/{n}" for t, v in tot.items() if v < n}); print("TOTAL %.1f%%" % (100 * sum(tot.values()) / (n * len(C))))
