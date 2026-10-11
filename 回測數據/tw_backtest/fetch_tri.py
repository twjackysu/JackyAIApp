"""Fetch TWSE 發行量加權股價報酬指數 (TAIEX total return index) month by month."""
import json
import ssl
import time
import urllib.request
from pathlib import Path

import pandas as pd

OUT = Path(__file__).parent / "data" / "taiex_tri.csv"
# TWSE cert lacks Subject Key Identifier; Python 3.13 strict mode rejects it.
# Keep full chain/hostname verification, only drop the X509 strict flag.
CTX = ssl.create_default_context()
CTX.verify_flags &= ~ssl.VERIFY_X509_STRICT
URL = "https://www.twse.com.tw/rwd/zh/TAIEX/MFI94U?date={}01&response=json"


def roc_to_date(s: str) -> pd.Timestamp:
    y, m, d = s.strip().split("/")
    return pd.Timestamp(int(y) + 1911, int(m), int(d))


def main() -> None:
    rows = {}
    if OUT.exists():
        old = pd.read_csv(OUT, index_col=0, parse_dates=True)["tri"]
        rows = old.to_dict()
    months = pd.period_range("2003-01", pd.Timestamp.today().strftime("%Y-%m"), freq="M")
    have = {pd.Timestamp(k).to_period("M") for k in rows}
    for p in months:
        if p in have and p != months[-1]:
            continue
        for attempt in range(3):
            try:
                req = urllib.request.Request(URL.format(p.strftime("%Y%m")),
                                             headers={"User-Agent": "Mozilla/5.0"})
                js = json.loads(urllib.request.urlopen(req, timeout=20, context=CTX).read())
                break
            except Exception as e:  # noqa: BLE001
                print(p, "retry", e)
                time.sleep(10)
        else:
            raise RuntimeError(f"failed {p}")
        for d, v in js.get("data", []):
            rows[roc_to_date(d)] = float(v.replace(",", ""))
        print(p, js.get("stat"), len(js.get("data", [])), flush=True)
        time.sleep(2.5)
    s = pd.Series(rows, name="tri").sort_index()
    s.index.name = "date"
    s.to_csv(OUT)
    print("saved", len(s), s.index[0], s.index[-1])


if __name__ == "__main__":
    main()
