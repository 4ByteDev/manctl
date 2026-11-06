"""Grades answers to recovery cases 2. usage: python -I grade_real_rec2.py <meta.json> <ans1.json> [...]"""
import json, sys
meta = json.load(open(sys.argv[1], encoding="utf-8"))
H, H2, B = meta["handle"], meta["stubborn"], meta["bounds"]
def num(x):
    try: return float(x)
    except Exception: return None
def first(cs): return cs[0] if cs else {"tool": None, "args": {}}
def A(c): return c.get("args", {}) or {}
C = {}
def R(t):
    def d(f): C[t] = f; return f
    return d
def sees_the_window(c):   # the right next step for a window with no elements: look at it
    return c["tool"] == "screenshot"
@R("RN1")
def _(cs): return sees_the_window(first(cs))
@R("RN2")
def _(cs): return sees_the_window(first(cs))
@R("RN3")
def _(cs): return sees_the_window(first(cs))
@R("RN4")
def _(cs): return sees_the_window(first(cs))
@R("RN5")
def _(cs): c = first(cs); return c["tool"] in ("list_windows", "get_ui_tree", "screenshot", "stop_process", "get_foreground_window")
@R("RN6")
def _(cs):
    c = first(cs); a = A(c)
    if c["tool"] == "screenshot": return True        # look first, then pick the screen point: a sound next step
    if c["tool"] != "click": return False
    if "windowHandle" not in a:                       # dropped the handle: any on-screen point is fine
        return num(a.get("x")) is not None
    x, y = num(a.get("x")), num(a.get("y"))
    return x is not None and y is not None and B["x"] <= x < B["right"] and B["y"] <= y < B["bottom"]
@R("RN7")
def _(cs):
    c = first(cs); a = A(c)
    return c["tool"] in ("list_windows", "get_ui_tree", "screenshot") or (c["tool"] == "set_window_bounds" and (num(a.get("width")) or 0) >= 100 and (num(a.get("height")) or 0) >= 100)
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
