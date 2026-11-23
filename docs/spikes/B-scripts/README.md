# Spike B scripts

Written by the Spike B research agent on 2026-11-22. Python stdlib; run with `python -I` from this folder, against `../../../upstream`. Each script finds the clone at `../../../upstream` relative to itself, or at `$UPSTREAM` if set.

No personal data: `collect.py` keeps authors only as short hashes (for distinct counts) and strips source branches from merge subjects.

Order: `collect.py` (writes `commits.json`, about 3 MB, not committed) → `areas.py` (area classifier, imported) → `top.py` / `renames_bulk.py` → `analyze.py` (`results.md`) → `analyze2.py` (`results2.md`) → `extra.py`, `extra2.py` → `conflict_proxy.py`.

Note: the WinUI-reference table in `results2.md` used an early regex that also matched the `Microsoft.UI.Reactor` namespace; use `extra_out.txt`.
