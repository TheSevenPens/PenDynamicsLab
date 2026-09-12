---
name: run-pendynamicslab
description: Launch and drive the PenDynamicsLab app on Windows to verify a change visually — capture the window, click its chrome, and draw real strokes on the canvas via synthetic pen injection. Use this whenever you need to see the app actually working rather than trusting tests or reading code: screenshotting the UI, checking a theme or layout change, reproducing a drawing/canvas/DrawSurface bug, testing tab switching or window resizing, or confirming anything about how a stroke renders. Reach for it even if the request just says "run the app", "show me", "does this actually work", or "check it on screen" — driving this app has two non-obvious traps (synthetic mouse input is silently ignored on the canvas, and PowerShell defaults to DPI-unaware coordinates) that will waste a lot of time if rediscovered.
---

# Running and driving PenDynamicsLab

This app is an Avalonia (.NET 10, Windows) desktop GUI. There is no headless UI harness — `dotnet test` covers curve math only, so anything about rendering, layout, theming, or stroke behaviour has to be seen.

Two traps make this app harder to drive than a typical GUI. Both are cheap to avoid and expensive to rediscover, so they come first.

## Trap 1: synthetic mouse input does nothing on the canvas

`AvaloniaPointerSession` (in the sibling `WinPenKit` repo) begins its handler with:

```csharp
if (point.Pointer.Type != PointerType.Pen)
    return;
```

So `mouse_event` / `SendInput` mouse clicks produce **no pen points at all** — no stroke, no telemetry, and the readout stays on "Out of range" as if you had done nothing. This looks like a broken script rather than a rejected input, which is what makes it costly.

To draw, inject a synthetic **pen** device: `CreateSyntheticPointerDevice(PT_PEN, ...)` + `InjectSyntheticPointerInput`. `scripts/pen.ps1` wraps this.

Mouse input **does** work for ordinary chrome — tabs, combo boxes, buttons, sliders. Use the mouse for those and pen injection only for the canvas.

## Trap 2: PowerShell gets the wrong DPI awareness twice over

A DPI-unaware process sees *virtualized* coordinates: `GetWindowRect` returns logical pixels, and `PrintWindow` renders a partial, cropped frame that looks like a genuine screenshot of a smaller window. You will read coordinates off that frame, click confidently, and hit the wrong control.

`SetProcessDPIAware()` is **not** enough to fix this, and reaching for it is the second half of the trap. It asks for *System* awareness, which is only correct while the window sits on a monitor running at the system DPI. This machine has three monitors and they do not agree:

```
PRIMARY     (0,0)–(3840,2160)       216 dpi  (2.25x)
secondary   (3840,0)–(7680,2160)    216 dpi  (2.25x)
secondary   (2151,2160)–(4711,3600) 168 dpi  (1.75x)
```

System DPI is 216, so on the 168 dpi panel a system-aware process is handed a fictional coordinate space scaled by 216/168 = 1.2857, and every measurement is wrong by that factor without looking wrong. In one session `GetWindowRect` reported the app's window 1.2857× larger than it was, `PrintWindow` rendered the real window into that oversized bitmap, and the blank margin covered 22% of the frame — which reads exactly like the application failing to paint part of itself, and was reported as a rendering bug before the harness turned out to be the thing at fault.

`scripts/win.ps1` now requests **PerMonitorV2**, which gets true physical coordinates on every monitor, and warns if it could not. **Dot-source it in every PowerShell call, before anything else touches DPI** — process awareness does not persist between tool invocations, and it can only be set once per process, so whoever asks first wins.

## Trap 3: a window hanging off its monitor silently eats input

Injected pointer input is delivered by absolute screen coordinate, so any part of the window that is off its monitor — or underneath the taskbar — receives nothing. The app is still running and still painting; strokes aimed at that region just never arrive.

This is easy to miss because the failure is partial and looks like a bug in whatever you are testing. On a multi-monitor setup the window can restore straddling two displays: in one session the window ran to y=2214 while the monitor's work area ended at y=2052, so the bottom 162px sat over the taskbar. Strokes drawn there produced nothing, while identical strokes higher up worked — which reads exactly like "the lower canvas is broken".

Call `Set-WindowDrawable` before any injection. It maximizes onto whichever monitor the window is mostly on and throws if the client area still does not fit:

