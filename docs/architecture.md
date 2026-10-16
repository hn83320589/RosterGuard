# RosterGuard 系統架構設計（草案 v0.1）

> 狀態：**已確認（2026-10-16）**。標註「❓」的項目皆採用文中建議方案，另補充：
> - 範例資料：資料庫首次建立時自動載入（9 員工、3 班別、兩個月範例班表），並提供選單「重設為範例資料」。
> - 姓名排序：`StringComparer.Ordinal`（Unicode 碼位），確保 CI 結果穩定。
>
> **實作時的調整**（與下文草案不同處）：
> 1. 需求人數只有內場、外場兩個固定職稱，改為 `shift_types.required_kitchen / required_floor` 兩個欄位，不另建 `shift_requirements` 表。
> 2. Session 啟動時把所有指派載入記憶體（單店多年也僅數千筆），評估時再取本月 ±7 天作為上下文；切月份與跨月復原都不必重新查詢。
> 3. 不啟用 WAL（單一使用者不需要，且 WAL 無法在交易內切換）；並關閉連線池，讓 Windows 上的檔案在每次呼叫後立即釋放。
> 4. Command 不直接修改狀態，而是回傳 `RosterDelta`（新增／刪除的指派）；復原 = 套用反向差量。

---

## 1. 需求整理

### 1.1 功能需求（摘要）
| 模組 | 重點 |
|---|---|
| 主檔 | 員工、班別 CRUD + 停用（不刪除）；停用者不進新排班選單，歷史保留 |
| 月班表 | 員工 × 日期格網、一格多班、取消指派、複製上週、切換月份 |
| 規則檢查 | 6 條規則，任何編輯後即時重算 |
| 違規面板 | 排序、點選捲動並標示格子、格子顏色/圖示、狀態列總數 |
| 候選人建議 | 只列「指派後不產生新違規」者；依本月累計工時 → 姓名排序；無人時給原因 |
| Undo/Redo | 指派、取消、複製上週；≥20 層；新操作清空 redo |
| 匯出 | 月班表 CSV（UTF-8 BOM）、個人 .ics |
| 持久化 | SQLite，每次編輯自動儲存，`%LocalAppData%\RosterGuard\` |

### 1.2 非功能需求
- **規模極小**：9 名員工 × 31 天 × 3 班 ≈ 最多數百筆指派／月。→ **每次編輯全量重算**即可（估計 < 1 ms），不需增量演算法或快取。
- **單機、單使用者、離線**：不需併發控制、不呼叫網路。
- **可測試性優先**：所有邏輯在純 `net9.0` Class Library，UI 只做繫結。

### 1.3 技術限制與已確認版本（2026-10-16 查 NuGet）
| 套件 | 版本 | 用途 | 授權 |
|---|---|---|---|
| .NET SDK | 9.0.315（本機已安裝） | | MIT |
| Avalonia / Avalonia.Desktop / Themes.Fluent | **11.3.22**（題目指定 11，不用 12.x） | UI | MIT |
| Avalonia.Headless.XUnit | 11.3.22（依賴 xunit 2.x） | UI 測試 | MIT |
| CommunityToolkit.Mvvm | 8.4.2 | MVVM source generator | MIT |
| Microsoft.Data.Sqlite | **9.0.21**（對齊 .NET 9，不用 10.x） | 儲存 | MIT |
| xunit / xunit.runner.visualstudio / Microsoft.NET.Test.Sdk | 2.9.3 / 3.1.5 / 最新 | 測試 | Apache-2.0 / MIT |
| Ical.Net | 5.2.3 | **僅測試專案**用來解析驗證匯出的 .ics | MIT |

> 不使用 EF Core、Dapper：資料表只有 5 張，手寫 SQL 更直白、無額外依賴。
> ICS 由 Core 手寫產生（RFC 5545 子集），production 不引入 Ical.Net。

---

## 2. 高階架構

```
┌──────────────────────────────────────────────────────────────┐
│ RosterGuard.App  (Avalonia 11, net9.0, WinExe)               │
│  Views (AXAML) ── ViewModels (CommunityToolkit.Mvvm)         │
│  MainWindow / RosterGrid / ViolationPanel / CandidatePanel   │
│  EmployeeEditor / ShiftTypeEditor / Settings                 │
│  Composition root：建立 SqliteRosterStore → RosterSession     │
└───────────────┬──────────────────────────────┬───────────────┘
                │ 參考                          │ 參考
