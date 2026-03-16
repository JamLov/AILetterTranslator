# Define project paths and output directories
$testProjects = @{
    "UnitTests"                = "tests/backend/UnitTests/LetterTranslation.Api.UnitTests.csproj";
    "IntegrationTests"         = "tests/backend/IntegrationTests/LetterTranslation.Api.IntegrationTests.csproj";
    "WorkerUnitTests"          = "tests/worker/UnitTests/LetterTranslation.Worker.UnitTests.csproj";
    "WorkerIntegrationTests"   = "tests/worker/IntegrationTests/LetterTranslation.Worker.IntegrationTests.csproj";
}

$baseResultsDir = "TestResults"
$rawCoverageBaseDir = Join-Path $baseResultsDir "raw-coverage"
$reportBaseDir = Join-Path $baseResultsDir "coverage-html-report"

# Clean up previous results completely
Write-Host "Cleaning old test results..." -ForegroundColor Cyan
if (Test-Path $baseResultsDir) {
    Remove-Item -Recurse -Force $baseResultsDir
}
New-Item -ItemType Directory -Path $baseResultsDir | Out-Null
New-Item -ItemType Directory -Path $rawCoverageBaseDir | Out-Null
New-Item -ItemType Directory -Path $reportBaseDir | Out-Null


# Run tests and collect coverage for each project
foreach ($name in $testProjects.Keys) {
    $projectPath = $testProjects[$name]
    $resultsDir = Join-Path $rawCoverageBaseDir $name
    
    Write-Host "Running tests for $name..." -ForegroundColor Cyan
    dotnet test $projectPath --collect:"XPlat Code Coverage" --results-directory $resultsDir
}

# Generate a separate HTML report for each project
foreach ($name in $testProjects.Keys) {
    $resultsDir = Join-Path $rawCoverageBaseDir $name
    $reportDir = Join-Path $reportBaseDir $name
    
    Write-Host "Generating HTML report for $name..." -ForegroundColor Cyan
    dotnet reportgenerator "-reports:$resultsDir/*/coverage.cobertura.xml" "-targetdir:$reportDir" "-reporttypes:Html"

    $indexPath = Join-Path $reportDir "index.html"
    if (Test-Path $indexPath) {
        Write-Host "Success! $name coverage report generated at: $(Resolve-Path $indexPath)" -ForegroundColor Green
    } else {
        Write-Host "Failed to generate $name coverage report." -ForegroundColor Red
    }
}
