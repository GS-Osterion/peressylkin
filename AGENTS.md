# Peressylkin («Перессылкин», «Ссылка в Сибирь») — for agents

Moves folders off a full drive to another drive and leaves an NTFS junction at the
old path, so programs keep working. Reversible ("rehab"). Never deletes user data.

## Commands (all non-interactive; add `--json`)

```
Peressylkin.exe check   <path>... [--to <root>]            # dry run, changes nothing
Peressylkin.exe exile   <path>... [--to <root>] --yes      # without --yes = check
          [--strict] [--ignore-launcher] [--by-name] [--no-stamp] [--allow-capital]
Peressylkin.exe rehab   <path>... --yes                    # move back, remove junction
Peressylkin.exe status  <path>...   |  list  |  recover [--yes]  |  schema  |  selftest [--to <root>]
Peressylkin.exe install [--desktop] [--no-start-menu]  |  uninstall
```

`--to` defaults to the last used root, or `<largest other fixed drive>:\Сибирь`.

## Safe order

1. `check ... --json` → read every row.
2. Only rows with `verdict` `ok`/`warn` will move. Ask the user before
   `--ignore-launcher` (Steam etc. can move games themselves) or `--allow-capital`.
3. `exile ... --yes --json`.

## Output

stdout: one JSON object per path, ASCII-only (non-ASCII escaped):
`path, verdict (ok|warn|refused|done|failed|unfinished|exiled), reason, message, target, bytes, processes`.
`reason` is a stable English code (full list: `Peressylkin.exe schema`); `message` is Russian for humans.
stderr (with `--json`): progress lines `{"progress":{"path","bytesDone","bytesTotal"}}`.

## Exit codes

0 ok · 1 bad args · 2 refused by checks · 3 a process holds files (see `processes`; ask the user to close it)
· 4 failed, rolled back (original untouched) · 5 needs administrator (also: a classified case — no details given) — never elevate yourself, give the
human the command from `message` · 6 unfinished cases — run `recover --yes` first.

## Notes

- It is a GUI-subsystem exe. In PowerShell ALWAYS pipe: `& Peressylkin.exe ... | Out-String` — without a pipe PowerShell does not wait, and output and `$LASTEXITCODE` are lost. Git Bash waits normally.
- `reason: peressylkin_property` (exit 2): the case archive (`%LOCALAPPDATA%\Sibir`), anything in it,
  any folder containing it (e.g. `AppData`, `AppData\Local`), or any folder with a plaque file
  (`АРХИВ/СИБИРЬ ТОВ. ПЕРЕССЫЛКИНА — НЕ ТРОГАТЬ.txt`). No flag overrides it; exile subfolders instead.
- Names: the program is Peressylkin; `%LOCALAPPDATA%\Sibir`, `X:\Сибирь`, registry keys `Sibir*`,
  namespace `Sibir` and `reason` codes keep the historical name on purpose (Comrade Peressylkin exiles to Siberia).
- Network drives are "abroad" (`network_source`, `network_target`): never exiled to or from, never rehabilitated.
- Data is never deleted: the original is removed only after the copy is verified and the junction works.
- Set up a skill for this program: `docs/claude-skill/SKILL.md` holds the command order for every case
  (exile, refusals, interruptions, rehab, install/update). Put it where your agent loads skills from.