```powershell
Set-WindowDrawable -Hwnd $h | Format-List
```

Measure the **client** rect, not the window rect — `Test-WindowDrawable` does. A maximized window's *window* rect overhangs the work area by the invisible resize border (typically 14px), which is not a real overflow and will make a naive check report failure on a perfectly good window.

## Quickstart

```powershell
$S = "<repo>\.claude\skills\run-pendynamicslab\scripts"
. "$S\win.ps1"; . "$S\pen.ps1"
$h = Get-AppHwnd
Set-WindowDrawable -Hwnd $h | Out-Null   # see Trap 3 — do this before injecting
Save-Shot -Hwnd $h -Path "shot.png"
```

Build and launch first if it isn't running:

```powershell
dotnet build --nologo
Start-Process ".\bin\Debug\net10.0-windows\PenDynamicsLab.exe"
Start-Sleep -Seconds 6
```

If the build fails with `MSB3027` / file locked, a previous instance is still running. Ask the user before killing it — it may be theirs, with unsaved presets.

## The coordinate model

Everything is expressed in **logical** window-relative coordinates, which is what `Save-Shot` gives you.

`Save-Shot` captures at full physical resolution and downscales by the DPI factor before saving, so the PNG matches the logical coordinate space 1:1. Read a position straight off that image and pass it to `Click-L` / `Drag-L`, which convert to physical screen coordinates internally.

Do not hardcode positions from this file. Capture, look, then click — the layout shifts with window size, and cards reflow when an expander collapses or the processing order changes.

`Assert-Ours` refuses any click whose target window doesn't belong to the app, so a stale coordinate fails loudly instead of clicking into someone else's window.

## Drawing a stroke

Pen input must go to the canvas, so first select the pointer API that accepts it:

1. Set **Pen API** to `Avalonia Pointer` (top-left combo). With `Wintab` selected and no tablet attached, nothing arrives.
2. Inject the stroke:

```powershell
$dev = New-PenDevice
$a = LP -Hwnd $h -LX 700 -LY 340    # logical -> physical
$b = LP -Hwnd $h -LX 700 -LY 890
Draw-PenLine -Dev $dev -X1 $a[0] -Y1 $a[1] -X2 $b[0] -Y2 $b[1] -Steps 45 -Pressure 800
[Pen]::DestroySyntheticPointerDevice($dev)
```

`Pressure` is 0–1024. `Draw-PenLine` sends a hover point, then down, then interpolated updates, then up — the app needs the in-contact flags and a nonzero pressure or it treats the samples as a hover and draws nothing.

Confirm it worked by reading the telemetry ribbon: Pressure and Orientation should show real numbers instead of `--`.

### Combo boxes open a separate window

Avalonia dropdowns are their own top-level window, so `PrintWindow` on the main window will not show them. Capture that screen region with `CopyFromScreen` instead, and when clicking an item verify the target belongs to the app's **process** (`GetWindowThreadProcessId`) rather than to the main window handle — `Assert-Ours` will reject a legitimate popup click.

## Things worth knowing about this UI

- **The canvas is only in the right-hand tabs.** `Stroke` is a single processed canvas; `Stroke compare` stacks processed over raw **vertically** (same width, roughly half height each).
- **The processed surface is one bitmap shared by two hosts.** `StrokeCanvasView` pins its `Image` at (0,0) on a `Canvas` inside a `ClipToBounds` border, so a bitmap larger than its host is clipped for display and kept whole. A mark that looks cut off in the Compare pane is not necessarily lost — switch back and check before concluding anything.
- **Shrinking the window destroys pixels.** `DrawSurface.EnsureSize` preserves content by blitting the old bitmap in at the origin, so anything outside the smaller bounds is gone and does not come back when you grow the window again. Useful to know both as a real defect and as a way to invalidate a test accidentally.
- **A stroke needs nonzero pressure** unless "Draw at zero pressure" is ticked.

## Verifying, honestly

Look at the screenshot. A blank or partial frame means the capture or the launch failed, not that the feature is broken — check the frame is the size you expect before drawing conclusions from it.

When a claim is about pixels surviving some operation, capture before *and* after and compare the same landmark in both, rather than reasoning from one image. A display can clip content it has not destroyed, and the two are indistinguishable in a single frame.
