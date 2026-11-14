# Secret Archive & Burn Case Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** «Засекретить» moves a case into an admin-only secret archive (append-only for normal processes), «Сжечь дело» erases a rehabilitated case, network paths are refused everywhere as «заграница».

**Architecture:** `Archive` gains a cross-process mutex and in-place removal; `SecretArchive : Archive` owns the ACL'd `.secret.jsonl` (append via raw `CreateFile`/`WriteFile` when not elevated). `SecretMark` puts empty NTFS streams on secret targets and Siberia roots so normal-process `Checks` still refuse them. `Secrecy` implements classify / declassify / burn; `Mover` routes records of secret cases to the secret archive. GUI gets a row context menu and an admin-only `SecretForm`; CLI only hides secret cases behind `needs_admin`.

**Tech Stack:** C# / .NET Framework 4.8, WinForms, P/Invoke kernel32/shell32, custom MiniTest runner (`tests/Sibir.Tests`, `[Fact]`, `Assert.*`, `Assert.Skip`), `./build.ps1 [-Filter regex]`.

**Spec:** `docs/superpowers/specs/2026-11-14-secret-archive-design.md`

## Global Constraints

- Secret archive: `%LOCALAPPDATA%\Sibir\.secret.jsonl`, attribute Hidden, same JSONL format/semantics as `archive.jsonl`.
- Secret ACL (protected, no inheritance): `BUILTIN\Administrators` FullControl; `SYSTEM` FullControl; current user `AppendData | Synchronize`; `OWNER RIGHTS` (S-1-3-4) `Synchronize`.
- Stream name: `Peressylkin.secret` (empty), on the secret target folder and on its Siberia root.
- Network texts: `Strings.Abroad = "За границу не ссылают — высылают. А высланных не реабилитируют"` replaces `Strings.NetworkTarget`. Codes: `network_target` (kept), `network_source` (new).
- Classify warning (once, flag in settings): «Засекреченные дела выдаются к просмотру только по санкции прокурора».
- Classified link without rights: «Дело засекречено. Реабилитация — по санкции прокурора» (`Strings.Classified`); CLI exit 5, `reason: needs_admin`, no target/bytes.
- Broken safe: «Сейф вскрыт: секретный архив кто-то подменил. Засекречивание отменено» (`Strings.SafeBroken`).
- Burn: only status `rehabilitated` in this release. No backup, no act.
- Never delete user data. No CLI commands for classify/burn/secret list.
- Version `1.2.0`.

## Review Focus

1. A normal process exiles/rehabs while another rewrites the archive (classify/burn) — no line lost. Test in Task 1.
2. `.secret.jsonl` replaced by a readable file — classify refuses, writes nothing. Test in Task 3.
3. Source equal to / under a secret target, or containing a secret Siberia root, while the public archive has no such case — refused. Tests in Task 4.
4. Classify interrupted after the secret line is written but before public lines are removed — repeating it is idempotent, admin view shows the case once. Test in Task 6.
5. Classified link via Explorer menu / CLI `status` / `rehab` without rights — no target leaked, exit 5. Test in Task 8.

---

### Task 1: Archive lock, delete-share, in-place removal

**Files:**
- Create: `src/Sibir/Core/ArchiveLock.cs`
- Modify: `src/Sibir/Core/Archive.cs`
- Test: `tests/Sibir.Tests/ArchiveTests.cs`