┌───────────────▼──────────────┐  ┌────────────▼───────────────┐
│ RosterGuard.Core (net9.0)    │◄─┤ RosterGuard.Data (net9.0)   │
│  純邏輯，零 UI/IO 依賴         │  │ Microsoft.Data.Sqlite       │
│  Domain / Time / Rules /     │  │ SqliteRosterStore :         │
│  Candidates / History /      │  │   IRosterStore              │
│  Export / Session / Seed     │  │ Schema 建立與版本 (user_version)│
└──────────────────────────────┘  └────────────────────────────┘
        ▲                                   ▲
┌───────┴───────────────────────┐  ┌────────┴──────────────────┐
│ RosterGuard.Core.Tests (xUnit)│  │ RosterGuard.App.Tests      │
│ 規則/時間/候選人/UndoRedo/匯出  │  │ Avalonia.Headless.XUnit    │
│ + Data 整合測試（暫存 DB 檔）   │  │ 啟動、指派、違規顯示、捲動   │
└───────────────────────────────┘  └───────────────────────────┘
```

**相依方向**：App → Data → Core；Core 不依賴任何人。Core 定義 `IRosterStore` 介面，Data 實作（Dependency Inversion），因此 `RosterSession`（含 Undo/Redo + 即時重算）可以用 in-memory fake store 做單元測試。

> 題目要求 Core 是「獨立 Class Library 不引用 Avalonia」——把 SQLite 另拆 `Data` 專案，讓 Core 連 IO 都沒有，測試更乾淨。代價是多一個專案。

### 2.1 Solution 結構
```
RosterGuard.sln
├─ src/
│  ├─ RosterGuard.Core/
│  │  ├─ Domain/        Employee, ShiftType, Assignment, Role, EmploymentType, RuleSettings
│  │  ├─ Time/          ShiftInterval, ShiftTimeCalculator, WeekMath
│  │  ├─ Rules/         IRule, 6 個 Rule 類別, RuleEngine, Violation, ViolationKey, Severity
│  │  ├─ Candidates/    CandidateFinder, Candidate, NoCandidateReason
│  │  ├─ History/       IRosterCommand, AssignCommand, UnassignCommand, CopyWeekCommand, UndoRedoStack
│  │  ├─ Export/        CsvRosterExporter, IcsExporter
│  │  ├─ Session/       RosterSession, IRosterStore, RosterSnapshot
│  │  └─ Seed/          SampleData（9 員工、3 班別、兩個月含違規班表）
│  ├─ RosterGuard.Data/   SqliteRosterStore, Schema
│  └─ RosterGuard.App/    Views, ViewModels, Converters, App.axaml
├─ tests/
│  ├─ RosterGuard.Core.Tests/
│  └─ RosterGuard.App.Tests/
├─ samples/  expected-violations.md（預期違規清單）
├─ docs/     architecture.md, screenshots/
├─ .github/workflows/ci.yml
├─ Directory.Build.props（Nullable, TreatWarningsAsErrors, LangVersion）
└─ Directory.Packages.props（Central Package Management 鎖版本）
```

---

## 3. Domain Model 與時間計算

```csharp
enum Role { Kitchen, Floor }                 // 內場、外場
enum EmploymentType { FullTime, PartTime }

record Employee(int Id, string Name, Role Role, EmploymentType Type,
                int WeeklyLimitMinutes, DayOfWeekSet UnavailableDays, bool IsActive);

record ShiftType(int Id, string Name, TimeOnly Start, TimeOnly End,
                 IReadOnlyDictionary<Role,int> Required, bool IsActive);

