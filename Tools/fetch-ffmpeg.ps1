# Downloads the LGPL FFmpeg build used for recording and places ffmpeg.exe in Source\AudioReactor.App\ThirdParty\ffmpeg.
# ffmpeg.exe (~130 MB) is not stored in git. Verifies the SHA-256 of the archive.
param([string]$Url = "https://github.com/FFmpeg-Builds/FFmpeg-Builds/releases/download/latest/ffmpeg-n8.1-latest-win64-lgpl-8.1.zip")
$ErrorActionPreference = "Stop"
$dest = Join-Path $PSScriptRoot "..\Source\AudioReactor.App\ThirdParty\ffmpeg"; New-Item -ItemType Directory -Force $dest | Out-Null
$work = Join-Path ([IO.Path]::GetTempPath()) ("ar-ffmpeg-" + [guid]::NewGuid().ToString("N")); New-Item -ItemType Directory $work | Out-Null
try {
  $zip = Join-Path $work "ffmpeg.zip"; Invoke-WebRequest $Url -OutFile $zip
  $hash = (Get-FileHash $zip -Algorithm SHA256).Hash; Write-Host "SHA-256: $hash"
  Expand-Archive $zip -DestinationPath (Join-Path $work "x")
  $exe = Get-ChildItem (Join-Path $work "x") -Recurse -Filter ffmpeg.exe | Select-Object -First 1
  Copy-Item $exe.FullName $dest -Force
  Copy-Item (Join-Path $exe.Directory.Parent.FullName "LICENSE.txt") (Join-Path $dest "LICENSE-FFmpeg-LGPLv3.txt") -Force
  Write-Host "ffmpeg.exe installed to $dest (note: builds are rolling; the hash above may differ from the one in README.txt)"
} finally { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
