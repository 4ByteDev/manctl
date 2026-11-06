import json, sys, re, glob

# usage: grade.py <names.json> <answer1.json> [answer2.json ...]
# names.json maps concept -> official parameter name (per spec version), plus tool-name overrides
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
def tool(calls, name):
    name = N.get('tool:' + name, name)
    for c in calls:
        if c.get('tool') == name: return c.get('args', {}) or {}
    return None
def has(a, key, pred):
    if a is None: return False
    k = p(key)
    return k in a and pred(a[k])
def eq(v): return lambda x: low(x) == low(v)
def inn(v): return lambda x: low(v) in low(x)
def numeq(v): return lambda x: num(x) is not None and abs(num(x) - v) < 1e-6
def absent(a, *keys): return a is not None and all(p(k) not in a for k in keys)
def true(x): return x is True or low(x) == 'true'
def norm_keys(x): return low(x).replace(' ', '').replace('control', 'ctrl')

def checks():
    C = {}
    def T(t):
        def deco(f): C[t] = f; return f
        return deco
    @T('T1')
    def _(cs):
        a = tool(cs, 'click'); return has(a, 'name', inn('save')) and has(a, 'windowHandle', numeq(1234)) and absent(a, 'x', 'y')
    @T('T2')
    def _(cs):
        a = tool(cs, 'click'); return has(a, 'x', numeq(640)) and has(a, 'y', numeq(360)) and absent(a, 'name')
    @T('T3')
    def _(cs):
        a = tool(cs, 'click'); return has(a, 'x', numeq(300)) and has(a, 'y', numeq(200)) and has(a, 'doubleClick', true)
    @T('T4')
    def _(cs):
        a = tool(cs, 'click'); return has(a, 'name', inn('file')) and has(a, 'button', eq('right')) and has(a, 'windowHandle', numeq(88))
    @T('T5')
    def _(cs):
        a = tool(cs, 'type_text'); return has(a, 'text', eq('hello world')) and has(a, 'name', inn('search')) and has(a, 'windowTitle', inn('chrome'))
    @T('T6')
    def _(cs):
        a = tool(cs, 'key_press'); return has(a, 'keys', lambda x: norm_keys(x) == 'ctrl+s')
    @T('T7')
    def _(cs):
        a = tool(cs, 'handle_file_dialog'); return has(a, 'path', inn('out\\report.txt')) and has(a, 'action', eq('save'))
    @T('T8')
    def _(cs):
        a = tool(cs, 'wait_for_window'); return has(a, 'windowTitle', inn('setup wizard')) and (p('mode') not in a or low(a[p('mode')]) == 'appears') and has(a, 'timeout', numeq(120))
    @T('T9')
    def _(cs):
        a = tool(cs, 'screenshot'); return has(a, 'x', numeq(100)) and has(a, 'y', numeq(200)) and has(a, 'width', numeq(300)) and has(a, 'height', numeq(40)) and has(a, 'scale', numeq(2))
    @T('T10')
    def _(cs):
        a = tool(cs, 'screenshot'); return has(a, 'scale', numeq(0.5)) and absent(a, 'x', 'y', 'width', 'height', 'windowHandle')
    @T('T11')
    def _(cs):
        a = tool(cs, 'run_command')
        if a is None: return False
        blob = low(' '.join(str(v) for v in a.values()))
        return 'git' in blob and 'status' in blob and 'e:\\proj' in blob
    @T('T12')
    def _(cs):
        a = tool(cs, 'start_process')
        return a is not None and has(a, 'executable', inn('emulator.exe')) and has(a, 'arguments', inn('-avd pixel6')) and has(a, 'waitForType', eq('androidBoot'))
    @T('T13')
    def _(cs):
        a = tool(cs, 'android_click'); return has(a, 'name', inn('login')) and absent(a, 'x', 'y')
    @T('T14')
    def _(cs):
        a = tool(cs, 'android_click'); return has(a, 'x', numeq(540)) and has(a, 'y', numeq(1200)) and absent(a, 'name')
    @T('T15')
    def _(cs):
        a = tool(cs, 'set_window_state'); return has(a, 'state', eq('maximized')) and has(a, 'windowHandle', numeq(777))
    @T('T16')
    def _(cs):
        a = tool(cs, 'set_window_bounds'); return has(a, 'windowTitle', inn('calculator')) and has(a, 'x', numeq(0)) and has(a, 'y', numeq(0)) and has(a, 'width', numeq(800)) and has(a, 'height', numeq(600))
    @T('T17')
    def _(cs):
        a = tool(cs, 'read_text'); return has(a, 'windowHandle', numeq(55))
    @T('T18')
    def _(cs):
        a = tool(cs, 'wait_for_file'); return has(a, 'path', inn('log.txt')) and has(a, 'mode', eq('contains_text')) and has(a, 'matchText', eq('DONE')) and has(a, 'timeout', numeq(600))
    @T('T19')
    def _(cs):
        a = tool(cs, 'run_sequence')
        if a is None or 'steps' not in a: return False
        st = a['steps']
        if not isinstance(st, list) or len(st) != 4: return False
        blob = low(json.dumps(a))
        acts = [low(s.get('action', '')) for s in st]
        if acts != ['click', 'type_text', 'key_press', 'click']: return False
        s0, s1, s2, s3 = st
        n = p('name')
        return (low(s0.get(n, '')).find('name') >= 0 and low(s1.get(p('text'), '')) == 'ivan' and norm_keys(s2.get(p('keys'), '')) == 'tab'
                and low(s3.get(n, '')).find('ok') >= 0 and '42' in blob)
    @T('T20')
    def _(cs):
        a = tool(cs, 'set_env_var'); return has(a, 'envName', eq('JAVA_HOME')) and has(a, 'value', inn('c:\\jdk')) and (p('scope') not in a or low(a[p('scope')]) == 'process')
    @T('T21')
    def _(cs):
        a = tool(cs, 'android_logcat'); return has(a, 'lines', numeq(100))
    @T('T22')
    def _(cs):
        a = tool(cs, 'android_push'); return has(a, 'localPath', inn('c:\\a.apk')) and has(a, 'devicePath', eq('/sdcard/a.apk'))
    @T('T23')
    def _(cs):
        a = tool(cs, 'scroll'); return has(a, 'deltaY', numeq(-360)) and has(a, 'x', numeq(500)) and has(a, 'y', numeq(400))
    @T('T24')
    def _(cs): return tool(cs, 'list_windows') is not None
    @T('T25')
    def _(cs):
        a = tool(cs, 'verify'); return has(a, 'property', eq('toggled')) and has(a, 'name', inn('remember me')) and has(a, 'windowHandle', numeq(9)) and has(a, 'expected', true)
    @T('T26')
    def _(cs):
        a = tool(cs, 'stop_process'); return has(a, 'profileName', eq('emu'))
    @T('T27')
    def _(cs):
        a = tool(cs, 'close_window'); return has(a, 'windowTitle', inn('untitled - notepad'))
    @T('T28')
    def _(cs):
        a = tool(cs, 'wait_for_process_idle'); return has(a, 'processName', inn('msbuild')) and has(a, 'idle', numeq(60)) and has(a, 'timeout', numeq(1800))
    return C

C = checks()
tot = {t: 0 for t in C}
n = 0
for f in sys.argv[2:]:
    try: ans = json.load(open(f, encoding='utf-8'))
    except Exception as e:
        print('BAD FILE', f, e); continue
    n += 1
    fails = []
    for t, fn in C.items():
        try: ok = bool(fn(calls_of(ans, t)))
        except Exception as e: ok = False
        tot[t] += ok
        if not ok: fails.append(t)
    print(f.split('\\')[-1], 'score', len(C) - len(fails), '/', len(C), 'fails', fails)
if n:
    print('PER-TASK pass rate:', {t: f'{v}/{n}' for t, v in tot.items() if v < n})
    print('TOTAL %.1f%%' % (100 * sum(tot.values()) / (n * len(C))))
