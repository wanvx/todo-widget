# DPI-aware screen capture helper for verifying the desktop widget.
# Run:  pwsh -File tools\capture.ps1 -Tag 02
param(
    [string]$OutDir = 'C:\小工具\代办事情清单\artifacts',
    [string]$Tag = 'shot',
    [string]$ProcessName = 'TodoWidget',
    [int]$Pad = 12
)

Add-Type -Namespace Cap -Name Api -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)] public static extern bool SetProcessDpiAwarenessContext(System.IntPtr value);
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)] public static extern bool IsWindowVisible(System.IntPtr h);
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)] public static extern bool EnumWindows(EnumProc cb, System.IntPtr p);
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode, SetLastError=true)] public static extern IntPtr FindWindowW(string cls, string win);
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct RECT { public int Left, Top, Right, Bottom; }
public delegate bool EnumProc(System.IntPtr h, System.IntPtr p);
'@

# Must run before any window is created in this process so the capture is 1:1
# with physical pixels instead of being DPI-virtualised and upscaled.
[void][Cap.Api]::SetProcessDpiAwarenessContext([System.IntPtr](-4))

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

$vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
Write-Output "VirtualScreen $($vs.Width)x$($vs.Height) @ $($vs.X),$($vs.Y)"

# --- full desktop -------------------------------------------------------------
$full = New-Object System.Drawing.Bitmap($vs.Width, $vs.Height)
$g = [System.Drawing.Graphics]::FromImage($full)
$g.CopyFromScreen($vs.X, $vs.Y, 0, 0, (New-Object System.Drawing.Size($vs.Width, $vs.Height)))
$g.Dispose()
$fullPath = Join-Path $OutDir "$Tag-full.png"
$full.Save($fullPath, [System.Drawing.Imaging.ImageFormat]::Png)
$full.Dispose()
Write-Output "full    -> $fullPath"

# --- widget crop --------------------------------------------------------------
# Locate the widget by owning process: it is a WS_EX_TOOLWINDOW window with
# ShowInTaskbar=false, so FindWindow-by-title and MainWindowHandle are both unreliable.
$target = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $target) {
    Write-Output "crop    -> process '$ProcessName' is not running"
    exit 0
}

$targetPid = [uint32]$target.Id
$script:hits = New-Object System.Collections.ArrayList
$cb = [Cap.Api+EnumProc] {
    param($hw, $lp)
    $owner = [uint32]0
    [void][Cap.Api]::GetWindowThreadProcessId($hw, [ref]$owner)
    if ($owner -eq $targetPid -and [Cap.Api]::IsWindowVisible($hw)) {
        $rc = New-Object Cap.Api+RECT
        [void][Cap.Api]::GetWindowRect($hw, [ref]$rc)
        # 折叠后窗口只有 ~160x40 物理像素，阈值必须放低，否则截不到小标签。
        if (($rc.Right - $rc.Left) -gt 100 -and ($rc.Bottom - $rc.Top) -gt 20) {
            [void]$script:hits.Add($rc)
        }
    }
    return $true
}
[void][Cap.Api]::EnumWindows($cb, [System.IntPtr]::Zero)

if ($script:hits.Count -eq 0) {
    Write-Output "crop    -> no widget window found for pid $targetPid"
    exit 0
}

# The widget and any open dialog are separate top-level windows of the same
# process, so every qualifying window gets its own crop.
$i = 0
foreach ($r in $script:hits) {
    $i++
    $w = $r.Right - $r.Left
    $hh = $r.Bottom - $r.Top
    Write-Output "window $i -> $($r.Left),$($r.Top) ${w}x${hh}"

    $x0 = [Math]::Max($vs.X, $r.Left - $Pad)
    $y0 = [Math]::Max($vs.Y, $r.Top - $Pad)
    $x1 = [Math]::Min($vs.X + $vs.Width, $r.Right + $Pad)
    $y1 = [Math]::Min($vs.Y + $vs.Height, $r.Bottom + $Pad)
    $cw = $x1 - $x0
    $ch = $y1 - $y0
    if ($cw -le 0 -or $ch -le 0) { Write-Output '  (off screen)'; continue }

    $crop = New-Object System.Drawing.Bitmap($cw, $ch)
    $g2 = [System.Drawing.Graphics]::FromImage($crop)
    $g2.CopyFromScreen($x0, $y0, 0, 0, (New-Object System.Drawing.Size($cw, $ch)))
    $g2.Dispose()
    $suffix = if ($script:hits.Count -eq 1) { '' } else { "-$i" }
    $cropPath = Join-Path $OutDir "$Tag-widget$suffix.png"
    $crop.Save($cropPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $crop.Dispose()
    Write-Output "crop    -> $cropPath (${cw}x${ch})"
}
