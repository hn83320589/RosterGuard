. "$PSScriptRoot\uia.ps1"
$ErrorActionPreference = 'Stop'
$dir = "$Scratch\grid-undo"; Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue; New-Item -ItemType Directory $dir | Out-Null
$global:results = @()
function Check([string]$item, [string]$what, [bool]$ok, [string]$detail = '') {
    $global:results += [pscustomobject]@{ Item = $item; Check = $what; Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Detail = $detail }
}
function Cells($w) { @(Find-All $w -name 'Avalonia.Controls.Grid' -type ([Windows.Automation.ControlType]::Button)) }
function CellText($c) { (($c.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name } | Where-Object { $_ }) -join ',') }
function Status($w) { (Find-One $w -id 'StatusViolationText').Current.Name }
function FlyoutBox($p, [string]$prefix) {
    foreach ($t in $AE::RootElement.FindAll([Windows.Automation.TreeScope]::Children, (New-Object Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)))) {
        foreach ($cb in (Find-All $t -type ([Windows.Automation.ControlType]::CheckBox))) { if ($cb.Current.Name.StartsWith($prefix)) { return $cb } }
    }
    throw "flyout checkbox $prefix not found"
}
function ToggleShift($p, $w, $cell, [string]$shift) {
    Invoke-El $cell; Start-Sleep -Milliseconds 300
    Toggle-El (FlyoutBox $p $shift)
    Focus-Roster $p; [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 300
}

$p = Start-Roster "$dir\roster.db"; Start-Sleep 2
try {
    $w = Get-RosterWindow $p
    Focus-Roster $p
    $cells = Cells $w
    # 陳志明 = row 0；10/03 為空格
    $c = $cells[2]
    $baseStatus = Status $w

    # ---- #3 同格多班、取消指派 ----
    ToggleShift $p $w $c '早班'
    ToggleShift $p $w $c '午班'
    $t = CellText $c
    Check '#3' '同一格勾選早班＋午班，格子顯示兩班' ($t -like '*早班*' -and $t -like '*午班*') $t
    ToggleShift $p $w $c '早班'
    $t = CellText $c
    Check '#3' '取消早班後只剩午班' ($t -notlike '*早班*' -and $t -like '*午班*') $t

    # ---- #9 編輯後違規即時更新 ----
    $s1 = Status $w
    Check '#9' '指派後狀態列違規數立即改變' ($s1 -ne $baseStatus) "$baseStatus → $s1"
    ToggleShift $p $w $c '午班'
    $s2 = Status $w
    Check '#9' '取消後違規數回到原值' ($s2 -eq $baseStatus) $s2

    # ---- #9 點違規捲動並標示 ----
    $hs = (Find-One $w -id 'HorizontalScroll').GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
    $before = $hs.Current.HorizontalScrollPercent
    $item = (Find-All $w -type ([Windows.Automation.ControlType]::ListItem)) | Where-Object {
        (($_.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }) -join ',') -like '*週工時超過上限*' } | Select-Object -First 1
    Select-El $item; Start-Sleep 1
    $after = $hs.Current.HorizontalScrollPercent
    Check '#9' '點「週工時超過上限」後格網水平捲動' ($after -gt $before) ("{0:N1}% → {1:N1}%" -f $before, $after)
    Save-WindowShot $w "$dir\violation-highlight.png"

    # ---- #11 撤銷／重做 ≥ 20 層 ----
    $hs.SetScrollPercent(0, -1); Start-Sleep -Milliseconds 300
    $snapshot = Cells $w | ForEach-Object { CellText $_ }
    $statusBefore = Status $w
    # 劉怡君（row 7）、蔡宗翰（row 8）10/01–10/20 各加午班，共 25 次操作（遇到已有午班就略過）
    $ops = 0
    foreach ($row in 7, 8) {
        foreach ($day in 1..20) {
            if ($ops -ge 25) { break }
            $cell = (Cells $w)[$row * 31 + $day - 1]
            if ((CellText $cell) -like '*午班*') { continue }
            ToggleShift $p $w $cell '午班'; $ops++
        }
    }
    Check '#11' "完成 $ops 次指派" ($ops -ge 25) "$ops"
    $undo = Find-One $w -id 'UndoButton'; $redo = Find-One $w -id 'RedoButton'
    for ($i = 0; $i -lt $ops; $i++) { Invoke-El $undo }
    $now = Cells $w | ForEach-Object { CellText $_ }
    $diff = @(for ($i = 0; $i -lt $snapshot.Count; $i++) { if ($snapshot[$i] -ne $now[$i]) { $i } })
    Check '#11' "撤銷 $ops 次後所有格子還原" ($diff.Count -eq 0) "差異格數 $($diff.Count)"
    Check '#11' '撤銷後違規清單同步還原' ((Status $w) -eq $statusBefore) (Status $w)
    for ($i = 0; $i -lt $ops; $i++) { Invoke-El $redo }
    $redone = @((Cells $w) | Where-Object { (CellText $_) -like '*午班*' }).Count
    $undoneCount = @($snapshot | Where-Object { $_ -like '*午班*' }).Count
    Check '#11' "重做 $ops 次後午班格數增加 $ops" (($redone - $undoneCount) -eq $ops) "$undoneCount → $redone"
    Invoke-El $undo
    Check '#11' '撤銷一步後重做按鈕可用' ($redo.Current.IsEnabled) ''
    ToggleShift $p $w ((Cells $w)[0 * 31 + 2]) '打烊班'
    Check '#11' '撤銷後做新操作，重做按鈕停用（redo 堆疊清空）' (-not $redo.Current.IsEnabled) ''
}
finally {
    Stop-Process $p -Confirm:$false
}
$global:results | Format-Table -AutoSize | Out-String -Width 220



