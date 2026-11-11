---
name: sibir
description: Free space on a drive by moving folders to another drive while keeping their old paths working (NTFS junction) — "Ссылка в Сибирь". Use when the user wants to move AppData/saves/caches/program folders off C: without breaking programs, or to move such a folder back.
---

# Sibir — move a folder, keep its path

Tool: `Sibir.exe` (installed copy: `%LOCALAPPDATA%\Sibir\Sibir.exe`; source: `K:\Project Sibir`). In PowerShell always append `| Out-String` (GUI exe: otherwise no wait, no output, no exit code). Full reference: `AGENTS.md` in the repo, or `Sibir.exe schema`.

1. `Sibir.exe check "<folder>" [...] --json` — always first; read each row's `verdict` and `reason`.
2. Show the user what will move (size, target). Never add `--ignore-launcher` (Steam/Epic/GOG can move games themselves) or `--allow-capital` without explicit consent.
3. `Sibir.exe exile "<folder>" [...] --yes --json` (optionally `--to <root>`).
4. Exit 3 → a program holds files (`processes`): ask the user to close it. Exit 5 → needs admin: hand the command to the user, do not elevate. Exit 6 → `Sibir.exe recover --yes` first.
5. Undo: `Sibir.exe rehab "<folder>" --yes --json`. Inspect: `status`, `list`.

The tool never deletes data: the original is removed only after the copy is verified and the junction works.
