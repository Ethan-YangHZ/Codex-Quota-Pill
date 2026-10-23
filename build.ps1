param([switch]$Publish, [switch]$Test)
$ErrorActionPreference = 'Stop'
# These variables affect this build process only, never user/system environment settings.
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '../tools/cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '../tools/packages'
$sdk = Join-Path $PSScriptRoot '../tools/dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $sdk)) { $sdk = (Get-Command dotnet -ErrorAction Stop).Source }
Push-Location $PSScriptRoot
try {
    if ($Test) { & $sdk run --project tests/QuotaPill.Tests -c Release -p:UseSharedCompilation=false -v quiet }
    elseif ($Publish) { & $sdk publish src/QuotaPill -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:UseSharedCompilation=false --disable-build-servers -o '../../03_最终交付/Windows-x64' -v quiet }
    else { & $sdk build src/QuotaPill -c Release -p:UseSharedCompilation=false --disable-build-servers -v quiet }
    if ($LASTEXITCODE -ne 0) { throw "dotnet exited $LASTEXITCODE" }
} finally { Pop-Location }
