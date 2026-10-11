"""Backtest engine + strategy library for TAIEX / 0050 timing strategies.

Conventions
- weight w_t (0..1) is decided with information up to close t.
- lag=1: trade at close t (closing auction), so position over day t+1 is w_t.
- lag=2: trade at close t+1 (conservative).
- cost charged on |Δposition| at COST per unit turnover.
- uninvested cash earns an approximate TW deposit rate.
"""
from __future__ import annotations

from pathlib import Path

import numpy as np
import pandas as pd

DATA = Path(__file__).parent / "data"
COST = 0.0025          # per unit turnover (commission x2 avg + ETF tax + slippage)
TD = 252


# ---------------------------------------------------------------- data
def _yf_close(name: str) -> pd.Series:
    df = pd.read_csv(DATA / name, header=[0, 1], index_col=0, parse_dates=True)
    return df["Close"].iloc[:, 0].dropna()


def cash_rate(idx: pd.DatetimeIndex) -> pd.Series:
    """Approximate annual TW 1y time-deposit rate by year (rough)."""
    table = {1997: .060, 1998: .055, 1999: .050, 2000: .050, 2001: .035, 2002: .020,
             2003: .014, 2004: .015, 2005: .020, 2006: .022, 2007: .025, 2008: .020,
             2009: .009, 2010: .011, 2011: .013, 2012: .014, 2013: .014, 2014: .014,
             2015: .013, 2016: .011, 2017: .010, 2018: .010, 2019: .010, 2020: .008,
             2021: .008, 2022: .012, 2023: .016, 2024: .017, 2025: .017, 2026: .017}
    return pd.Series([table.get(d.year, .017) for d in idx], index=idx)


def load_series(extend: bool = True) -> tuple[pd.Series, float]:
    """Total-return TAIEX. Official TRI from 2003; before that TWII price + est. dividend."""
    tri = pd.read_csv(DATA / "taiex_tri.csv", index_col=0, parse_dates=True)["tri"]
    px = _yf_close("TWII.csv")
    both = pd.concat([tri, px], axis=1, join="inner").dropna()
    yrs = (both.index[-1] - both.index[0]).days / 365.25
    g = (both.iloc[-1] / both.iloc[0]) ** (1 / yrs)
    div = float(g.iloc[0] / g.iloc[1] - 1)        # implied dividend yield
    if not extend:
        return tri, div
    pre = px[px.index < tri.index[0]]
    t = np.arange(len(pre))[::-1] + 1             # trading days before splice
    pre_tr = pre * (1 + div) ** (-t / TD)
    pre_tr = pre_tr / pre.iloc[-1] * px.loc[:tri.index[0]].iloc[-1]   # continuity
    scale = tri.iloc[0] / px.loc[tri.index[0]]
    s = pd.concat([pre_tr * scale, tri]).sort_index()
    s = s[~s.index.duplicated()]
    s.name = "tr"
    return s, div


# ---------------------------------------------------------------- engine
def run(price: pd.Series, w: pd.Series, lag: int = 1, cost: float = COST) -> pd.DataFrame:
    w = w.reindex(price.index).ffill().fillna(0).clip(0, 1)
    pos = w.shift(lag).fillna(0)
    r = price.pct_change().fillna(0)
    rc = (1 + cash_rate(price.index)) ** (1 / TD) - 1
    turn = pos.diff().abs().fillna(pos.abs())
    ret = pos * r + (1 - pos) * rc - turn * cost
    return pd.DataFrame({"ret": ret, "pos": pos, "turn": turn})


def stats(res: pd.DataFrame, rf: pd.Series | None = None) -> dict:
    ret = res["ret"]
    eq = (1 + ret).cumprod()
    yrs = len(ret) / TD
    cagr = eq.iloc[-1] ** (1 / yrs) - 1
    vol = ret.std() * np.sqrt(TD)
    rfd = ((1 + cash_rate(ret.index)) ** (1 / TD) - 1) if rf is None else rf
    sharpe = (ret - rfd).mean() / ret.std() * np.sqrt(TD) if ret.std() > 0 else np.nan
    dd = eq / eq.cummax() - 1
    mdd = dd.min()
    roll12 = eq.pct_change(TD).min()
    m = (1 + ret).resample("ME").prod() - 1
    cvar = m[m <= m.quantile(.05)].mean()
    return {
        "CAGR": cagr, "Vol": vol, "Sharpe": sharpe, "MDD": mdd,
        "Calmar": cagr / abs(mdd) if mdd < 0 else np.nan,
        "Worst12m": roll12, "CVaR5m": cvar,
        "Exposure": res["pos"].mean(), "Turn/yr": res["turn"].sum() / yrs,
    }


