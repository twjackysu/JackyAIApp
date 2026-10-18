# tw_backtest — 0050 / 00631L 回落煞車回測

規格中所有回測數字都可以用本資料夾的程式和資料重現。

## 一鍵重現規格數字

```bash
python reproduce_spec.py
```

輸出內容依序是：資料檔 SHA256、套件版本、規格第 2 節的表格、其他引用數字、驗收測試的期望值。
參考輸出：`results/reproduce_spec.txt`（產生於 2026-10-11，資料截至 2026-10-08）。

環境：Python 3.13.5、pandas 3.0.6、numpy 2.3.5（只依賴 pandas 和 numpy）。

## 資料快照（data/）

| 檔案 | 來源 | 內容 | SHA256 前 16 碼 |
|---|---|---|---|
| `taiex_tri.csv` | 證交所 MFI94U，`fetch_tri.py` | 發行量加權股價報酬指數，2003-01-02 ~ 2026-10-08 | `e9f95b791f9b9e7c` |
| `TWII.csv` | Yahoo Finance `^TWII` | 加權指數（價格），只用於 1997-07 ~ 2002-12 | `53d82388985db277` |
| `twse_0050.csv` | 證交所 STOCK_DAY，`fetch_stock_day.py 0050 2014-10` | 0050 原始收盤價 | `7f7e6f5dc9b3a3fb` |
| `twse_00631L.csv` | 證交所 STOCK_DAY，`fetch_stock_day.py 00631L 2014-10` | 00631L 原始收盤價 | `e89e648246570e62` |
| `div_0050.csv` | 證交所除權息計算結果（TWT49U） | 0050 2014~2026 年共 23 次配息 | `6836bdff249d193a` |
| `actual_returns.csv` | `calib.py` 產生 | 0050（含息、處理 1:4 分割）和 00631L（處理 1:22 分割）的日報酬 | `bd8a113724e5cf35` |

重新抓資料會更新最新月份的資料，雜湊值和數字也會跟著變動。要驗證規格裡的數字，請使用上表的快照。

## 方法和假設（規格數字的定義）

| 項目 | 設定 | 程式位置 |
|---|---|---|
| 訊號序列 | 2003 年以後用官方報酬指數；1997–2002 用加權指數 × 估計股利 3.59%/年（由 2003–2026 年報酬指數和價格指數的差推算） | `bt.load_series` |
| 訊號 | 回落煞車：dd < -8%（相對 252 日高點）→ 防守；收盤 > SMA60 → 進攻；兩者同時成立時進攻優先 | `bt.drawdown_brake`、`bt._hyst` |
| 成交 | lag=1：用當天收盤價成交；lag=2：隔天收盤成交 | `lev.simulate(lag=)` |
| 模擬 0050 | 報酬指數日報酬 − 0.3%/年 | `lev.asset_returns`、`FEE_A` |
| 模擬 00631L | 2 × 報酬指數日報酬 − 現金利率 − 3.5%/年（= 2 × 0.3% + 實測耗損 2.9%） | `lev.asset_returns`、`FEE_L` |
| 校正來源 | 2014-11 ~ 2026-10 實際資料：00631L 相對「2 × 0050 − rf」每年落後 2.90% | `calib.py` |
| 現金利率 | 依年份近似的台灣一年期定存利率 | `bt.cash_rate` |
| 交易成本 | 每單位換手 0.25% | `lev.COST` |
| 再平衡 | 狀態切換時，或有效曝險偏離目標超過 0.10 時 | `lev.simulate(band=)` |
| 配置 | `composition(E, k)`，見規格第 3 節 | `lev.composition` |
| 指標 | CAGR、最大回撤（收盤價計算）、Sharpe（扣除現金利率） | `bt.stats` |

## 其他分析（規格之外的引用來源）

| 程式 | 內容 | 輸出 |
|---|---|---|
| `run_all.py` + `summarize.py` | 只用 0050：約 1,200 組參數掃描、樣本內/樣本外/walk-forward、創新高事件研究 | `results/summary.txt` |
| `robust.py` | 只用 0050：煞車參數的穩定性、各次崩盤表現、深跌加碼測試 | `results/robust.txt` |
| `lev_run.py 0.003 0.035` | 0050 + 00631L：1,386 組參數掃描、walk-forward | `results/lev_grid_lag*.csv`、`results/lev_walk_forward.csv` |
| `lev_analyze.py` | 0050 + 00631L：進攻 × 防守對照表、實際資料驗證、崩盤表現、年報酬 | `results/lev_analyze.txt` |
| `gap_study.py` | 跳空開低後的盤中表現（指數開盤價 vs 0050 成交價）、單日大跌後的報酬；`docs/DAILY_PLAYBOOK_PLAN.md` 引用 | `results/gap_study.txt` |
| `brake_levels.py [額外資料.csv]` | 回落煞車「下一個交易日收盤」的觸發門檻（SMA60 與 -8% 的封閉解）；預設補上 2026-10-12～10-16 的報酬指數 | stdout |

注意：`lev_analyze.py` 的表格使用 k=0（不保留現金）；規格中的配置有保留現金（k=0.2 / 0.1），所以規格數字以 `reproduce_spec.py` 為準。

## 已知限制
- 2014 年以前的 0050 和 00631L 是模擬的；1997–2002 年的股利是估計值。
- 優勢主要來自 2000 年和 2008 年兩次大空頭，樣本數少。
- 沒有計入股利所得稅、借券、盤中跳空（00631L 曾經單日跌 20%）。
- 0050 在 2014~2026 年每年跑贏報酬指數約 2%（台積電權重較高），模擬時沒有計入，所以實際資料的報酬高於模擬值。
