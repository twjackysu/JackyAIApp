"""Fetch raw (unadjusted) daily closes from TWSE STOCK_DAY, month by month.

usage: python fetch_stock_day.py 00631L 2014-10
"""
import json
import ssl
import sys
import time
import urllib.request
from pathlib import Path

import pandas as pd

# TWSE cert lacks Subject Key Identifier; keep verification, drop only the strict flag.
CTX = ssl.create_default_context()
CTX.verify_flags &= ~ssl.VERIFY_X509_STRICT
URL = "https://www.twse.com.tw/rwd/zh/afterTrading/STOCK_DAY?date={}01&stockNo={}&response=json"


def roc_to_date(s: str) -> pd.Timestamp:
    y, m, d = s.strip().split("/")
    return pd.Timestamp(int(y) + 1911, int(m), int(d))


def num(s: str) -> float:
    s = s.replace(",", "").strip()
    return float(s) if s not in ("", "--", "---") else float("nan")


def main(code: str, start: str) -> None:
    out = Path(__file__).parent / "data" / f"twse_{code}.csv"
    rows = {}
    if out.exists():
        rows = pd.read_csv(out, index_col=0, parse_dates=True).to_dict("index")
    have = {pd.Timestamp(k).to_period("M") for k in rows}
    months = pd.period_range(start, pd.Timestamp.today().strftime("%Y-%m"), freq="M")
    for p in months:
        if p in have and p != months[-1]:
            continue
        for _ in range(3):
            try:
                req = urllib.request.Request(URL.format(p.strftime("%Y%m"), code),
                                             headers={"User-Agent": "Mozilla/5.0"})
                js = json.loads(urllib.request.urlopen(req, timeout=20, context=CTX).read())
                break
            except Exception as e:  # noqa: BLE001
                print(p, "retry", e, flush=True)
                time.sleep(10)
        else:
            raise RuntimeError(f"failed {p}")
        # fields: 日期 成交股數 成交金額 開盤價 最高價 最低價 收盤價 漲跌價差 成交筆數
        for r in js.get("data", []):
            rows[roc_to_date(r[0])] = {"open": num(r[3]), "high": num(r[4]),
                                       "low": num(r[5]), "close": num(r[6]),
                                       "volume": num(r[1])}
        print(p, js.get("stat"), len(js.get("data", [])), flush=True)
        pd.DataFrame.from_dict(rows, orient="index").sort_index().to_csv(out)
        time.sleep(2.5)
    print("saved", out, len(rows))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
