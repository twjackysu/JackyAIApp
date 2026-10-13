# TASK-050：contract test 月營收（`t187ap05_L`）改驗證「形狀」，不因單一公司當日尚無列而失敗

## Owner
Backend Engineer

## Status
READY

## Priority
LOW（僅測試；對正式環境無影響。目的是讓每日排程的 contract test 紅燈保持「有事才紅」）

## Objective
排程 `contract-tests.yml` 連續兩天（run 103＝10/12、run 104＝10/13）失敗，**唯一失敗項**都是 `TwseOpenApiContractTests.Revenue_t187ap05_L_ForTodaysRandomPicks_HasUsableMonthlyRevenue`：

| run | 查不到列的公司 | 同次通過的公司 |
|---|---|---|
| 103（10/12） | 2882、1301、2308 | —（3/3 皆失敗） |
| 104（10/13） | 1301、2891 | 2603（四個欄位斷言全過） |

2603 在 run 104 通過全部欄位斷言 ⇒ **欄位／schema 沒變**，變的只是「某幾家公司當天在這份資料集裡有沒有列」。9/12（run 68）同一測試也失敗過一天（1/3 查不到 2891），隔天自行恢復。

這個測試的定位是「契約（形狀）」，CLAUDE.md 寫明 live contract 測試「asserting response *shape* not exact values」；但現在的第一個斷言（該公司有列）其實是**資料覆蓋**，不是契約。把它當契約來紅，會讓 Boss 習慣忽略紅燈（CLAUDE.md 已經有一個已知的雜訊項：104 人力銀行）。

正式程式碼不需要改：`TWSEFundamentalDataProvider.FetchRevenueData` 找不到列就回 `null`，AI 個股分析只少「月營收」一項，不會報錯。

## Requirements
只改 `JackyAIApp.Server.Tests/LiveContracts/TwseOpenApiContractTests.cs` 的 `Revenue_t187ap05_L_ForTodaysRandomPicks_HasUsableMonthlyRevenue`（Theory 與 `TodaysTwCompanyPicks` 不變）：

1. **該 symbol 有列**：維持現有的四個欄位斷言（`營業收入-當月營收` 為正數、`資料年月` 非空、`營業收入-去年同月增減(%)`／`營業收入-上月比較增減(%)` 欄位存在），**不放寬**。
2. **該 symbol 沒有列**：不再 `Assert` 失敗。改為對**整份資料**斷言形狀，通過就 `return`（沿用本檔 `FetchArrayAsync(...) is not { } data) return;` 的軟跳過慣例）：
   - 陣列至少 100 列（這份資料集約涵蓋全部上市公司，100 遠低於實際規模，只用來擋「空陣列／被換成別的東西」）；
   - 至少一列同時具備 `公司代號`、`營業收入-當月營收`（可解析為正數）、`資料年月`（非空）、`營業收入-去年同月增減(%)`、`營業收入-上月比較增減(%)`。
   - 斷言訊息要帶上 symbol 與實際列數，方便從 Actions 日誌判斷。
3. **不要加日期條件**（例如「每月 10～15 日放寬」）。這種測試在 TWSE 改更新節奏時會再度誤報，且把假設寫死在測試裡。
4. 不改 `TWSEFundamentalDataProvider` 或任何正式程式碼。

## Acceptance Criteria
- `dotnet build` 與 `dotnet test --filter "Category!=LiveContract"` 通過（此變更只在 LiveContract，離線測試數不變）。
- 在 PR 分支上以 `workflow_dispatch` 手動跑一次 `contract-tests.yml`，`Revenue_t187ap05_L_*` 三個 symbol 都通過；PR 描述貼 run 連結與結果。
- 變異驗證一次（改回後再提交，PR 描述寫一行結果）：暫時把整份資料斷言裡的 `營業收入-當月營收` 改成錯誤欄位名，確認「整份資料形狀」斷言會失敗——證明放寬後的測試仍然守得住欄位更名。
- 只動測試檔；無產品程式碼、migration、API 變更。

## Technical Notes
- **假設（未驗證）**：TWSE 在每月申報截止（10 日）後的幾天內分批更新該資料集，所以窗口內部分公司暫時沒有列。依據只是時間型態（9/12 一天、10/12～13 兩天失敗，其餘日子通過）；Tech Lead 的沙箱連不到 `openapi.twse.com.tw`（403），**無法直接看資料**。若 10/14 起仍有公司缺列，請在 PR 描述回報，Tech Lead 再判斷是否為資料集改版。
- 10/7、10/8（run 93／94）的失敗不屬於本任務：那兩個 job 耗時 12～14 分鐘（其他日子約 3～5 分鐘），形態不同；我無法取得失敗清單（Actions 日誌儲存在被沙箱 proxy 擋住的 blob 網域）。
- 沙箱可能沒有 .NET：先前 Engineer 以 `apt` 安裝 `dotnet-sdk-10.0` 成功。

## Dependencies
無。

## PR
（尚無）

## Result

## Review
