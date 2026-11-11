# «Перепись населения» — read-only census of data folders: who owns each one?
# Never deletes anything. Writes a CSV report and prints a grouped summary.
# Usage: perepis.ps1 [-Out <csv>] [-MinMB 1] [-NoInternet]
param([string]$Out = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Перепись.csv'), [double]$MinMB = 1, [switch]$NoInternet)
$ErrorActionPreference = 'Continue'

function Norm([string]$s) { if (-not $s) { return '' }; ($s.ToLowerInvariant() -replace '[^\p{L}\p{N}]', '') }

# ---------- sources ----------
$owners = New-Object System.Collections.Generic.List[object]   # Name, Kind, Detail
function Add-Owner($name, $kind, $detail) { if ($name -and (Norm $name).Length -ge 3) { $owners.Add([pscustomobject]@{ Name = $name; N = (Norm $name); Kind = $kind; Detail = $detail }) } }

# installed programs
foreach ($k in 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*') {
  Get-ItemProperty $k -EA SilentlyContinue | Where-Object DisplayName | ForEach-Object {
    Add-Owner $_.DisplayName 'installed' $_.DisplayName
    if ($_.Publisher) { Add-Owner $_.Publisher 'installed' "издатель: $($_.Publisher) ($($_.DisplayName))" }
    if ($_.InstallLocation) { Add-Owner (Split-Path $_.InstallLocation.TrimEnd('\', '"') -Leaf) 'installed' $_.DisplayName }
  }
}

# Steam
$steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -EA SilentlyContinue).SteamPath -replace '/', '\'
$installedApps = @{}; $cloudApps = @{}; $playedApps = @{}
if ($steam) {
  $libs = @($steam) + ([regex]::Matches((Get-Content "$steam\steamapps\libraryfolders.vdf" -Raw -EA SilentlyContinue), '"path"\s+"(.+?)"') | ForEach-Object { $_.Groups[1].Value -replace '\\\\', '\' })
  foreach ($lib in $libs | Select-Object -Unique) {
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
  # names/developers/publishers of played-but-not-installed games (cached; store API)
  $cacheFile = Join-Path $env:LOCALAPPDATA 'Sibir\steam-apps-cache.json'
  $cache = @{}; if (Test-Path $cacheFile) { (Get-Content $cacheFile -Raw | ConvertFrom-Json).psobject.Properties | ForEach-Object { $cache[$_.Name] = $_.Value } }
  $need = $playedApps.Keys | Where-Object { -not $installedApps.ContainsKey($_) -and -not $cache.ContainsKey($_) }
  if (-not $NoInternet) {
    foreach ($id in $need) {
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

$system = 'microsoft', 'windows', 'packages', 'temp', 'nvidia', 'nvidiacorporation', 'intel', 'amd', 'realtek', 'google', 'mozilla', 'apple', 'comms', 'connecteddevicesplatform',
  'd3dscache', 'crashdumps', 'history', 'virtualstore', 'publishers', 'programs', 'packagecache', 'packagemanagement', 'ssh', 'regid19912microsoft', 'sibir', 'claudedata', 'claude', 'anthropic',
  'usoshared', 'usoprivate', 'ssh', 'softwaredistribution', 'mozillamaintenanceservice', 'desktopini', 'iconcache', 'peernetworking', 'placeholdertilelogofolder', 'microsoftedge'

# ---------- folders to census ----------
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

function Match-Owner($name, $parent) {
  $n = Norm $name; $p = Norm $parent
  if ($system -contains $n -or ($p -and $system -contains $p)) { return [pscustomobject]@{ Kind = 'system'; Detail = 'системное / драйверы' } }
  $best = $null
  foreach ($o in $owners) {
    $hit = ($n.Length -ge 4 -and ($o.N -eq $n -or $o.N.Contains($n) -or ($o.N.Length -ge 5 -and $n.Contains($o.N)))) -or
           ($p.Length -ge 4 -and ($o.N -eq $p -or ($o.N.Length -ge 5 -and $p.Contains($o.N))))
    if (-not $hit) { continue }
    $rank = @{ 'installed' = 3; 'steam-installed' = 3; 'steam-removed-cloud' = 2; 'steam-removed' = 1 }[$o.Kind]
    if (-not $best -or $rank -gt $best.R) { $best = [pscustomobject]@{ Kind = $o.Kind; Detail = $o.Detail; R = $rank } }
  }
  if ($best) { return $best }
  return [pscustomobject]@{ Kind = 'orphan'; Detail = 'хозяин не найден' }
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
      'steam-removed-cloud' { 'МОЖНО УДАЛИТЬ (облако Steam)' } 'steam-removed' { 'решить: игра удалена, облака нет' } default { 'решить: хозяин не найден' }
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
$rows | Group-Object Вердикт | Sort-Object Name | ForEach-Object { "{0,-38} {1,4} папок {2,8:N0} МБ" -f $_.Name, $_.Count, ($_.Group | Measure-Object МБ -Sum).Sum }
"`n--- кандидаты (не 'жив'/'системное'), крупнейшие:"
$rows | Where-Object { $_.Вердикт -like 'МОЖНО*' -or $_.Вердикт -like 'решить*' } | Sort-Object МБ -Descending | Select-Object -First 40 |
  ForEach-Object { "{0,7:N0} МБ  {1,5} лет  {2}  [{3}]" -f $_.МБ, $_.Лет, $_.Папка, $_.Хозяин }
