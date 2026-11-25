# Builds the per-user MSI installers (English and Italian) from dist\Marea.
# One-time prerequisites:
#   dotnet tool restore
#   dotnet wix eula accept wix7        <- accepts the WiX v7 OSMF EULA: done by whoever builds
#   dotnet wix extension add WixToolset.Util.wixext/7.0.1
# Usage: powershell -File build\installer.ps1 [-Version 0.1.0]
param([string]$Version = "0.2.0")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location (Join-Path $root "installer")
try {
    if (-not (Test-Path ..\dist\Marea\Marea.exe)) { throw "Run build\publish.ps1 first" }
    dotnet tool restore | Out-Null
    foreach ($culture in "en-US", "it-IT") {
        $msi = "..\dist\Marea-$Version-win-x64-$culture.msi"
        dotnet wix build Marea.wxs -arch x64 -d Version=$Version -culture $culture -loc "Marea.$culture.wxl" `
            -ext WixToolset.Util.wixext -sice ICE38 -sice ICE64 -sice ICE91 -o $msi
        if ($LASTEXITCODE -ne 0) { throw "wix build failed ($culture)" }
        Get-FileHash $msi -Algorithm SHA256 | ForEach-Object { "{0}  {1}" -f $_.Hash.ToLower(), (Split-Path $msi -Leaf) } |
            Set-Content "$msi.sha256" -Encoding ascii
        Write-Host "Created $msi"
    }
}
finally {
    Pop-Location
}
