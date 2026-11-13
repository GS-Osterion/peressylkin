# Peressylkin Rename Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Visible name becomes Peressylkin (exe, CLI, docs, repo, release); hidden names stay; the case archive and every Siberia root get a plaque and cannot be exiled.

**Architecture:** New `Core/Plaque.cs` owns plaque file names, text and detection. `Checks.Run` gets one new refusal (`peressylkin_property`) driven by `CheckOptions.ArchiveDir` and plaque detection. Exile entry points (CLI, GUI) and install call `Plaque.Ensure*` before work. Exe rename is `AssemblyName` + `ShellIntegration.InstalledExe` + legacy-exe cleanup.

**Tech Stack:** C# / .NET Framework 4.8, WinForms, custom MiniTest runner (`tests/Sibir.Tests`, `[Fact]`, `Assert.*`), `build.ps1`.

**Spec:** `docs/superpowers/specs/2026-11-13-peressylkin-rename-design.md`

## Global Constraints

- Hidden names unchanged: `%LOCALAPPDATA%\Sibir`, registry keys `SibirExile/SibirRehab/SibirCase`, namespace `Sibir`, projects `src/Sibir`, `tests/Sibir.Tests`, destination folder `Сибирь`, all existing `reason` codes.
- Visible name: `Peressylkin` (Latin), «Перессылкин» (Russian). `Strings.AppName` stays «Ссылка в Сибирь».
- No bypass flag for `peressylkin_property` in this release (Большой Террор is a later task).
- Version `1.1.0`.
- Spec correction: «Сняться с учёта» keeps today's behaviour (removes menu items only, files and archive stay); the legacy `Sibir.exe` is removed on install, not uninstall.

## Review Focus

