param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& dotnet run --project (Join-Path $projectRoot 'Tests/CoreTests/CoreTests.csproj') -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
& dotnet run --project (Join-Path $projectRoot 'tools/PackageBuilder/PackageBuilder.csproj') -c $Configuration -- $projectRoot
if ($LASTEXITCODE -ne 0) { throw 'Package build failed.' }
