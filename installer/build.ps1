# Publishes VbEqualizer as a self-contained win-x64 single-file exe, then builds the MSI
# installer from it. Run from anywhere; paths are resolved relative to this script.
#
# Prerequisites (one-time):
#   dotnet tool install --global wix
#   wix eula accept wix7
#   wix extension add -g WixToolset.UI.wixext
#
# NOTE: WiX v7+ requires accepting FireGiant's "Open Source Maintenance Fee" EULA before it
# will run (`wix eula accept wix7`) -- see https://wixtoolset.org/osmf/. The fee itself only
# applies to users with >= US$10,000/year in revenue from software that uses WiX; anyone else
# is exempt from the fee (but still has to accept the EULA to use the prebuilt wix.exe).
# Building WiX from source instead avoids the EULA entirely, at the cost of more setup.

$ErrorActionPreference = "Stop"
$installerDir = $PSScriptRoot
$repoRoot = Split-Path -Parent $installerDir

Write-Host "Publishing self-contained win-x64 build..."
Push-Location "$repoRoot\src\VbEqualizer"
try {
    dotnet publish -c Release -p:PublishProfile=win-x64-selfcontained
} finally {
    Pop-Location
}

Write-Host "Building MSI installer..."
Push-Location $installerDir
try {
    wix build Product.wxs -ext WixToolset.UI.wixext -arch x64 -out MicEqualizerSetup.msi
} finally {
    Pop-Location
}

Write-Host "Done: $installerDir\MicEqualizerSetup.msi"