**Interfaces:**
- Produces:
  - `static class ArchiveLock { static IDisposable Take(); }` — named mutex `Local\Sibir.Archive`, created with `MutexSecurity` granting the current user SID `Synchronize | Modify` (an elevated creator's default DACL would lock out normal processes); `AbandonedMutexException` counts as acquired.
  - `Archive` no longer `sealed`; `public virtual void Write(ArchiveRecord r)` (body under `ArchiveLock.Take()`); `public virtual IReadOnlyList<ArchiveRecord> Current()`; `public void Remove(ISet<string> ids)` — under the lock, read raw lines, keep lines whose Id is not in `ids` (unparsable lines kept), write to `<path>.~tmp` in the same folder, `File.Replace(tmp, path, null)`; no backup. Missing file → no-op.
  - All `FileStream`s in `Archive` add `FileShare.Delete` so `File.Replace` succeeds while a reader is open.
  - `ArchiveRecord.StampRemoved` (`bool`).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Archive_Remove_DropsAllLinesOfCase()
{   // write id1 in_transit, id1 exiled, id2 exiled; Remove({"id1"})
    Assert.Equal(1, a.Current().Count); Assert.Equal("id2", a.Current()[0].Id);
    Assert.False(File.Exists(path + ".~tmp"));
    Assert.Equal(0, Directory.GetFiles(dir).Count(f => f.EndsWith(".bak")));
}
[Fact] public void Archive_Remove_KeepsLinesWrittenConcurrently()
{   // 4 threads × 50 Write of fresh ids while the main thread calls Remove({"victim"}) 20 times
    Assert.Equal(200, a.Current().Count);
}
[Fact] public void Archive_Remove_WhileReaderOpen()
{   // open path with FileShare.ReadWrite|Delete, Remove succeeds
}
[Fact] public void Archive_StampRemoved_RoundTrips() { /* write StampRemoved=true, read back true */ }
```

- [ ] **Step 2: Run** `./build.ps1 -Filter ArchiveTests` — Expected: build error (`Remove`, `StampRemoved` missing).
- [ ] **Step 3: Implement** `ArchiveLock`, `Archive.Remove`, `virtual`, `FileShare.Delete`, `StampRemoved`.
- [ ] **Step 4: Run** `./build.ps1 -Filter ArchiveTests` — Expected: `Passed!`, then `./build.ps1` — all pass.
- [ ] **Step 5: Commit** `feat: archive lock and in-place case removal`

---

### Task 2: Заграница (network paths refused everywhere)

**Files:**
- Create: `src/Sibir/Core/Abroad.cs`
- Modify: `src/Sibir/Strings.cs`, `src/Sibir/Core/Checks.cs:93`, `src/Sibir/Cli/Cli.cs` (Reasons, `Rehab`, `Status`), `src/Sibir/Gui/GuiApp.cs` (`Rehab`, `ShowCase`)
- Test: `tests/Sibir.Tests/ChecksTests.cs`, `tests/Sibir.Tests/CliTests.cs`, `tests/Sibir.Tests/StringsTests.cs`

**Interfaces:**
- Produces: `static class Abroad { static string ReasonFor(string path, IDriveEnv env); }` — `"network_source"` if `env.IsNetworkPath(path)`; `"network_target"` if `path` is a reparse point whose `Junction.GetTarget` is a network path; else `null`.
- `Strings.Abroad` (text in Global Constraints); `Strings.NetworkTarget` removed, all uses → `Strings.Abroad`.
- `Checks.Run`: before `nested`, `if (env.IsNetworkPath(src)) Refuse("network_source", Strings.Abroad)`; root check keeps `network_target` with `Strings.Abroad`.
- CLI `rehab`/`status` and GUI menu rehab/case: `Abroad.ReasonFor` first → refused, exit 2, message `Strings.Abroad`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void NetworkSource() => Is(One(Src(@"n\A")), Verdict.Refused, "network_source");   // FakeEnv network volume
[Fact] public void NetworkTarget_AbroadText() { /* existing NetworkTarget case: Message == Strings.Abroad */ }
// CliTests: rehab/status on a network path → exit 2, reason network_source
// CliTests.Schema reason list contains "network_source"
```

- [ ] **Step 2: Run** `./build.ps1 -Filter "ChecksTests|CliTests"` — Expected: FAIL (`network_source` missing).
- [ ] **Step 3: Implement** as in Interfaces; add `"network_source"` to `Cli.Reasons` and to the list in `CliTests` line ~176.
- [ ] **Step 4: Run** `./build.ps1` — Expected: `Passed!`.
- [ ] **Step 5: Commit** `feat: network paths are abroad — refused everywhere`

---

### Task 3: SecretArchive (ACL, append-only, compromise check)

**Files:**
- Create: `src/Sibir/Core/SecretArchive.cs`
- Modify: `src/Sibir/Native/NativeMethods.cs` (`WriteFile`, `CreateFile` overload taking `ref SECURITY_ATTRIBUTES`), `tests/Sibir.Tests/TempDir.cs` (attribute reset best-effort: wrap `f.Attributes = Normal` in try, then `File.Delete`)
- Test: `tests/Sibir.Tests/SecretArchiveTests.cs`

**Interfaces:**
- Consumes: `Archive` (virtual `Write`/`Current`, `Remove`, `ArchiveLock`) from Task 1.
- Produces: `sealed class SecretArchive : Archive`
  - `static string DefaultPath` → `%LOCALAPPDATA%\Sibir\.secret.jsonl`
  - `static bool Elevated` (via `Elevation.IsAdmin()`; settable `internal` for tests: `ForceElevated`)
  - `override void Write(r)`: under `ArchiveLock`. Elevated → `EnsureAcl()` then plain append. Not elevated → `CreateFile(path, FILE_APPEND_DATA|SYNCHRONIZE, FILE_SHARE_READ|WRITE|DELETE, sa with the secret ACL, OPEN_ALWAYS, FILE_ATTRIBUTE_HIDDEN)` and one `WriteFile` of `"\n" + json + "\n"` (cannot read the last byte).
  - `bool IsCompromised()` — file exists and `CreateFile` with `FILE_READ_DATA` or with `READ_CONTROL` succeeds → true. Elevated → always false (it repairs instead).
  - `void EnsureAcl()` — elevated only: if the file's DACL differs from the secret ACL, set it (protected).
  - `override Current()` / `Remove` — elevated only; non-elevated `Current()` throws `UnauthorizedAccessException`. Duplicate Ids already collapse (last wins).
  - `static FileSecurity SecretAcl()` — the four rules from Global Constraints.

- [ ] **Step 1: Write the failing tests** (run non-elevated; elevated ones `Assert.Skip` unless `Elevation.IsAdmin()`)

```csharp
[Fact] public void NotElevated_AppendCreatesUnreadableFile()
{   s.Write(rec); s.Write(rec2);                 // second append on existing file works
    Assert.Throws<UnauthorizedAccessException>(() => File.ReadAllText(path));
    Assert.True((File.GetAttributes(path) & FileAttributes.Hidden) != 0);
    Assert.False(s.IsCompromised());
}
[Fact] public void PlainFileInPlace_IsCompromised() { File.WriteAllText(path, ""); Assert.True(s.IsCompromised()); }
[Fact] public void NotElevated_CurrentThrows() { Assert.Throws<UnauthorizedAccessException>(() => s.Current()); }
[Fact] public void Elevated_ReadsBothLinesAndDuplicatesCollapse() { /* skip unless admin; 2 writes same Id → Current().Count == 1 */ }
[Fact] public void Elevated_EnsureAclRepairsReadableFile() { /* skip unless admin; plain file → EnsureAcl → normal-rights read check via ACL rules == SecretAcl */ }
```

- [ ] **Step 2: Run** `./build.ps1 -Filter SecretArchiveTests` — Expected: build error.
- [ ] **Step 3: Implement** `SecretArchive`, native additions; the security descriptor for `CreateFile` comes from `SecretAcl().GetSecurityDescriptorBinaryForm()` pinned into `SECURITY_ATTRIBUTES`.
- [ ] **Step 4: Run** `./build.ps1 -Filter SecretArchiveTests` non-elevated — Expected: `Passed!` with elevated tests SKIP; then once from an elevated PowerShell — Expected: all PASS.
- [ ] **Step 5: Commit** `feat: secret archive with admin-only read`

---

### Task 4: SecretMark + Checks see secret places

**Files:**
- Create: `src/Sibir/Core/SecretMark.cs`
- Modify: `src/Sibir/Native/NativeMethods.cs` (`GetFileAttributes`, `DeleteFile`, `GetVolumeInformation`), `src/Sibir/Core/Checks.cs`, `src/Sibir/Core/Settings.cs`, `src/Sibir/Cli/Cli.cs` + `src/Sibir/Gui/ExileForm.cs` (where `LastRoot` is saved)
- Test: `tests/Sibir.Tests/SecretMarkTests.cs`, `tests/Sibir.Tests/ChecksTests.cs`

**Interfaces:**
- Produces: `static class SecretMark`
  - `const string Stream = "Peressylkin.secret"`; path = `LongPath.Ex(dir) + ":" + Stream`, accessed only through P/Invoke (`CreateFile` CREATE_ALWAYS / `GetFileAttributes` / `DeleteFile`).
  - `bool SupportsStreams(string dir)` — `GetVolumeInformation` flag `FILE_NAMED_STREAMS` (0x40000) on the volume root of `dir`.
  - `void Set(string dir)`, `bool Has(string dir)`, `void Clear(string dir)` (missing → no-op).
  - `string RootOf(string target, IEnumerable<string> knownRoots)` — longest known root that `target` is under; else nearest ancestor holding `Plaque.SiberiaFile`; else `Path.GetDirectoryName(target)`.
- `Settings.SiberiaRoots` (`List<string>`, normalized, distinct) — appended wherever `LastRoot` is saved after a successful exile; `Load` null-guards it like `RegimeDrives`.
- `Checks.Run` additions (after `in_siberia` for open cases):
  - `src` or any ancestor `SecretMark.Has` → `in_siberia`, `Strings.InSiberia`.
  - any root in `settings.SiberiaRoots ∪ {rootN}` with `LongPath.SameOrUnder(r, src) && SecretMark.Has(r)` → `peressylkin_property`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Mark_SetHasClear_ContentUntouched() { /* files list of dir identical before/after Set; Has true; Clear; Has false */ }
[Fact] public void SupportsStreams_TempIsNtfs() => Assert.True(SecretMark.SupportsStreams(_t.Path));
[Fact] public void RootOf_PrefersKnownRoot() { /* RootOf(@"E:\Data\Сибирь\C\X", {@"E:\Data\Сибирь"}) == @"E:\Data\Сибирь" */ }
// ChecksTests (real dirs under TempDir, empty public archive):
[Fact] public void SecretTarget_InSiberia()      { SecretMark.Set(tgt); Is(One(tgt), Verdict.Refused, "in_siberia"); }
[Fact] public void UnderSecretTarget_InSiberia() { SecretMark.Set(tgt); Is(One(Path.Combine(tgt, "sub")), Verdict.Refused, "in_siberia"); }
[Fact] public void ContainsSecretRoot_Property()  { settings.SiberiaRoots = {root}; SecretMark.Set(root); Is(One(parentOfRoot), Verdict.Refused, "peressylkin_property"); }
```

- [ ] **Step 2: Run** `./build.ps1 -Filter "SecretMarkTests|ChecksTests"` — Expected: build error.
- [ ] **Step 3: Implement** `SecretMark`, `Settings.SiberiaRoots`, checks, root recording in CLI and GUI exile.
- [ ] **Step 4: Run** `./build.ps1` — Expected: `Passed!`.
- [ ] **Step 5: Commit** `feat: secret marks keep secret places out of exile`

---

### Task 5: Stamp removal on the link

**Files:**
- Modify: `src/Sibir/Core/Stamp.cs`, `src/Sibir/Native/NativeMethods.cs` (`SHChangeNotify`)
- Test: `tests/Sibir.Tests/MoverTests.cs` (or new `StampTests.cs`)

**Interfaces:**
- Produces: `static void Stamp.Unstamp(string link, string targetDir)` — `Remove(targetDir)`, clear `FileAttributes.System` on the link, `SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, link)`; each part no-op when already absent.

- [ ] **Step 1: Write the failing test** `Unstamp_RemovesIniAndSystemAttr` — after `Stamp.Apply` on a real junction: `!File.Exists(ini)`, `(attrs(link) & System) == 0`, link still a reparse point to target.
- [ ] **Step 2: Run** `./build.ps1 -Filter Unstamp` — Expected: build error.
- [ ] **Step 3: Implement** `Unstamp`.
- [ ] **Step 4: Run** `./build.ps1 -Filter Unstamp` — Expected: PASS.
- [ ] **Step 5: Commit** `feat: unstamp a link`

---

### Task 6: Secrecy — classify, declassify, burn

**Files:**
- Create: `src/Sibir/Core/Secrecy.cs`
- Modify: `src/Sibir/Strings.cs`
- Test: `tests/Sibir.Tests/SecrecyTests.cs`

**Interfaces:**
- Consumes: `Archive.Remove`, `SecretArchive` (Task 3), `SecretMark` (Task 4), `Stamp.Unstamp` (Task 5), `Stamp.Apply`.
- Produces: `sealed class Secrecy(Archive pub, SecretArchive secret, IEnumerable<string> knownRoots, string stampIconPath)`, results `MoveResult` (`Ok`, `Reason`, `Message`):
  - `Classify(ArchiveRecord rec)`. Allowed statuses `exiled`, `rehabilitated`, else `Reason "not_allowed"`. Order:
    1. `secret.IsCompromised()` → `"safe_broken"`, `Strings.SafeBroken`, nothing written.
    2. `exiled` and `!SecretMark.SupportsStreams(rec.Target)` → `"no_streams"`, `Strings.NoStreams` = «Этот диск не умеет хранить секреты (не NTFS): засекретить нельзя».
    3. `exiled` and `rec.StampCreated` → `Stamp.Unstamp(rec.Source, rec.Target)`.
    4. `secret.Write(copy)` with `StampCreated=false`, `StampRemoved = rec.StampCreated || rec.StampRemoved`.
    5. `exiled` → `SecretMark.Set(rec.Target)`, `SecretMark.Set(SecretMark.RootOf(rec.Target, knownRoots))`.
    6. `pub.Remove({rec.Id})`.
  - `Declassify(ArchiveRecord rec)` (elevated): `pub.Write(copy)` (with `StampCreated` = result of `Stamp.Apply` when `rec.StampRemoved` and status `exiled`, `StampRemoved=false`), `secret.Remove({rec.Id})`, `SecretMark.Clear(rec.Target)`, `RefreshRoot(root)`.
  - `Burn(ArchiveRecord rec, Archive where)` — only `rehabilitated`, else `"not_allowed"`; `where.Remove({rec.Id})`.
  - `void RefreshRoot(string root)` (elevated): clear the root mark if no `exiled` record in `secret.Current()` has its target under `root`.
  - `static bool IsClassifiedLink(string path, Archive pub)` — reparse point, `pub.FindExiled(path) == null`, `SecretMark.Has(Junction.GetTarget(path))`.
- `Strings.Classified`, `Strings.SafeBroken`, `Strings.NoStreams`, `Strings.ClassifyWarning`, `Strings.BurnConfirm` = «Сжечь дело {0}? Восстановить его будет нельзя».

- [ ] **Step 1: Write the failing tests** (real junction from `MoverTests` setup; non-elevated)

```csharp
[Fact] public void Classify_Exiled_GoneFromPublic_MarkSet_StampOff()
{   Assert.Empty(pub.Current()); Assert.True(SecretMark.Has(tgt)); Assert.True(SecretMark.Has(root));
    Assert.False(File.Exists(Path.Combine(tgt, "desktop.ini"))); Assert.True(Junction.IsReparsePoint(src));
    Assert.True(Secrecy.IsClassifiedLink(src, pub));
}
[Fact] public void Classify_Rehabilitated_NoMark() { Assert.Empty(pub.Current()); Assert.False(SecretMark.Has(tgt)); }
[Fact] public void Classify_Compromised_WritesNothing() { File.WriteAllText(secretPath, ""); r.Reason == "safe_broken"; pub.Current().Count == 1; new FileInfo(secretPath).Length == 0; }
[Fact] public void Classify_Twice_Idempotent() { /* second call after a simulated crash before step 6 (call secret.Write manually) → Ok, pub empty */ }
[Fact] public void Classify_InTransit_NotAllowed() { }
[Fact] public void Burn_Exiled_NotAllowed() { r.Reason == "not_allowed"; pub.Current().Count == 1; }
[Fact] public void Burn_Rehabilitated_NoTrace() { Assert.Empty(pub.Current()); Assert.Empty(Directory.GetFiles(dir, "*.bak")); }
[Fact] public void Declassify_RestoresStampAndPublicRecord() { /* skip unless admin */ }
```

- [ ] **Step 2: Run** `./build.ps1 -Filter SecrecyTests` — Expected: build error.
- [ ] **Step 3: Implement** `Secrecy` and strings.
- [ ] **Step 4: Run** `./build.ps1` — Expected: `Passed!` (admin tests SKIP).
- [ ] **Step 5: Commit** `feat: classify, declassify and burn cases`

---

### Task 7: Mover routes secret cases

**Files:**
- Modify: `src/Sibir/Core/Mover.cs`
- Test: `tests/Sibir.Tests/MoverTests.cs`

**Interfaces:**
- Consumes: `SecretArchive`, `SecretMark`, `Secrecy.RefreshRoot`.
- Produces: `Mover(Archive archive, MoverOptions opt, IFaultHook hook = null, SecretArchive secret = null)` — `secret` passed only by elevated callers.
  - `Rehab`: `FindExiled` in `archive`, then `secret`; every `Write` of that case goes to the archive it came from (private `Archive HomeOf(ArchiveRecord)`), as do `RestoreExiled`'s writes. On success of a secret case: `new Secrecy(...).RefreshRoot(root)` (target folder is gone with its mark).
  - `Recover`: unfinished of both archives; each written back to its own archive.
  - `ExcludeStamp` keeps using `StampCreated` (false for secret cases, so `desktop.ini` left by a crash between unstamp steps comes home as a user file — accepted by the spec order).

- [ ] **Step 1: Write the failing tests** (skip unless admin)

```csharp
[Fact] public void Rehab_SecretCase_ProgressStaysSecret() { /* after rehab: pub.Current() empty; secret.Current().Single().Status == "rehabilitated"; root mark cleared */ }
[Fact] public void Recover_SecretRehabilitating() { /* crash at SwapLink via SimulateCrash; Recover → secret record "exiled" or "rehabilitated", pub empty */ }
[Fact] public void NoSecret_RehabOfClassifiedLink_NotExiled() { /* non-elevated Mover → Reason "not_exiled" (callers map it, Task 8) */ }
```

- [ ] **Step 2: Run** `./build.ps1 -Filter MoverTests` — Expected: FAIL / build error.
- [ ] **Step 3: Implement** routing.
- [ ] **Step 4: Run** `./build.ps1` normal and elevated — Expected: `Passed!`.
- [ ] **Step 5: Commit** `feat: mover keeps secret cases in the secret archive`

---

### Task 8: CLI and elevation hide secret cases

**Files:**
- Modify: `src/Sibir/Cli/Cli.cs` (`CliContext`, `Rehab`, `Status`, `Recover`, `UnfinishedRow`, `NewMover`), `src/Sibir/Shell/Elevation.cs`
- Test: `tests/Sibir.Tests/CliTests.cs`

**Interfaces:**
- Produces:
  - `CliContext.Secret` (`SecretArchive`, set in `Real()`), `CliContext.SecretReadable` (`=> SecretArchive.Elevated`). `NewMover` passes `Secret` only when readable; elevated `recover` / unfinished checks include `Secret.Unfinished()`.
  - `rehab`/`status` (with and without `--yes`): `Secrecy.IsClassifiedLink(p, ctx.Archive)` and not readable → row `verdict refused, reason needs_admin, message Strings.Classified + ". Попросите человека выполнить: Start-Process '<exe>' -Verb RunAs"`, no `target`, `bytes 0`; exit 5.
  - `Elevation.Relaunch(string[] args)` appends `--as <current user SID>`; `static string AsSid(string[] args)` parses it; `static bool WrongAccount(string[] args)` — `IsAdmin()` and `AsSid != null` and differs from `WindowsIdentity.GetCurrent().User.Value`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Status_ClassifiedLink_NeedsAdmin_NoTarget() { /* code == 5; row reason needs_admin; target null; message contains "засекречено" */ }
[Fact] public void Rehab_ClassifiedLink_NeedsAdmin() { /* --yes and without; code == 5; link untouched */ }
[Fact] public void List_HidesClassified() { /* list rows do not contain the source */ }
[Fact] public void Elevation_AsSidParsed() { Assert.Equal("S-1-5-21-1", Elevation.AsSid(new[] { "--as", "S-1-5-21-1" })); }
```

