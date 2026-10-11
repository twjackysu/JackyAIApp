"""Grid search + out-of-sample + walk-forward for TAIEX timing strategies.

Periods
  PRE  1997-07..2002-12  (price index + estimated dividends; extra out-of-sample)
  IS   2003-01..2014-12  (in-sample: parameters chosen here)
  OOS  2015-01..today    (out-of-sample)
"""
from __future__ import annotations

import itertools
import sys
from pathlib import Path

import numpy as np
import pandas as pd

import bt

OUT = Path(__file__).parent / "results"
OUT.mkdir(exist_ok=True)
PERIODS = {"PRE": ("1997-07-01", "2002-12-31"), "IS": ("2003-01-01", "2014-12-31"),
           "OOS": ("2015-01-01", "2099-12-31"), "FULL": ("1997-07-01", "2099-12-31")}


def grid(floors):
    g = [("BuyHold", {}, bt.buy_hold)]
    for off in floors:
        for n, band in itertools.product([20, 40, 60, 80, 100, 120, 150, 200, 250, 300],
                                         [0, .01, .02, .03, .05]):
            g.append(("SMA", dict(n=n, band=band, off=off), bt.sma_filter))
        for n, band in itertools.product([20, 60, 120, 200], [0, .02]):
            g.append(("EMA", dict(n=n, band=band, off=off), bt.ema_filter))
        for f, s in itertools.product([5, 10, 20, 50], [60, 100, 120, 200, 250]):
            if f < s:
                g.append(("DualMA", dict(fast=f, slow=s, off=off), bt.dual_ma))
        for m in [3, 4, 5, 6, 8, 10, 12]:
            g.append(("MonthlySMA", dict(months=m, off=off), bt.monthly_sma))
        for m in [1, 3, 6, 9, 12]:
            g.append(("TSMOM", dict(months=m, off=off), bt.ts_momentum))
        for e, x in itertools.product([20, 50, 100, 250], [10, 20, 50, 100]):
            g.append(("Donchian", dict(entry=e, exit=x, off=off), bt.donchian))
        for t, lk in itertools.product([.10, .12, .15, .18, .20, .25, .30], [20, 60]):
            g.append(("VolTarget", dict(target=t, look=lk, floor=off), bt.vol_target))
        for d, re_ in itertools.product([.08, .10, .15, .20], [20, 60, 120]):
            g.append(("DDBrake", dict(dd=d, reenter=re_, off=off), bt.drawdown_brake))
        for n, band, t in itertools.product([60, 120, 200], [0, .02], [.15, .20, .25]):
            g.append(("Combo", dict(n=n, band=band, off=off, target=t), bt.combo))
    # 逢跌加碼 ladder: base is the floor; only meaningful for the user's 0.8/0.9 range
    for base in [b for b in floors if b >= 0.5]:
        for d1, d2, peak, hold, tn in itertools.product(
                [.05, .08, .10, .15], [.15, .20, .25, .30, .40], ["ath", "252"],
                [True, False], [0, 200]):
            if d1 < d2:
                g.append(("DipLadder", dict(base=base, d1=d1, d2=d2, peak=peak,
                                            hold=hold, trend_n=tn), bt.dip_ladder))
    # constant-exposure baselines inside the user's range
    for c in [0.8, 0.9]:
        g.append(("Constant", dict(c=c), lambda p, c: pd.Series(c, index=p.index)))
    return g


def evaluate(price, g, lag):
    rows, rets = [], {}
    for fam, kw, fn in g:
        res = bt.run(price, fn(price, **kw), lag=lag)
        key = fam + " " + " ".join(f"{k}={v}" for k, v in kw.items())
        rets[key] = res
        row = {"key": key, "family": fam, **{f"p_{k}": v for k, v in kw.items()}}
        for pname, (a, b) in PERIODS.items():
            sub = res.loc[a:b]
            if len(sub) > 200:
                for k, v in bt.stats(sub).items():
                    row[f"{pname}_{k}"] = v
        rows.append(row)
    return pd.DataFrame(rows), rets


