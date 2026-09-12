Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Runtime.InteropServices;
public class W {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f,int dx,int dy,uint d,IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern IntPtr GetThreadDpiAwarenessContext();
  [DllImport("user32.dll")] public static extern uint GetAwarenessFromDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int cx,int cy,uint f);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int nCmdShow);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
}
"@
# Per-monitor-v2, not SetProcessDPIAware.
#
# SetProcessDPIAware asks for SYSTEM awareness, which is only correct while the window is on a
# monitor running at the system DPI. On any other monitor Windows hands a system-aware process
# virtualized coordinates - a fictional screen space scaled by systemDpi/monitorDpi - and every
# measurement taken here is wrong by that factor without looking wrong.
#
# It cost real time to find. On a 168 dpi panel beside two 216 dpi ones, GetWindowRect reported
# the app's window 1.2857x larger than it was, so PrintWindow rendered the real window into an
# oversized bitmap and left blank margins covering 22% of the frame. That reads exactly like the
# application failing to paint part of itself, and was reported as such before the harness turned
# out to be the thing at fault.
#
# PerMonitorV2 (context -4) gets true physical coordinates on every monitor. Process DPI awareness
# can only be set once, so this must run before anything else asks for it.
if (-not [W]::SetProcessDpiAwarenessContext([IntPtr](-4))) {
  # Already set by the host process, or too old to know the call. Fall back, then verify.
  [void][W]::SetProcessDPIAware()
}
$script:DpiAwareness = [W]::GetAwarenessFromDpiAwarenessContext([W]::GetThreadDpiAwarenessContext())
if ($script:DpiAwareness -ne 2) {
  Write-Warning ("win.ps1: DPI awareness is {0} (2 = per-monitor). Coordinates and captures will " -f $script:DpiAwareness +
                 "be wrong on any monitor whose DPI differs from the system DPI.")
}

