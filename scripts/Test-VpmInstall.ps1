param([string]$VrcGet = '', [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (!$VrcGet) { $VrcGet = Join-Path $root 'artifacts/vrc-get.exe' }
$id = 'com.usamishin.kaihenshelf.core'
$manifest = Get-Content (Join-Path $root "Packages/$id/package.json") -Encoding UTF8 -Raw | ConvertFrom-Json
$testRoot = Join-Path $root ('artifacts/VpmValidation-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $testRoot 'package'
New-Item -ItemType Directory -Path $source -Force | Out-Null
Expand-Archive -LiteralPath (Join-Path $root "artifacts/$id-$($manifest.version).zip") -DestinationPath $source
# Only this unique temporary package registration is added and removed.
& $VrcGet user-package add $source
if ($LASTEXITCODE -ne 0) { throw 'Temporary package registration failed' }
try {
  foreach ($case in @('fresh','legacy','current','renamed')) {
    $project = Join-Path $testRoot $case
    New-Item -ItemType Directory -Path "$project/Assets/Keep", "$project/Packages", "$project/ProjectSettings", "$project/UserSettings/KaihenShelf" -Force | Out-Null
    Copy-Item (Join-Path $root 'Packages/manifest.json') "$project/Packages/manifest.json"
    Copy-Item (Join-Path $root 'ProjectSettings/ProjectVersion.txt') "$project/ProjectSettings/ProjectVersion.txt"
    Set-Content "$project/Assets/Keep/product.txt" 'keep product unchanged'
    Set-Content "$project/UserSettings/KaihenShelf/history.json" '{"sentinel":"keep history"}'
    if ($case -ne 'fresh') {
      $legacy = if ($case -eq 'legacy') { "$project/Assets/VrcKaihenLibrary" } elseif ($case -eq 'current') { "$project/Assets/KaihenShelf" } else { "$project/Assets/RenamedTool" }
      New-Item -ItemType Directory -Path $legacy -Force | Out-Null
      Set-Content "$legacy/obsolete.txt" 'old tool'
      Copy-Item (Join-Path $root "Packages/$id.meta") ($legacy + '.meta')
    }
    $before = (Get-FileHash "$project/UserSettings/KaihenShelf/history.json").Hash
    & $VrcGet install $id --project $project --offline --yes
    if ($LASTEXITCODE -ne 0) { throw "Install failed: $case" }
    if (!(Test-Path "$project/Packages/$id/package.json")) { throw "Missing installed package: $case" }
    if ($case -ne 'fresh' -and (Test-Path -LiteralPath $legacy)) { throw "Legacy folder remains: $case" }
    if (!(Test-Path "$project/Assets/Keep/product.txt") -or (Get-FileHash "$project/UserSettings/KaihenShelf/history.json").Hash -ne $before) { throw 'User content changed' }
    Write-Output "PASS VPM $case install and user content preservation"
  }
  # Compile an installed VPM package in an isolated Unity project; no product import or user consent changes.
  $project = Join-Path $testRoot 'fresh'
  New-Item -ItemType Directory -Path "$project/Assets/Editor" -Force | Out-Null
  $harness = @'
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
public static class KaihenShelfVpmSmoke {
  public static void Run() {
    try {
      var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(KaihenShelf.AvatarDefaultsLoader).Assembly);
      if (package == null || package.name != "com.usamishin.kaihenshelf.core") throw new Exception("Package resolution failed");
      if (!KaihenShelf.AvatarDefaultsLoader.Load(out var warning)) throw new Exception(warning);
      File.WriteAllText(Path.Combine(Application.dataPath, "../vpm-smoke.txt"), "PASS package assembly and avatar defaults from Packages");
      EditorApplication.Exit(0);
    } catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
  }
}
'@
  [IO.File]::WriteAllText("$project/Assets/Editor/KaihenShelfVpmSmoke.cs", $harness)
  $log = Join-Path $testRoot 'unity.log'
  $arguments = @('-batchmode','-nographics','-projectPath',('"' + $project + '"'),'-executeMethod','KaihenShelfVpmSmoke.Run','-logFile',('"' + $log + '"'))
  $process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
  if (!$process.WaitForExit(180000)) { throw "Unity still running; inspect process $($process.Id) and $log" }
  if ($process.ExitCode -ne 0 -or !(Test-Path "$project/vpm-smoke.txt") -or (Select-String -LiteralPath $log -Pattern 'error CS\d+|Scripts have compiler errors')) { throw "Unity smoke failed: $log" }
  Get-Content "$project/vpm-smoke.txt"
  # Uninstall/reinstall also preserves assets and history, using a project without the test harness.
  $project = Join-Path $testRoot 'current'
  $historyHash = (Get-FileHash "$project/UserSettings/KaihenShelf/history.json").Hash
  & $VrcGet remove $id --project $project --offline --yes
  if ($LASTEXITCODE -ne 0 -or (Test-Path "$project/Packages/$id/package.json")) { throw 'Package removal failed' }
  & $VrcGet install $id --project $project --offline --yes
  if ($LASTEXITCODE -ne 0 -or !(Test-Path "$project/Packages/$id/package.json")) { throw 'Package reinstall failed' }
  if (!(Test-Path "$project/Assets/Keep/product.txt") -or (Get-FileHash "$project/UserSettings/KaihenShelf/history.json").Hash -ne $historyHash) { throw 'User data changed on reinstall' }
  Write-Output 'PASS uninstall/reinstall preserves user content'
  Set-Content (Join-Path $testRoot 'result.txt') 'PASS fresh, legacy, current, renamed; user data preserved; Unity compile and defaults; uninstall/reinstall'
} finally {
  & $VrcGet user-package remove $source
  Write-Output "Validation project: $testRoot"
}
