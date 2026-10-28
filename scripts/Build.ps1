param([switch]$Test)
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    & dotnet build .\TronEvolutionPatcher.sln -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    if ($Test) {
        & dotnet test .\TronEvolutionPatcher.sln -c Release --no-build --no-restore --nologo --blame-hang-timeout 60s
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    }
} finally {
    Pop-Location
}
