# Captures ONLY an Audio Reactor window (never the full screen) using PrintWindow.
# -Title selects another top-level window of the app by (partial) title, e.g. "Export for YouTube".
param([string]$Out, [int]$WaitMs = 0, [string]$Title = "")
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public class Win {
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
 [DllImport("user32.dll")] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
 delegate bool EnumProc(IntPtr h, IntPtr l);
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
 public static IntPtr Find(uint pid, string title) {
   IntPtr found = IntPtr.Zero;
   EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) { var sb = new StringBuilder(256); GetWindowText(h, sb, 256); if (sb.ToString().Contains(title)) { found = h; return false; } } return true; }, IntPtr.Zero);
   return found; }
}
"@
if ($WaitMs -gt 0) { Start-Sleep -Milliseconds $WaitMs }
$p = Get-Process AudioReactor -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Error "Audio Reactor is not running"; exit 1 }
$h = if ($Title) { [Win]::Find([uint32]$p.Id, $Title) } else { $p.MainWindowHandle }
if ($h -eq [IntPtr]::Zero) { Write-Error "Window not found"; exit 1 }
$r = New-Object Win+RECT; [Win]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.R - $r.L; $ht = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
[Win]::PrintWindow($h, $dc, 2) | Out-Null; $g.ReleaseHdc($dc)
$bmp.Save($Out); "saved ${w}x${ht} -> $Out"
