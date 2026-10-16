# Types text into whatever window currently has focus, via the clipboard so
# non-ASCII (Chinese) input works — SendKeys cannot synthesise CJK characters.
# Run in an STA host:  powershell -STA -NoProfile -ExecutionPolicy Bypass -File tools\type.ps1 -Text "..." -Enter
param(
    [Parameter(Mandatory = $true)][string]$Text,
    [switch]$Enter,
    [int]$DelayMs = 300
)

Add-Type -AssemblyName System.Windows.Forms

Set-Clipboard -Value $Text
Start-Sleep -Milliseconds $DelayMs
[System.Windows.Forms.SendKeys]::SendWait('^v')
Start-Sleep -Milliseconds $DelayMs

if ($Enter) {
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Milliseconds $DelayMs
}

Write-Output "typed '$Text' enter=$Enter"
