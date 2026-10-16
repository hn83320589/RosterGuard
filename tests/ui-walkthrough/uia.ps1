# 以 Windows UI Automation 操作「實際發佈的 exe」，跑測試報告第 6 節的人工實測清單（#1、#3、#9、#11、#12）。
# 每個腳本都用暫存資料夾裡的新資料庫（首次啟動載入範例資料），不會動到 %LocalAppData% 的正式資料。
# 執行期間會開啟並操作 RosterGuard 視窗，請勿同時操作滑鼠鍵盤。
#
#   dotnet publish src/RosterGuard.App -c Release -r win-x64 --self-contained true `
#     -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/win-x64
#   pwsh tests/ui-walkthrough/run-grid-undo.ps1           # #3 同格多班、#9 違規即時更新與捲動、#11 撤銷重做 25 層
#   pwsh tests/ui-walkthrough/run-masterdata-restart.ps1  # #1 員工／班別新增編輯停用與重開、#12 各月獨立與重開
#
# exe 位置可用環境變數 ROSTERGUARD_EXE 覆寫。
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
$script:AE = [Windows.Automation.AutomationElement]
$script:Scratch = Join-Path ([IO.Path]::GetTempPath()) "RosterGuardWalkthrough"
$script:Exe = if ($env:ROSTERGUARD_EXE) { $env:ROSTERGUARD_EXE } else { Join-Path $PSScriptRoot "..\..\publish\win-x64\RosterGuard.exe" }

function Start-Roster([string]$db) {
    $env:ROSTERGUARD_DB = $db
    $p = Start-Process $Exe -PassThru
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 500
        $w = Get-RosterWindow $p
        if ($w) { return $p }
    }
    throw "window did not appear"
}

function Get-RosterWindow($p) {
    $AE::RootElement.FindFirst([Windows.Automation.TreeScope]::Children,
        (New-Object Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)))
}

function Find-All($root, [string]$name = $null, [string]$id = $null, $type = $null) {
    $conds = @()
    if ($name) { $conds += New-Object Windows.Automation.PropertyCondition($AE::NameProperty, $name) }
    if ($id) { $conds += New-Object Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id) }
    if ($type) { $conds += New-Object Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $type) }
    $c = if ($conds.Count -eq 0) { [Windows.Automation.Condition]::TrueCondition }
         elseif ($conds.Count -eq 1) { $conds[0] }
         else { New-Object Windows.Automation.AndCondition(, [Windows.Automation.Condition[]]$conds) }
    $root.FindAll([Windows.Automation.TreeScope]::Descendants, $c)
}

function Find-One($root, [string]$name = $null, [string]$id = $null, $type = $null) {
    $r = Find-All $root $name $id $type
    if ($r.Count -eq 0) { throw "not found: name=$name id=$id" }
    $r[0]
}

function Invoke-El($el) { $el.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 400 }
function Toggle-El($el) { $el.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 400 }
function Select-El($el) { $el.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 400 }
function Set-Text($el, [string]$v) { $el.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($v); Start-Sleep -Milliseconds 300 }
function Get-Text($el) {
    try { $el.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { $el.Current.Name }
}

function Save-WindowShot($el, [string]$path) {
    $r = $el.Current.BoundingRectangle
    $bmp = New-Object Drawing.Bitmap ([int]$r.Width), ([int]$r.Height)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
    $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

function Dump($root, [int]$max = 400) {
    $all = Find-All $root
    $n = 0
    foreach ($e in $all) {
        if ($n++ -ge $max) { break }
        "{0} | id={1} | name={2}" -f $e.Current.ControlType.ProgrammaticName.Replace('ControlType.', ''), $e.Current.AutomationId, $e.Current.Name
    }
    "total=$($all.Count)"
}

Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Win32 { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); }
"@
function Focus-Roster($p) { $p.Refresh(); [void][Win32]::SetForegroundWindow($p.MainWindowHandle); Start-Sleep -Milliseconds 300 }