- [ ] **Step 2: Run** `./build.ps1 -Filter "CliTests|Elevation"` — Expected: FAIL.
- [ ] **Step 3: Implement**.
- [ ] **Step 4: Run** `./build.ps1` — Expected: `Passed!`.
- [ ] **Step 5: Commit** `feat: classified cases answer needs_admin`

---

### Task 9: GUI — classify/burn menu, secret window, elevation

**Files:**
- Create: `src/Sibir/Gui/SecretForm.cs`
- Modify: `src/Sibir/Gui/MainForm.cs`, `src/Sibir/Gui/GuiApp.cs`, `src/Sibir/Core/Settings.cs` (`SecretWarned`)
- Test: manual checklist below (WinForms, no UI test harness in this repo)

**Interfaces:**
- Consumes: `Secrecy`, `SecretArchive`, `Elevation.WrongAccount`, `CliContext.Secret`.
- Produces:
  - `MainForm`: `ListViewItem.Tag = ArchiveRecord`; row context menu «Засекретить» (statuses `exiled`/`rehabilitated`), «Сжечь дело» (only `rehabilitated`; one-line confirm `Strings.BurnConfirm`). First classify shows `Strings.ClassifyWarning` once, then `Settings.SecretWarned = true`. Button «Секретные записи» added only when `Elevation.IsAdmin()`.
  - `SecretForm` (elevated only): same columns over `Secret.Current()`; actions «Рассекретить», «Реабилитировать» (via `ProgressForm` + elevated `NewMover`), «Сжечь дело» (only `rehabilitated`). Calls `Secret.EnsureAcl()` on open.
  - `GuiApp.Run`: if `Elevation.WrongAccount(args)` → message «Права выданы другой учётной записи: её архив — не ваш. Секретные записи недоступны» and exit. Elevated: recovery offer also counts `Secret.Unfinished()`; `NewMover` passes `Secret` when elevated.
  - `GuiApp.Rehab`: classified links (`Secrecy.IsClassifiedLink`) not elevated → `Strings.Classified` + Yes/No «Запросить санкцию?» → `Elevation.Relaunch(new[]{"--menu-rehab", path})`; elevated → rehab from the secret archive.
  - `GuiApp.ShowCase`: classified link → `Strings.Classified`, no details.