function Get-AppHwnd { param($ProcName='PenDynamicsLab')
  $p = Get-Process -Name $ProcName -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
  if (-not $p) { throw "no window for $ProcName" }
  return $p.MainWindowHandle
}
function Get-Scale { param([IntPtr]$Hwnd) return [double]([W]::GetDpiForWindow($Hwnd))/96.0 }
function Get-Geom { param([IntPtr]$Hwnd)
  $wr = New-Object W+RECT; [void][W]::GetWindowRect($Hwnd,[ref]$wr)
  $o = New-Object W+POINT; [void][W]::ClientToScreen($Hwnd,[ref]$o)
  [pscustomobject]@{ L=$wr.L;T=$wr.T;W=$wr.R-$wr.L;H=$wr.B-$wr.T;Scale=(Get-Scale -Hwnd $Hwnd);CliX=$o.X;CliY=$o.Y }
}
function Save-Shot { param([IntPtr]$Hwnd,[string]$Path,[int]$Flag=2)
  $s = Get-Scale -Hwnd $Hwnd
  $r = New-Object W+RECT; [void][W]::GetWindowRect($Hwnd,[ref]$r)
  $w = $r.R-$r.L; $h = $r.B-$r.T
  $bmp = New-Object System.Drawing.Bitmap($w,$h)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc(); $ok = [W]::PrintWindow($Hwnd,$hdc,$Flag); $g.ReleaseHdc($hdc); $g.Dispose()
  $lw = [int]($w/$s); $lh = [int]($h/$s)
  $small = New-Object System.Drawing.Bitmap($lw,$lh)
  $g2 = [System.Drawing.Graphics]::FromImage($small)
  $g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g2.DrawImage($bmp,0,0,$lw,$lh); $g2.Dispose()
  $small.Save($Path,[System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose(); $small.Dispose()
  "PrintWindow=$ok phys=${w}x${h} logical=${lw}x${lh} scale=$s -> $Path"
}
function Assert-Ours { param([int]$X,[int]$Y,[IntPtr]$Hwnd)
  $pt = New-Object W+POINT; $pt.X=$X; $pt.Y=$Y
  $under = [W]::WindowFromPoint($pt)
  $root = [W]::GetAncestor($under,2)
  if ($root -ne $Hwnd) { throw "point ($X,$Y) belongs to hwnd $root not ours ($Hwnd) - refusing" }
}
function LP { param([IntPtr]$Hwnd,[int]$LX,[int]$LY)
  $s = Get-Scale -Hwnd $Hwnd
  $r = New-Object W+RECT; [void][W]::GetWindowRect($Hwnd,[ref]$r)
  return @([int]($r.L + $LX*$s), [int]($r.T + $LY*$s))
}
function Click-L { param([IntPtr]$Hwnd,[int]$LX,[int]$LY,[switch]$NoCheck)
  $p = LP -Hwnd $Hwnd -LX $LX -LY $LY
  if (-not $NoCheck) { Assert-Ours -X $p[0] -Y $p[1] -Hwnd $Hwnd }
  [void][W]::SetCursorPos($p[0],$p[1]); Start-Sleep -Milliseconds 120
  [W]::mouse_event(0x0002,0,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 70
  [W]::mouse_event(0x0004,0,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 180
  "clicked logical($LX,$LY) -> phys($($p[0]),$($p[1]))"
}
function Drag-L { param([IntPtr]$Hwnd,[int]$LX1,[int]$LY1,[int]$LX2,[int]$LY2,[int]$Steps=45)
  $a = LP -Hwnd $Hwnd -LX $LX1 -LY $LY1
  $b = LP -Hwnd $Hwnd -LX $LX2 -LY $LY2
  Assert-Ours -X $a[0] -Y $a[1] -Hwnd $Hwnd
  Assert-Ours -X $b[0] -Y $b[1] -Hwnd $Hwnd
  [void][W]::SetCursorPos($a[0],$a[1]); Start-Sleep -Milliseconds 200
  [W]::mouse_event(0x0002,0,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 120
  for ($i=1;$i -le $Steps;$i++) {
    $x=[int]($a[0]+($b[0]-$a[0])*$i/$Steps); $y=[int]($a[1]+($b[1]-$a[1])*$i/$Steps)
    [void][W]::SetCursorPos($x,$y); Start-Sleep -Milliseconds 30
  }
  [W]::mouse_event(0x0004,0,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 200
  "dragged logical($LX1,$LY1)->($LX2,$LY2)"
}

# Popups (combo dropdowns) are their own top-level window, so Assert-Ours rejects them.
# Check process ownership instead before clicking into one.
function Assert-OurProcess { param([int]$X,[int]$Y,[string]$ProcName='PenDynamicsLab')
  $ourPid = (Get-Process -Name $ProcName -ErrorAction Stop | Select-Object -First 1).Id
  $pt = New-Object W+POINT; $pt.X=$X; $pt.Y=$Y
  $u = [W]::WindowFromPoint($pt); $tp = 0
  [void][W]::GetWindowThreadProcessId($u,[ref]$tp)
  if ($tp -ne $ourPid) { throw "point ($X,$Y) belongs to pid $tp, not $ProcName ($ourPid) - refusing" }
  return $u
}
function Click-Screen { param([int]$X,[int]$Y,[string]$ProcName='PenDynamicsLab')
  [void](Assert-OurProcess -X $X -Y $Y -ProcName $ProcName)
  [void][W]::SetCursorPos($X,$Y); Start-Sleep -Milliseconds 130
  [W]::mouse_event(0x0002,0,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 70
  [W]::mouse_event(0x0004,0,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 200
  "clicked screen($X,$Y)"
}
# Capture an arbitrary screen region (for dropdowns/popups PrintWindow cannot see),
# downscaled to logical pixels so coordinates match Save-Shot output.
function Save-Region { param([int]$X,[int]$Y,[int]$W,[int]$H,[string]$Path,[double]$Scale=0)
  if ($Scale -le 0) { $Scale = (Get-Scale -Hwnd (Get-AppHwnd)) }
  $bmp = New-Object System.Drawing.Bitmap($W,$H)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($X,$Y,0,0,(New-Object System.Drawing.Size($W,$H))); $g.Dispose()
  $lw=[int]($W/$Scale); $lh=[int]($H/$Scale)
  $sm = New-Object System.Drawing.Bitmap($lw,$lh)
  $g2=[System.Drawing.Graphics]::FromImage($sm)
  $g2.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g2.DrawImage($bmp,0,0,$lw,$lh); $g2.Dispose()
  $sm.Save($Path,[System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); $sm.Dispose()
  "region ${W}x${H} -> ${lw}x${lh} $Path"
}
function Resize-Win { param([IntPtr]$Hwnd,[int]$PhysW,[int]$PhysH)
  $r = New-Object W+RECT; [void][W]::GetWindowRect($Hwnd,[ref]$r)
  [void][W]::SetWindowPos($Hwnd,[IntPtr]::Zero,$r.L,$r.T,$PhysW,$PhysH,0x4)
  "resized to ${PhysW}x${PhysH} physical"
}

# Pointer injection lands at absolute screen coordinates, so any part of the window
# hanging off its monitor - or sitting under the taskbar - simply receives nothing.
# Windows will happily restore a window straddling two monitors, and the app's saved
# geometry may put it there, so check before injecting rather than after a stroke
# mysteriously fails to appear.
#
# Measure the CLIENT rect, not the window rect: a maximized window's rect overhangs
# the work area by the invisible resize border, which is not a real overflow.
function Test-WindowDrawable { param([IntPtr]$Hwnd)
  Add-Type -AssemblyName System.Windows.Forms | Out-Null
  $sc = [System.Windows.Forms.Screen]::FromHandle($Hwnd); $wa = $sc.WorkingArea
  $o = New-Object W+POINT;  [void][W]::ClientToScreen($Hwnd,[ref]$o)
  $cr = New-Object W+RECT;  [void][W]::GetClientRect($Hwnd,[ref]$cr)
  [pscustomobject]@{
    Monitor = $sc.DeviceName
    ClientLeft = $o.X; ClientTop = $o.Y
    ClientRight = $o.X + $cr.R; ClientBottom = $o.Y + $cr.B
    WorkLeft = $wa.Left; WorkTop = $wa.Top; WorkRight = $wa.Right; WorkBottom = $wa.Bottom
    Fits = ($o.X -ge $wa.Left) -and ($o.Y -ge $wa.Top) -and
           (($o.X + $cr.R) -le $wa.Right) -and (($o.Y + $cr.B) -le $wa.Bottom)
  }
}

# Maximize onto whichever monitor the window is mostly on, then confirm the client
# area is fully inside that monitor's work area. Call this before any pen injection.
function Set-WindowDrawable { param([IntPtr]$Hwnd)
  $before = Test-WindowDrawable -Hwnd $Hwnd
  if ($before.Fits) { return $before }
  [void][W]::ShowWindow($Hwnd, 3)   # SW_MAXIMIZE
  Start-Sleep -Milliseconds 1200
  $after = Test-WindowDrawable -Hwnd $Hwnd
  if (-not $after.Fits) {
    throw ("window client ({0},{1})-({2},{3}) still outside {4} work area ({5},{6})-({7},{8}) - " +
           "pen injection into the overhang will be swallowed" -f
           $after.ClientLeft,$after.ClientTop,$after.ClientRight,$after.ClientBottom,
           $after.Monitor,$after.WorkLeft,$after.WorkTop,$after.WorkRight,$after.WorkBottom)
  }
  return $after
}
