#!/usr/bin/env python3
"""截圖巡檢：比對新拍的截圖與 repo 裡的（迴圈專用，合併前與 .loop/ 一起刪）。

用法：python3 .loop/shots/compare.py <新截圖目錄> [門檻百分比，預設 0.05]

門檻要低：一行說明文字改了只佔整張的 0.5% 左右，第 56 輪用 1% 的門檻漏掉了首頁說明句（D-200）的舊圖（第 58 輪）。
差異小於 0.2% 且位置在 (186,138)–(245,161) 的手機圖通常只是 /finance 的日期。
"""
import os
import sys

from PIL import Image, ImageChops

new_dir = sys.argv[1]
threshold = float(sys.argv[2]) if len(sys.argv) > 2 else 0.05
repo_dir = os.path.join(os.path.dirname(__file__), '../../docs/ai-native/screenshots')
for name in sorted(os.listdir(new_dir)):
    if not name.endswith('.png'):
        continue
    old = os.path.join(repo_dir, name)
    if not os.path.exists(old):
        print('NEW', name)
        continue
    a = Image.open(os.path.join(new_dir, name)).convert('RGB')
    b = Image.open(old).convert('RGB')
    if a.size != b.size:
        print('SIZE', name, a.size, b.size)
        continue
    diff = ImageChops.difference(a, b).convert('L').point(lambda x: 255 if x > 16 else 0)
    pct = diff.histogram()[255] / (a.size[0] * a.size[1]) * 100
    if pct > threshold:
        print(f'{pct:.2f}%', name, diff.getbbox())
