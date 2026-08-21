/** Surfaced on hover rather than as caption text: the figure needs several separate caveats
 * (why not simple growth, why dividends are in, why not annualized, why it differs from the XIRR
 * on 總覽) and stacking those under the chart buried the numbers under more prose than figures —
 * the same reason EXPOSURE_TOOLTIP exists. */
export const BENCHMARK_RETURN_TOOLTIP = [
  '區間報酬 = 時間加權報酬率（TWR），含息、未年化',
  '　　　每日（市值 + 當日配息）÷（前日市值 + 當日加碼），連乘',
  '',
  '為什麼扣掉加碼：',
  '　　　線是「部位市值」，你加碼它就變高。',
  '　　　若直接看線的漲幅，存進去的錢會被算成賺的。',
  '　　　TWR 把加碼從當日基期扣掉，只留部位本身漲跌。',
  '',
  '為什麼含息：',
  '　　　除息當天，價格會掉掉配息那一段。',
  '　　　只看價格的話，高股息部位等於把配掉的錢當成虧損，',
  '　　　而殖利率低的對照組被扣得少，等於白送它分數。',
  '　　　配息以「領到現金」計入，不再投入買更多單位。',
  '',
  '為什麼對照標的可比：',
  '　　　對照線用每股價格，不是基金總規模。',
  '　　　ETF 申購是按淨值增發單位數，不動每股價格。',
  '　　　對照標的自己的配息同樣計入，兩邊一致。',
  '',
  '與總覽的年化報酬率（XIRR）不同：',
  '　　　XIRR 是資金加權且年化，算進你的進場時點；',
  '　　　這裡是時間加權且未年化，兩者本來就有差異。',
  '',
  '圖上的線仍是純市值（不含息），方便和對帳單核對，',
  '所以線的漲幅和上面的 % 本來就不會一致。',
  '僅計算已填成本的股票/基金持倉。',
].join('\n');
