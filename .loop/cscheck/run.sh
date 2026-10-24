#!/usr/bin/env bash
# 離線 C# 檢查：對 base（預設 origin/master）與目前工作目錄各編譯一次，只列出新出現的診斷。
# 需要 `sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0`。用法：.loop/cscheck/run.sh [base-ref]
set -euo pipefail
REPO=$(git rev-parse --show-toplevel)
BASE=${1:-origin/master}
OUT=${CSCHECK_OUT:-/tmp/cscheck}
mkdir -p "$OUT"
ROSLYN=$(ls -d /usr/lib/dotnet/sdk/*/Roslyn/bincore | tail -1)
REF=$(ls -d /usr/lib/dotnet/packs/Microsoft.NETCore.App.Ref/*/ref/net10.0 | tail -1)

if [ ! -f "$OUT/cscheck.dll" ] || [ "$REPO/.loop/cscheck/Program.cs" -nt "$OUT/cscheck.dll" ]; then
  dotnet "$ROSLYN/csc.dll" -nologo -nullable:enable -langversion:latest -out:"$OUT/cscheck.dll" -noconfig -nostdlib \
    $(for f in "$REF"/*.dll; do echo "-r:$f"; done) \
    -r:"$ROSLYN/Microsoft.CodeAnalysis.dll" -r:"$ROSLYN/Microsoft.CodeAnalysis.CSharp.dll" \
    "$REPO/.loop/cscheck/Program.cs"
  sed 's/"System.GC.Server": true/"System.GC.Server": false/' "$ROSLYN/csc.runtimeconfig.json" > "$OUT/cscheck.runtimeconfig.json"
  cp "$ROSLYN/Microsoft.CodeAnalysis.dll" "$ROSLYN/Microsoft.CodeAnalysis.CSharp.dll" "$OUT/"
fi

# 第三方原始碼（第一次會 clone，之後重用）。版本要跟 csproj 的 PackageReference 一致。
SRC=$OUT/src
mkdir -p "$SRC"
[ -d "$SRC/anthropic" ] || { git clone -q --depth 1 --branch Anthropic-v12.60.0 https://github.com/anthropics/anthropic-sdk-csharp "$SRC/anthropic" || true; }
[ -d "$SRC/xunit-assert" ] || { git clone -q --depth 1 https://github.com/xunit/assert.xunit "$SRC/xunit-assert" || true; }
[ -d "$SRC/moq" ] || { git clone -q --depth 1 --branch v4.20.72 https://github.com/devlooped/moq "$SRC/moq" || true; }
[ -d "$SRC/efcore" ] || { git clone -q --depth 1 --branch v10.0.13 --filter=blob:none --sparse https://github.com/dotnet/efcore "$SRC/efcore" \
  && git -C "$SRC/efcore" sparse-checkout set src/EFCore src/EFCore.Abstractions src/EFCore.Relational src/EFCore.SqlServer src/EFCore.InMemory src/Shared || true; }
export CSCHECK_REFS="Anthropic=$SRC/anthropic/src/Anthropic;xunit.assert=$SRC/xunit-assert;xunit.stubs=$REPO/.loop/cscheck/stubs;Moq=$SRC/moq/src/Moq;Microsoft.EntityFrameworkCore=$SRC/efcore/src"

rm -rf "$OUT/base" && mkdir -p "$OUT/base"
git -C "$REPO" archive "$BASE" JackyAIApp.Server JackyAIApp.Server.Tests | tar -x -C "$OUT/base"

dotnet "$OUT/cscheck.dll" "$OUT/base" | cut -f1-3 | sort -u > "$OUT/base.txt"
dotnet "$OUT/cscheck.dll" "$REPO" > "$OUT/head-lines.txt"
cut -f1-3 "$OUT/head-lines.txt" | sort -u > "$OUT/head.txt"

# 只留 head 才有的診斷（以 檔案+代碼+訊息 比較），附上行號。
comm -13 "$OUT/base.txt" "$OUT/head.txt" > "$OUT/new.txt"
# 找不到型別／名稱（CS0246、CS0234、CS0103）多半是沒有第三方套件，只彙總名稱，自己判斷是不是打錯本站的型別。
awk -F'\t' 'NR==FNR { k[$0]=1; next } ($1"\t"$2"\t"$3) in k' "$OUT/new.txt" "$OUT/head-lines.txt" | sort -u > "$OUT/new-lines.txt"
grep -v -P '\t(CS0246|CS0234|CS0103)\t' "$OUT/new-lines.txt" || true
echo "--- 找不到的名稱（第三方套件或打錯字）：" >&2
grep -P '\t(CS0246|CS0234|CS0103)\t' "$OUT/new-lines.txt" | grep -oP "(?<=name ')[^']+" | sort -u | tr '\n' ' ' >&2 || true
echo >&2
echo "--- base $(wc -l < "$OUT/base.txt") 種診斷，head $(wc -l < "$OUT/head.txt") 種，新出現 $(wc -l < "$OUT/new.txt") 種" >&2
