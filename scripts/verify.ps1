<#
.SYNOPSIS
    DigiKaveri-inspired verification runner for WinPenguins.
.DESCRIPTION
    Modes:
      Default (Smart) : Inspects changed files in git to select fast vs full UI/handle sweeps.
      -Quick          : Formatting check + strict analyzer Release build + fast tests (<15s, no slow handle loops).
      -Full           : Strict Release build + ALL tests (UI matrix, crawler, fuzzers, handle leaks) + Coverlet coverage gate + optional SingleFile publish.
      -Publish        : Also verifies standalone publish\WinPenguins.exe builds cleanly (automatically included in -Full).
      -MinCoverage    : Line coverage floor percentage enforced on WinPenguins core Engine/Config/Diagnostics during -Full (default 75).
#>
[CmdletBinding()]
param(
    [switch]$Quick,
    [switch]$Full,
    [switch]$Publish,
    [int]$MinCoverage = 75
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'WinPenguins.slnx'
$testProj = Join-Path $repoRoot 'tests\WinPenguins.Tests\WinPenguins.Tests.csproj'
$appProj  = Join-Path $repoRoot 'src\WinPenguins\WinPenguins.csproj'
$resultsDir = Join-Path $repoRoot 'TestResults\verify'
$stopwatchTotal = [System.Diagnostics.Stopwatch]::StartNew()

function Run-Step {
    param([string]$Title, [scriptblock]$Action)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Write-Host "`n==> $Title" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "Step failed ($LASTEXITCODE): $Title"
    }
    Write-Host ("    OK ({0:F1} s)" -f $sw.Elapsed.TotalSeconds) -ForegroundColor Green
}

$includeSlow = [bool]$Full
$includePublish = [bool]($Full -or $Publish)

if (-not $Quick -and -not $Full) {
    $changed = @(git -C $repoRoot status --porcelain | ForEach-Object { ($_ -split '\s+', 2)[-1] })
    if ($changed -match 'UI/|Platform/|FluentControls|Directory\.Build\.props|\.editorconfig') {
        $includeSlow = $true
        Write-Host "Smart plan: UI/Platform/Analyzer files changed -> running FULL UI + Handle-Leak suite." -ForegroundColor Yellow
    } else {
        Write-Host "Smart plan: Running strict build + fast logic/fuzz/guard tests (use -Full for UI matrix & handle stress)." -ForegroundColor Yellow
    }
}

# Step 1: Git hygiene / formatting (~3s via --folder without MSBuild reload)
Run-Step "Code formatting check (whitespace)" {
    dotnet format whitespace $repoRoot --folder --verify-no-changes
}

# Step 2: Strict Release build (analyzers + IDisposable + nullable as errors)
Run-Step "Release build (strict analyzers & TreatWarningsAsErrors)" {
    dotnet build $solution -c Release
}

# Step 3: Automated tests
if ($includeSlow) {
    if (Test-Path $resultsDir) {
        Remove-Item $resultsDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null

    Run-Step "Full suite (Units + AST Guards + UI Auditor/Crawler + CsCheck Fuzzers + Handle Leaks + Coverage)" {
        dotnet test $testProj -c Release --no-build `
            --results-directory $resultsDir `
            --collect:"XPlat Code Coverage" `
            --logger "console;verbosity=normal"
    }

    Run-Step "Coverage floor check (Engine + Config >= $MinCoverage%)" {
        $covFile = Get-ChildItem $resultsDir -Recurse -Filter 'coverage.cobertura.xml' | Select-Object -First 1
        if (-not $covFile) {
            throw "Coverage XML report was not generated."
        }

        [xml]$xml = Get-Content $covFile.FullName
        $packages = @($xml.coverage.packages.package | Where-Object { $_.name -eq 'WinPenguins' })
        if ($packages.Count -eq 0) {
            throw "WinPenguins package missing from Cobertura report."
        }

        $coreClasses = @($packages.classes.class | Where-Object {
            $_.filename -match '(^|[/\\])(Engine|Config|Diagnostics)[/\\]'
        })
        $covered = 0
        $valid = 0
        foreach ($cls in $coreClasses) {
            $lines = @($cls.lines.line)
            $valid += $lines.Count
            $covered += @($lines | Where-Object { [int]$_.hits -gt 0 }).Count
        }
        if ($valid -eq 0) {
            throw "No Engine/Config/Diagnostics lines found in Cobertura report."
        }
        $pct = [math]::Round(($covered * 100.0) / $valid, 1)
        Write-Host ("    Core Engine/Config/Diagnostics line coverage: {0}% ({1}/{2} lines)" -f $pct, $covered, $valid) -ForegroundColor Gray
        if ($pct -lt $MinCoverage) {
            throw "Coverage $pct% fell below minimum floor $MinCoverage%."
        }
    }
} else {
    Run-Step "Fast suite (Units + AST Guards + CsCheck Fuzzers + About Audit)" {
        dotnet test $testProj -c Release --no-build --filter "Category!=Slow" --logger "console;verbosity=normal"
    }
}

# Step 4: Standalone single-file publish check (safe even if installed copy isn't running)
if ($includePublish) {
    Run-Step "Publish smoke verification (SingleFile -> TestResults\verify-publish)" {
        $pubOut = Join-Path $resultsDir 'publish-smoke'
        dotnet publish $appProj -p:PublishProfile=SingleFile -o $pubOut
        $exe = Join-Path $pubOut 'WinPenguins.exe'
        if (-not (Test-Path $exe)) {
            throw "Expected published executable missing at $exe"
        }
        $mb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
        Write-Host ("    Verified standalone WinPenguins.exe ({0} MB)" -f $mb) -ForegroundColor Gray
    }
}

Write-Host ("`nAll verification checks PASSED in {0:F1} s." -f $stopwatchTotal.Elapsed.TotalSeconds) -ForegroundColor Green
