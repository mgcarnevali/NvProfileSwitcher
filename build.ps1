$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $here 'GameProfileSwitcher\GameProfileSwitcher.csproj'
$out = Join-Path $here 'publish'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host 'The .NET 8 SDK is required to build this project.' -ForegroundColor Yellow
    Write-Host 'Install it, then run this script again.'
    exit 1
}

dotnet restore $proj
dotnet publish $proj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $out

Write-Host "`nBuilt:" -ForegroundColor Green
Write-Host (Join-Path $out 'GameProfileSwitcher.exe')