record Assignment(int EmployeeId, DateOnly Date, int ShiftTypeId);   // Date = 班別「開始日」
record RuleSettings(int MinRestMinutes = 11 * 60);
```

### 3.1 時間規則（Core/Time）
- 班別時段一律為 **半開區間 `[start, end)`**，以 `DateTime`（本地牆鐘、Kind=Unspecified）表示。
- `End <= Start` ⇒ 跨日：`end = date + 1 day + End`。因此 **18:00→02:00 = 8 小時**；`End == Start`（如 08:00→08:00）= 24 小時。
- 所有時數內部以 **整數分鐘** 計算，避免浮點誤差讓「剛好等於上限」的邊界失準。UI 以小時顯示（可 0.5 小時）。
- 週：`WeekStart(date) = date - ((7 + (int)date.DayOfWeek - 1) % 7)`，週一為起點。

---

## 4. 規則引擎（Core/Rules）

```csharp
interface IRule { IEnumerable<Violation> Evaluate(RosterContext ctx); }
// RosterContext：員工、班別、設定、評估範圍內的所有指派（已展開成 ShiftInterval）
RuleEngine.Evaluate(ctx) => rules.SelectMany(...).OrderBy(Severity).ThenBy(Date).ThenBy(Employee).ThenBy(Rule)
```

**每次編輯後全量重算**，結果是不可變的 `IReadOnlyList<Violation>`；UI 以此重建面板與格子標示。

| # | 規則 | 判定方式 | 嚴重度 | 標示位置 |
|---|---|---|---|---|
| R1 | 時段重疊 | 同員工任兩區間 `a.start < b.end && b.start < a.end` | Error | 兩班各自的格子 |
| R2 | 間隔不足 | 同員工依 start 排序後相鄰兩班，`0 ≤ next.start − prev.end < MinRest`（剛好 11h 不違規；已重疊者交給 R1，不重複報） | Error | 後一班的格子 |
| R3 | 缺少休息日 | 「工作日」= 該日 `[00:00, 24:00)` 與任一班區間有交集；連續 ≥7 個工作日即違規。以**每段最大連續工作日區段**報一筆（非每個 7 日視窗各報一筆，避免洗版）。注意：16:00→00:00 的班不會吃掉隔天（半開區間） | Error | 區段第 7 天起的格子 |
| R4 | 週工時超過上限 | 依開始日歸週（週一起），週總分鐘 `> 上限` 才違規 | Error | 該週最後一班（超出點）格子 |
| R5 | 不可上班日 | 指派的「開始日」星期 ∈ 員工不可上班星期 | Warning❓ | 該格 |
| R6 | 人力不足/超額 | 每日 × 啟用班別 × 職稱，已排人數 vs 需求；不足/超額各一種 | Warning | 格網下方「人力需求列」對應格 |

- **R5 與 R1 互不干擾**：各自獨立 rule，各自產出；測試會構造「同時觸發兩者」與「只觸發一者」的案例。
- **Violation 身分鍵 `ViolationKey`**＝(規則, 員工, 錨點日期, 班別, 職稱)，**不含說明文字與數量**。候選人比對「新違規」時用 key 做集合差，例如同一班人力 0/2 → 1/2 的不足違規 key 不變，不算新違規。

### 4.1 跨月份邊界 ❓待決（建議：做）
R2/R3/R4 天生跨月（10/1 的班要看 9/30 的打烊班；週可能跨月）。建議：
- 評估時載入「本月 ± 7 天」的指派作為上下文；
- 面板只顯示**錨點落在本月**的違規。

否則月初／月底會出現「看不到的違規」或「假的休息日」。

---

## 5. 候選人建議（Core/Candidates）

輸入：一個人力不足的 slot（日期、班別、職稱）。
1. 候選池：在職、職稱相符、該日尚未排此班的員工。
2. 對每位候選人：`after = Evaluate(ctx + 指派)`；若 `keys(after) − keys(before) == ∅` 則可用。
3. 排序：本月累計工時（分鐘，依開始日在本月）↑ → 姓名（`StringComparer.Ordinal`）↑ → Id ↑（保證穩定）。
4. 無人可用：統計每位候選人「第一個造成新違規的規則」，產出原因：
   - 全部同一規則 →「全部候選人都會違反間隔規則」
   - 混合 →「3 人違反間隔、2 人週工時超過上限」
   - 候選池空 →「沒有在職的外場員工」
5. 一鍵指派 = 執行 `AssignCommand`（可 undo）。

計算量：9 人 × 一次全量評估，可忽略。

---

## 6. Undo/Redo 與 Session（Core/History, Core/Session）

```csharp
interface IRosterCommand { string Description { get; }
    RosterDelta Apply(RosterState s);     // 回傳實際新增/刪除的指派
    RosterDelta Revert(RosterState s); }
