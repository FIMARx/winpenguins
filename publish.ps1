$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

Write-Host "Running unit tests..."
dotnet test WinPenguins.slnx -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Publishing standalone single-file executable..."
dotnet publish src/WinPenguins/WinPenguins.csproj -p:PublishProfile=SingleFile -o publish
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $repoRoot "publish\WinPenguins.exe"
if (Test-Path $exe) {
    $mb = [Math]::Round((Get-Item $exe).Length / 1MB, 2)
    Write-Host "Published standalone binary: $exe ($mb MB)"
}
