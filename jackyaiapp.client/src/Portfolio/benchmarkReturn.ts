/** Surfaced on hover rather than as caption text: the figure needs three separate caveats
 * (why not simple growth, why not annualized, why it differs from the XIRR on 總覽) and stacking
 * those under the chart buried the numbers under more prose than figures — the same reason
 * EXPOSURE_TOOLTIP exists. */
export const BENCHMARK_RETURN_TOOLTIP = [
  '區間報酬 = 時間加權報酬率（TWR），未年化',
  '　　　每日 市值 ÷（前日市值 + 當日加碼），連乘',
  '',
  '為什麼扣掉加碼：',
  '　　　線是「部位市值」，你加碼它就變高。',
  '　　　若直接看線的漲幅，存進去的錢會被算成賺的。',
  '　　　TWR 把加碼從當日基期扣掉，只留部位本身漲跌。',
  '',
  '為什麼對照標的可比：',
  '　　　對照線用每股價格，不是基金總規模。',
  '　　　ETF 申購是按淨值增發單位數，不動每股價格。',
  '',
  '與總覽的年化報酬率（XIRR）不同：',
  '　　　XIRR 是資金加權且年化，算進你的進場時點；',
  '　　　這裡是時間加權且未年化，兩者本來就有差異。',
  '',
  '所有線都不含股息，僅計算已填成本的股票/基金持倉。',
].join('\n');
