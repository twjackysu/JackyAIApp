"""Neighbourhood robustness for DDBrake + crash-episode table + deep-dip re-entry test."""
import itertools
import numpy as np
import pandas as pd
import bt

p, _ = bt.load_series()
P = {"FULL": ("1997", "2099"), "IS": ("2003", "2014"), "OOS": ("2015", "2099"), "PRE": ("1997", "2002")}


def st(res, per):
    a, b = P[per]
    return bt.stats(res.loc[a:b])


bh = bt.run(p, bt.buy_hold(p))
print("BH FULL", {k: round(v, 3) for k, v in st(bh, "FULL").items()})

for off in (0.8, 0.5, 0.0):
    for lag in (1, 2):
        rows = {}
        for dd, re_ in itertools.product([.05, .06, .07, .08, .09, .10, .12, .15], [40, 50, 60, 80, 100]):
            r = bt.run(p, bt.drawdown_brake(p, dd, re_, off), lag=lag)
            s = st(r, "FULL")
            rows[(dd, re_)] = f"{s['CAGR']:.1%}/{s['MDD']:.0%}"
        t = pd.Series(rows).unstack()
        print(f"\nDDBrake off={off} lag={lag}  FULL CAGR/MDD  (rows dd, cols reenter SMA)")
        print(t.to_string())

# crash episodes
eps = {"2000 dotcom": ("2000-02", "2001-10"), "2008 GFC": ("2007-10", "2009-03"),
       "2011 euro": ("2011-02", "2011-12"), "2015 china": ("2015-04", "2016-01"),
       "2018 trade": ("2018-01", "2019-01"), "2020 covid": ("2020-01", "2020-04"),
       "2022 rates": ("2022-01", "2022-11"), "2025 tariff": ("2025-01", "2025-05")}
cands = {"BuyHold": bt.buy_hold(p), "Const 0.8": pd.Series(.8, index=p.index),
         "DDBrake 8%/60 off0.8": bt.drawdown_brake(p, .08, 60, .8),
         "DDBrake 8%/60 off0.5": bt.drawdown_brake(p, .08, 60, .5),
         "DDBrake 8%/60 off0": bt.drawdown_brake(p, .08, 60, 0),
         "Dip 15/20 base0.8": bt.dip_ladder(p, .8, .15, .2, "ath", True, 200)}
res = {k: bt.run(p, w) for k, w in cands.items()}
rows = []
for name, (a, b) in eps.items():
    row = {"episode": name}
    for k, r in res.items():
        eq = (1 + r["ret"].loc[a:b]).cumprod()
        row[k] = (eq / eq.cummax() - 1).min()
    rows.append(row)
print("\nMax drawdown inside each crash episode")
print(pd.DataFrame(rows).set_index("episode").map(lambda v: f"{v:.0%}").to_string())

# yearly returns
yr = pd.DataFrame({k: (1 + r["ret"]).groupby(r.index.year).prod() - 1 for k, r in res.items()})
print("\nCalendar-year returns")
print(yr.map(lambda v: f"{v:.0%}").to_string())

# deep-dip re-entry on top of DDBrake
print("\nDDBrake 8%/60 + deep-dip re-entry (go back to 100% when drawdown from ATH <= -X)")
for off in (0.8, 0.5, 0.0):
    base = bt.drawdown_brake(p, .08, 60, off)
    dd_ath = p / p.cummax() - 1
    for X in [None, .25, .30, .35, .40, .50]:
        w = base if X is None else base.where(dd_ath > -X, 1.0)
        r = bt.run(p, w)
        s = st(r, "FULL")
        print(f" off={off} X={X}: CAGR {s['CAGR']:.1%} MDD {s['MDD']:.0%} Calmar {s['Calmar']:.2f} | OOS CAGR {st(r,'OOS')['CAGR']:.1%}")
