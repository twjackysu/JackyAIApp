"""Grid: timing signal x (E_on, E_off) for 0050 + 00631L portfolios."""
from __future__ import annotations

import itertools
import sys
from pathlib import Path

import numpy as np
import pandas as pd

import bt
import lev

OUT = Path(__file__).parent / "results"
PERIODS = {"PRE": ("1997", "2002"), "IS": ("2003", "2014"), "OOS": ("2015", "2099"),
           "FULL": ("1997", "2099")}
E_ON = [1.0, 1.2, 1.4, 1.6, 1.8, 2.0]
E_OFF = [0.0, 0.3, 0.5, 0.8, 1.0]


def signals(p):
    """Return {name: state series in [0,1]} (1 = offense)."""
    s = {"Static": pd.Series(1.0, index=p.index)}
    for dd, re_ in itertools.product([.06, .08, .10, .12], [50, 60, 80]):
        s[f"DDBrake dd={dd} re={re_}"] = bt.drawdown_brake(p, dd, re_, 0.0)
    for n, b in itertools.product([60, 100, 120, 200], [0, .02]):
        s[f"SMA n={n} band={b}"] = bt.sma_filter(p, n, 0.0, b)
    for m in [4, 6, 10]:
        s[f"MonthlySMA m={m}"] = bt.monthly_sma(p, m, 0.0)
    for m in [3, 6, 12]:
        s[f"TSMOM m={m}"] = bt.ts_momentum(p, m, 0.0)
    for e, x in [(20, 20), (50, 50)]:
        s[f"Donchian {e}/{x}"] = bt.donchian(p, e, x, 0.0)
    # two-tier: DDBrake and long trend; 1 = both ok, .5 = one ok, 0 = none
    for dd in [.08, .10]:
        a = bt.drawdown_brake(p, dd, 60, 0.0)
        b = bt.sma_filter(p, 200, 0.0, 0.02)
        s[f"TwoTier dd={dd}+SMA200"] = (a + b) / 2
    return s


def vol_signals(p):
    """Continuous: exposure = clip(target/vol, E_off, E_on); optionally gated by DDBrake."""
    r = p.pct_change()
    out = {}
    for t, lk in itertools.product([.15, .20, .25, .30], [20, 60]):
        v = (r.rolling(lk).std() * np.sqrt(bt.TD)).bfill()
        out[f"VolT t={t} lk={lk}"] = (t / v, None)
        out[f"VolT+DDBrake t={t} lk={lk}"] = (t / v, bt.drawdown_brake(p, .08, 60, 0.0))
    return out


def target_E(state, e_on, e_off):
    return e_off + state * (e_on - e_off)


def quantise(E, step=0.1):
    return (E / step).round() * step


def run_grid(p, R, lag=1, k=0.0):
    rows, rets = [], {}
    sigs = signals(p)
    vsig = vol_signals(p)
    combos = []
    for name, st in sigs.items():
        for e_on in E_ON:
            for e_off in (E_OFF if name != "Static" else [e_on]):
                if e_off <= e_on:
                    combos.append((name, e_on, e_off, quantise(target_E(st, e_on, e_off))))
    for name, (raw, gate) in vsig.items():
        for e_on in E_ON:
            for e_off in E_OFF:
                if e_off <= e_on:
                    E = raw.clip(lower=e_off, upper=e_on)
                    if gate is not None:
                        E = E.where(gate > 0, e_off)
                    combos.append((name, e_on, e_off, quantise(E)))
    for name, e_on, e_off, E in combos:
        res = lev.simulate(R, E, k=k, lag=lag)
        key = f"{name} | on={e_on} off={e_off}"
        rets[key] = res
        row = {"key": key, "signal": name, "family": name.split()[0], "E_on": e_on, "E_off": e_off}
        for pn, (a, b) in PERIODS.items():
            for m, v in bt.stats(res.loc[a:b]).items():
                row[f"{pn}_{m}"] = v
        rows.append(row)
    return pd.DataFrame(rows), rets


def walk_forward(rets, keys, mdd_cap, start_year=2005):
    """Each year: among keys whose MDD on all prior data >= mdd_cap, pick max CAGR."""
    idx = next(iter(rets.values())).index
    years = sorted({d.year for d in idx if d.year >= start_year})
    ends = [np.searchsorted(idx, pd.Timestamp(f"{y}-01-01")) - 1 for y in years]
    # prefix CAGR and prefix MDD at each year end, per key
    cagr = np.empty((len(keys), len(years)))
    mdd = np.empty_like(cagr)
    for i, k in enumerate(keys):
        eq = np.cumprod(1 + rets[k]["ret"].to_numpy())
        dd = np.minimum.accumulate(eq / np.maximum.accumulate(eq) - 1)
        for j, e in enumerate(ends):
            cagr[i, j] = eq[e] ** (bt.TD / (e + 1)) - 1
            mdd[i, j] = dd[e]
    out, picks = [], []
    for j, y in enumerate(years):
        ok = mdd[:, j] >= mdd_cap
        i = int(np.argmax(np.where(ok, cagr[:, j], -np.inf))) if ok.any() else int(np.argmax(mdd[:, j]))
        picks.append((y, keys[i]))
        out.append(rets[keys[i]].loc[str(y)])
    return pd.concat(out), picks


def main(fee_a: float, fee_l: float):
    p, _ = bt.load_series()
    R = lev.asset_returns(p, fee_a, fee_l)
    df, rets = run_grid(p, R, lag=1)
    df.to_csv(OUT / "lev_grid_lag1.csv", index=False)
    df2, _ = run_grid(p, R, lag=2)
    df2.to_csv(OUT / "lev_grid_lag2.csv", index=False)
    print("configs", len(df))
    wf_rows = []
    keys = df.key.tolist()
    for cap in [-0.25, -0.30, -0.35, -0.40, -0.50]:
        wf, picks = walk_forward(rets, keys, cap)
        s = bt.stats(wf)
        pk = pd.Series([k for _, k in picks]).value_counts()
        wf_rows.append({"mdd_cap": cap, **s, "top_pick": pk.index[0], "top_pick_years": pk.iloc[0],
                        "n_distinct": len(pk)})
    bh = bt.stats(rets["Static | on=1.0 off=1.0"].loc["2005":])
    wf_rows.append({"mdd_cap": "BuyHold 0050", **bh})
    pd.DataFrame(wf_rows).to_csv(OUT / "lev_walk_forward.csv", index=False)
    print("done")


if __name__ == "__main__":
    main(float(sys.argv[1]), float(sys.argv[2]))
