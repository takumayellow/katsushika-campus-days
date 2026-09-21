# Smoke test: launch the built player, wait, capture the primary screen, then close it.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools/smoke_run.ps1 [-Exe path] [-WaitSec 25] [-Out path] [-Extra '-kcd-start','campus']
param(
  [string]$Exe = "$PSScriptRoot\..\build\Windows\KatsushikaCampusDays.exe",
  [int]$WaitSec = 25,
  [string]$Out = "$PSScriptRoot\..\docs\screenshots\smoke_title.png",
  [string]$PlayerLog = "$PSScriptRoot\..\unity\logs\smoke_player.log",
  [string[]]$Extra = @()
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
$Exe = [IO.Path]::GetFullPath($Exe); $Out = [IO.Path]::GetFullPath($Out); $PlayerLog = [IO.Path]::GetFullPath($PlayerLog)
if (!(Test-Path $Exe)) { Write-Error "exe not found: $Exe" }
# -File 経由では -Extra "'-kcd-start','campus'" のように 1 要素で届くので、区切りで割って引用符を剥がす
$ExtraArgs = @()
foreach ($e in $Extra) { foreach ($piece in ($e -split '[,\s]+')) { $t = $piece.Trim("'`""); if ($t -ne '') { $ExtraArgs += $t } } }
$argList = @('-screen-width','1600','-screen-height','900','-screen-fullscreen','0','-logFile',"`"$PlayerLog`"") + $ExtraArgs
Write-Output ("SMOKE_ARGS " + ($ExtraArgs -join ' '))
$p = Start-Process -FilePath $Exe -ArgumentList $argList -PassThru
Start-Sleep -Seconds $WaitSec
if ($p.HasExited) { Write-Output "SMOKE_FAIL exited early code=$($p.ExitCode)"; exit 2 }
$sig = @'
using System; using System.Runtime.InteropServices;
public static class KcdWin {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
}
'@
Add-Type -TypeDefinition $sig
# 125% 表示だと GetWindowRect が論理座標で返り、PrintWindow は物理ピクセルで描くので右下が切れる。物理座標に揃える。
[KcdWin]::SetProcessDPIAware() | Out-Null
$p.Refresh(); $h = $p.MainWindowHandle
if ($h -eq [IntPtr]::Zero) { Write-Output "SMOKE_FAIL no main window"; Stop-Process -Id $p.Id -Force; exit 3 }
[KcdWin]::SetForegroundWindow($h) | Out-Null; Start-Sleep -Milliseconds 800
$r = New-Object KcdWin+RECT; [KcdWin]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.R - $r.L; $hh = $r.B - $r.T
# 論理座標のままなら物理ピクセルに直す（PrintWindow は物理ピクセルで描く）。要求した 1600 幅と枠込みの論理幅の比で倍率を出す。
$scale = 1.0; if (($w - 14) -lt 1600) { $scale = 1600.0 / ($w - 14) }
$w = [int][Math]::Ceiling($w * $scale) + 2; $hh = [int][Math]::Ceiling($hh * $scale) + 2
Write-Output "SMOKE_RECT logical=$($r.R - $r.L)x$($r.B - $r.T) scale=$scale bitmap=${w}x${hh}"
$bmp = New-Object System.Drawing.Bitmap $w, $hh
$g = [System.Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc()
$ok = [KcdWin]::PrintWindow($h, $dc, 2)   # 2 = PW_RENDERFULLCONTENT (works even when occluded)
$g.ReleaseHdc($dc)
if (-not $ok) { Write-Output "SMOKE_WARN PrintWindow failed" }
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Stop-Process -Id $p.Id -Force
Write-Output "SMOKE_OK pid=$($p.Id) shot=$Out"
