param([Parameter(Mandatory=$true)][string]$Exe)
$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class IslandNative {
    public delegate bool EnumProc(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X; public int Y; public Point(int x, int y) { X=x; Y=y; } }
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
    public static IntPtr Find(uint id) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, data) => {
            uint processId; GetWindowThreadProcessId(window, out processId);
            var title = new StringBuilder(256); GetWindowText(window, title, title.Capacity);
            if (processId == id && title.ToString().StartsWith("Psychic Island")) { found = window; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@
$process = Start-Process -FilePath (Resolve-Path $Exe) -PassThru
try {
    Start-Sleep -Seconds 8
    $process.Refresh()
    if ($process.HasExited) { throw "Application exited prematurely: $($process.ExitCode)" }
    $handle = [IslandNative]::Find($process.Id)
    if ($handle -eq [IntPtr]::Zero) { throw 'Application did not create its island window.' }
    [void][IslandNative]::SetForegroundWindow($handle)
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '播放或暂停')
    # Hover must leave the clock compact; only a deliberate header click expands it.
    Add-Type -AssemblyName System.Windows.Forms
    $rect = $window.Current.BoundingRectangle
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point([int]($rect.X + $rect.Width / 2), [int]($rect.Y + 30))
    Start-Sleep -Seconds 2
    if ($window.Current.BoundingRectangle.Height -gt 100) { throw 'Hover unexpectedly expanded the island.' }
    $emptyPoint = New-Object IslandNative+Point([int]($rect.X + 20), [int]($rect.Y + 30))
    if ([IslandNative]::WindowFromPoint($emptyPoint) -eq $handle) { throw 'Transparent compact margins blocked the desktop.' }
    function Click-Header {
        $bounds = $window.Current.BoundingRectangle
        [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point([int]($bounds.X + $bounds.Width / 2), [int]($bounds.Y + 30))
        [IslandNative]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 70
        [IslandNative]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    }
    Click-Header
    Start-Sleep -Seconds 1
    $control = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    $expandedRect = $window.Current.BoundingRectangle
    Write-Host "Window bounds after click: $expandedRect"
    if ($null -eq $control) { throw 'Expanded media controls were not found.' }
    Write-Host "Control offscreen: $($control.Current.IsOffscreen)"
    Add-Type -AssemblyName System.Drawing
    $screen = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bitmap = New-Object System.Drawing.Bitmap($screen.Width, $screen.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($screen.Location, [System.Drawing.Point]::Empty, $screen.Size)
        $bitmap.Save((Join-Path $PWD 'smoke-screenshot.png'))
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
    if ($expandedRect.Height -lt 260 -or $control.Current.IsOffscreen) { throw 'Click did not visibly expand the media panel.' }
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(20, 500)
    Start-Sleep -Seconds 1
    if ($window.Current.BoundingRectangle.Height -lt 260) { throw 'Moving the pointer away collapsed the island.' }
    Click-Header
    Start-Sleep -Seconds 1
    if ($window.Current.BoundingRectangle.Height -gt 100) { throw 'Second click did not collapse the island.' }
    # Reverse an in-flight animation and ensure the old completion cannot win.
    Click-Header
    Start-Sleep -Milliseconds 100
    Click-Header
    Start-Sleep -Seconds 1
    if ($window.Current.BoundingRectangle.Height -gt 100) { throw 'Rapid toggle left the island expanded.' }
    Click-Header
    Start-Sleep -Seconds 1
    if ($window.Current.BoundingRectangle.Height -lt 260) { throw 'Island failed to reopen after rapid toggle.' }
    # Escape provides a keyboard dismissal path.
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Seconds 1
    if ($window.Current.BoundingRectangle.Height -gt 100) { throw 'Escape did not collapse the island.' }
    $beforeDrag = $window.Current.BoundingRectangle
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point([int]($beforeDrag.X + $beforeDrag.Width / 2), [int]($beforeDrag.Y + 30))
    [IslandNative]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 100
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point([int]($beforeDrag.X + $beforeDrag.Width / 2 + 70), [int]($beforeDrag.Y + 60))
    Start-Sleep -Milliseconds 100
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point([int]($beforeDrag.X + $beforeDrag.Width / 2 + 100), [int]($beforeDrag.Y + 70))
    Start-Sleep -Milliseconds 100
    [IslandNative]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Seconds 1
    $afterDrag = $window.Current.BoundingRectangle
    if ($afterDrag.Height -gt 100) { throw 'Dragging incorrectly toggled expansion.' }
    if ([Math]::Abs($afterDrag.X - $beforeDrag.X) -lt 10 -and [Math]::Abs($afterDrag.Y - $beforeDrag.Y) -lt 10) { throw 'Dragging did not move the island.' }
    if (![IslandNative]::PostMessage($handle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)) { throw 'Window did not accept close.' }
    if (!$process.WaitForExit(10000)) { throw 'Application did not shut down cleanly.' }
    if ($process.ExitCode -ne 0) { throw "Application exit code: $($process.ExitCode)" }
    Write-Host 'PASS: startup, hover stays compact, click expansion/collapse, pointer leave, rapid reversal, Escape, drag separation, transparent margin hit testing, accessible media control, and clean shutdown.'
} finally {
    if (!$process.HasExited) { Stop-Process -Id $process.Id -Force }
}
