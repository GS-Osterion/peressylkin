# «Перепись населения» v2 — read-only census of data folders: who owns each one?
# Never deletes anything. Writes a CSV report and prints a grouped summary.
# Owners: installed programs (registry), Steam (installed / played+cloud), Epic, EA, GOG, game folders found on other drives.
# Decisions file (%LOCALAPPDATA%\Sibir\perepis-decisions.txt): "keep|delete|doubt<TAB>path" — overrides the verdict.
# Usage: perepis.ps1 [-Out <csv>] [-MinMB 1] [-NoInternet]
param([string]$Out = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Перепись.csv'), [double]$MinMB = 1, [switch]$NoInternet)
$ErrorActionPreference = 'Continue'

$stop = @('the', 'of', 'and', 'a', 'games', 'game', 'studio', 'studios', 'inc', 'ltd', 'llc', 'gmbh', 'co', 'corp', 'corporation', 'entertainment',
          'software', 'interactive', 'edition', 'definitive', 'special', 'goty', 'remastered', 'launcher', 'data', 'saves', 'mygames', 'my',
          'tools', 'tool', 'app', 'apps', 'bin', 'other', 'torrent', 'setup', 'updater', 'update', 'cache', 'saved', 'local', 'common')
function Tokens([string]$s) {
  if (-not $s) { return @() }
  $s = $s -creplace '([a-z])([A-Z])', '$1 $2' -replace '(\p{L})(\d)', '$1 $2' -replace '(\d)(\p{L})', '$1 $2'
  @(($s.ToLowerInvariant() -split '[^\p{L}\p{N}]+') | Where-Object { $_ -and $_ -notin $stop })
}
function Key([string]$s) { (Tokens $s) -join '' }

# ---------- owners ----------
$owners = New-Object System.Collections.Generic.List[object]
function Add-Owner($name, $kind, $detail) {
  $t = Tokens $name
  if ($t.Count -gt 0 -and ($t -join '').Length -ge 3) { $owners.Add([pscustomobject]@{ T = $t; K = ($t -join ''); Kind = $kind; Detail = $detail }) }
}

foreach ($k in 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*') {
  Get-ItemProperty $k -EA SilentlyContinue | Where-Object DisplayName | ForEach-Object {
    Add-Owner $_.DisplayName 'installed' $_.DisplayName
    if ($_.Publisher) { Add-Owner $_.Publisher 'installed' "издатель: $($_.Publisher) ($($_.DisplayName))" }
    if ($_.InstallLocation) { Add-Owner (Split-Path $_.InstallLocation.TrimEnd('\', '"') -Leaf) 'installed' $_.DisplayName }
  }
}
# Epic
Get-ChildItem "$env:ProgramData\Epic\EpicGamesLauncher\Data\Manifests\*.item" -EA SilentlyContinue | ForEach-Object {
  $j = Get-Content $_.FullName -Raw | ConvertFrom-Json
  Add-Owner $j.DisplayName 'installed' "Epic: $($j.DisplayName)"; if ($j.InstallLocation) { Add-Owner (Split-Path $j.InstallLocation -Leaf) 'installed' "Epic: $($j.DisplayName)" }
}
# EA / GOG (registry)
foreach ($k in 'HKLM:\SOFTWARE\WOW6432Node\Electronic Arts\*', 'HKLM:\SOFTWARE\WOW6432Node\EA Games\*') {
  Get-ChildItem $k -EA SilentlyContinue | ForEach-Object { Add-Owner $_.PSChildName 'installed' "EA: $($_.PSChildName)" }
}
Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\GOG.com\Games\*' -EA SilentlyContinue | ForEach-Object { Add-Owner $_.gameName 'installed' "GOG: $($_.gameName)" }

# Steam
$steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -EA SilentlyContinue).SteamPath -replace '/', '\'
$installedApps = @{}; $cloudApps = @{}; $playedApps = @{}; $steamCommon = @()
if ($steam) {
  $libs = @($steam) + ([regex]::Matches((Get-Content "$steam\steamapps\libraryfolders.vdf" -Raw -EA SilentlyContinue), '"path"\s+"(.+?)"') | ForEach-Object { $_.Groups[1].Value -replace '\\\\', '\' })
  foreach ($lib in $libs | Select-Object -Unique) {
    $steamCommon += "$lib\steamapps\common"
    Get-ChildItem "$lib\steamapps\appmanifest_*.acf" -EA SilentlyContinue | ForEach-Object {
      $t = Get-Content $_.FullName -Raw
      $id = [regex]::Match($t, '"appid"\s+"(\d+)"').Groups[1].Value
      $nm = [regex]::Match($t, '"name"\s+"(.+?)"').Groups[1].Value
      $dir = [regex]::Match($t, '"installdir"\s+"(.+?)"').Groups[1].Value
      $installedApps[$id] = $nm
      Add-Owner $nm 'steam-installed' "Steam: $nm"; Add-Owner $dir 'steam-installed' "Steam: $nm"
    }
  }
  foreach ($u in Get-ChildItem "$steam\userdata" -Directory -EA SilentlyContinue) {
    Get-ChildItem $u.FullName -Directory -EA SilentlyContinue | Where-Object Name -match '^\d+$' | ForEach-Object {
      $playedApps[$_.Name] = $true
      if (Test-Path (Join-Path $_.FullName 'remote')) { $cloudApps[$_.Name] = $true }
    }
    $lc = Get-Content "$($u.FullName)\config\localconfig.vdf" -Raw -EA SilentlyContinue
    if ($lc) { [regex]::Matches($lc, '"(\d{3,8})"\s*\{\s*"LastPlayed"') | ForEach-Object { $playedApps[$_.Groups[1].Value] = $true } }
  }
  $cacheFile = Join-Path $env:LOCALAPPDATA 'Sibir\steam-apps-cache.json'
  $cache = @{}; if (Test-Path $cacheFile) { (Get-Content $cacheFile -Raw | ConvertFrom-Json).psobject.Properties | ForEach-Object { $cache[$_.Name] = $_.Value } }
  if (-not $NoInternet) {
    foreach ($id in $playedApps.Keys | Where-Object { -not $installedApps.ContainsKey($_) -and -not $cache.ContainsKey($_) }) {
      try {
        $r = Invoke-RestMethod "https://store.steampowered.com/api/appdetails?appids=$id&filters=basic,developers,publishers&l=russian" -TimeoutSec 10
        $d = $r.$id.data
        $cache[$id] = if ($d) { [pscustomobject]@{ name = $d.name; dev = @($d.developers) -join ', '; pub = @($d.publishers) -join ', ' } } else { [pscustomobject]@{ name = $null } }
      } catch { }
      Start-Sleep -Milliseconds 300
    }
    New-Item -ItemType Directory -Force (Split-Path $cacheFile) | Out-Null
    $cache | ConvertTo-Json -Depth 3 | Set-Content $cacheFile -Encoding utf8
  }
  foreach ($id in $playedApps.Keys) {
    if ($installedApps.ContainsKey($id) -or -not $cache[$id] -or -not $cache[$id].name) { continue }
    $c = $cache[$id]; $kind = if ($cloudApps[$id]) { 'steam-removed-cloud' } else { 'steam-removed' }
    $det = "Steam (удалена): $($c.name)" + $(if ($cloudApps[$id]) { ' — есть облако' } else { ' — облака нет' })
    Add-Owner $c.name $kind $det
    foreach ($x in ($c.dev -split ', ') + ($c.pub -split ', ')) { Add-Owner $x $kind "$det; студия: $x" }
  }
}

# Game folders on non-system fixed drives without a launcher (torrents, repacks, portable)
$gameRoots = foreach ($drv in Get-PSDrive -PSProvider FileSystem | Where-Object { $_.Root -ne "$env:SystemDrive\" -and $_.Free -ne $null }) {
  Get-ChildItem $drv.Root -Directory -EA SilentlyContinue | Where-Object { $_.Name -match 'game|игр' -and $_.Name -notmatch 'GameData|steamapps|Saved Games|My Games' } |
    ForEach-Object { $_.FullName; Get-ChildItem $_.FullName -Directory -EA SilentlyContinue | Where-Object { $_.Name -match 'game|игр|other' } | ForEach-Object FullName }
}
foreach ($gr in $gameRoots | Select-Object -Unique) {
  if ($steamCommon | Where-Object { $gr -like "$_*" }) { continue }
  Get-ChildItem $gr -Directory -EA SilentlyContinue | Where-Object { $_.Name -notmatch '^(tools?|torrent|other|setup|ruffle.*)$' } | ForEach-Object { Add-Owner $_.Name 'on-disk' "игра на диске без лаунчера: $($_.FullName)" }
}

$system = 'microsoft', 'windows', 'packages', 'temp', 'nvidia', 'nvidiacorporation', 'intel', 'amd', 'realtek', 'google', 'mozilla', 'apple', 'comms', 'connecteddevicesplatform',
  'd3dscache', 'crashdumps', 'history', 'virtualstore', 'publishers', 'programs', 'packagecache', 'packagemanagement', 'ssh', 'regid19912microsoft', 'sibir', 'claudedata', 'claude', 'anthropic',
  'usoshared', 'usoprivate', 'softwaredistribution', 'mozillamaintenanceservice', 'iconcache', 'peernetworking', 'placeholdertilelogofolder', 'microsoftedge'

# decisions
$decisions = @{}
$decFile = Join-Path $env:LOCALAPPDATA 'Sibir\perepis-decisions.txt'
if (Test-Path $decFile) { Get-Content $decFile | Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object { $a = $_ -split "`t", 2; if ($a.Count -eq 2) { $decisions[$a[1].Trim().TrimEnd('\').ToLowerInvariant()] = $a[0].Trim() } } }

# ---------- folders ----------
$saved = (New-Object -ComObject Shell.Application).NameSpace('shell:SavedGames').Self.Path
$docs = [Environment]::GetFolderPath('MyDocuments')
$roots = @(
  @{ P = $env:APPDATA; Deep = $false }, @{ P = $env:LOCALAPPDATA; Deep = $false }, @{ P = "$env:USERPROFILE\AppData\LocalLow"; Deep = $true },
  @{ P = 'C:\ProgramData'; Deep = $false }, @{ P = "$docs\My Games"; Deep = $false }, @{ P = $saved; Deep = $false }
)
function Folders($root, $deep) {
  foreach ($d in Get-ChildItem $root -Directory -Force -EA SilentlyContinue) {
    if ($deep -and -not ($d.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
      $subs = Get-ChildItem $d.FullName -Directory -Force -EA SilentlyContinue
      if ($subs) { foreach ($s in $subs) { [pscustomobject]@{ Dir = $s; Parent = $d.Name } }; continue }
    }
    [pscustomobject]@{ Dir = $d; Parent = $null }
  }
}

$rank = @{ 'installed' = 5; 'steam-installed' = 5; 'on-disk' = 4; 'steam-removed-cloud' = 3; 'steam-removed' = 2 }
function Hits($tokens) {
  if ($tokens.Count -eq 0) { return @() }
  $k = $tokens -join ''
  foreach ($o in $owners) {
    # whole-word match: same key, or every folder word is a word of the owner, or every owner word is a word of the folder
    $all1 = -not ($tokens | Where-Object { $_ -notin $o.T })
    $all2 = $o.T.Count -ge 1 -and $o.K.Length -ge 5 -and -not ($o.T | Where-Object { $_ -notin $tokens })
    if ($o.K -eq $k -or ($all1 -and $k.Length -ge 4) -or $all2) { $o }
  }
}
function Match-Owner($name, $parent) {
  $k = Key $name; $pk = Key $parent
  if ($system -contains $k -or ($pk -and $system -contains $pk)) { return [pscustomobject]@{ Kind = 'system'; Detail = 'системное / драйверы' } }
  $best = (@(Hits (Tokens $name)) + @(Hits (Tokens $parent))) | Sort-Object { $rank[$_.Kind] } -Descending | Select-Object -First 1
  if ($best) { return $best }
  [pscustomobject]@{ Kind = 'orphan'; Detail = 'хозяин не найден' }
}

$rows = foreach ($r in $roots) {
  if (-not (Test-Path $r.P)) { continue }
  foreach ($f in Folders $r.P $r.Deep) {
    $d = $f.Dir
    if ($d.Attributes -band [IO.FileAttributes]::ReparsePoint) {
      $kind = 'exiled'; $detail = 'ссылка → ' + $d.Target; $size = 0; $newest = $d.LastWriteTime
    } else {
      $files = Get-ChildItem $d.FullName -Recurse -File -Force -EA SilentlyContinue
      $size = ($files | Measure-Object Length -Sum).Sum
      if ($size / 1MB -lt $MinMB) { continue }
      $newest = ($files | Measure-Object LastWriteTime -Maximum).Maximum
      $m = Match-Owner $d.Name $f.Parent; $kind = $m.Kind; $detail = $m.Detail
    }
    $verdict = switch ($kind) {
      'installed' { 'жив, не трогать' } 'steam-installed' { 'жив, не трогать' } 'system' { 'не трогать' } 'exiled' { 'в Сибири' }
      'on-disk' { 'под сомнением: игра без лаунчера (торрент?)' }
      'steam-removed-cloud' { 'можно удалить: облако Steam' } 'steam-removed' { 'решить: игра удалена, облака нет' } default { 'решить: хозяин не найден' }
    }
    switch ($decisions[$d.FullName.TrimEnd('\').ToLowerInvariant()]) {
      'keep' { $verdict = 'решено: оставить' } 'delete' { $verdict = 'решено: удалить' } 'doubt' { $verdict = 'под сомнением: торрент (решено)' }
    }
    [pscustomobject]@{
      Вердикт = $verdict; МБ = [math]::Round($size / 1MB); Изменено = if ($newest) { $newest.ToString('yyyy-MM-dd') } else { '' }
      Лет = if ($newest) { [math]::Round(((Get-Date) - $newest).TotalDays / 365, 1) } else { '' }
      Папка = $d.FullName; Хозяин = $detail; Тип = $kind
    }
  }
}

$rows | Sort-Object Вердикт, @{ e = 'МБ'; Descending = $true } | Export-Csv $Out -NoTypeInformation -Encoding utf8BOM -Delimiter ';'
"Отчёт: $Out"
$rows | Group-Object Вердикт | Sort-Object Name | ForEach-Object { "{0,-44} {1,4} папок {2,8:N0} МБ" -f $_.Name, $_.Count, ($_.Group | Measure-Object МБ -Sum).Sum }
foreach ($g in 'можно удалить*', 'под сомнением*', 'решить*') {
  "`n--- $g (крупнейшие):"
  $rows | Where-Object Вердикт -like $g | Sort-Object МБ -Descending | Select-Object -First 25 |
    ForEach-Object { "{0,7:N0} МБ  {1,5} лет  {2}  [{3}]" -f $_.МБ, $_.Лет, $_.Папка, $_.Хозяин }
}
