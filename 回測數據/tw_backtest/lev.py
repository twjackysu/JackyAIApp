"""0050 + 00631L (daily 2x) portfolio simulator.

Effective exposure E = w_0050 + 2 * w_00631L  (fractions of total portfolio value).
Composition for a target E with minimum cash fraction k:
    E <= 1-k          -> 0050 = E,                  00631L = 0,          cash = 1-E
    1-k < E <= 2(1-k) -> 0050 = 2(1-k)-E,           00631L = E-(1-k),    cash = k
k = "max" puts all exposure in 00631L: 00631L = E/2, cash = 1-E/2.
"""
from __future__ import annotations

import numpy as np
import pandas as pd

import bt

COST = 0.0025          # per unit of value traded (commission + ETF tax + slippage)
FEE_0050 = 0.0035      # 0050 expense + tracking vs TAIEX TR (overridden by calibration)
FEE_L = 0.012          # 00631L expense + drag beyond 2x - rf (overridden by calibration)


def asset_returns(tr: pd.Series, fee_a=None, fee_l=None) -> pd.DataFrame:
    fee_a = FEE_0050 if fee_a is None else fee_a
    fee_l = FEE_L if fee_l is None else fee_l
    r = tr.pct_change().fillna(0)
    rf = (1 + bt.cash_rate(tr.index)) ** (1 / bt.TD) - 1
    return pd.DataFrame({
        "A": r - fee_a / bt.TD,
        "L": 2 * r - rf - fee_l / bt.TD,          # futures-style 2x: pays ~1x financing
        "C": rf,
    })


def composition(E: float, k) -> tuple[float, float]:
    if k == "max":
        return 0.0, E / 2
    inv = 1 - k
    if E <= inv:
        return E, 0.0
    E = min(E, 2 * inv)
    return 2 * inv - E, E - inv


def simulate(rets: pd.DataFrame, E_target: pd.Series, k=0.0, lag=1, band=0.10,
             cost=COST) -> pd.DataFrame:
    """Daily holdings drift; rebalance when target changes or exposure drifts > band."""
    Et = E_target.reindex(rets.index).ffill().fillna(1.0).shift(lag).fillna(1.0).to_numpy()
    ra, rl, rc = rets["A"].to_numpy(), rets["L"].to_numpy(), rets["C"].to_numpy()
    n = len(Et)
    wa, wl = composition(Et[0], k)
    wc = 1 - wa - wl
    out_ret = np.empty(n)
    out_E = np.empty(n)
    out_turn = np.zeros(n)
    last_tgt = Et[0]
    for i in range(n):
        # rebalance at start of day i (decided at previous close) if needed
        cur_E = wa + 2 * wl
        if Et[i] != last_tgt or abs(cur_E - Et[i]) > band:
            na, nl = composition(Et[i], k)
            turn = abs(na - wa) + abs(nl - wl)
            out_turn[i] = turn
            wa, wl = na, nl
            wc = 1 - wa - wl - turn * cost
            last_tgt = Et[i]
        out_E[i] = wa + 2 * wl
        # grow
        va, vl, vc = wa * (1 + ra[i]), wl * (1 + rl[i]), wc * (1 + rc[i])
        tot = va + vl + vc
        tot = max(tot, 1e-12)
        out_ret[i] = tot - 1          # weights summed to 1 before trading cost

        wa, wl, wc = va / tot, vl / tot, vc / tot
    return pd.DataFrame({"ret": out_ret, "pos": out_E, "turn": out_turn}, index=rets.index)