def walk_forward(rets: dict, keys: list[str], start_year=2005, metric="Calmar"):
    """Each Jan 1 pick the best key on all data so far (expanding), hold for the year."""
    any_res = next(iter(rets.values()))
    years = sorted({d.year for d in any_res.index if d.year >= start_year})
    out, picks = [], []
    for y in years:
        hist_end = f"{y - 1}-12-31"
        best, bv = None, -np.inf
        for k in keys:
            h = rets[k]["ret"].loc[:hist_end]
            if len(h) < 3 * bt.TD:
                continue
            s = bt.stats(pd.DataFrame({"ret": h, "pos": rets[k]["pos"].loc[:hist_end],
                                       "turn": rets[k]["turn"].loc[:hist_end]}))
            if s[metric] > bv:
                best, bv = k, s[metric]
        picks.append((y, best))
        out.append(rets[best].loc[str(y)])
    return pd.concat(out), picks


def event_study(price):
    """Forward returns after close makes a new 252-day / all-time high vs all days."""
    hi252 = price.rolling(252).max().shift(1)
    ath = price.cummax().shift(1)
    rows = []
    for h, lbl in [(21, "1m"), (63, "3m"), (126, "6m"), (252, "12m")]:
        fwd = price.shift(-h) / price - 1
        fwd_dd = (price[::-1].rolling(h, min_periods=1).min()[::-1].shift(-1) / price - 1)
        for name, mask in [("all days", pd.Series(True, index=price.index)),
                           ("new 252d high", price > hi252),
                           ("new all-time high", price > ath),
                           ("below 252d high by >20%", price < hi252 * .8)]:
            f = fwd[mask].dropna()
            d = fwd_dd[mask].loc[f.index]
            rows.append({"horizon": lbl, "condition": name, "n_days": len(f),
                         "mean": f.mean(), "median": f.median(), "P(up)": (f > 0).mean(),
                         "P(ret<-10%)": (f < -.10).mean(),
                         "avg max drop in window": d.mean()})
    return pd.DataFrame(rows)


def main():
    price, div = bt.load_series()
    print(f"series {price.index[0].date()}..{price.index[-1].date()}  est. div yield pre-2003 {div:.2%}")
    floors_user = [0.8, 0.9]
    floors_ref = [0.0, 0.3, 0.5]
    g = grid(floors_user + floors_ref)
    for lag in (1, 2):
        df, rets = evaluate(price, g, lag)
        df.to_csv(OUT / f"grid_lag{lag}.csv", index=False)
        if lag == 1:
            rets_lag1 = rets
        print(f"lag{lag}: {len(df)} configs")
    ev = event_study(price)
    ev.to_csv(OUT / "event_study.csv", index=False)

    # walk-forward with the user's floor constraint and with reference floors
    wf_rows = []
    df1 = pd.read_csv(OUT / "grid_lag1.csv")
    floor_col = df1[["p_off", "p_floor", "p_base", "p_c"]].bfill(axis=1).iloc[:, 0].fillna(1.0)
    for label, ok in [("floor>=0.8", floor_col >= 0.8), ("any-floor (ref)", floor_col >= 0)]:
        keys = df1.loc[ok, "key"].tolist()
        wf, picks = walk_forward(rets_lag1, keys)
        s = bt.stats(wf)
        bh = bt.stats(rets_lag1["BuyHold "].loc[wf.index[0]:])
        wf_rows.append({"set": label, **s})
        wf_rows.append({"set": "BuyHold same period", **bh})
        pd.DataFrame(picks, columns=["year", "pick"]).to_csv(
            OUT / f"wf_picks_{label.split()[0].replace('>=', 'ge')}.csv", index=False)
        wf["ret"].to_csv(OUT / f"wf_ret_{label.split()[0].replace('>=', 'ge')}.csv")
    pd.DataFrame(wf_rows).to_csv(OUT / "walk_forward.csv", index=False)
    print("done")


if __name__ == "__main__":
    sys.exit(main())
