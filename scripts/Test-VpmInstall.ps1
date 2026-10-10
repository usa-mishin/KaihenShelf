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
  foreach ($case in @('fresh','legacy','renamed')) {
    $project = Join-Path $testRoot $case
    New-Item -ItemType Directory -Path "$project/Assets/Keep", "$project/Packages", "$project/ProjectSettings", "$project/UserSettings/KaihenShelf" -Force | Out-Null
    Copy-Item (Join-Path $root 'Packages/manifest.json') "$project/Packages/manifest.json"
    Copy-Item (Join-Path $root 'ProjectSettings/ProjectVersion.txt') "$project/ProjectSettings/ProjectVersion.txt"
    Set-Content "$project/Assets/Keep/product.txt" 'keep product unchanged'
    Set-Content "$project/UserSettings/KaihenShelf/history.json" '{"sentinel":"keep history"}'
    if ($case -ne 'fresh') {
      $legacy = if ($case -eq 'legacy') { "$project/Assets/VrcKaihenLibrary" } else { "$project/Assets/RenamedTool" }
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
  $log = Join-Path $testRoot 'unity.log'
  $arguments = @('-batchmode','-nographics','-quit','-projectPath',('"' + $project + '"'),'-logFile',('"' + $log + '"'))
  $process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
  if (!$process.WaitForExit(180000)) { throw "Unity still running; inspect process $($process.Id) and $log" }
  if ($process.ExitCode -ne 0 -or (Select-String -LiteralPath $log -Pattern 'error CS\d+|Scripts have compiler errors')) { throw "Unity compile failed: $log" }
  Write-Output 'PASS Unity 2022.3 installed VPM package compilation'
  Set-Content (Join-Path $testRoot 'result.txt') 'PASS fresh, legacy, renamed; user data preserved; Unity compile'
} finally {
  & $VrcGet user-package remove $source
  Write-Output "Validation project: $testRoot"
}
