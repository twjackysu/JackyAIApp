#!/usr/bin/env python3
"""迴圈的鎖（.loop/lock.json）：只用 Python 標準函式庫，不靠 xxd、uuidgen 這類不一定有裝的指令。

  python3 .loop/lock.py check    # 印 FREE 或 HELD <expires_at>；HELD 時結束碼 1
  python3 .loop/lock.py write    # 寫入新的鎖（現在起 110 分鐘），印出內容

第 53 輪（2026-10-28 11:42 台北）留下一個欄位錯位、expires_at 是空字串的鎖，之後約 34 小時沒有一輪做事。
所以 check 對看不懂的鎖不當成「還在跑」：expires_at 讀不出來時，改用這個檔案最後一次 commit 的時間加 110 分鐘。
"""
import json
import secrets
import subprocess
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

LOCK = Path(__file__).resolve().parent / "lock.json"
TTL = timedelta(minutes=110)


def parse(value):
    if not isinstance(value, str) or not value.strip():
        return None
    try:
        t = datetime.fromisoformat(value.strip().replace("Z", "+00:00"))
    except ValueError:
        return None
    return t if t.tzinfo else None


def committed_at():
    out = subprocess.run(
        ["git", "log", "-1", "--format=%cI", "--", str(LOCK)],
        capture_output=True, text=True, cwd=LOCK.parent,
    ).stdout.strip()
    return parse(out)


def expires():
    try:
        data = json.loads(LOCK.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        data = {}
    t = parse(data.get("expires_at")) if isinstance(data, dict) else None
    if t is None:
        base = committed_at()
        t = base + TTL if base else None
    return t


def main(cmd):
    now = datetime.now(timezone.utc)
    if cmd == "check":
        if not LOCK.exists():
            print("FREE")
            return 0
        t = expires()
        if t is not None and t > now:
            print(f"HELD {t.isoformat()}")
            return 1
        print("FREE")
        return 0
    if cmd == "write":
        lock = {
            "run_id": secrets.token_hex(6),
            "started_at": now.strftime("%Y-%m-%dT%H:%M:%SZ"),
            "expires_at": (now + TTL).strftime("%Y-%m-%dT%H:%M:%SZ"),
        }
        LOCK.write_text(json.dumps(lock) + "\n", encoding="utf-8")
        print(json.dumps(lock))
        return 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else ""))
