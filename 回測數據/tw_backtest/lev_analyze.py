"""Deep-dive on DDBrake with 0050 + 00631L: E_on x E_off tables, real-data check, crashes."""
import itertools
import sys

import numpy as np
import pandas as pd

import bt
import lev

FEE_A, FEE_L = 0.003, 0.035
p, _ = bt.load_series()
R = lev.asset_returns(p, FEE_A, FEE_L)

act = pd.read_csv("data/actual_returns.csv", index_col=0, parse_dates=True)
RA = pd.DataFrame({"A": act.r_0050, "L": act.r_00631L,
                   "C": (1 + bt.cash_rate(act.index)) ** (1 / bt.TD) - 1})


def E_series(state, on, off):
    return off + state * (on - off)


def fmt(s):
    return f"{s['CAGR']:.1%}/{s['MDD']:.0%}"


def table(dd, re_, lag=1, period=("1997", "2099"), rets=R, k=0.0):
    st = bt.drawdown_brake(p, dd, re_, 0.0)
    rows = {}
    for on, off in itertools.product([1.0, 1.2, 1.4, 1.6, 1.8, 2.0], [0.0, 0.3, 0.5, 0.8, 1.0]):
        res = lev.simulate(rets, E_series(st, on, off), k=k, lag=lag)
        rows[(on, off)] = fmt(bt.stats(res.loc[period[0]:period[1]]))
    return pd.Series(rows).unstack()


print("CAGR/MDD.  rows = E_on (offense exposure), cols = E_off (defense exposure)\n")
for (dd, re_), lag, per, lbl in [((.08, 60), 1, ("1997", "2099"), "FULL 1997-2026 synthetic"),
                                 ((.08, 60), 2, ("1997", "2099"), "FULL lag2 (trade next close)"),
                                 ((.06, 60), 1, ("1997", "2099"), "FULL dd=6%"),
                                 ((.10, 60), 1, ("1997", "2099"), "FULL dd=10%"),
                                 ((.08, 80), 1, ("1997", "2099"), "FULL re=SMA80"),
                                 ((.08, 60), 1, ("2015", "2099"), "OOS 2015-2026 synthetic")]:
    print(f"== DDBrake dd={dd} SMA{re_} | {lbl}")
    print(table(dd, re_, lag, per).to_string(), "\n")

print("== REAL 0050 + 00631L returns, 2014-11..2026-10 (DDBrake 8%/60)")
print(table(.08, 60, 1, ("2014", "2099"), RA).to_string(), "\n")
print("== SYNTHETIC same window, for comparison")
print(table(.08, 60, 1, (str(act.index[0].date()), "2099")).to_string(), "\n")

# cash buffer effect
st = bt.drawdown_brake(p, .08, 60, 0.0)
print("== cash buffer k (min cash when E>1-k); DDBrake 8%/60, FULL synthetic")
for on in [1.2, 1.4, 1.6]:
    line = []
    for k in [0.0, 0.1, 0.2, 0.3, "max"]:
        res = lev.simulate(R, E_series(st, on, 0.0), k=k)
        line.append(f"k={k}: {fmt(bt.stats(res))}")
    print(f" on={on}: " + " | ".join(line))

# crash episodes + yearly
eps = {"2000 dotcom": ("2000-02", "2001-10"), "2008 GFC": ("2007-10", "2009-03"),
       "2011 euro": ("2011-02", "2011-12"), "2015 china": ("2015-04", "2016-01"),
       "2018 trade": ("2018-01", "2019-01"), "2020 covid": ("2020-01", "2020-04"),
       "2022 rates": ("2022-01", "2022-11"), "2025 tariff": ("2025-01", "2025-05")}
cfg = {"0050 BH": (1.0, 1.0, False), "Static 1.4": (1.4, 1.4, False),
       "Brake on1.0 off0": (1.0, 0.0, True), "Brake on1.4 off0": (1.4, 0.0, True),
       "Brake on1.6 off0": (1.6, 0.0, True), "Brake on1.4 off0.5": (1.4, 0.5, True),
       "Brake on1.4 off0.8": (1.4, 0.8, True), "Brake on2.0 off0": (2.0, 0.0, True)}
res = {n: lev.simulate(R, E_series(st, on, off) if b else pd.Series(on, index=p.index))
       for n, (on, off, b) in cfg.items()}
print("\n== Max drawdown inside crash episodes (synthetic)")
rows = []
for name, (a, b) in eps.items():
    row = {"episode": name}
    for n, r in res.items():
        eq = (1 + r["ret"].loc[a:b]).cumprod()
        row[n] = f"{(eq / eq.cummax() - 1).min():.0%}"
    rows.append(row)
print(pd.DataFrame(rows).set_index("episode").to_string())
print("\n== Full-period stats")
print(pd.DataFrame({n: bt.stats(r) for n, r in res.items()}).T[
    ["CAGR", "MDD", "Sharpe", "Worst12m", "Exposure", "Turn/yr"]].round(3).to_string())
print("\n== Calendar-year returns")
yr = pd.DataFrame({n: (1 + r["ret"]).groupby(r.index.year).prod() - 1 for n, r in res.items()})
print(yr.map(lambda v: f"{v:.0%}").to_string())
