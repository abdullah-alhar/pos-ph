# Builds the Windows installer (Setup.exe) with auto-update support.
#   powershell -ExecutionPolicy Bypass -File build\release.ps1 -Version 1.0.1
# Output: publish\releases\VetPOS.App-win-Setup.exe (+ the update packages to publish).
param([Parameter(Mandatory = $true)][string]$Version)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet tool restore
dotnet test
Remove-Item -Recurse -Force publish\win-x64 -ErrorAction SilentlyContinue
dotnet publish src\VetPos.App -c Release -o publish\win-x64 -p:Version=$Version
# Ship no debug symbols (harder to reverse-engineer; the shop only gets the compiled app).
Get-ChildItem publish\win-x64 -Recurse -Filter *.pdb | Remove-Item -Force
dotnet vpk pack -u VetPOS.App -v $Version -p publish\win-x64 -e VetPOS.exe -o publish\releases `
  --packTitle "VetPOS" --packAuthors "VetPOS" -i src\VetPos.App\Assets\vetpos.ico --runtime win-x64 --framework vcredist143-x64 --splashImage build\splash.png
Write-Host "Done: publish\releases" -ForegroundColor Green