# ---------------------------------------------------------------- strategies
def _hyst(up: pd.Series, down: pd.Series, on: float = 1.0, off: float = 0.0) -> pd.Series:
    """State machine: switch to on when `up`, to off when `down`, else hold."""
    sig = pd.Series(np.nan, index=up.index)
    sig[up] = on
    sig[down & ~up] = off
    return sig.ffill().fillna(on)


def buy_hold(p):
    return pd.Series(1.0, index=p.index)


def sma_filter(p, n=200, off=0.0, band=0.0):
    ma = p.rolling(n).mean()
    w = _hyst(p > ma * (1 + band), p < ma * (1 - band), 1.0, off)
    w[ma.isna()] = 1.0
    return w


def ema_filter(p, n=200, off=0.0, band=0.0):
    ma = p.ewm(span=n, adjust=False).mean()
    return _hyst(p > ma * (1 + band), p < ma * (1 - band), 1.0, off)


def dual_ma(p, fast=20, slow=120, off=0.0):
    f, s = p.rolling(fast).mean(), p.rolling(slow).mean()
    w = pd.Series(np.where(f > s, 1.0, off), index=p.index)
    w[s.isna()] = 1.0
    return w


def _month_end_mask(p):
    me = p.groupby(p.index.to_period("M")).tail(1).index
    return p.index.isin(me)


def monthly_sma(p, months=10, off=0.0):
    m = p[_month_end_mask(p)]
    ma = m.rolling(months).mean()
    sig = pd.Series(np.where(m > ma, 1.0, off), index=m.index)
    sig[ma.isna()] = 1.0
    return sig.reindex(p.index).ffill().fillna(1.0)


def ts_momentum(p, months=12, off=0.0):
    m = p[_month_end_mask(p)]
    mom = m.pct_change(months)
    sig = pd.Series(np.where(mom > 0, 1.0, off), index=m.index)
    sig[mom.isna()] = 1.0
    return sig.reindex(p.index).ffill().fillna(1.0)


def donchian(p, entry=100, exit=50, off=0.0):
    hi = p.rolling(entry).max().shift(1)
    lo = p.rolling(exit).min().shift(1)
    return _hyst(p > hi, p < lo, 1.0, off)


def vol_target(p, target=0.18, look=20, cap=1.0, step=0.1, floor=0.0):
    r = p.pct_change()
    v = r.rolling(look).std() * np.sqrt(TD)
    raw = (target / v).clip(lower=floor, upper=cap).fillna(cap)
    return ((raw / step).round() * step).clip(lower=floor)   # quantise to cut turnover


def drawdown_brake(p, dd=0.15, reenter=60, off=0.0):
    """Exit when drawdown from 252d high > dd; re-enter when price > SMA(reenter)."""
    hi = p.rolling(TD, min_periods=1).max()
    ma = p.rolling(reenter).mean()
    return _hyst(p > ma, p / hi - 1 < -dd, 1.0, off)


def combo(p, n=120, band=0.0, off=0.0, target=0.20, look=20):
    """Trend filter AND vol target; never below `off`."""
    return (sma_filter(p, n, 0.0, band) * vol_target(p, target, look)).clip(lower=off)


def dip_ladder(p, base=0.8, d1=0.10, d2=0.20, peak="ath", hold=True, trend_n=0):
    """逢跌加碼: weight `base` near the peak, (base+1)/2 after a d1 drawdown,
    1.0 after a d2 drawdown.
    peak    : "ath" = running all-time high, "252" = rolling 252-day high.
    hold    : True  -> keep the highest level reached until a new peak (降回保守 only at new high)
              False -> weight follows the current drawdown (trims as price rebounds).
    trend_n : >0 -> only add while price > SMA(trend_n) (避免空頭接刀); else stay at base.
    """
    hi = p.cummax() if peak == "ath" else p.rolling(bt_td(), min_periods=1).max()
    dd = p / hi - 1
    mid = (base + 1) / 2
    lvl = pd.Series(np.select([dd <= -d2, dd <= -d1], [1.0, mid], base), index=p.index)
    if trend_n:
        ok = p > p.rolling(trend_n).mean()
        lvl = lvl.where(ok | p.rolling(trend_n).mean().isna(), base)
    if not hold:
        return lvl
    at_peak = p >= hi
    grp = at_peak.cumsum()                          # new episode at each peak
    return lvl.groupby(grp).cummax()


def bt_td():
    return TD


def breakout_rule(p, look=252, hold=60, base=1.0, after=0.8):
    """User's question: after close makes a new `look`-day high, set weight `after`
    for `hold` days, otherwise `base`. after>base = 加碼, after<base = 減碼."""
    hi = p.rolling(look).max().shift(1)
    brk = (p > hi).astype(float)
    recent = brk.rolling(hold, min_periods=1).max() > 0
    return pd.Series(np.where(recent, after, base), index=p.index)
