# 第三方 API 契約測試 (Contract Tests) — 架構設計

> 目標:當第三方 API 悄悄改欄位名/結構/停用時,能自動偵測,而不是等使用者回報功能壞掉。
> 範圍:**只測本專案直接連的 API**。透過第三方套件(TWStockLib)取得的資料不測 — 交給該套件自己的測試。
> 供 Sonnet 實作。設計原則:效能、可維護、可擴充、可讀。撰寫日期:2026-07-06。

## 一、核心觀念:兩種測試,別混為一談

| | Offline 解析測試 (parser/regression) | Live 契約測試 (contract) |
|---|---|---|
| 打真網路? | ❌ 用存下來的 fixture JSON | ✅ 打真 API |
| 抓什麼壞掉 | **我們自己**改壞了解析邏輯 | **對方**改了 API 結構 |
| 在哪跑 | 每次 CI / PR(快、穩定) | 排程(cron),**不進 PR CI** |
| 已存在範例 | `DividendServiceTests`(StubHandler + fixture) | 無 — 這份計畫要新增 |
| 失敗代表 | code review 該擋下的 bug | 上游變了,功能即將壞,要修 |

使用者要的「third party api interface test + 每天隨機挑幾個 stock」= **Live 契約測試 + cron**。
但**強烈建議兩種一起補**:offline 測試才是防止我們自己改壞的第一道防線,而且不依賴外部可用性,能進 CI。

**鐵律:Live 契約測試絕對不能進 PR 的 CI**。理由:(1) 外部 API 掛掉會讓無關的 PR 變紅;(2) 需要 API key(FRED/Finnhub);(3) 慢。它只在排程跑,壞了開 issue/通知,不擋開發。

## 二、直接連第三方 API 的完整清單

掃過整個 `JackyAIApp.Server`,以下是**直接發 HTTP 並解析回應結構**的整合點(會因對方改動而壞的)。已排除純設定 URL(PayPal/ECPay/OAuth redirect)與透過 TWStockLib 取得的歷史股價。

### A 組:Portfolio 核心(使用者最依賴,優先最高)

| # | 整合 | 端點 | 消費者 | 解析的關鍵欄位 | 脆弱度 |
|---|---|---|---|---|---|
| A1 | **Yahoo Finance v8 chart** | `query1.finance.yahoo.com/v8/finance/chart/{sym}` | `YahooFinanceMarketDataProvider`(US 價)、`DividendService`(`events=div`)、`MarketOverviewProvider`(儀表板)、`PortfolioService`(US 價) | `chart.result[0].meta.{currency,regularMarketPrice,chartPreviousClose}`、`.timestamp[]`、`.indicators.quote[0].{open,high,low,close,volume}`、`.events.dividends{}.{amount,date}` | 高(非官方 API,無合約保證,已知會擋 non-browser UA) |
| A2 | **FundRich(基富通)** | `apis.fundrich.com.tw/...` 三支:`FRSIndex/`(搜尋)、`GetFundChart`、`GetFundDividendHistory` | `FundDataService`、`DividendService` | 搜尋:`data.list[].{fundId,name,isSell}`;NAV:`data.tableRow.{currency,priceHistory[].{date,price}}`;配息:`data.tableRow.history[].{exDividendDate,perUnitPrice}` | 極高(逆向工程私有 API,無文件,需偽造 Origin/Referer) |
| A3 | **匯率 open.er-api.com** | `open.er-api.com/v6/latest/USD` | `PortfolioService.FetchUsdTwdRateAsync` | `rates.TWD` | 中(免費公開 JSON,結構簡單但可能停用) |

### B 組:Finance 個股分析

