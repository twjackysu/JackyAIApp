"""跳空與大跌日事件研究（docs/DAILY_PLAYBOOK_PLAN.md 第 2 節引用的數字）。

用法：python gap_study.py   → 輸出到 stdout，並寫入 results/gap_study.txt

1. 加權指數（^TWII）大跳空開低後，開盤→收盤的表現
2. 同一件事改用可交易的 0050 成交價重算（除息日用參考價、排除 1:4 分割日）
3. 收盤跌幅 ≤ -2% / -3% 之後 1/5/20/60 日的報酬，以及分多空格局（SMA200）

重點：加權指數的「開盤價」在開盤當下有許多成分股尚未成交、沿用前一日收盤，
跳空幅度被低估，於是看起來像「跳空後會繼續跌」。用 0050 成交價量測時這個現象消失。
"""
import io
import sys
from contextlib import redirect_stdout
from pathlib import Path

import numpy as np
import pandas as pd

HERE = Path(__file__).resolve().parent
DATA = HERE / "data"

BUCKETS = [(-1, -0.02), (-0.02, -0.01), (-0.01, 0.01), (0.01, 0.02), (0.02, 1)]


def load_twii() -> pd.DataFrame:
    d = pd.read_csv(DATA / "TWII.csv", skiprows=[1, 2]).rename(columns={"Price": "Date"})
    d["Date"] = pd.to_datetime(d["Date"])
    d = d.set_index("Date").astype(float)
    d["gap"] = d.Open / d.Close.shift(1) - 1
    d["intra"] = d.Close / d.Open - 1
    d["cc"] = d.Close / d.Close.shift(1) - 1
    return d


def load_0050() -> pd.DataFrame:
    p = pd.read_csv(DATA / "twse_0050.csv", index_col=0, parse_dates=True)
    div = pd.read_csv(DATA / "div_0050.csv", parse_dates=["date"])
    p["prev"] = p.close.shift(1)
    for _, r in div.iterrows():
        if r.date in p.index:
            p.loc[r.date, "prev"] = r.ref_price
    p["gap"] = p.open / p.prev - 1
    p["intra"] = p.close / p.open - 1
    return p[abs(p.gap) < 0.15]  # 1:4 分割日


def bucket_table(df: pd.DataFrame, label: str) -> None:
    print(f"\n== {label}：依開盤跳空幅度分組的「開盤→收盤」報酬")
    for lo, hi in BUCKETS:
        s = df[(df.gap > lo) & (df.gap <= hi)].intra
        t = s.mean() / (s.std(ddof=1) / np.sqrt(len(s))) if len(s) > 2 else float("nan")
        print(f" 跳空 ({lo:+.0%},{hi:+.0%}]  n={len(s):5d}  平均 {s.mean():+.2%}  中位數 {s.median():+.2%}"
              f"  收紅比例 {(s > 0).mean():.0%}  t={t:+.2f}")


def main() -> None:
    twii = load_twii()
    etf = load_0050()

    print(f"資料：^TWII {twii.index[0].date()}~{twii.index[-1].date()}；0050 {etf.index[0].date()}~{etf.index[-1].date()}")
    bucket_table(twii["2013":], "加權指數 2013 起（指數開盤價，會低估跳空）")
    bucket_table(etf, "0050 成交價（可交易）")

    j = etf.join(twii[["gap", "intra"]], rsuffix="_idx", how="inner")
    big = j[j.gap_idx <= -0.02]
    print("\n== 指數跳空 ≤ -2% 的日子：指數 vs 0050 的跳空與盤中")
    print(big[["gap_idx", "gap", "intra_idx", "intra"]]
          .rename(columns={"gap_idx": "指數跳空", "gap": "0050跳空", "intra_idx": "指數盤中", "intra": "0050盤中"})
          .round(4).to_string())

    c = twii.Close
    print("\n== 收盤跌幅門檻之後的報酬（加權指數價格，不含息，1997 起）")
    for th in [-0.02, -0.03]:
        ev = twii.index[twii.cc <= th]
        parts = []
        for h in [1, 5, 20, 60]:
            fwd = c.shift(-h) / c - 1
            e = fwd.loc[ev].dropna()
            parts.append(f"{h}日: {e.mean():+.2%}（收紅 {(e > 0).mean():.0%}，最差 {e.min():+.1%}；全體平均 {fwd.mean():+.2%}）")
        print(f" 單日 ≤ {th:.0%}：n={len(ev)}")
        for line in parts:
            print("   " + line)

    sma = c.rolling(200).mean()
    print("\n== 單日 ≤ -2% 之後，依當時是否在 SMA200 之上分組")
    for name, mask in [("SMA200 之上", c > sma), ("SMA200 之下", c <= sma)]:
        ev = twii.index[(twii.cc <= -0.02) & mask]
        f5 = (c.shift(-5) / c - 1).loc[ev].dropna()
        f20 = (c.shift(-20) / c - 1).loc[ev].dropna()
        print(f" {name}：n={len(ev)}  5日 {f5.mean():+.2%}  20日 {f20.mean():+.2%}"
              f"  20日收紅 {(f20 > 0).mean():.0%}  20日最差 {f20.min():+.1%}")


if __name__ == "__main__":
    buf = io.StringIO()
    with redirect_stdout(buf):
        main()
    out = buf.getvalue()
    sys.stdout.write(out)
    (HERE / "results" / "gap_study.txt").write_text(out, encoding="utf-8")
