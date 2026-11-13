---
name: peressylkin
description: Free space on a drive by moving folders to another drive while keeping their old paths working (NTFS junction) — Peressylkin, "Ссылка в Сибирь". Use when the user wants to move AppData/saves/caches/program folders off C: (or any full drive) without breaking programs, to move such a folder back, to see what was moved, or to install/update/remove the tool.
---

# Peressylkin — move a folder, keep its path

Tool: `Peressylkin.exe` (installed copy: `%LOCALAPPDATA%\Sibir\Peressylkin.exe`; source: github.com/GS-Osterion/peressylkin).
Full reference: `AGENTS.md` in the repo, or `Peressylkin.exe schema` (all reasons, flags, exit codes).

**Always:** in PowerShell pipe the call — `& Peressylkin.exe ... | Out-String` (GUI-subsystem exe: without a pipe there is no wait, no output, no exit code). Git Bash waits normally.
With `--json`, stdout is one JSON row per path; stderr gets progress lines — for long moves redirect stderr to a file (`2> progress.log`) instead of reading it.
The tool never deletes data: the original is removed only after the copy is verified and the junction works.

## Move folders away (exile)

1. `check "<folder>" [...] [--to <root>] --json` — always first, changes nothing. `--to` defaults to the last used root or `<largest other fixed drive>:\Сибирь`.
2. Read every row. Only `verdict` `ok` / `warn` will move. Show the user what will move, its size and `target`; explain any `warn` (`registered_app`: a reinstall may recreate the folder; `removable_target`: unplugging the drive breaks programs).
3. `exile "<folder>" [...] [--to <root>] --yes --json`.
4. Report each row's `verdict` and `message` (Russian, for humans).

## When a check refuses (exit 2)

- `launcher` — Steam/Epic/GOG/etc. own this folder and can move it themselves. Suggest the launcher's own "move" first. Add `--ignore-launcher` only with the user's explicit consent.
- `capital` — the target is the system drive. `--allow-capital` only with explicit consent.
- `peressylkin_property` — the tool's case archive (`%LOCALAPPDATA%\Sibir`), a folder containing it (e.g. whole `AppData`), a Siberia root, or a folder holding exiled folders. Cannot be overridden: offer specific subfolders instead (e.g. `AppData\Local\<app>`).
- `protected` (Windows, Program Files, the profile itself…), `store_app`, `in_siberia`, `already_exiled`, `case_open` — not movable as given; explain `message`.
- `no_space`, `same_drive`, `network_target`, `regime_city`, `target_is_source` — choose another `--to`.
- `nested` — don't pass a folder together with its own subfolder.

## When a move stops

- Exit 3 (`locked`) — a program holds files (`processes` lists them). Ask the user to close it, then retry. Never kill processes yourself.
- Exit 4 (`copy_failed` / `verify_failed`) — rolled back, original untouched. Report and stop.
- Exit 5 (`needs_admin`) — hand the user the command from `message` to run elevated. Never elevate yourself.
- Exit 6 (`unfinished`) — an earlier operation was interrupted: `recover --yes --json` first, then repeat.

## Bring a folder back (rehab)

`rehab "<original path>" --yes --json` — moves it back and removes the junction. `not_exiled` = nothing to bring back; `no_space` = free space on the original drive first.

## See what was moved

`status "<folder>" --json` (one folder) · `list --json` (whole archive).

## Install, update, remove

- `install [--desktop] [--no-start-menu]` — copies itself to `%LOCALAPPDATA%\Sibir`, adds the Explorer menu items and a Start menu shortcut (`--desktop`: also on the desktop). Run the same command from a new version to update; a 1.0.0 `Sibir.exe` is replaced.
- `uninstall` — removes menu items and our shortcuts. The archive and exiled folders stay and keep working.
- `selftest [--to <root on another drive>]` — built-in test range; works only in its own test folders.
