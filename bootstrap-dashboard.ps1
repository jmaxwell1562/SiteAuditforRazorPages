param(
    [switch]$SkipPython,
    [switch]$SkipDotnetRestore
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

Write-Host "Bootstrapping dashboard workspace at $repoRoot"

$requirementsPath = Join-Path $repoRoot 'requirements.txt'
if (-not $SkipPython) {
    if (Test-Path $requirementsPath) {
        if (-not (Test-Path (Join-Path $repoRoot '.venv'))) {
            Write-Host 'Creating Python virtual environment...'
            python -m venv .venv
        }

        $pythonExe = Join-Path $repoRoot '.venv\Scripts\python.exe'
        Write-Host 'Installing Python dependencies...'
        & $pythonExe -m pip install --upgrade pip
        & $pythonExe -m pip install -r $requirementsPath
    }
    else {
        Write-Host 'requirements.txt not found. Skipping Python dependency install.'
    }
}

if (-not $SkipDotnetRestore) {
    $projects = Get-ChildItem -Path $repoRoot -Filter *.csproj -Recurse -ErrorAction SilentlyContinue
    if ($projects) {
        foreach ($project in $projects) {
            Write-Host "Restoring .NET dependencies for $($project.FullName)..."
            dotnet restore $project.FullName

            Write-Host "Building $($project.FullName)..."
            dotnet build $project.FullName --no-restore
        }
    }
    else {
        Write-Host '.csproj not found. Skipping dotnet restore.'
    }
}

New-Item -ItemType Directory -Path (Join-Path $repoRoot 'Audits') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $repoRoot 'wwwroot\reports') -Force | Out-Null

Write-Host 'Bootstrap complete.'