| # | 整合 | 端點 | 消費者 | 解析的關鍵欄位 | 脆弱度 |
|---|---|---|---|---|---|
| B1 | **TWSE OpenAPI**(估值/營收/EPS/收盤) | `openapi.twse.com.tw/v1/...`:`BWIBBU_d`、`t187ap05_L`、`t187ap14_L`、`STOCK_DAY_ALL` | `TWSEFundamentalDataProvider` | `Code`/`公司代號`、`PEratio`、`PBratio`、`DividendYield`、`營業收入-當月營收`、`基本每股盈餘(元)`、`ClosingPrice`… **含中文欄位名** | 高(官方但改版會換中文欄名) |
| B2 | **TWSE OpenAPI**(大盤/類股/融資) | `FMTQIK`、`MI_INDEX`、`MI_MARGN` | `TWSEMacroEconomyProvider` | 類股中文名 hash、指數欄位 | 中 |
| B3 | **TWSE OpenAPI**(籌碼) | (見 `TWSEChipDataProvider`) | `TWSEChipDataProvider` | 三大法人/融資欄位 | 中 |
| B4 | **TWSE OpenAPI**(公司清單) | `openapi.twse.com.tw/v1` company list | `StockCodeResolver`、`TWSEDataService` | 公司代號/名稱 | 中 |
| B5 | **Bank of Taiwan 匯率 CSV** | `rate.bot.com.tw/xrt/flcsv/0/day` | `TWSEMacroEconomyProvider` | CSV 欄位位置 | 高(已知 BOT 會對非瀏覽器擋成 HTML — Portfolio 已因此改用 A3,但 macro 還在用) |
| B6 | **CBC 利率 CSV** | `cbc.gov.tw/.../A13Rate.csv` | `TWSEMacroEconomyProvider` | CSV 欄位 | 中 |
| B7 | **SEC EDGAR**(美股基本面) | `sec.gov/files/company_tickers.json`、`data.sec.gov/api/xbrl/companyfacts/CIK*.json` | `SECFundamentalDataProvider` | ticker→CIK 對照、XBRL `facts.us-gaap.*` tag(EPS/BVPS/dividend) | 高(XBRL tag 名多變) |
| B8 | **SEC EDGAR**(內部人交易 HTML) | `sec.gov/...` | `SECInsiderTradingProvider` | **HTML table 爬蟲**(HtmlAgilityPack) | 極高(HTML 結構隨時變) |
| B9 | **Finnhub 新聞** | `finnhub.io/api/v1/news` | `FinnhubNewsProvider` | array `[].{datetime,headline,summary,source}` | 中(需 key) |
| B10 | **FRED 總經** | `api.stlouisfed.org/fred/series/observations` | `FredIndicatorProvider` | `observations[].{date,value}` | 低(官方穩定,需 key) |

### C 組:其他(優先較低,非核心投資功能)

| # | 整合 | 消費者 | 脆弱度 |
|---|---|---|---|
| C1 | **104 人力銀行** JSON | `Job104Provider` | 高(私有 API) |
| C2 | **Cake** `__NEXT_DATA__` | `CakeJobsProvider` | 極高(爬 Next.js 內嵌 JSON) |
| C3 | **Dify** | `DifyApiClient` | 低/可能已停用 — 實作前先確認還有沒有在用 |

## 三、Offline 解析測試(每個整合都該有,進 CI)

沿用 `DividendServiceTests` 的既有 pattern:自訂 `HttpMessageHandler` stub 回傳存好的 fixture JSON → 斷言解析出正確的 domain 物件。

- **每個 provider 一個測試類別**,涵蓋:(a) 正常回應解析正確;(b) 缺欄位/null 值不炸(回 null 或空,不 throw);(c) HTTP 非 200 → graceful 降級;(d) 網路 throw → 不冒泡。
- Fixture 放 `JackyAIApp.Server.Tests/Fixtures/{Provider}/*.json`,**存的是真實 API 回應的節錄**(實作時各打一次真 API 存下來,順便當作契約基準快照)。
- 這類測試**離線、快、穩**,是防止「我們自己重構時改壞解析」的主力。B1/A2 這種吃中文欄名的最需要。

## 四、Live 契約測試(排程跑,不進 PR CI)

### 4.1 分類與隔離機制

用 xUnit `[Trait("Category", "LiveContract")]` 標記。CI 的 `dotnet test` 加 `--filter "Category!=LiveContract"` 排除(**PR CI 一定要加這行**),排程 job 則用 `--filter "Category=LiveContract"` 只跑這些。

需要 API key 的(FRED/Finnhub)在 key 不存在時**自動 skip**(`Assert.Skip` 或條件判斷),不要 fail。

### 4.2 每個契約測試斷言什麼

**不是**斷言「AAPL 現價 = 150」(值天天變,測了必假陽性)。而是斷言**結構契約還在**:
- 回應能成功解析成 domain 物件(不是 null/空)。
- 解析器依賴的**關鍵欄位存在且型別對**:例:Yahoo → `meta.regularMarketPrice` 是數字且 > 0、`indicators.quote[0].close` 是非空陣列;FundRich 配息 → `history[].exDividendDate` 能 parse 成日期、`perUnitPrice` 能 parse 成數字;TWSE B1 → 回傳陣列非空且第一筆有 `Code`/`公司代號` 與 `PEratio`。
- 合理範圍 sanity check(價 > 0、殖利率 0–100、日期在近期內)—— 抓「欄位還在但語意變了」。

失敗訊息要能一眼看出**哪個整合、哪個欄位**不見了(把缺的欄位名寫進 assert message)。

### 4.3 「每天隨機挑幾個 stock」

