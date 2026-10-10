param([string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$editorRoot = Split-Path -Parent $UnityEditor
if (-not (Test-Path -LiteralPath $UnityEditor)) { throw 'Specify -UnityEditor with the installed Unity.exe path.' }
& dotnet build (Join-Path $projectRoot 'tools/CompileCheck/CompileCheck.csproj') -c Release "-p:UnityEditorRoot=$editorRoot"
if ($LASTEXITCODE -ne 0) { throw 'Unity API compile check failed.' }
