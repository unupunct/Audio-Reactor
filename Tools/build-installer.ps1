# Builds Release\publish (self-contained win-x64), generates the WiX file list, builds the MSI and the Setup.exe bundle.
# Requires: .NET SDK, local WiX tool (dotnet tool restore), Source\AudioReactor.App\ThirdParty\ffmpeg\ffmpeg.exe (see Tools\fetch-ffmpeg.ps1).
param([string]$Version = "1.0.0", [switch]$SkipPublish)
$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot\.."; Set-Location $root
$pub = Join-Path $root "Release\publish"; $inst = Join-Path $root "Installer"; $obj = Join-Path $inst "obj"
New-Item -ItemType Directory -Force $obj | Out-Null

if (-not (Test-Path "$root\Source\AudioReactor.App\ThirdParty\ffmpeg\ffmpeg.exe")) { throw "ffmpeg.exe missing. Run Tools\fetch-ffmpeg.ps1 first." }
if (-not $SkipPublish) {
  if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
  dotnet publish "$root\Source\AudioReactor.App" -c Release -r win-x64 --self-contained true -p:Version=$Version -o $pub
  if ($LASTEXITCODE) { throw "publish failed" }
}
# Remove debug symbols from the payload
Get-ChildItem $pub -Recurse -Include *.pdb | Remove-Item -Force

$pubLen = (Resolve-Path $pub).Path.Length + 1
# ---- generate Files.wxs (one component per file, per-user safe) ----
$sb = New-Object System.Text.StringBuilder
$null = $sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"><Fragment>')
$dirs = @{ "ROOT" = "INSTALLFOLDER" }; $n = 0
function Parent-Key([string]$rel) { $p = Split-Path $rel -Parent; if ([string]::IsNullOrEmpty($p)) { "ROOT" } else { $p } }
function Dir-Id([string]$rel) { if ($dirs.ContainsKey($rel)) { return $dirs[$rel] }; $script:n++; $dirs[$rel] = "D$script:n"; return $dirs[$rel] }
$allDirs = Get-ChildItem $pub -Recurse -Directory | ForEach-Object { $_.FullName.Substring($pubLen) } | Sort-Object
foreach ($d in $allDirs) { $null = Dir-Id $d }
foreach ($d in $allDirs) {
  $parent = Parent-Key $d; $null = $sb.AppendLine("<DirectoryRef Id=""$($dirs[$parent])""><Directory Id=""$($dirs[$d])"" Name=""$([System.Security.SecurityElement]::Escape((Split-Path $d -Leaf)))"" /></DirectoryRef>")
}
$null = $sb.AppendLine('<ComponentGroup Id="AppFiles">')
$files = Get-ChildItem $pub -Recurse -File; $i = 0
foreach ($f in $files) {
  $i++; $rel = $f.FullName.Substring($pubLen); $dir = Parent-Key $rel
  $src = [System.Security.SecurityElement]::Escape($f.FullName)
  $null = $sb.AppendLine("<Component Id=""C$i"" Directory=""$($dirs[$dir])""><File Id=""F$i"" Source=""$src"" KeyPath=""yes"" /></Component>")
}
$null = $sb.AppendLine('</ComponentGroup></Fragment></Wix>')
[IO.File]::WriteAllText("$obj\Files.wxs", $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Files.wxs: $i files"

# ---- license RTF (from LICENSE + third-party notices) ----
$lic = (Get-Content "$root\LICENSE" -Raw) + "`r`n`r`nThird-party components: see THIRD-PARTY-NOTICES.md (FFmpeg is LGPL v3)."
$rtf = "{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\fs18 " + ($lic -replace '\\', '\\\\' -replace '\{', '\{' -replace '\}', '\}' -replace "`r?`n", '\par ') + "}"
[IO.File]::WriteAllText("$obj\License.rtf", $rtf, [Text.Encoding]::ASCII)

$icon = Join-Path $root "Source\AudioReactor.App\Assets\AudioReactor.ico"
$msi = Join-Path $inst "AudioReactor.msi"
dotnet tool run wix build "$inst\Package.wxs" "$obj\Files.wxs" -arch x64 -ext WixToolset.UI.wixext -d Version=$Version -d IconFile=$icon -d LicenseRtf="$obj\License.rtf" -o $msi
if ($LASTEXITCODE) { throw "msi build failed" }
$exe = Join-Path $inst "AudioReactor-Setup.exe"
dotnet tool run wix build "$inst\Bundle.wxs" -arch x64 -ext WixToolset.BootstrapperApplications.wixext -d Version=$Version -d IconFile=$icon -d MsiFile=$msi -o $exe
if ($LASTEXITCODE) { throw "bundle build failed" }
Get-ChildItem $inst -Include *.msi, *.exe -Recurse -Depth 0 | Select-Object Name, @{n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } }
