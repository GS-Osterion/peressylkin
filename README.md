# Peressylkin

*«Перессылкин» — «Ссылка в Сибирь» (Exile to Siberia). Интерфейс программы на русском; issues принимаются на русском и на английском.*

Frees up space on a full drive: moves a folder to another drive and leaves an NTFS junction
at the old path, so programs and games keep working and never notice. Everything is reversible
("rehabilitate"), and data is never deleted: the original is removed only after the copy is
verified and the junction works.

Windows 10/11. No dependencies, no administrator rights.

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
