Add-Type @"
using System;using System.Runtime.InteropServices;
public static class Pen {
  public const uint PT_PEN = 3;
  public const uint PF_NONE=0, PF_NEW=0x1, PF_INRANGE=0x2, PF_INCONTACT=0x4,
                    PF_PRIMARY=0x2000, PF_DOWN=0x10000, PF_UPDATE=0x20000, PF_UP=0x40000;
  public const uint PEN_MASK_PRESSURE = 0x1;

  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int l,t,r,b; }

  [StructLayout(LayoutKind.Sequential)]
  public struct POINTER_INFO {
    public uint pointerType; public uint pointerId; public uint frameId; public uint pointerFlags;
    public IntPtr sourceDevice; public IntPtr hwndTarget;
    public POINT ptPixelLocation; public POINT ptHimetricLocation;
    public POINT ptPixelLocationRaw; public POINT ptHimetricLocationRaw;
    public uint dwTime; public uint historyCount; public int InputData; public uint dwKeyStates;
    public ulong PerformanceCount; public int ButtonChangeType;
  }
  [StructLayout(LayoutKind.Sequential)]
  public struct POINTER_PEN_INFO {
    public POINTER_INFO pointerInfo;
    public uint penFlags; public uint penMask; public uint pressure; public uint rotation;
    public int tiltX; public int tiltY;
  }
  [StructLayout(LayoutKind.Sequential)]
  public struct POINTER_TOUCH_INFO {
    public POINTER_INFO pointerInfo; public uint touchFlags; public uint touchMask;
    public RECT rcContact; public RECT rcContactRaw; public uint orientation; public uint pressure;
  }
  [StructLayout(LayoutKind.Explicit)]
  public struct POINTER_TYPE_INFO {
    [FieldOffset(0)] public uint type;
    [FieldOffset(8)] public POINTER_TOUCH_INFO touchInfo;
    [FieldOffset(8)] public POINTER_PEN_INFO penInfo;
  }

  [DllImport("user32.dll", SetLastError=true)]
  public static extern IntPtr CreateSyntheticPointerDevice(uint pointerType, uint maxCount, uint mode);
  [DllImport("user32.dll", SetLastError=true)]
  public static extern bool InjectSyntheticPointerInput(IntPtr device, POINTER_TYPE_INFO[] info, uint count);
  [DllImport("user32.dll")] public static extern void DestroySyntheticPointerDevice(IntPtr device);

  public static POINTER_TYPE_INFO Make(int x,int y,uint flags,uint pressure) {
    var ti = new POINTER_TYPE_INFO(); ti.type = PT_PEN;
    var pi = new POINTER_PEN_INFO();
    pi.pointerInfo.pointerType = PT_PEN;
    pi.pointerInfo.pointerId = 1;
    pi.pointerInfo.pointerFlags = flags;
    pi.pointerInfo.ptPixelLocation.x = x;
    pi.pointerInfo.ptPixelLocation.y = y;
    pi.penMask = PEN_MASK_PRESSURE;
    pi.pressure = pressure;   // 0..1024
    ti.penInfo = pi;
    return ti;
  }
}
"@
function New-PenDevice { 
  $d = [Pen]::CreateSyntheticPointerDevice([Pen]::PT_PEN, 1, 1)
  if ($d -eq [IntPtr]::Zero) { throw "CreateSyntheticPointerDevice failed: $([ComponentModel.Win32Exception]::new([Runtime.InteropServices.Marshal]::GetLastWin32Error()).Message)" }
  return $d
}
function Send-Pen { param([IntPtr]$Dev,[int]$X,[int]$Y,[uint32]$Flags,[uint32]$Pressure)
  $arr = @([Pen]::Make($X,$Y,$Flags,$Pressure))
  $ok = [Pen]::InjectSyntheticPointerInput($Dev,$arr,1)
  if (-not $ok) { throw "inject failed at ($X,$Y): $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
}
function Draw-PenLine { param([IntPtr]$Dev,[int]$X1,[int]$Y1,[int]$X2,[int]$Y2,[int]$Steps=40,[uint32]$Pressure=700)
  $F=[Pen]
  # hover in range first
  Send-Pen -Dev $Dev -X $X1 -Y $Y1 -Flags ($F::PF_INRANGE -bor $F::PF_UPDATE) -Pressure 0
  Start-Sleep -Milliseconds 60
  Send-Pen -Dev $Dev -X $X1 -Y $Y1 -Flags ($F::PF_DOWN -bor $F::PF_INRANGE -bor $F::PF_INCONTACT) -Pressure $Pressure
  Start-Sleep -Milliseconds 40
  for ($i=1;$i -le $Steps;$i++) {
    $x=[int]($X1+($X2-$X1)*$i/$Steps); $y=[int]($Y1+($Y2-$Y1)*$i/$Steps)
    Send-Pen -Dev $Dev -X $x -Y $y -Flags ($F::PF_UPDATE -bor $F::PF_INRANGE -bor $F::PF_INCONTACT) -Pressure $Pressure
    Start-Sleep -Milliseconds 20
  }
  Send-Pen -Dev $Dev -X $X2 -Y $Y2 -Flags ($F::PF_UP -bor $F::PF_INRANGE) -Pressure 0
  Start-Sleep -Milliseconds 60
  "pen line ($X1,$Y1)->($X2,$Y2) p=$Pressure"
}
