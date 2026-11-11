param([Parameter(Mandatory=$true)][string]$Exe)
$ErrorActionPreference = 'Stop'
$process = Start-Process -FilePath (Resolve-Path $Exe) -PassThru
try {
    Start-Sleep -Seconds 8
    $process.Refresh()
    if ($process.HasExited) { throw "Application exited prematurely: $($process.ExitCode)" }
    if ($process.MainWindowHandle -eq 0) { throw 'Application did not create a window.' }
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '播放或暂停')
    # Expand by moving the pointer onto the island, then verify the media control is exposed.
    Add-Type -AssemblyName System.Windows.Forms
    $rect = $window.Current.BoundingRectangle
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point([int]($rect.X + $rect.Width / 2), [int]($rect.Y + 30))
    Start-Sleep -Seconds 2
    $control = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $control) { throw 'Expanded media controls were not found.' }
    Add-Type -AssemblyName System.Drawing
    $screen = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bitmap = New-Object System.Drawing.Bitmap($screen.Width, $screen.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($screen.Location, [System.Drawing.Point]::Empty, $screen.Size)
        $bitmap.Save((Join-Path $PWD 'smoke-screenshot.png'))
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
    if (!$process.CloseMainWindow()) { throw 'Window did not accept close.' }
    if (!$process.WaitForExit(10000)) { throw 'Application did not shut down cleanly.' }
    if ($process.ExitCode -ne 0) { throw "Application exit code: $($process.ExitCode)" }
    Write-Host 'PASS: startup, hover expansion, accessible media control, and clean shutdown.'
} finally {
    if (!$process.HasExited) { Stop-Process -Id $process.Id -Force }
}
