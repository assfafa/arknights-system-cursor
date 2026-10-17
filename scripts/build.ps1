param([string]$Version = '1.0.0')

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $projectRoot
try {
    foreach ($project in @('src\App\ArknightsSystemCursor.csproj', 'src\Cli\ArknightsSystemCursor.Cli.csproj', 'tests\ArknightsSystemCursor.Tests.csproj')) {
        & dotnet build $project -c Release --nologo -v minimal ('-p:Version=' + $Version)
        if ($LASTEXITCODE -ne 0) {
            throw ('Build failed: ' + $project)
        }
    }
    & '.\tests\bin\Release\net48\ArknightsSystemCursor.Tests.exe'
    if ($LASTEXITCODE -ne 0) {
        throw 'Tests failed.'
    }
    $artifactsDirectory = Join-Path $projectRoot 'artifacts'
    $packageDirectory = Join-Path $artifactsDirectory ('ArknightsSystemCursor-v' + $Version + '-win-portable')
    New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
    foreach ($application in @('App', 'Cli')) {
        $binaryDirectory = Join-Path $projectRoot ('src\' + $application + '\bin\Release\net48')
        Get-ChildItem -LiteralPath $binaryDirectory -File | Where-Object {
            $_.Extension -eq '.exe' -or $_.Name.EndsWith('.exe.config')
        } | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $packageDirectory -Force
        }
    }
    foreach ($document in @('README.md', 'LICENSE', 'AGENTS.md', 'CLAUDE.md')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $document) -Destination $packageDirectory -Force
    }
    $packageDocs = Join-Path $packageDirectory 'docs'
    New-Item -ItemType Directory -Path $packageDocs -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $projectRoot 'docs') -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $packageDocs -Force
    }
    $zipPath = $packageDirectory + '.zip'
    Compress-Archive -LiteralPath $packageDirectory -DestinationPath $zipPath -Force
    $standalone = Join-Path $artifactsDirectory 'ArknightsSystemCursor.exe'
    Copy-Item -LiteralPath (Join-Path $packageDirectory 'ArknightsSystemCursor.exe') -Destination $standalone -Force
    $checksumLines = foreach ($path in @($zipPath, $standalone)) {
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        $hash + '  ' + [IO.Path]::GetFileName($path)
    }
    $checksumLines | Set-Content -LiteralPath (Join-Path $artifactsDirectory 'SHA256SUMS.txt') -Encoding ascii
    Write-Host ('Release files: ' + $artifactsDirectory)
}
finally {
    Pop-Location
}
