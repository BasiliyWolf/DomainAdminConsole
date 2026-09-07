param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release',
    [switch]$Publish,
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root
try {
    dotnet restore .\DomainAdminConsole.csproj
    dotnet build .\DomainAdminConsole.csproj -c $Configuration --no-restore

    if ($Publish) {
        $sc = if ($SelfContained) { 'true' } else { 'false' }
        dotnet publish .\DomainAdminConsole.csproj `
            -c $Configuration `
            -r win-x64 `
            --self-contained $sc `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -o .\publish
    }
}
finally {
    Pop-Location
}