對**逐檔查詢**的整合(A1 Yahoo 價/配息、B1 TWSE 個股基本面、B7 SEC),不要每檔都打:
- 各市場維護一個**精選 pool**(靜態清單即可):TW 放 15–20 檔各產業龍頭(2330、2317、0050、00878…)、US 放 15–20 檔流動性高的(AAPL、MSFT、NVDA…)。
- 每次執行用**以日期為種子的 RNG** 隨機抽 K=3~5 檔(seed = 當天日期 → 同一天重跑結果一致、好追;不同天覆蓋不同標的)。
- 好處:長期輪替覆蓋整個 pool、單次執行輕量、不狂打對方 API。
- 對**單一端點**的整合(A3 匯率、B2/B4/B5/B6 大盤類、B9/B10 新聞總經)沒有 per-stock 概念,固定打那一支即可。

### 4.4 排程執行(GitHub Actions cron)

新增 `.github/workflows/contract-tests.yml`:
```yaml
on:
  schedule:
    - cron: '0 1 * * *'   # 每天一次(UTC,避開台美盤中尖峰);台股資料日更足夠
  workflow_dispatch: {}    # 允許手動觸發
```
- 只跑 `--filter "Category=LiveContract"`。
- FRED/Finnhub 的 key 放 GitHub Secrets,透過環境變數注入(沒設就讓那些測試 skip)。
- **失敗時開 issue 或發通知**(GitHub Actions 內建 job failure 通知即可,或用 action 自動建 issue)。重點是「壞了你會知道」,不是擋 PR。
- 建議把逐檔的 Yahoo/TWSE 契約測試每天跑;SEC/FundRich 這種較敏感或較慢的可以降頻(每週),用不同 cron job 或 trait 細分。

## 五、實作結構建議

```
JackyAIApp.Server.Tests/
├── Fixtures/                          # offline 測試用的真實回應節錄
│   ├── Yahoo/chart_aapl.json
│   ├── FundRich/dividend_history.json
│   └── TWSE/bwibbu_d.json ...
├── Services/                          # 既有 offline 單元/解析測試(進 CI)
│   └── ...ProviderTests.cs
└── LiveContracts/                     # 新增:live 契約測試(排程才跑)
    ├── LiveContractBase.cs            # 共用:HttpClient、隨機挑股工具、skip-if-no-key
    ├── StockPoolProvider.cs          # TW/US 精選 pool + 日期種子隨機抽 K 檔
    ├── YahooChartContractTests.cs
    ├── FundRichContractTests.cs
    ├── TwseOpenApiContractTests.cs
    ├── FxRateContractTests.cs
    ├── SecEdgarContractTests.cs
    ├── FinnhubContractTests.cs
    └── FredContractTests.cs
```

- `LiveContractBase`:提供真的 `HttpClient`(不 mock)、共用 UA header、`RequireApiKey(name)` helper(回傳 key 或觸發 skip)。
- 契約測試**不碰 DI/DbContext**,直接建 provider 或直接打 HTTP 再套用同一份解析邏輯 —— 目的只驗「對方的形狀」,不驗我們的 wiring。
- 若解析邏輯藏在 private method,實作時可能要把「純解析」抽成 internal static(輸入 JSON string → 輸出 domain 物件),讓 offline 與 live 測試共用同一個解析器(避免測試複製解析邏輯而失去意義)。這重構本身也讓 provider 更好測。

## 六、優先順序(給 Sonnet 的執行序)

分階段,每階段一個 PR、獨立可合併、CI 全綠:

1. **Phase 1 — 骨架 + Portfolio 核心(A 組)**:建 `LiveContracts/` 底座 + `StockPoolProvider` + Yahoo/FundRich/FX 的 live 契約測試 + 對應 offline 解析測試(FundDataService 目前無測試,補上)。cron workflow 先建好只跑 A 組。
2. **Phase 2 — Finance TWSE(B1–B4)**:TWSE OpenAPI 契約 + offline(中文欄名最該釘住)。
3. **Phase 3 — 其餘 Finance(B5–B10)**:BOT/CBC CSV、SEC、Finnhub、FRED。SEC HTML 爬蟲(B8)特別脆,契約測試價值最高。
4. **Phase 4 — Jobs(C 組)**:104/Cake,視這功能重要性決定要不要做;Dify 先確認是否還在用。

## 七、非目標 / 注意事項

- **不測 TWStockLib 取得的資料**(歷史股價經 `TWStockLibHistoricalProvider`)—— 那是套件的責任。只測我們**直接**打的 API。
- 契約測試**不驗數值正確性**(價格對不對),只驗**結構與型別契約**,避免因市場波動假陽性。
- Live 測試要對「對方暫時掛掉」有容忍度:可考慮 retry 一次,或區分「HTTP 5xx/timeout(對方臨時故障,warn 但不算 fail)」vs「200 但欄位不見(真的變更,fail)」。避免對方偶發 503 就狂開 issue。
- 別把 API key 寫進 repo;FRED/Finnhub key 一律走 GitHub Secrets + 環境變數,缺了就 skip。
- 排程頻率別過高:資料多為日更,一天一次足夠;逐檔用隨機抽樣控制單次請求量,當好 API 公民。
