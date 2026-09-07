param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release',
    [switch]$Publish,
    [switch]$SelfContained,
    [switch]$Zip
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root
try {
    dotnet restore .\DomainAdminConsole.csproj
    dotnet build .\DomainAdminConsole.csproj -c $Configuration --no-restore

    if ($Publish -or $Zip) {
        $sc = if ($SelfContained) { 'true' } else { 'false' }
        dotnet publish .\DomainAdminConsole.csproj `
            -c $Configuration `
            -r win-x64 `
            --self-contained $sc `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -o .\publish
    }

    if ($Zip) {
        $archive = Join-Path $root 'DomainAdminConsole-win-x64.zip'
        if (Test-Path $archive) { Remove-Item $archive -Force }
        Compress-Archive -Path .\publish\* -DestinationPath $archive -CompressionLevel Optimal
        Write-Host "Update package: $archive"
    }
}
finally {
    Pop-Location
}