// AssignCommand / UnassignCommand / CopyWeekCommand(來源週, 目標週)
```
- `UndoRedoStack(capacity = 100)`（≥20 層），新 command 執行時清空 redo。
- `RosterSession` 是 UI 唯一入口：`Execute / Undo / Redo` →（1）改記憶體狀態（2）`IRosterStore` 以 transaction 寫入差量（3）重算違規（4）發出 `Changed` 事件。**違規清單永遠由狀態導出**，所以 undo 後自動同步還原。
- **CopyWeek 語意 ❓待決**，建議：
  - 目標週先清空再複製（覆蓋，而非合併），整個動作是一筆 undo；
  - 跳過已停用的員工／班別；
  - 只處理目標週落在當前月份的日期（月份外的日子使用者看不到）。
- **Undo 歷史範圍 ❓待決**，建議：全域一條堆疊，切月份不清空；undo 一個非本月的操作時自動跳到該月。不跨程式重啟保存。

---

## 7. 持久化（Data）

```sql
PRAGMA user_version = 1;
CREATE TABLE employees  (id INTEGER PRIMARY KEY, name TEXT NOT NULL, role INTEGER, type INTEGER,
                         weekly_limit_minutes INTEGER, unavailable_days INTEGER, -- bitmask Mon..Sun
                         is_active INTEGER NOT NULL DEFAULT 1);
CREATE TABLE shift_types(id INTEGER PRIMARY KEY, name TEXT NOT NULL, start_minute INTEGER, end_minute INTEGER,
                         sort_order INTEGER, is_active INTEGER NOT NULL DEFAULT 1);
CREATE TABLE shift_requirements(shift_type_id INTEGER REFERENCES shift_types(id), role INTEGER, required INTEGER,
                         PRIMARY KEY(shift_type_id, role));
CREATE TABLE assignments(employee_id INTEGER REFERENCES employees(id), date TEXT NOT NULL, -- 'yyyy-MM-dd'
                         shift_type_id INTEGER REFERENCES shift_types(id),
                         PRIMARY KEY(employee_id, date, shift_type_id));
