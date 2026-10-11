"""Print markdown summary of results/ produced by run_all.py."""
from pathlib import Path

import pandas as pd

R = Path(__file__).parent / "results"
pd.set_option("display.width", 250)
pd.set_option("display.max_columns", 40)
pd.set_option("display.max_colwidth", 90)

COLS = ["CAGR", "MDD", "Calmar", "Sharpe", "Worst12m", "Exposure", "Turn/yr"]


def floor_of(df):
    return df[["p_off", "p_floor", "p_base", "p_c"]].bfill(axis=1).iloc[:, 0].fillna(1.0)


def show(df, periods=("IS", "OOS", "PRE", "FULL"), cols=COLS):
    out = df[["key"]].copy()
    for p in periods:
        for c in cols:
            out[f"{p}.{c}"] = df[f"{p}_{c}"]
    return out


def fmt(df):
    d = df.copy()
    for c in d.columns:
        if d[c].dtype.kind == "f":
            if any(x in c for x in ("Calmar", "Sharpe", "Turn")):
                d[c] = d[c].map(lambda v: f"{v:.2f}")
            else:
                d[c] = d[c].map(lambda v: f"{v:.1%}")
    return d.to_string(index=False)


def main():
    g1 = pd.read_csv(R / "grid_lag1.csv")
    g2 = pd.read_csv(R / "grid_lag2.csv").set_index("key")
    g1["floor"] = floor_of(g1)

    print("## Benchmarks")
    b = g1[g1.family.isin(["BuyHold", "Constant"])]
    print(fmt(show(b, cols=["CAGR", "MDD", "Calmar", "Sharpe", "Worst12m"])))

    for title, sel in [("USER RANGE floor>=0.8", g1.floor >= 0.8),
                       ("REFERENCE floor<=0.5", g1.floor <= 0.5)]:
        sub = g1[sel & ~g1.family.isin(["BuyHold", "Constant"])]
        print(f"\n## {title}: best params per family chosen on IS Calmar -> OOS/PRE")
        best = sub.loc[sub.groupby("family")["IS_Calmar"].idxmax()].sort_values("IS_Calmar", ascending=False)
        print(fmt(show(best, cols=["CAGR", "MDD", "Calmar", "Exposure"])))
        best = best.assign(lag2_OOS_Calmar=best.key.map(g2["OOS_Calmar"]),
                           lag2_OOS_CAGR=best.key.map(g2["OOS_CAGR"]))
        print("lag2 (trade next close) OOS check:")
        print(fmt(best[["key", "OOS_CAGR", "lag2_OOS_CAGR", "OOS_Calmar", "lag2_OOS_Calmar"]]))

        print(f"\n## {title}: robustness = share of ALL param sets beating BuyHold")
        bh = g1[g1.family == "BuyHold"].iloc[0]
        rows = []
        for fam, d in sub.groupby("family"):
            row = {"family": fam, "n": len(d)}
            for p in ("IS", "OOS", "PRE"):
                row[f"{p} %Calmar>BH"] = (d[f"{p}_Calmar"] > bh[f"{p}_Calmar"]).mean()
                row[f"{p} %CAGR>BH"] = (d[f"{p}_CAGR"] > bh[f"{p}_CAGR"]).mean()
                row[f"{p} medMDD"] = d[f"{p}_MDD"].median()
            rows.append(row)
        print(fmt(pd.DataFrame(rows)))

    print("\n## Walk-forward (re-optimise each year on all prior data, Calmar)")
    print(fmt(pd.read_csv(R / "walk_forward.csv")[["set", "CAGR", "MDD", "Calmar", "Sharpe", "Worst12m", "Exposure"]]))
    for f in sorted(R.glob("wf_picks_*.csv")):
        p = pd.read_csv(f)
        print(f.name, p.pick.value_counts().head(5).to_dict())

    print("\n## Event study: forward returns after new highs")
    print(fmt(pd.read_csv(R / "event_study.csv")))


if __name__ == "__main__":
    main()
