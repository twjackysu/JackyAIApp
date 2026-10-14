/** Explains the 占比 column of the holdings list. */
export const POSITION_SHARE_TOOLTIP =
  '占比 = 市值 ÷ 流動總資產（台股＋美股＋基金＋現金）。不含不動產，也不扣負債；負債顯示為負的占比，不動產不計算占比。';

/**
 * A holding's (or a group's) share of liquid gross assets, in percent — the same denominator as
 * 水位 and 再平衡建議 (`liquidGrossAssetsTWD`: TW stock + US stock + fund + cash, before
 * subtracting debt). Real estate isn't a liquid asset, so it has no share (null). Liability is
 * already negative (sign-flip invariant, see PortfolioService), so it comes out as e.g. -12.5.
 */
export function liquidSharePct(
  marketValueTWD: number,
  assetType: string,
  liquidGrossAssetsTWD: number,
): number | null {
  if (assetType === 'REAL_ESTATE' || liquidGrossAssetsTWD <= 0) return null;
  return (marketValueTWD / liquidGrossAssetsTWD) * 100;
}
