# Define test projects with their coverlet include/exclude filters
# Each test project only measures coverage for its target assembly
#
# Exclude patterns:
#   - OpenAPI source-generated code (compiled into the Api assembly but not our code)
#   - Program.cs excluded from unit tests (startup/DI wiring is tested by integration tests)
#   - Program.cs kept in integration tests since they exercise the real pipeline

$openApiExclude = "[LetterTranslation.Api]Microsoft.AspNetCore.OpenApi.Generated.*,[LetterTranslation.Api]System.Runtime.CompilerServices.*"

$testProjects = [ordered]@{
    "BackendUnitTests" = @{
        Path    = "tests/backend/UnitTests/LetterTranslation.Api.UnitTests.csproj"
        Include = "[LetterTranslation.Api]*"
        Exclude = "[LetterTranslation.Api]Program,$openApiExclude"
    }
    "BackendIntegrationTests" = @{
        Path    = "tests/backend/IntegrationTests/LetterTranslation.Api.IntegrationTests.csproj"
        Include = "[LetterTranslation.Api]*"
        Exclude = "$openApiExclude"
    }
    "WorkerUnitTests" = @{
        Path    = "tests/worker/UnitTests/LetterTranslation.Worker.UnitTests.csproj"
        Include = "[LetterTranslation.Worker]*"
        Exclude = "[LetterTranslation.Worker]Program"
    }
    "WorkerIntegrationTests" = @{
        Path    = "tests/worker/IntegrationTests/LetterTranslation.Worker.IntegrationTests.csproj"
        Include = "[LetterTranslation.Worker]*"
        Exclude = ""
    }
    "SharedUnitTests" = @{
        Path    = "tests/shared/UnitTests/LetterTranslation.Shared.UnitTests.csproj"
        Include = "[LetterTranslation.Shared]*"
        Exclude = ""
    }
}

$baseResultsDir = "TestResults"
$rawCoverageBaseDir = Join-Path $baseResultsDir "raw-coverage"
$reportBaseDir = Join-Path $baseResultsDir "coverage-html-report"
$summaryPath = Join-Path $baseResultsDir "coverage-summary.json"

# Clean up previous results
Write-Host "Cleaning old test results..." -ForegroundColor Cyan
if (Test-Path $baseResultsDir) {
    Remove-Item -Recurse -Force $baseResultsDir
}
New-Item -ItemType Directory -Path $baseResultsDir | Out-Null
New-Item -ItemType Directory -Path $rawCoverageBaseDir | Out-Null
New-Item -ItemType Directory -Path $reportBaseDir | Out-Null

# Run tests and collect coverage for each project
foreach ($name in $testProjects.Keys) {
    $project = $testProjects[$name]
    $resultsDir = Join-Path $rawCoverageBaseDir $name

    Write-Host "Running tests for $name..." -ForegroundColor Cyan
    $args = @(
        "test", $project.Path,
        "--collect:XPlat Code Coverage",
        "--results-directory", $resultsDir,
        "--",
        "DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Include=$($project.Include)"
    )
    if ($project.Exclude) {
        $args += "DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Exclude=$($project.Exclude)"
    }
    & dotnet @args
}

# Parse coverage XML and build summary
$summary = [ordered]@{}

foreach ($name in $testProjects.Keys) {
    $resultsDir = Join-Path $rawCoverageBaseDir $name
    $xmlFile = Get-ChildItem -Path $resultsDir -Recurse -Filter "coverage.cobertura.xml" | Select-Object -First 1

    if (-not $xmlFile) {
        Write-Host "No coverage file found for $name" -ForegroundColor Red
        continue
    }

    [xml]$xml = Get-Content $xmlFile.FullName
    $root = $xml.coverage

    $projectSummary = [ordered]@{
        lineRate       = [math]::Round([double]$root.'line-rate' * 100, 1)
        branchRate     = [math]::Round([double]$root.'branch-rate' * 100, 1)
        linesCovered   = [int]$root.'lines-covered'
        linesValid     = [int]$root.'lines-valid'
        branchesCovered = [int]$root.'branches-covered'
        branchesValid  = [int]$root.'branches-valid'
        classes        = @()
    }

    foreach ($package in $root.packages.package) {
        foreach ($class in $package.classes.class) {
            $lr = [math]::Round([double]$class.'line-rate' * 100, 1)
            $br = [math]::Round([double]$class.'branch-rate' * 100, 1)
            $projectSummary.classes += [ordered]@{
                name       = $class.name
                lineRate   = $lr
                branchRate = $br
            }
        }
    }

    $summary[$name] = $projectSummary
}

$summary | ConvertTo-Json -Depth 4 | Set-Content $summaryPath -Encoding UTF8
Write-Host "`nCoverage summary written to $summaryPath" -ForegroundColor Green

# Print summary table
Write-Host "`n--- Coverage Summary ---" -ForegroundColor Cyan
foreach ($name in $summary.Keys) {
    $s = $summary[$name]
    Write-Host ("{0,-30} Lines: {1,5}% ({2}/{3})  Branches: {4,5}% ({5}/{6})" -f `
        $name, $s.lineRate, $s.linesCovered, $s.linesValid, `
        $s.branchRate, $s.branchesCovered, $s.branchesValid) -ForegroundColor White
}
Write-Host ""

# Generate HTML reports
foreach ($name in $testProjects.Keys) {
    $resultsDir = Join-Path $rawCoverageBaseDir $name
    $reportDir = Join-Path $reportBaseDir $name

    Write-Host "Generating HTML report for $name..." -ForegroundColor Cyan
    dotnet reportgenerator "-reports:$resultsDir/*/coverage.cobertura.xml" "-targetdir:$reportDir" "-reporttypes:Html"

    $indexPath = Join-Path $reportDir "index.html"
    if (Test-Path $indexPath) {
        Write-Host "Success! $name report at: $(Resolve-Path $indexPath)" -ForegroundColor Green
    } else {
        Write-Host "Failed to generate $name report." -ForegroundColor Red
    }
}
