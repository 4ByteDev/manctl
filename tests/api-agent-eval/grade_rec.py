import json, sys
def low(x): return str(x).lower()
def num(x):
    try: return float(x)
    except Exception: return None
def first(cs): return cs[0] if cs else {'tool': None, 'args': {}}
def A(c): return c.get('args', {}) or {}
def blob(a): return low(' '.join(str(v) for v in a.values()))
C = {}
def R(t):
    def d(f): C[t] = f; return f
    return d
@R('R1')
def _(cs): c = first(cs); return c['tool'] == 'key_press' and low(A(c).get('keys', '')) == 'home'
@R('R2')
def _(cs): c = first(cs); return c['tool'] == 'start_process' and low(A(c).get('executable', '')) == 'notepad.exe'
@R('R3')
def _(cs): c = first(cs); return c['tool'] == 'focus_window' and num(A(c).get('windowHandle')) == 787858
@R('R4')
def _(cs): c = first(cs); return c['tool'] in ('list_windows', 'get_foreground_window') or (c['tool'] == 'click' and 'windowTitle' in A(c))
@R('R5')
def _(cs):
    c = first(cs); a = A(c)
    return (c['tool'] == 'screenshot' and num(a.get('x')) == 820 and num(a.get('y')) == 420 and num(a.get('width')) == 993 and 100 <= (num(a.get('height')) or 0) <= 150) or c['tool'] in ('list_windows', 'get_foreground_window')
@R('R6')
def _(cs): c = first(cs); return (c['tool'] == 'click' and low(A(c).get('name', '')) == 'play') or c['tool'] in ('find_element', 'get_ui_tree', 'read_text')
@R('R7')
def _(cs): c = first(cs); return (c['tool'] == 'type_text' and low(A(c).get('name', '')) in ('find', 'replace')) or c['tool'] in ('find_element', 'get_ui_tree')
@R('R8')
def _(cs): c = first(cs); return c['tool'] == 'list_windows' or (c['tool'] == 'wait_for_window' and (num(A(c).get('timeoutSeconds')) or 0) > 60)
@R('R9')
def _(cs): c = first(cs); return c['tool'] in ('android_find', 'android_ui_tree')
@R('R10')
def _(cs): c = first(cs); a = A(c); return c['tool'] == 'run_command' and low(a.get('executable', '')) == 'git' and low(a.get('arguments', '')) == 'status'
@R('R11')
def _(cs): c = first(cs); return c['tool'] == 'set_env_var' and low(A(c).get('scope', '')) in ('process', 'user')
@R('R12')
def _(cs): c = first(cs); return c['tool'] == 'wait_for_file' and low(A(c).get('mode', '')) == 'exists'
@R('R13')
def _(cs):
    c = first(cs); a = A(c)
    return c['tool'] == 'start_process' and (num(a.get('waitForTimeoutSeconds')) or 0) >= 120 and not a.get('restartIfRunning')
tot = {t: 0 for t in C}; n = 0
for f in sys.argv[1:]:
    ans = json.load(open(f, encoding='utf-8')); n += 1; fails = []
    for t, fn in C.items():
        cs = next((e.get('calls', []) for e in ans if e.get('task') == t), [])
        try: ok = bool(fn(cs))
        except Exception: ok = False
        tot[t] += ok
        if not ok: fails.append(t)
    print(f.split('\\')[-1], len(C) - len(fails), '/', len(C), 'fails', fails)
print('failing:', {t: f'{v}/{n}' for t, v in tot.items() if v < n}); print('TOTAL %.1f%%' % (100 * sum(tot.values()) / (n * len(C))))