1. Source path given with trailing slash / different case / `\\?\` prefix for the archive dir — must still refuse (use `LongPath.Same/SameOrUnder`, which already normalise).
2. `AppData\Local\<sibling>` (e.g. `papja`) — must stay allowed; a test pins this.
3. Plaque file deleted by the user — next exile recreates it; plaque write failure (read-only root) must not fail the exile.
4. Upgrade over 1.0.0 while the old `Sibir.exe` is running — install must still succeed; deletion is best-effort.
5. Exiling a Siberia root on another drive than the current `--to` (plaque present, not in archive) — refused by plaque detection.

---

### Task 1: Plaque + `peressylkin_property` check

**Files:**
- Create: `src/Sibir/Core/Plaque.cs`
- Modify: `src/Sibir/Core/Checks.cs` (CheckOptions, `Run` after the `IsProtected` line ~80), `src/Sibir/Strings.cs`, `src/Sibir/Cli/Cli.cs:46` (schema reason list)
- Test: `tests/Sibir.Tests/ChecksTests.cs`, new `tests/Sibir.Tests/PlaqueTests.cs`

**Interfaces:**
- Produces:
  - `static class Plaque` in `Sibir.Core`:
    - `const string ArchiveFile = "АРХИВ ТОВ. ПЕРЕССЫЛКИНА — НЕ ТРОГАТЬ.txt"`
    - `const string SiberiaFile = "СИБИРЬ ТОВ. ПЕРЕССЫЛКИНА — НЕ ТРОГАТЬ.txt"`
    - `static string DefaultArchiveDir` = `%LOCALAPPDATA%\Sibir`
    - `static bool IsMarked(string dir)` — true if either file exists directly in `dir` (no subtree walk)
    - `static void EnsureArchive(string dir)`, `static void EnsureSiberia(string root)` — create dir if missing, write plaque (UTF-8 with BOM) only if absent; swallow IO/UnauthorizedAccess exceptions.
  - `CheckOptions.ArchiveDir` (string, default `Plaque.DefaultArchiveDir`)
  - `Strings.Property = "Имущество тов. Перессылкина. Ссылке не подлежит"`

Plaque texts (exact copy, Russian, plain text):
- Archive: «Здесь архив дел тов. Перессылкина (программа «Ссылка в Сибирь», github.com/GS-Osterion/peressylkin). По нему реабилитируют сосланные папки: без него их не вернуть. Имя папки «Sibir» — историческое. Не удалять, не переносить, не ссылать.»
- Siberia: «Здесь отбывают ссылку папки, сосланные тов. Перессылкиным (программа «Ссылка в Сибирь», github.com/GS-Osterion/peressylkin). На них ведут ссылки со старых мест. Вручную не удалять и не переименовывать — вернуть через «Реабилитировать».»

- [ ] **Step 1: Write failing tests** in `ChecksTests` (archive dir = `_t.Sub(@"c\Users\u\AppData\Local\Sibir")`, passed via `new CheckOptions { ArchiveDir = ... }`):
  - `Property_ArchiveItself` → `Is(..., Verdict.Refused, "peressylkin_property")`
  - `Property_InsideArchive` (`...\Sibir\sub`) → refused `peressylkin_property`
  - `Property_AncestorOfArchive` (`...\AppData\Local` and `...\AppData`) → refused `peressylkin_property`
  - `Property_SiblingAllowed` (`...\AppData\Local\papja`) → `Verdict.Ok`, `"ok"`
  - `Property_PlaqueAnywhere` (folder on `d` containing `Plaque.SiberiaFile`) → refused `peressylkin_property`
  - `Property_ArchivePathVariants` (archive path with trailing `\` and upper-cased) → refused
  
  In `PlaqueTests`: `Ensure_CreatesOnce_KeepsUserEdits` (write, overwrite content, Ensure again → content unchanged), `Ensure_RecreatesAfterDelete`, `Ensure_ReadOnlyDir_DoesNotThrow`, `IsMarked_OnlyDirectChild` (plaque in subfolder → false).
- [ ] **Step 2:** `./build.ps1 -Filter Property,Plaque` → build fails / tests FAIL.
- [ ] **Step 3:** Implement `Plaque`; in `Checks.Run` right after the `protected` refusal: refuse `peressylkin_property` when `LongPath.SameOrUnder(src, archiveDir) || LongPath.SameOrUnder(archiveDir, src) || Plaque.IsMarked(src)`. Add `"peressylkin_property"` to the schema reason list in `Cli.cs:46`.
- [ ] **Step 4:** `./build.ps1` → all tests pass (`Passed!`).
- [ ] **Step 5:** Commit `feat: archive and Siberia plaques, peressylkin_property refusal`.

### Task 2: Plaques are placed

**Files:**
- Modify: `src/Sibir/Cli/Cli.cs` (exile branch before the `foreach` at ~133 when `doIt`; `install` case ~94), `src/Sibir/Gui/ExileForm.cs:~136`, `src/Sibir/Gui/MainForm.cs` (`ToggleRegistration` install branch)
- Test: `tests/Sibir.Tests/CliTests.cs`

**Interfaces:**
- Consumes: `Plaque.EnsureArchive(string)`, `Plaque.EnsureSiberia(string)`, `Plaque.DefaultArchiveDir` (Task 1). CLI uses the archive dir from `ctx` (directory of `ctx.Archive` path) so tests stay in temp dirs.

- [ ] **Step 1: Failing test** `Exile_PlacesBothPlaques` in `CliTests`: after a successful `exile --yes` into a temp root, `File.Exists(Path.Combine(root, Plaque.SiberiaFile))` and the archive plaque exists in the test archive's directory. `Check_PlacesNothing`: plain `check` creates no plaque (check is a dry run).
- [ ] **Step 2:** `./build.ps1 -Filter Plaque` → FAIL.
- [ ] **Step 3:** Call `EnsureArchive` + `EnsureSiberia(root)` once before exiling (CLI when `doIt`, GUI before `ProgressForm`); `EnsureArchive(ShellIntegration.InstallDir)` in both install paths.
- [ ] **Step 4:** `./build.ps1` → pass.
- [ ] **Step 5:** Commit `feat: place plaques on install and exile`.

### Task 3: Exe becomes Peressylkin.exe

**Files:**
- Modify: `src/Sibir/Sibir.csproj` (`AssemblyName` → `Peressylkin`, `Version` → `1.1.0`, `Product` → `Перессылкин — Ссылка в Сибирь`), `src/Sibir/Shell/ShellIntegration.cs:18,49-61`, `src/Sibir/Cli/Cli.cs:281,292,304` (tool line, install help, help header → `Peressylkin.exe — Ссылка в Сибирь`; all `Sibir.exe` in help → `Peressylkin.exe`), `build.ps1:16,18`, any test that references the built exe name.
- Test: `tests/Sibir.Tests/ShellTests.cs`

**Interfaces:**
- Produces: `ShellIntegration.InstalledExe` = `%LOCALAPPDATA%\Sibir\Peressylkin.exe`; `const string LegacyExe = "Sibir.exe"`; `InstallFiles(string fromExe, string installDir = null)` — optional dir parameter for tests, defaulting to `InstallDir`.

- [ ] **Step 1: Failing test** `InstallFiles_RemovesLegacyExe`: temp install dir containing `Sibir.exe`; `InstallFiles(<temp copy of some exe>, dir)` → returns `dir\Peressylkin.exe`, `Sibir.exe` gone. `InstallFiles_LegacyLocked_StillSucceeds`: hold `Sibir.exe` open with `FileShare.None` → no throw, returns new path.
- [ ] **Step 2:** `./build.ps1 -Filter InstallFiles` → FAIL.
- [ ] **Step 3:** Implement; legacy delete after copying the new exe, wrapped in try/catch IO/UnauthorizedAccess. `rg -n "Sibir\.exe" src tests build.ps1` → only `LegacyExe` and history comments remain.
- [ ] **Step 4:** `./build.ps1` → pass, prints `OK: out\Peressylkin.exe`.
- [ ] **Step 5:** Commit `feat: exe renamed to Peressylkin.exe, legacy Sibir.exe cleaned on install`.

### Task 4: Docs and repository

**Files:**
- Modify: `README.md`, `dist-readme.txt`, `AGENTS.md`, `docs/claude-skill/SKILL.md` (`name: peressylkin`, paths `%LOCALAPPDATA%\Sibir\Peressylkin.exe`), spec (uninstall correction).

- [ ] **Step 1:** Replace `Sibir.exe` → `Peressylkin.exe` and project name in all four; README in English with a short Russian line; add to README and AGENTS.md a "Names" note: hidden names `Sibir`/`Сибирь` are historical by design, list them; document `peressylkin_property` in AGENTS.md (exit 2, cannot be overridden by an agent).
- [ ] **Step 2:** `rg -n "Sibir\.exe|name: sibir" README.md dist-readme.txt AGENTS.md docs/claude-skill` → no hits.
- [ ] **Step 3:** `gh repo rename peressylkin -R GS-Osterion/sibir --yes`; `git remote set-url origin https://github.com/GS-Osterion/peressylkin.git`; `git fetch origin` succeeds; update repo description.
- [ ] **Step 4:** Commit `docs: Peressylkin name, historical hidden names`, fast-forward `main`, push both branches.

### Task 5: Release 1.1.0

- [ ] **Step 1:** `./build.ps1` → `Passed!`, `OK: out\Peressylkin.exe`. Run `out\Peressylkin.exe selftest --json | Out-String` → exit 0.
- [ ] **Step 2:** Assemble `dist\Peressylkin\` (Peressylkin.exe, sibir.ico, stamp.ico, AGENTS.md, `claude-skill\SKILL.md`, `ПРОЧТИ.txt` = dist-readme.txt) and zip to `dist\Peressylkin-1.1.0.zip`.
- [ ] **Step 3:** Ask the user to install over their 1.0.0 (`Peressylkin.exe` → «Встать на учёт») and confirm: menu items work, old `Sibir.exe` gone from `%LOCALAPPDATA%\Sibir`, both plaques present after one exile, `Личное дело` shows old cases.
- [ ] **Step 4:** Tag `v1.1.0`, `gh release create v1.1.0 dist\Peressylkin-1.1.0.zip` with notes (rename, plaques, protection, unsigned-exe SmartScreen note).
