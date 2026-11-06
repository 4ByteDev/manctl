# Agent-understanding evaluation kit

Checks how well a *weak* AI model understands the manctl API from nothing but its descriptions.
A small model (for example Claude Haiku at the lowest effort) reads the tool cards and a set of tasks, writes the tool
calls it would make as JSON, and the `grade*.py` scripts score them automatically. A deliberately wrong answer file must
score near 0 (negative control).

| Script | What it does |
|---|---|
| `gen_cards.py <exe> <with_rules.md> <without_rules.md>` | Starts the server, takes the MCP `instructions` and `tools/list`, writes the cards exactly as an agent sees them |
| `tasks.md`, `tasks_hard.md`, `tasks_all.md` | 56 tasks (T1-T28 easy, H1-H28 phrased like a person, with traps) |
| `grade.py`, `grade_hard.py <names.json> ans*.json` | score answers to the tasks (`names_p3.json` maps parameter names) |
| `gen_recovery.py`, `gen_recovery2.py`, `gen_focus_cases.py` | call the real server with wrong calls / on windows with no elements and record the REAL answers |
| `grade_real_rec.py`, `grade_real_rec2.py <meta.json> ans*.json` | check that the agent's next step after such an answer is sensible |
| `names_only.md`, `grade_names.py` | tool choice from names alone (no descriptions) |

Typical run: 3 agents get the same prompt ("read cards + tasks, write ans_X.json"), then `python -I grade.py names_p3.json ans_A.json ans_B.json ans_C.json`.
Limits: this measures how understandable the descriptions are, not the server itself (`../api_v2_test.py` does that).
