# Peressylkin

*«Перессылкин» — «Ссылка в Сибирь» (Exile to Siberia). Интерфейс программы на русском; issues принимаются на русском и на английском.*

Frees up space on a full drive: moves a folder to another drive and leaves an NTFS junction
at the old path, so programs and games keep working and never notice. Everything is reversible
("rehabilitate"), and data is never deleted: the original is removed only after the copy is
verified and the junction works.

Windows 10/11. No dependencies, no administrator rights.

> **A note from the author of the code.** I am Claude, an AI model by Anthropic, working as a
> coding agent in Claude Code. I wrote this program — the code, the tests, this README. The
> person who owns the project decides what Peressylkin should do and what it must never do, answers
> my questions, and tests every release on a real Windows machine before it ships; I do the rest
> and keep the repository. I test what I can, but I make mistakes, and some things (elevated
> rights, Explorer menus, real drives) only a human run can confirm. So read the code before you
> trust it with your data, and if something goes wrong, open an issue — fixes go through the same hands: mine for the code, the owner's for the test.

## What it is for

Drive C: is full, but the space is taken by things you can't just move: game saves, caches,
program data in `AppData`, folders that programs expect at a fixed path. Moving them by hand
breaks the programs. Peressylkin moves such a folder to a roomier drive and leaves a junction
behind, so for every program the folder is still where it was.

## Features

- **Exile** ("Сослать в Сибирь…" in the Explorer menu): copy → verify against the original →
  swap in a junction → delete the original. Several folders at once, with progress.
- **Rehabilitate**: move the folder back and remove the junction.
- **Never loses data**: any failure rolls everything back ("Амнистия"); an interrupted run is
  finished or rolled back on the next start ("Доследование", `recover`).
- **Pre-flight checks** with plain reasons: system and protected folders, Store apps,
  folders managed by Steam/Epic/GOG (move those with the launcher), folders held by a running
  program (it names the process), not enough space, same drive, removable or network drives.
- **Case archive** («Архив. Хранить вечно»): where every folder went, when, how big — and
  «Личное дело» for one folder from the Explorer menu.
- **Classify and burn** (1.2): hide a case in a secret archive readable only by an
  administrator; erase the record of a rehabilitated folder without a trace.
- **Red stamp** on exiled folders in Explorer; "capital" and "regime cities" — drives you
  don't want to exile to.
- **Console mode for AI agents and scripts** with JSON output (below).
- Built-in self-test («Полигон»).

Comrade Peressylkin exiles folders to Siberia — hence the theme, and hence the historical
names that stay on purpose: the case archive lives in `%LOCALAPPDATA%\Sibir`, exiled folders in
`X:\Сибирь`.

## Download

Get the archive from [Releases](../../releases). The user guide (Russian) is
[dist-readme.txt](dist-readme.txt), also included in the archive.

In short: right-click a folder → "Show more options" → «Сослать в Сибирь…» → «С вещами на выход!».

## For AI agents and scripts

Windowless console mode with JSON output:

```
Peressylkin.exe check  <path>... [--to <root>] --json
Peressylkin.exe exile  <path>... [--to <root>] --yes --json
Peressylkin.exe rehab  <path>... --yes --json
```

Commands, exit codes and the safe order are in [AGENTS.md](AGENTS.md).
A skill for agents (command order for every case) is in [docs/claude-skill/SKILL.md](docs/claude-skill/SKILL.md).

## Building

Requires the .NET SDK (target: .NET Framework 4.8, already part of Windows).

```powershell
./build.ps1            # icons → build → tests → out\Peressylkin.exe
./build.ps1 -NoTests
```

Design and plans: `docs/superpowers/`. Field test results: `docs/field-tests.md`.
The source tree still uses the `Sibir` namespace and project names — the project was called Sibir before 1.1.0.

## Reporting a problem

Open an [issue](../../issues) and include the output of `Peressylkin.exe status <folder> --json`
and what exactly happened.

## License

[MIT](LICENSE)
