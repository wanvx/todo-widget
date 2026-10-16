# Drives the running widget for verification: click a widget-local point or send keys.
# Run:  powershell -NoProfile -ExecutionPolicy Bypass -File tools\ui.ps1 -Action click -X 151 -Y 85
param(
    [ValidateSet('click', 'move', 'keys', 'drag', 'focus')]
    [string]$Action = 'click',
    [string]$ProcessName = 'TodoWidget',
    [int]$X = 0,
    [int]$Y = 0,
    [int]$X2 = 0,
    [int]$Y2 = 0,
    [string]$Keys = '',
    [switch]$Local
)

Add-Type -Namespace Ui -Name Api -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)] public static extern bool SetProcessDpiAwarenessContext(System.IntPtr value);
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)] public static extern bool SetCursorPos(int x, int y);
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, System.IntPtr extra);
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)] public static extern bool IsWindowVisible(System.IntPtr h);
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)] public static extern bool EnumWindows(EnumProc cb, System.IntPtr p);
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)] public static extern bool SetForegroundWindow(System.IntPtr h);
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)] public static extern void keybd_event(byte vk, byte scan, uint flags, System.IntPtr extra);
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)] public static extern bool BringWindowToTop(System.IntPtr h);
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)] public static extern bool ShowWindow(System.IntPtr h, int cmd);
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true)] public static extern bool SetWindowPos(System.IntPtr h, System.IntPtr after, int x, int y, int cx, int cy, uint flags);
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct RECT { public int Left, Top, Right, Bottom; }
public delegate bool EnumProc(System.IntPtr h, System.IntPtr p);
'@

[void][Ui.Api]::SetProcessDpiAwarenessContext([System.IntPtr](-4))

$constMouseEventLeftDown = 0x0002
$constMouseEventLeftUp = 0x0004

# Raise the widget in the Z order so injected clicks actually land on it. Z order —
# not activation — is what decides which window receives a mouse click, so this works
# without fighting the Windows foreground lock.
function Focus-Widget([System.IntPtr]$h) {
    [void][Ui.Api]::SetWindowPos($h, [System.IntPtr](-1), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0010)
    Start-Sleep -Milliseconds 150
    [Ui.Api]::keybd_event(0x12, 0, 0, [System.IntPtr]::Zero)
    [Ui.Api]::keybd_event(0x12, 0, 2, [System.IntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [void][Ui.Api]::SetForegroundWindow($h)
    Start-Sleep -Milliseconds 120
}

function Get-WidgetHwnd {
    $target = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $target) { return [System.IntPtr]::Zero }
    $targetPid = [uint32]$target.Id
    $script:hwnd = [System.IntPtr]::Zero
    $cb = [Ui.Api+EnumProc] {
        param($hw, $lp)
        $owner = [uint32]0
        [void][Ui.Api]::GetWindowThreadProcessId($hw, [ref]$owner)
        if ($owner -eq $targetPid -and [Ui.Api]::IsWindowVisible($hw)) {
            $rc = New-Object Ui.Api+RECT
            [void][Ui.Api]::GetWindowRect($hw, [ref]$rc)
            if (($rc.Right - $rc.Left) -gt 100 -and ($rc.Bottom - $rc.Top) -gt 20) {
                $script:hwnd = $hw
                return $false
            }
        }
        return $true
    }
    [void][Ui.Api]::EnumWindows($cb, [System.IntPtr]::Zero)
    return $script:hwnd
}

$hwnd = Get-WidgetHwnd
if ($hwnd -eq [System.IntPtr]::Zero) { Write-Output 'widget window not found'; exit 1 }

$rect = New-Object Ui.Api+RECT
[void][Ui.Api]::GetWindowRect($hwnd, [ref]$rect)

# Widget-local coordinates are converted to absolute screen pixels so the caller
# can describe what it clicks in terms of the captured crop.
if ($Local) { $X += $rect.Left; $Y += $rect.Top; $X2 += $rect.Left; $Y2 += $rect.Top }

Write-Output "hwnd=$hwnd rect=$($rect.Left),$($rect.Top) $($rect.Right-$rect.Left)x$($rect.Bottom-$rect.Top)"

switch ($Action) {
    'click' {
        Focus-Widget $hwnd
        [void][Ui.Api]::SetCursorPos($X, $Y)
        Start-Sleep -Milliseconds 120
        [Ui.Api]::mouse_event($constMouseEventLeftDown, 0, 0, 0, [System.IntPtr]::Zero)
        Start-Sleep -Milliseconds 60
        [Ui.Api]::mouse_event($constMouseEventLeftUp, 0, 0, 0, [System.IntPtr]::Zero)
        Write-Output "clicked $X,$Y"
    }
    'move' {
        [void][Ui.Api]::SetCursorPos($X, $Y)
        Write-Output "moved to $X,$Y"
    }
    'focus' {
        # Raise only: needed before a screenshot when another window covers the widget.
        Focus-Widget $hwnd
        Write-Output "focused $hwnd"
    }
    'drag' {
        Focus-Widget $hwnd
        [void][Ui.Api]::SetCursorPos($X, $Y)
        Start-Sleep -Milliseconds 250
        [Ui.Api]::mouse_event($constMouseEventLeftDown, 0, 0, 0, [System.IntPtr]::Zero)
        Start-Sleep -Milliseconds 150
        $steps = 14
        for ($i = 1; $i -le $steps; $i++) {
            $nx = [int]($X + ($X2 - $X) * $i / $steps)
            $ny = [int]($Y + ($Y2 - $Y) * $i / $steps)
            [void][Ui.Api]::SetCursorPos($nx, $ny)
            Start-Sleep -Milliseconds 55
        }
        Start-Sleep -Milliseconds 150
        [Ui.Api]::mouse_event($constMouseEventLeftUp, 0, 0, 0, [System.IntPtr]::Zero)
        Write-Output "dragged $X,$Y -> $X2,$Y2"
    }
    'keys' {
        # Deliberately does NOT steal focus: the caller targets whatever window
        # already has it (typically the modal 添加/编辑 dialog).
        Add-Type -AssemblyName System.Windows.Forms
        Start-Sleep -Milliseconds 150
        [System.Windows.Forms.SendKeys]::SendWait($Keys)
        Write-Output "sent keys '$Keys'"
    }
}
