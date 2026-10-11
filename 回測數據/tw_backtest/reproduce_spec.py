"""Reproduce every number quoted in the 0050 + 00631L spec, from the raw data in data/.

usage:  python reproduce_spec.py
Prints data-file SHA256, library versions, then each spec number with its definition.
"""
import hashlib
import platform
import sys
from pathlib import Path

import numpy as np
import pandas as pd

import bt
import lev

HERE = Path(__file__).parent
FEE_A, FEE_L = 0.003, 0.035          # calibrated in calib.py (see README)
PROFILES = {"conservative": (1.0, 0.0, 0.2), "balanced": (1.4, 0.0, 0.2),
            "aggressive": (1.8, 0.0, 0.1)}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()[:16]


def f(s):
    return f"{s['CAGR']:.1%} / {s['MDD']:.0%}"


def main():
    print("== environment")
    print(" python", platform.python_version(), "| pandas", pd.__version__, "| numpy", np.__version__)
    for fn in ["taiex_tri.csv", "TWII.csv", "twse_0050.csv", "twse_00631L.csv",
               "div_0050.csv", "actual_returns.csv"]:
        print(f" sha256[:16] data/{fn}: {sha(HERE / 'data' / fn)}")

    p, div = bt.load_series()
    print(f"\n== series: {p.index[0].date()}..{p.index[-1].date()}, pre-2003 est. dividend {div:.2%}")
    R = lev.asset_returns(p, FEE_A, FEE_L)
    act = pd.read_csv(HERE / "data" / "actual_returns.csv", index_col=0, parse_dates=True)
    RA = pd.DataFrame({"A": act.r_0050, "L": act.r_00631L,
                       "C": (1 + bt.cash_rate(act.index)) ** (1 / bt.TD) - 1})
    st = bt.drawdown_brake(p, .08, 60, 0.0)
    E = lambda on, off: off + st * (on - off)

    print("\n== Spec section 2 table: CAGR / MDD")
    print(" (FULL = 1997-07..latest synthetic; lag2 = trade next close; REAL = actual 0050/00631L "
          f"{act.index[0].date()}..{act.index[-1].date()})")
    for name, (on, off, k) in PROFILES.items():
        full = bt.stats(lev.simulate(R, E(on, off), k=k, lag=1))
        lag2 = bt.stats(lev.simulate(R, E(on, off), k=k, lag=2))
        real = bt.stats(lev.simulate(RA, E(on, off), k=k, lag=1))
        print(f" {name:13s} on={on} off={off} k={k}:  FULL {f(full)} | lag2 {f(lag2)} | REAL {f(real)}")
    one = pd.Series(1.0, index=p.index)
    print(f" 0050 buy&hold:  FULL {f(bt.stats(lev.simulate(R, one)))} | "
          f"REAL {f(bt.stats(lev.simulate(RA, one)))}")
    print(" note: spec table rows used k=0 for the FULL column in the first draft; k effect shown below")
    for name, (on, off, _) in PROFILES.items():
        print(f"   {name:13s} k=0 FULL {f(bt.stats(lev.simulate(R, E(on, off), k=0.0)))}")

    print("\n== other quoted numbers")
    bal_off03 = bt.stats(lev.simulate(R, E(1.4, 0.3), k=0.2))
    print(f" balanced with defense E=0.3: {f(bal_off03)}")
    for c in (1.4, 2.0):
        print(f" static {c}x no brake: {f(bt.stats(lev.simulate(R, pd.Series(c, index=p.index))))}")
    for off in (0.5, 0.8):
        print(f" on=1.4 defense E={off}: {f(bt.stats(lev.simulate(R, E(1.4, off), k=0.2)))}")
    for k in (0.0, 0.1, 0.2):
        print(f" on=1.4 off=0 cash k={k}: {f(bt.stats(lev.simulate(R, E(1.4, 0.0), k=k)))}")
    bal = lev.simulate(R, E(1.4, 0.0), k=0.2)
    yr = (1 + bal["ret"]).groupby(bal.index.year).prod() - 1
    bh = lev.simulate(R, one)
    yb = (1 + bh["ret"]).groupby(bh.index.year).prod() - 1
    print(f" 2011: balanced {yr[2011]:.0%} vs 0050 {yb[2011]:.0%}")
    w = st
    yrs = len(w) / bt.TD
    print(f" exits per year {(w.diff() < 0).sum() / yrs:.1f}, share of days in DEFENSE {(w < 1).mean():.0%}")

    print("\n== acceptance: state at last date")
    hi = p.rolling(252, min_periods=1).max()
    print(f" {p.index[-1].date()}: state={'OFFENSE' if st.iloc[-1] == 1 else 'DEFENSE'}, "
          f"dd from 252d high {p.iloc[-1] / hi.iloc[-1] - 1:.1%}")
    print("\n== acceptance: composition")
    for e, k in [(1.4, .2), (1.0, .2), (1.8, .2), (0.0, .2), (1.8, .1)]:
        a, l = lev.composition(e, k)
        print(f" composition({e}, {k}) -> 0050 {a:.2f}, 00631L {l:.2f}, cash {1 - a - l:.2f}")


if __name__ == "__main__":
    sys.exit(main())
