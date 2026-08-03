$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot 'publish'))

if (-not $artifactRoot.StartsWith($repositoryRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Unexpected artifact directory.'
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

dotnet restore (Join-Path $repositoryRoot 'MeowShot.sln')
dotnet build (Join-Path $repositoryRoot 'MeowShot.sln') -c Release --no-restore
dotnet publish (Join-Path $repositoryRoot 'src\MeowShot\MeowShot.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDirectory

$portableArchive = Join-Path $artifactRoot 'MeowShot-portable-x64.zip'
if (Test-Path -LiteralPath $portableArchive -PathType Leaf) {
    Remove-Item -LiteralPath $portableArchive
}
Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $portableArchive -CompressionLevel Optimal

$innoCompiler = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1

if ($innoCompiler) {
    & $innoCompiler (Join-Path $repositoryRoot 'installer\MeowShot.iss')
} else {
    Write-Warning 'Inno Setup 6 not found. Portable archive was created; installer was skipped.'
}

Write-Host "Release artifacts: $artifactRoot"
