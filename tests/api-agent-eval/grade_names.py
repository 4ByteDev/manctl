import json, sys
EXP = {  # task -> list of alternatives; each alternative = set of tools that must ALL appear (and 'drag' forbidden for H1)
 'T1': [{'click'}], 'T2': [{'click'}], 'T3': [{'click'}], 'T4': [{'click'}], 'T5': [{'type_text'}], 'T6': [{'key_press'}],
 'T7': [{'handle_file_dialog'}], 'T8': [{'wait_for_window'}], 'T9': [{'screenshot'}], 'T10': [{'screenshot'}], 'T11': [{'run_command'}],
 'T12': [{'start_process'}], 'T13': [{'android_click'}], 'T14': [{'android_click'}], 'T15': [{'set_window_state'}], 'T16': [{'set_window_bounds'}],
 'T17': [{'read_text'}], 'T18': [{'wait_for_file'}], 'T19': [{'run_sequence'}], 'T20': [{'set_env_var'}], 'T21': [{'android_logcat'}],
 'T22': [{'android_push'}], 'T23': [{'scroll'}], 'T24': [{'list_windows'}], 'T25': [{'verify'}], 'T26': [{'stop_process'}],
 'T27': [{'close_window'}], 'T28': [{'wait_for_process_idle'}],
 'H1': [{'click'}], 'H2': [{'drag'}], 'H3': [{'read_text'}, {'find_element'}], 'H4': [{'close_window'}], 'H5': [{'wait_for_file'}], 'H6': [{'click'}],
 'H7': [{'run_command_async'}, {'start_process'}], 'H8': [{'run_command'}], 'H9': [{'click', 'wait_for_element'}], 'H10': [{'key_press'}],
 'H11': [{'set_window_bounds'}], 'H12': [{'screenshot'}], 'H13': [{'set_env_var'}], 'H14': [{'android_screenshot'}],
 'H15': [{'android_install_latest_apk'}], 'H16': [{'android_key_event'}], 'H17': [{'android_find'}, {'android_ui_tree'}], 'H18': [{'get_clipboard'}],
 'H19': [{'scroll'}], 'H20': [{'lock_input'}], 'H21': [{'key_press'}], 'H22': [{'click'}], 'H23': [{'wait_for_process_idle'}],
 'H24': [{'list_monitors'}], 'H25': [{'screenshot'}], 'H26': [{'click'}], 'H27': [{'type_text'}], 'H28': [{'get_foreground_window'}]}
FORBID = {'H1': {'drag', 'mouse_move'}, 'H3': {'screenshot'}, 'H17': {'android_screenshot'}}
tot = {t: 0 for t in EXP}; n = 0
for f in sys.argv[1:]:
    ans = json.load(open(f, encoding='utf-8')); n += 1; fails = []
    for t, alts in EXP.items():
        e = next((x for x in ans if x.get('task') == t), None)
        got = set(e.get('tools', [])) if e else set()
        ok = any(a <= got for a in alts) and not (FORBID.get(t, set()) & got)
        tot[t] += ok
        if not ok: fails.append((t, sorted(got)))
    print(f.split('\\')[-1], len(EXP) - len(fails), '/', len(EXP), fails)
print('failing:', {t: f'{v}/{n}' for t, v in tot.items() if v < n}); print('TOTAL %.1f%%' % (100 * sum(tot.values()) / (n * len(EXP))))
