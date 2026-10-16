# Writes a demo data.json next to a TodoWidget build so the widget can be
# eyeballed with one task per urgency colour plus a completed and an overdue one.
#
# Each task is serialised on its own: Windows PowerShell 5.1 wraps arrays passed
# to ConvertTo-Json as { "value": [...], "Count": n }, which TodoWidget would
# (correctly) reject as a corrupt file.
param(
    [string]$Target = 'C:\小工具\代办事情清单\dist'
)

$now = Get-Date
$stamp = $now.ToString('yyyy-MM-ddTHH:mm:ss')

function New-TaskJson {
    param([string]$Title, [double]$Hours, [bool]$Done)

    $due = $now.AddHours($Hours)
    $due = Get-Date -Year $due.Year -Month $due.Month -Day $due.Day -Hour $due.Hour -Minute 0 -Second 0

    $completed = $null
    if ($Done) { $completed = $stamp }

    [ordered]@{
        Id          = [guid]::NewGuid().ToString('N')
        Title       = $Title
        DueAt       = $due.ToString('yyyy-MM-ddTHH:mm:ss')
        IsDone      = $Done
        CompletedAt = $completed
        CreatedAt   = $stamp
    } | ConvertTo-Json -Depth 3
}

$entries = @(
    New-TaskJson -Title '交付季度报告' -Hours (24 * 10) -Done $false
    New-TaskJson -Title '预约牙医'     -Hours (24 * 5)  -Done $false
    New-TaskJson -Title '提交报销单'   -Hours (24 * 2)  -Done $false
    New-TaskJson -Title '回复客户邮件' -Hours 20        -Done $false
    New-TaskJson -Title '买牛奶'       -Hours -5        -Done $false
    New-TaskJson -Title '整理书架'     -Hours -30       -Done $false
    New-TaskJson -Title '归档旧合同'   -Hours (24 * 30) -Done $true
)

$json = '[' + "`r`n" + ($entries -join ("," + "`r`n")) + "`r`n]"

if (-not (Test-Path $Target)) { New-Item -ItemType Directory -Force -Path $Target | Out-Null }
$path = Join-Path $Target 'data.json'
[System.IO.File]::WriteAllText($path, $json, (New-Object System.Text.UTF8Encoding($false)))

# Fail loudly if the emitted file is not what the app expects.
$check = Get-Content $path -Raw | ConvertFrom-Json
if ($check.Count -ne 7) { throw "seed produced $($check.Count) entries, expected 7" }

Write-Output "wrote $path ($($check.Count) tasks)"
