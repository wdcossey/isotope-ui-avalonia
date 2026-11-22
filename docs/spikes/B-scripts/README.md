# Spike B scripts

Written by the Spike B research agent on 2026-11-22. Python stdlib; run with `python -I` from this folder, against `../../../upstream` (paths inside the scripts point at the session scratchpad and the clone; each script sets the clone path in `U` near its top, `<repo root>/upstream`; change it if the clone moves).

Order: `collect.py` (writes `commits.json`, about 3 MB, not committed) → `areas.py` (area classifier, imported) → `top.py` / `renames_bulk.py` → `analyze.py` (`results.md`) → `analyze2.py` (`results2.md`) → `extra.py`, `extra2.py` → `conflict_proxy.py`.

Note: the WinUI-reference table in `results2.md` used an early regex that also matched the `Microsoft.UI.Reactor` namespace; use `extra_out.txt`.
