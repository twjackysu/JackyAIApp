"""回落煞車「明天的觸發價位」（docs/DAILY_PLAYBOOK_PLAN.md 附錄 A 引用的數字）。

規則與 JackyAIApp.Server/Services/Portfolio/DrawdownBrake/DrawdownBrakeCalculator.cs 相同：
  收盤 > SMA60 → 進攻；否則回撤 < -8%（相對 252 日高點）→ 防守；兩者都不成立 → 維持前一天狀態。
因為 SMA60 和 252 日高點都包含「明天的收盤」，所以門檻有封閉解：
  SMA60 門檻：v > (前 59 天收盤合計) / 59
  防守門檻：  v < 0.92 × 前 251 天最高收盤（v 不會是新高，否則回撤為 0）

用法：python brake_levels.py [額外資料.csv]
  額外資料.csv 欄位 date,tri，補上 data/taiex_tri.csv 之後的交易日（例如從證交所 MFI94U 抄下）。
  沒給檔案時使用下方 EXTRA（2026-10-12 ~ 10-16，來源：證交所發行量加權股價報酬指數）。
"""
import sys
from pathlib import Path

import pandas as pd

HERE = Path(__file__).resolve().parent
EXTRA = {
    "2026-10-12": 114004.54,
    "2026-10-13": 111783.15,
    "2026-10-14": 112441.07,
    "2026-10-15": 108611.89,
    "2026-10-16": 108938.83,
}
DD_TRIGGER, REENTER_SMA, LOOKBACK_HIGH = 0.08, 60, 252


def load(extra_csv: str | None) -> pd.Series:
    s = pd.read_csv(HERE / "data" / "taiex_tri.csv", parse_dates=["date"]).set_index("date")["tri"]
    if extra_csv:
        e = pd.read_csv(extra_csv, parse_dates=["date"]).set_index("date")["tri"]
    else:
        e = pd.Series(EXTRA).rename(index=pd.Timestamp)
    return pd.concat([s[~s.index.isin(e.index)], e]).sort_index()


def state_series(s: pd.Series) -> pd.Series:
    sma = s.rolling(REENTER_SMA).mean()
    dd = s / s.rolling(LOOKBACK_HIGH, min_periods=1).max() - 1
    state, out = "OFFENSE", []
    for c, m, d in zip(s, sma, dd):
        if pd.notna(m) and c > m:
            state = "OFFENSE"
        elif d < -DD_TRIGGER:
            state = "DEFENSE"
        out.append(state)
    return pd.Series(out, index=s.index)


def main() -> None:
    s = load(sys.argv[1] if len(sys.argv) > 1 else None)
    last, last_date = s.iloc[-1], s.index[-1].date()
    state = state_series(s).iloc[-1]
    sma_level = s.iloc[-(REENTER_SMA - 1):].sum() / (REENTER_SMA - 1)
    high = s.iloc[-(LOOKBACK_HIGH - 1):].max()
    dd_level = (1 - DD_TRIGGER) * high

    print(f"最後一筆 {last_date}：報酬指數 {last:,.2f}，狀態 {state}，"
          f"距 252 日高點 {high:,.2f} 回撤 {last / high - 1:+.2%}")
    print(f"下一個交易日收盤：")
    print(f"  高於 {sma_level:,.2f}（{sma_level / last - 1:+.2%}）→ 進攻（站上 SMA60）")
    print(f"  低於 {dd_level:,.2f}（{dd_level / last - 1:+.2%}）且未站上 SMA60 → 防守")
    if dd_level < sma_level:
        print(f"  介於兩者之間 → 維持 {state}")
    else:
        print("  防守門檻高於 SMA60 門檻：跌破 SMA60 即同時成立回撤條件")


if __name__ == "__main__":
    main()