- [ ] **Step 1: Implement** the above.
- [ ] **Step 2: Run** `./build.ps1` — Expected: `Passed!`, `OK: out\Peressylkin.exe`.
- [ ] **Step 3: Manual check** (on a test folder, other drive):
  - exile → «Засекретить» → warning shown once → row gone, stamp icon gone in Explorer, `list` doesn't show it.
  - Explorer «Реабилитировать» on the link → «Дело засекречено…» → accept UAC → folder home, secret window shows «реабилитирован».
  - Normal window: no «Секретные записи» button; elevated: button present, «Сжечь дело» greyed for «сослан».
  - Burn a rehabilitated public case → gone from table, no `.bak` in `%LOCALAPPDATA%\Sibir`.
- [ ] **Step 4: Commit** `feat: GUI classify, burn and secret records window`

---

### Task 10: Docs and version

**Files:**
- Modify: `AGENTS.md`, `docs/claude-skill/SKILL.md`, `README*` (if it lists reasons), `src/Sibir/Sibir.csproj` (`<Version>1.2.0</Version>`)

- [ ] **Step 1:** AGENTS.md / SKILL.md: `network_source` next to `network_target` («заграница»: any network path refused, choose a local one); exit 5 may mean a classified case — give the human the command, never ask for details.
- [ ] **Step 2: Run** `./build.ps1` — Expected: `Passed!`.
- [ ] **Step 3: Commit** `docs: secret cases and abroad; 1.2.0`