CREATE INDEX ix_assignments_date ON assignments(date);
CREATE TABLE settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);
```
- 「各月份獨立儲存」= 指派以日期為鍵，月份是查詢範圍（`date BETWEEN ...`），切月份不互相影響。
- 員工／班別只停用不刪除，FK 保證歷史班表可回溯。
- DB 路徑：預設 `%LocalAppData%\RosterGuard\rosterguard.db`；可由建構參數覆寫（測試與 Headless 用暫存檔）。
- 每次 command / 主檔編輯即寫入（autosave），WAL 模式。

---

## 8. 匯出（Core/Export）

**CSV**：列 = 員工（姓名、職稱），欄 = 本月每日 `10/01(四)`，格內多班以 `、` 連接；最後一欄本月總工時。`UTF-8 with BOM`、CRLF、RFC 4180 引號跳脫。Core 只產生字串／寫入 `Stream`，存檔對話框在 App。

**ICS**（單一員工、當月）：
- `VCALENDAR / VERSION:2.0 / PRODID / CALSCALE:GREGORIAN`，附 `VTIMEZONE Asia/Taipei`（固定 +0800），`DTSTART;TZID=Asia/Taipei:20261001T180000`、`DTEND` 跨日正確為隔天 02:00。
- `SUMMARY:打烊班`（中文）、`UID` 可重現（`{員工}-{日期}-{班別}@rosterguard`）、`DTSTAMP`。
- UTF-8 **無 BOM**、CRLF、文字跳脫（`, ; \`）、**75 octet 折行且不切斷 UTF-8 多位元組字元**。
- 測試：以 Ical.Net 解析，驗證事件數、起訖、中文標題。

---

## 9. UI 設計（App）

```
┌ 選單：檔案(匯出CSV/ICS) 主檔(員工/班別/設定) 編輯(復原/重做/複製上週) ┐
│ ◀ 2026年10月 ▶                                                    │
├──────────┬─────────────────────────────────────────┬──────────────┤
│ 員工(凍結) │ 10/1 四 │ 10/2 五 │ ...（水平捲動）          │ 違規面板       │
│ 王小明 外場│ 早 ⚠   │ 打烊🔴 │                          │ 規則│員工│日期│說明│
│ ...       │        │        │                          │ （點選→捲動標示）│
├──────────┼─────────────────────────────────────────┤ 候選人建議     │
│ 人力 早/外 │ 2/2    │ 1/2 🔻 │  ← 點選「不足」顯示候選人   │ 姓名 工時 [指派]│
├──────────┴─────────────────────────────────────────┴──────────────┤
│ 狀態列：違規 12 筆（Error 8 / Warning 4）  已儲存                     │
```
- 格網用**自訂 ItemsControl + ScrollViewer**（不用 DataGrid）：31 個動態欄位在 DataGrid 中要程式生成，且 Headless 測試較難操作；自訂格網較好做「捲動到格子 + 高亮」（`BringIntoView`）。
- 點格子 → Flyout 列出**啟用中**班別的勾選清單（勾 = 指派，取消勾 = 取消指派）；已指派的停用班別仍顯示。
- 格子依違規類型上色＋圖示（圖例在面板頂端），一格多種違規時顯示最嚴重色＋多個圖示。
- `Ctrl+Z / Ctrl+Y` 以 Window `KeyBindings` 綁到 Undo/Redo command。
- 人力不足/超額沒有對應員工，所以格網下方加「人力需求列」（班別×職稱），作為 R6 的標示位置與候選人建議入口。
- README 截圖：以 Headless + Skia 渲染（`CaptureRenderedFrame`）自動產生，避免手動截圖過期。

---

## 10. 測試策略

| 層 | 工具 | 內容 | 目標數 |
|---|---|---|---|
| Time | xUnit | 跨午夜時數、起訖、24h 班、週一計算 | ~6 |
| Rules | xUnit `[Theory]` | 每條規則的邊界（驗收 4–8 逐條對應） | ~25 |
| Candidates | xUnit | 只列無新違規、排序穩定、同工時依姓名、無人原因 | ~6 |
| History/Session | xUnit + FakeStore | 20+ 層、redo 清空、undo 後違規還原、CopyWeek | ~6 |
| Export | xUnit + Ical.Net | BOM、引號跳脫、ICS 解析、跨日、中文、折行 | ~5 |
| Data | xUnit + 暫存 SQLite | 重開後內容不變、停用保留歷史、月份隔離 | ~4 |
| Sample | xUnit | 載入範例班表 → 違規結果 == `expected-violations` | 1 |
| UI | Avalonia.Headless.XUnit | 啟動、指派、違規顯示、點選捲動標示、Ctrl+Z | 4–5 |

合計單元測試 ≈ 50+（驗收要求 ≥40）。範例資料的預期違規清單同時是一份自動化測試，文件與程式不會漂移。

---

## 11. CI/CD（.github/workflows/ci.yml）
`windows-latest` → `actions/setup-dotnet` `9.0.x` → `restore` → `build -c Release` → `test`（含 Headless）→ `publish src/RosterGuard.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` → `upload-artifact`。README 放 workflow badge（需要 GitHub repo 路徑 ❓）。

---

## 12. 主要取捨

| 決策 | 選擇 | 放棄的方案 / 理由 |
|---|---|---|
| 重算策略 | 全量重算 | 增量演算法：資料量小，正確性 > 效能 |
| 資料存取 | Microsoft.Data.Sqlite 手寫 SQL | EF Core：5 張表不值得 migration/追蹤成本 |
| 專案切分 | Core / Data / App 三層 | Core 含 SQLite：Core 會依賴 IO，測試變慢 |
| Undo | Command pattern（記錄差量） | 整份快照：也可行，但差量同時能拿來做增量寫 DB |
| 格網 | 自訂 ItemsControl | DataGrid：動態欄位與捲動定位較麻煩 |
| 時間單位 | 整數分鐘 | double 小時：邊界比較有浮點風險 |
| ICS | 手寫 + 測試用 Ical.Net 驗證 | production 引用 Ical.Net：多一個依賴 |
| 違規報法 | R3 每段連續工作日報一筆 | 每個 7 日視窗一筆：同一問題重複 N 筆 |

## 13. 已知限制（預計寫進 README）
- 修改班別起訖時間會**回溯影響**所有既有班表的違規判定（指派只存班別 Id，不做時間快照）。❓
- Undo 歷史不跨程式重啟。
- 只支援 Windows 測試；Avalonia 理論上跨平台但不保證。
- 不處理到離職、請假、國定假日、特定日期人力覆寫（依題目範圍）。

## 14. 開發里程碑（每步可編譯、可測試、獨立 commit）
| M | 內容 | 預估 |
|---|---|---|
| 0 | Solution 骨架、CPM、CI 綠燈 | 2h |
| 1 | Domain + Time（TDD） | 2h |
| 2 | 6 條規則 + RuleEngine（TDD） | 6h |
| 3 | Session + Undo/Redo + CopyWeek | 3h |
| 4 | 候選人建議 | 2h |
| 5 | CSV / ICS 匯出 | 2.5h |
| 6 | SQLite Store + 整合測試 | 2.5h |
| 7 | UI：主視窗、格網、面板、主檔編輯 | 8h |
| 8 | Headless 測試 | 2h |
| 9 | 範例資料 + 預期違規清單 + README + 截圖 | 3h |
| | **合計** | **≈ 33h**（題目估 26–34h） |
