"""Build split/dividend-adjusted daily returns for 0050 and 00631L from TWSE raw data,
then calibrate the synthetic model used in lev.py.

Synthetic model:  r_0050 = r_TRI - fee_a/252
                  r_L    = 2 * r_TRI - rf - fee_l/252
"""
from pathlib import Path

import numpy as np
import pandas as pd

import bt

D = Path(__file__).parent / "data"


def _norm(s: pd.Series) -> pd.Series:
    s.index = pd.DatetimeIndex(s.index).as_unit("ns")
    return s.sort_index()


def returns_0050(rt: pd.Series) -> pd.Series:
    """Total-return daily returns of 0050 (dividends reinvested, 2025 1:4 split handled)."""
    px = _norm(pd.read_csv(D / "twse_0050.csv", index_col=0, parse_dates=True)["close"].dropna())
    div = _norm(pd.read_csv(D / "div_0050.csv", index_col=0, parse_dates=True)["dividend"])
    r = (px + div.reindex(px.index).fillna(0)) / px.shift(1) - 1
    # split: a one-day ratio ~0.25 (1 -> 4)
    split_days = r.index[r < -0.6]
    for d in split_days:
        i = px.index.get_loc(d)
        # remove the day's expected index move before rounding the split ratio
        ratio = round(px.iloc[i - 1] * (1 + rt.get(d, 0)) / px.iloc[i])
        r.iloc[i] = px.iloc[i] * ratio / px.iloc[i - 1] - 1
        print(f"0050 split on {d.date()} ratio 1:{ratio}")
    return r.dropna()


def returns_L(rt: pd.Series) -> pd.Series:
    """00631L daily returns (no dividends; 1:22 split on 2026-03-31)."""
    px = _norm(pd.read_csv(D / "twse_00631L.csv", index_col=0, parse_dates=True)["close"].dropna())
    r = px / px.shift(1) - 1
    for d in r.index[r < -0.6]:
        i = px.index.get_loc(d)
        ratio = round(px.iloc[i - 1] * (1 + 2 * rt.get(d, 0)) / px.iloc[i])
        r.iloc[i] = px.iloc[i] * ratio / px.iloc[i - 1] - 1
        print(f"00631L split on {d.date()} ratio 1:{ratio}")
    return r.dropna()


def main():
    tri, _ = bt.load_series(extend=False)
    rt = _norm(tri.pct_change().dropna())
    ra, rl = returns_0050(rt), returns_L(rt)
    df = pd.concat({"I": rt, "A": ra, "L": rl}, axis=1, join="inner", sort=True).dropna()
    rf = (1 + bt.cash_rate(df.index)) ** (1 / bt.TD) - 1
    yrs = len(df) / bt.TD
    cg = lambda r: (1 + r).prod() ** (1 / yrs) - 1
    print(f"window {df.index[0].date()}..{df.index[-1].date()} ({yrs:.1f}y)")
    print(f"actual CAGR  TRI {cg(df.I):.2%}  0050 {cg(df.A):.2%}  00631L {cg(df.L):.2%}")
    bad = df[(df.A - df.I).abs() > 0.04]
    print("0050 vs TRI daily anomalies >4%:", len(bad))
    # fee_a: compounded gap 0050 vs TRI;  fee_l: compounded gap L vs (2*0050 - rf)
    fee_a = cg(df.I) - cg(df.A)
    fee_l_vs_tri = cg(2 * df.I - rf) - cg(df.L)
    fee_l_vs_a = cg(2 * df.A - rf) - cg(df.L)
    print(f"fee_a (TRI - 0050)             {fee_a:+.2%}/yr")
    print(f"fee_l vs 2*TRI-rf               {fee_l_vs_tri:+.2%}/yr")
    print(f"fee_l vs 2*0050-rf (true drag)  {fee_l_vs_a:+.2%}/yr")
    print("yearly 0050 - TRI:", ((1 + df.A).groupby(df.index.year).prod()
                                 - (1 + df.I).groupby(df.index.year).prod()).round(3).to_dict())
    print("corr L vs 2*0050:", round(np.corrcoef(df.L, 2 * df.A)[0, 1], 4))
    pd.DataFrame({"r_0050": df.A, "r_00631L": df.L, "r_tri": df.I}).to_csv(D / "actual_returns.csv")


if __name__ == "__main__":
    main()
