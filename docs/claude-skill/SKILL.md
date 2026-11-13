---
name: peressylkin
description: Free space on a drive by moving folders to another drive while keeping their old paths working (NTFS junction) — Peressylkin, "Ссылка в Сибирь". Use when the user wants to move AppData/saves/caches/program folders off C: without breaking programs, or to move such a folder back.
---

# Peressylkin — move a folder, keep its path

Tool: `Peressylkin.exe` (installed copy: `%LOCALAPPDATA%\Sibir\Peressylkin.exe`; source: github.com/GS-Osterion/peressylkin). In PowerShell always append `| Out-String` (GUI exe: otherwise no wait, no output, no exit code). Full reference: `AGENTS.md` in the repo, or `Peressylkin.exe schema`.

1. `Peressylkin.exe check "<folder>" [...] --json` — always first; read each row's `verdict` and `reason`.
2. Show the user what will move (size, target). Never add `--ignore-launcher` (Steam/Epic/GOG can move games themselves) or `--allow-capital` without explicit consent.
3. `Peressylkin.exe exile "<folder>" [...] --yes --json` (optionally `--to <root>`).
4. Exit 3 → a program holds files (`processes`): ask the user to close it. Exit 5 → needs admin: hand the command to the user, do not elevate. Exit 6 → `Peressylkin.exe recover --yes` first.
5. Undo: `Peressylkin.exe rehab "<folder>" --yes --json`. Inspect: `status`, `list`.

Exit 2 with `reason: peressylkin_property` = the case archive or a folder containing it (e.g. whole `AppData`) — cannot be overridden; offer subfolders instead.

The tool never deletes data: the original is removed only after the copy is verified and the junction works.
