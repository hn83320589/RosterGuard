# RosterGuard 測試報告

- 日期：2026-10-16
- 版本：`main`（本機測試於 `ff9a04d` 執行，之後的 commit 僅有文件變更）
- 環境：Windows 11 Pro 10.0.26200、.NET SDK 9.0.315
- 執行指令：

```powershell
dotnet build -c Release --no-incremental
dotnet test -c Release --no-build --logger trx
dotnet publish src/RosterGuard.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## 1. 結論

| 項目 | 結果 |
|---|---|
| Release 建置 | ✅ 0 警告、0 錯誤（`TreatWarningsAsErrors` 開啟） |
| 單元測試（RosterGuard.Core.Tests） | ✅ 105／105 通過，0 略過 |
| Headless UI 測試（RosterGuard.App.Tests） | ✅ 5／5 通過（另有 1 個截圖產生器，未設定環境變數時不做任何事，不計入） |
| win-x64 單一執行檔發佈 | ✅ 成功，`RosterGuard.exe` 約 95 MB，啟動後主視窗正常顯示並建立資料庫 |
| GitHub Actions | ✅ windows-latest 上全部步驟成功，產出 `RosterGuard-win-x64`、`test-results` 兩個 artifact（最新結果見 README 徽章與 [Actions 頁面](https://github.com/hn83320589/RosterGuard/actions/workflows/ci.yml)） |

驗收 17 項中，可自動驗證的部分全部通過。標為「實測」的項目，目前只有自動化測試覆蓋，仍需人工點一輪（見第 4 節）。

## 2. 測試分布

| 測試類別 | 數量 | 涵蓋內容 |
|---|---:|---|
| Time.ShiftTimeTests | 10 | 跨午夜班別的時數與起訖、週一為一週起點 |
| Rules.RestGapRuleTests | 9 | 最小間隔：11 小時邊界、跨午夜、跨月、可調參數 |
| Rules.RestDayRuleTests | 7 | 缺少休息日：第 7 日邊界、跨月錨點、跨午夜尾段不算休息 |
| Rules.WeeklyHoursRuleTests | 6 | 週工時：剛好等於上限、半小時、週日跨午夜班歸屬 |
| Rules.UnavailableAndOverlapRuleTests | 6 | 不可上班日與時段重疊各自獨立判定 |
| Rules.StaffingRuleTests | 6 | 人力不足／超額，各職稱分開計算 |
| Rules.RuleEngineTests | 2 | 違規排序、未知班別立即失敗 |
| Candidates.CandidateFinderTests | 10 | 候選人過濾、排序穩定性、無人可用時的原因說明 |
| History.UndoRedoStackTests | 4 | 容量、清空 redo |
| History.CopyWeekCommandTests | 5 | 複製上週的範圍、停用資料、單步撤銷 |
| Session.RosterSessionTests | 14 | 指派／撤銷／重做 ≥20 層、跨月撤銷、儲存失敗時狀態不變 |
| Export.CsvRosterExporterTests | 5 | UTF-8 BOM、欄位跳脫、欄數依月份天數 |
| Export.IcsExporterTests | 5 | Ical.Net 解析、中文標題、75 octets 折行、CRLF |
| Data.SqliteRosterStoreTests | 11 | 重開後資料不變、交易回滾、停用員工保留歷史 |
| Seed.SampleDataTests | 5 | 範例資料與 `samples/expected-violations.md` 自動比對 |
| **單元測試小計** | **105** | |
| MainWindowTests（Headless） | 5 | 啟動、指派＋Ctrl+Z/Y、點違規捲動、切換月份、候選人一鍵指派 |

## 3. 驗收標準對照

| # | 標準 | 判定方式 | 自動化證據 | 狀態 |
|---|---|---|---|---|
| 1 | 員工與班別新增、編輯、停用，重開後不變；停用者不在選單，歷史保留 | 實測 | `SqliteRosterStoreTests`：`Employees_round_trip…`、`Editing_an_employee…`、`Deactivated_employee_keeps_historical_assignments`；`RosterSessionTests.Inactive_employees_are_excluded…` | 🟡 自動化通過，待人工實測 |
| 2 | 跨午夜時數與起訖正確（18:00–02:00 = 8 小時） | 單元測試 | `ShiftTimeTests.Overnight_shift_18_to_02_is_8_hours`、`Overnight_shift_ends_on_next_day` | ✅ |
| 3 | 指派、取消指派，同格多班，格網正確 | 實測＋Headless | `MainWindowTests.Assigning_through_cell_flyout…`；`RosterSessionTests.Unassign_can_be_undone` | ✅ Headless／🟡 待人工實測 |
| 4 | 間隔規則邊界：剛好 11 小時不違規、少 1 分鐘違規、跨午夜接早班 | 單元測試 | `RestGapRuleTests.Exactly_11_hours…`、`One_minute_short_of_11_hours…`、`Closing_shift_followed_by_next_morning_shift…` | ✅ |
| 5 | 缺少休息日邊界 | 單元測試 | `RestDayRuleTests.Six_work_days_then_a_rest_day…`、`Seven_consecutive_work_days…`、`Seventh_day_touched_only_by_overnight_tail…` | ✅ |
| 6 | 週工時以週一起算，等於上限不違規 | 單元測試 | `WeeklyHoursRuleTests.Exactly_at_the_limit…`、`Weeks_start_on_monday_not_rolling_seven_days` | ✅ |
| 7 | 不可上班日與重疊各自偵測、互不干擾 | 單元測試 | `UnavailableAndOverlapRuleTests`（6 個） | ✅ |
| 8 | 人力不足與超額，各職稱分開 | 單元測試 | `StaffingRuleTests.Roles_are_counted_separately` 等 6 個 | ✅ |
| 9 | 編輯後違規即時更新；點違規捲動並標示格子 | 實測＋Headless | `MainWindowTests.Clicking_a_violation_highlights_and_scrolls…`、`Assigning_through_cell_flyout…` | ✅ Headless／🟡 待人工實測 |
| 10 | 候選人只列不產生新違規者，排序穩定，無人時顯示原因 | 單元測試 | `CandidateFinderTests`（10 個） | ✅ |
| 11 | 撤銷／重做 ≥20 層、同步還原、新操作清空 redo | 單元測試＋實測 | `RosterSessionTests.At_least_20_levels_can_be_undone`、`Undo_restores_assignments_and_violations`、`New_operation_after_undo_clears_redo` | ✅ 單元測試／🟡 待人工實測 |
| 12 | 各月獨立儲存、可切換、重開不變 | 實測 | `SqliteRosterStoreTests.Session_months_survive_restart_independently`；`MainWindowTests.Switching_months…` | 🟡 自動化通過，待人工實測 |
| 13 | CSV 用 Excel 開中文不亂碼；ICS 可被標準解析器讀取 | 測試＋實測 | `CsvRosterExporterTests.Output_starts_with_utf8_bom…`；`IcsExporterTests.Standard_parser_reads_events_with_chinese_titles…`（Ical.Net） | ✅ 測試／🟡 未實際用 Excel、Outlook、Google 日曆開啟 |
| 14 | 單元測試 ≥40 個且全數通過 | 執行測試 | 105 個通過 | ✅ |
| 15 | Headless 測試 ≥3 個，涵蓋啟動、指派、違規顯示 | 執行測試 | 5 個通過 | ✅ |
| 16 | README 含建置、截圖、架構、規則說明、已知限制 | 檢視 README | `README.md`、`docs/screenshots/` | ✅ |
| 17 | GitHub Actions 綠燈、徽章、win-x64 artifact | 檢視 Actions | `.github/workflows/ci.yml`；windows-latest 綠燈，README 徽章已指向本 repo，artifact `RosterGuard-win-x64` 已上傳 | ✅ |

## 4. 建議的人工實測清單

1. 新增一位員工與一個跨午夜班別，關閉程式再開啟，確認資料還在；停用該員工後，確認排班選單不再出現，但既有班表仍顯示。（#1）
2. 在同一格勾選兩個班別，再取消其中一個。（#3）
3. 排一筆會違規的班，確認違規面板與格子標示立即出現；點違規項目，確認格子被捲動到並標示。（#9）
4. 連續做 20 次以上指派，按 Ctrl+Z 全部撤銷再 Ctrl+Y 重做；撤銷後做新操作，確認無法再重做。（#11）
5. 在 9 月、10 月各改一格，切換月份並重開程式確認。（#12）
6. 匯出 CSV 以 Excel 直接開啟；匯出 ICS 匯入 Outlook 或 Google 日曆，確認中文標題與跨午夜班的起訖時間。（#13）

## 5. 已知限制與注意事項

- Headless 測試中，Flyout 內的 CheckBox 是直接設定 `IsChecked`，不是模擬滑鼠點擊；其餘操作（格子按鈕、違規清單項目、上下月、「指派」按鈕、Ctrl+Z／Ctrl+Y）都是模擬的滑鼠或鍵盤輸入。
- 修改班別的起訖時間會回溯影響所有月份的違規判定。
- 撤銷歷史在關閉程式後不保留。
