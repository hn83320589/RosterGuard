. "$PSScriptRoot\uia.ps1"
$ErrorActionPreference = 'Stop'
$dir = "$Scratch\masterdata-restart"; Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue; New-Item -ItemType Directory $dir | Out-Null
$db = "$dir\roster.db"
$global:results = @()
function Check([string]$item, [string]$what, [bool]$ok, [string]$detail = '') {
    $global:results += [pscustomobject]@{ Item = $item; Check = $what; Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Detail = $detail }
}
function Cells($w) { @(Find-All $w -name 'Avalonia.Controls.Grid' -type ([Windows.Automation.ControlType]::Button)) }
function Texts($el) { @($el.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name } | Where-Object { $_ }) }
function CellText($c) { (Texts $c) -join ',' }
function Tab($w, [string]$name) { Select-El (Find-One $w -name $name -type ([Windows.Automation.ControlType]::TabItem)); Start-Sleep -Milliseconds 600 }
function ListItemLike($w, [string]$pattern) { (Find-All $w -type ([Windows.Automation.ControlType]::ListItem)) | Where-Object { $_.Current.Name -like $pattern } | Select-Object -First 1 }
function FlyoutBoxes($p) {
    foreach ($t in $AE::RootElement.FindAll([Windows.Automation.TreeScope]::Children, (New-Object Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)))) {
        Find-All $t -type ([Windows.Automation.ControlType]::CheckBox) | Where-Object { $_.Current.Name -match '\d\d:\d\d' }
    }
}
function OpenFlyout($p, $cell) { Invoke-El $cell; Start-Sleep -Milliseconds 400; @(FlyoutBoxes $p) }
function CloseFlyout($p) { Focus-Roster $p; [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 400 }
function ToggleShift($p, $cell, [string]$shift) {
    $box = OpenFlyout $p $cell | Where-Object { $_.Current.Name.StartsWith($shift) } | Select-Object -First 1
    if (-not $box) { throw "shift $shift not in flyout" }
    Toggle-El $box; CloseFlyout $p
}
function Month($w) { (Find-One $w -id 'MonthTitleText').Current.Name }
function RowIndexOf($w, [string]$name) {
    $names = @(Find-All $w -type ([Windows.Automation.ControlType]::Text) | ForEach-Object { $_.Current.Name })
    $rowNames = @('陳志明', '林建宏', '黃美玲', '王小芳', '張家豪', '李雅婷', '吳俊傑', '劉怡君', '蔡宗翰', '測試員乙')
    $present = @($rowNames | Where-Object { $names -contains $_ })
    [array]::IndexOf($present, $name)
}

# ================= 第一次執行 =================
$p = Start-Roster $db; Start-Sleep 2
try {
    $w = Get-RosterWindow $p; Focus-Roster $p

    # #1 新增員工
    Tab $w '員工'
    Invoke-El (Find-One $w -id 'NewEmployeeButton')
    Set-Text (Find-One $w -id 'EmployeeNameBox') '測試員甲'
    Invoke-El (Find-One $w -id 'SaveEmployeeButton'); Start-Sleep -Milliseconds 500
    Check '#1' '新增員工「測試員甲」出現在清單' ($null -ne (ListItemLike $w '*Name = 測試員甲*')) ''

    # #1 編輯員工：改名、週工時上限 30
    Select-El (ListItemLike $w '*Name = 測試員甲*')
    Set-Text (Find-One $w -id 'EmployeeNameBox') '測試員乙'
    Set-Text (Find-One $w -id 'PART_TextBox') '30'
    Invoke-El (Find-One $w -id 'SaveEmployeeButton'); Start-Sleep -Milliseconds 500
    $e = ListItemLike $w '*Name = 測試員乙*'
    Check '#1' '編輯後清單顯示新姓名與 30 小時上限' ($null -ne $e -and $e.Current.Name -like '*WeeklyLimitMinutes = 1800*') $(if ($e) { $e.Current.Name } else { '' })

    # #1 新增並編輯班別
    Tab $w '班別'
    Invoke-El (Find-One $w -id 'NewShiftButton')
    Set-Text (Find-One $w -id 'ShiftNameBox') '夜班'
    Set-Text (Find-One $w -id 'ShiftStartBox') '22:00'
    Set-Text (Find-One $w -id 'ShiftEndBox') '06:00'
    Invoke-El (Find-One $w -id 'SaveShiftButton'); Start-Sleep -Milliseconds 500
    Select-El (ListItemLike $w '*Name = 夜班*')
    Set-Text (Find-One $w -id 'ShiftNameBox') '大夜班'
    Invoke-El (Find-One $w -id 'SaveShiftButton'); Start-Sleep -Milliseconds 500
    $s = ListItemLike $w '*Name = 大夜班*'
    Check '#1' '新增「夜班」22:00–06:00 並改名為「大夜班」' ($null -ne $s) $(if ($s) { $s.Current.Name } else { '' })

    # 指派：測試員乙 10/20 大夜班
    Tab $w '月班表'
    $row = RowIndexOf $w '測試員乙'
    Check '#1' '月班表出現新員工列' ($row -ge 0) "row=$row"
    ToggleShift $p ((Cells $w)[$row * 31 + 19]) '大夜班'
    Check '#1' '測試員乙 10/20 已排大夜班' ((CellText (Cells $w)[$row * 31 + 19]) -like '*大夜班*') (CellText (Cells $w)[$row * 31 + 19])

    # #12 9 月與 10 月各改一格：陳志明 9/03 加午班、陳志明 10/03 加打烊班
    ToggleShift $p ((Cells $w)[0 * 31 + 2]) '打烊班'
    $octText = CellText (Cells $w)[0 * 31 + 2]
    Invoke-El (Find-One $w -id 'PreviousMonthButton'); Start-Sleep -Milliseconds 800
    Check '#12' '切到上個月' ((Month $w) -eq '2026 年 9 月') (Month $w)
    $sepBefore = CellText (Cells $w)[0 * 30 + 2]
    ToggleShift $p ((Cells $w)[0 * 30 + 2]) '午班'
    $sepText = CellText (Cells $w)[0 * 30 + 2]
    Check '#12' '9/03 加上午班' ($sepText -like '*午班*' -and $sepText -ne $sepBefore) "$sepBefore → $sepText"
    Invoke-El (Find-One $w -id 'NextMonthButton'); Start-Sleep -Milliseconds 800
    Check '#12' '切回 10 月，10/03 仍是修改後內容' ((CellText (Cells $w)[0 * 31 + 2]) -eq $octText) (CellText (Cells $w)[0 * 31 + 2])
    Invoke-El (Find-One $w -id 'PreviousMonthButton'); Start-Sleep -Milliseconds 800
    Check '#12' '再切回 9 月，9/03 仍是修改後內容' ((CellText (Cells $w)[0 * 30 + 2]) -eq $sepText) (CellText (Cells $w)[0 * 30 + 2])
    Invoke-El (Find-One $w -id 'NextMonthButton'); Start-Sleep -Milliseconds 800

    # #1 停用員工與班別
    Tab $w '員工'
    Select-El (ListItemLike $w '*Name = 測試員乙*')
    Toggle-El (Find-All $w -type ([Windows.Automation.ControlType]::CheckBox) | Where-Object { $_.Current.Name.StartsWith('在職') } | Select-Object -First 1)
    Invoke-El (Find-One $w -id 'SaveEmployeeButton'); Start-Sleep -Milliseconds 500
    Tab $w '班別'
    Select-El (ListItemLike $w '*Name = 大夜班*')
    Toggle-El (Find-All $w -type ([Windows.Automation.ControlType]::CheckBox) | Where-Object { $_.Current.Name.StartsWith('啟用') } | Select-Object -First 1)
    Invoke-El (Find-One $w -id 'SaveShiftButton'); Start-Sleep -Milliseconds 500
    Tab $w '月班表'
}
finally { Stop-Process $p -Confirm:$false; Start-Sleep 1 }

# ================= 關閉後重新開啟 =================
$p = Start-Roster $db; Start-Sleep 2
try {
    $w = Get-RosterWindow $p; Focus-Roster $p

    $row = RowIndexOf $w '測試員乙'
    $rowTexts = (Find-All $w -type ([Windows.Automation.ControlType]::Text) | ForEach-Object { $_.Current.Name }) -join '|'
    Check '#1' '重開後 10 月仍有停用員工的列（有歷史班表）' ($row -ge 0) "row=$row"
    Check '#1' '該列標示「停用」' ($rowTexts -like '*外場・正職・停用*' -or $rowTexts -like '*內場・正職・停用*') ''
    $hist = CellText (Cells $w)[$row * 31 + 19]
    Check '#1' '停用員工 10/20 的大夜班歷史保留' ($hist -like '*大夜班*') $hist
    $boxes = OpenFlyout $p ((Cells $w)[$row * 31 + 19])
    $labels = ($boxes | ForEach-Object { $_.Current.Name }) -join ' / '
    Check '#1' '已排的停用班別在該格選單中標示（已停用）' ($labels -like '*大夜班*已停用*') $labels
    CloseFlyout $p
    $boxes = OpenFlyout $p ((Cells $w)[1 * 31 + 0])
    $labels = ($boxes | ForEach-Object { $_.Current.Name }) -join ' / '
    Check '#1' '其他格子的排班選單不出現停用班別' ($labels -notlike '*大夜班*') $labels
    CloseFlyout $p

    Check '#12' '重開後 10/03 仍是修改後內容' ((CellText (Cells $w)[0 * 31 + 2]) -eq $octText) (CellText (Cells $w)[0 * 31 + 2])
    Invoke-El (Find-One $w -id 'PreviousMonthButton'); Start-Sleep -Milliseconds 800
    Check '#12' '重開後 9/03 仍是修改後內容' ((CellText (Cells $w)[0 * 30 + 2]) -eq $sepText) (CellText (Cells $w)[0 * 30 + 2])
    Invoke-El (Find-One $w -id 'NextMonthButton'); Start-Sleep -Milliseconds 600
    Invoke-El (Find-One $w -id 'NextMonthButton'); Start-Sleep -Milliseconds 800
    Check '#1' '11 月（沒有歷史）不出現停用員工的列' ((Month $w) -eq '2026 年 11 月' -and (RowIndexOf $w '測試員乙') -lt 0) (Month $w)
    Invoke-El (Find-One $w -id 'PreviousMonthButton'); Start-Sleep -Milliseconds 800

    Tab $w '員工'
    $e = ListItemLike $w '*Name = 測試員乙*'
    Check '#1' '重開後員工資料不變（30 小時、已停用）' ($null -ne $e -and $e.Current.Name -like '*WeeklyLimitMinutes = 1800*' -and $e.Current.Name -like '*IsActive = False*') $(if ($e) { $e.Current.Name } else { '' })
    Save-WindowShot $w "$dir\employees-after-restart.png"
    Tab $w '班別'
    $s = ListItemLike $w '*Name = 大夜班*'
    Check '#1' '重開後班別資料不變（22:00–06:00、已停用）' ($null -ne $s -and $s.Current.Name -like '*Start = 下午 10:00, End = 上午 06:00*' -and $s.Current.Name -like '*IsActive = False*') $(if ($s) { $s.Current.Name } else { '' })
    Tab $w '月班表'
}
finally { Stop-Process $p -Confirm:$false }
$global:results | Format-Table -AutoSize -Wrap | Out-String -Width 250


