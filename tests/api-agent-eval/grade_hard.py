import json, sys

N = json.load(open(sys.argv[1], encoding='utf-8'))
def p(c): return N.get(c, c)
def low(x): return str(x).lower()
def num(x):
    try: return float(x)
    except Exception: return None
def calls_of(ans, t):
    for e in ans:
        if e.get('task') == t: return e.get('calls', []) or []
    return []
def T_(name): return N.get('tool:' + name, name)
def tools(cs): return [c.get('tool') for c in cs]
def only(cs, name):
    """first call of tool `name` (official) -> args or None"""
    for c in cs:
        if c.get('tool') == T_(name): return c.get('args', {}) or {}
    return None
def has(a, key, pred):
    if a is None: return False
    k = p(key)
    return k in a and pred(a[k])
def eq(v): return lambda x: low(x) == low(v)
def inn(v): return lambda x: low(v) in low(x)
def numeq(v): return lambda x: num(x) is not None and abs(num(x) - v) < 1e-6
def rng(lo, hi): return lambda x: num(x) is not None and lo <= num(x) <= hi
def true(x): return x is True or low(x) == 'true'
def absent(a, *keys): return a is not None and all(p(k) not in a for k in keys)
def norm_keys(x): return low(x).replace(' ', '').replace('control', 'ctrl')
def blob(a): return low(' '.join(str(v) for v in a.values())) if a else ''

C = {}
def H(t):
    def deco(f): C[t] = f; return f
    return deco

@H('H1')
def _(cs): a = only(cs, 'click'); return has(a, 'x', numeq(812)) and has(a, 'y', numeq(455)) and absent(a, 'name') and 'drag' not in tools(cs)
@H('H2')
def _(cs):
    a = only(cs, 'drag'); return a is not None and a.get('fromX') == 400 and a.get('fromY') == 300 and a.get('toX') == 520 and a.get('toY') == 300
@H('H3')
def _(cs):
    if 'screenshot' in tools(cs): return False
    a = only(cs, 'read_text'); b = only(cs, 'find_element')
    return (a is not None and has(a, 'windowHandle', numeq(31))) or (b is not None and has(b, 'name', inn('connected')))
@H('H4')
def _(cs):
    a = only(cs, 'close_window'); return has(a, 'windowTitle', inn('notepad')) and len(cs) <= 2
@H('H5')
def _(cs):
    a = only(cs, 'wait_for_file'); return has(a, 'path', inn('done.flag')) and has(a, 'mode', eq('exists')) and has(a, 'timeout', numeq(90))
@H('H6')
def _(cs):
    a = only(cs, 'click'); return has(a, 'x', numeq(250)) and has(a, 'y', numeq(160))
@H('H7')
def _(cs):
    a = only(cs, 'run_command_async'); b = only(cs, 'start_process')
    ok = lambda d: d is not None and 'node' in blob(d) and 'server.js' in blob(d) and 'e:\\app' in blob(d)
    return ok(a) or ok(b)
@H('H8')
def _(cs):
    a = only(cs, 'run_command')
    return a is not None and 'npm' in blob(a) and 'build' in blob(a) and 'e:\\app' in blob(a) and has(a, 'timeout', rng(600, 1800))
@H('H9')
def _(cs):
    a = only(cs, 'click'); b = only(cs, 'wait_for_element')
    if a is None or b is None: return False
    order = tools(cs);
    return (has(a, 'name', inn('install')) and has(a, 'windowHandle', numeq(12)) and has(b, 'name', inn('finish'))
            and has(b, 'timeout', rng(180, 600)) and order.index(T_('click')) < order.index(T_('wait_for_element')))
@H('H10')
def _(cs): a = only(cs, 'key_press'); return has(a, 'keys', lambda x: norm_keys(x) in ('win+r', 'windows+r', 'meta+r'))
@H('H11')
def _(cs):
    a = only(cs, 'set_window_bounds'); return has(a, 'windowHandle', numeq(5)) and has(a, 'x', numeq(0)) and has(a, 'y', numeq(0)) and has(a, 'width', rng(950, 970)) and has(a, 'height', rng(1000, 1080))
@H('H12')
def _(cs): a = only(cs, 'screenshot'); return has(a, 'windowHandle', numeq(55)) and has(a, 'scale', rng(0.1, 0.8))
@H('H13')
def _(cs): a = only(cs, 'set_env_var'); return has(a, 'envName', eq('PATH_EXTRA')) and has(a, 'value', inn('c:\\tools')) and has(a, 'scope', eq('user'))
@H('H14')
def _(cs): a = only(cs, 'android_screenshot'); return has(a, 'deviceSerial', eq('R58M1234'))
@H('H15')
def _(cs): a = only(cs, 'android_install_latest_apk'); return has(a, 'directory', inn('e:\\build\\apk')) and (p('pattern') not in a or inn('apk')(a[p('pattern')]))
@H('H16')
def _(cs): a = only(cs, 'android_key_event'); return has(a, 'keyCode', lambda x: low(x) in ('keycode_back', '4')) and has(a, 'deviceSerial', eq('emulator-5556'))
@H('H17')
def _(cs):
    if 'android_screenshot' in tools(cs): return False
    a = only(cs, 'android_find'); b = only(cs, 'android_ui_tree')
    return (a is not None and has(a, 'text', inn('подтвердить'))) or b is not None
@H('H18')
def _(cs): return only(cs, 'get_clipboard') is not None
@H('H19')
def _(cs): a = only(cs, 'scroll'); return has(a, 'deltaY', numeq(-600)) and has(a, 'x', numeq(960)) and has(a, 'y', numeq(540))
@H('H20')
def _(cs): return only(cs, 'lock_input') is not None
@H('H21')
def _(cs): a = only(cs, 'key_press'); return has(a, 'keys', lambda x: low(x) == 'enter') and has(a, 'windowHandle', numeq(33))
@H('H22')
def _(cs):
    a = only(cs, 'click')
    if a is None: return False
    base = has(a, 'name', inn('согласен')) and has(a, 'windowHandle', numeq(14))
    if a.get('setChecked') is True: return base
    ic = tools(cs).index(T_('click'))
    after = [c.get('args', {}) for c in cs[ic + 1:] if c.get('tool') == T_('verify')]
    return base and any(low(v.get(p('property'), '')) == 'toggled' and low(v.get(p('expected'), '')) in ('true', 'checked', 'on') for v in after)

@H('H23')
def _(cs): a = only(cs, 'wait_for_process_idle'); return has(a, 'processName', inn('setup')) and has(a, 'timeout', numeq(1200))
@H('H24')
def _(cs): return only(cs, 'list_monitors') is not None
@H('H25')
def _(cs): a = only(cs, 'screenshot'); return has(a, 'x', numeq(10)) and has(a, 'y', numeq(10)) and has(a, 'width', numeq(50)) and has(a, 'height', numeq(20)) and has(a, 'scale', rng(1.01, 8))
@H('H26')
def _(cs): a = only(cs, 'click'); return has(a, 'automationId', eq('btnOk')) and has(a, 'windowTitle', inn('settings'))
@H('H27')
def _(cs): a = only(cs, 'type_text'); return has(a, 'text', eq('Привет')) and has(a, 'role', eq('Edit')) and has(a, 'windowHandle', numeq(3))
@H('H28')
def _(cs): return only(cs, 'get_foreground_window') is not None

tot = {t: 0 for t in C}; n = 0
for f in sys.argv[2:]:
    try: ans = json.load(open(f, encoding='utf-8'))
    except Exception as e: print('BAD FILE', f, e); continue
    n += 1; fails = []
    for t, fn in C.items():
        try: ok = bool(fn(calls_of(ans, t)))
        except Exception: ok = False
        tot[t] += ok
        if not ok: fails.append(t)
    print(f.split('\\')[-1], 'score', len(C) - len(fails), '/', len(C), 'fails', fails)
if n:
    print('PER-TASK failing:', {t: f'{v}/{n}' for t, v in tot.items() if v < n})
    print('TOTAL %.1f%%' % (100 * sum(tot.values()) / (n * len(C))))
