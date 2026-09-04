import { PositionDto } from '@/apis/portfolioApis/types';

/**
 * Field-by-field explanation of how a foreign-currency holding's numbers are produced, in the
 * same shape a broker's own 欄位說明 uses. Lives here rather than as caption text under the chart
 * because it is three formulas plus a caveat — enough prose to bury the figures it explains.
 *
 * The cost line is the part worth reading: a broker fixes cost at the purchase day's 交割匯率,
 * which is not recoverable after the fact, so the only way for the two apps to agree is for the
 * user to enter the broker's own 總成本 (fee included) rather than an amount this app re-converts.
 */
export function valuationExplainer(position: PositionDto): string | null {
  if (position.fxRateUsed == null) return null;

  const isFund = position.assetType === 'FUND';
  const quantityLabel = isFund ? '單位數' : '股數';
  const priceLabel = isFund ? '淨值' : '最新價';
  const costInTwd = (position.costCurrency ?? position.currency) === 'TWD';

  const lines = [
    `市值 (TWD) = ${quantityLabel} × ${priceLabel} × 匯率`,
    costInTwd ? '成本 (TWD) = 你輸入的金額，不再換匯' : '成本 (TWD) = 原幣成本 × 同一個匯率',
    '未實現損益 = 市值 − 成本',
    '',
  ];

  if (position.fxRateSource === 'CATHAY') {
    lines.push(
      '匯率：國泰世華銀行「最新價那一天」的即期買入匯率。',
      '　　　國泰複委託用的是同一個口徑（其公式為',
      '　　　股數 × 最新價 × 昨收匯率），所以這裡的損益',
      '　　　會和國泰 App 一致。非營業日沿用前一營業日。',
    );
  } else if (position.fxRateSource === 'TDCC') {
    lines.push(
      '匯率：集保結算所與華南銀行議定的買入匯率（外幣換台幣），',
      '　　　與基富通顯示的損益一致。集保每營業日下午公布',
      '　　　一次，尚未公布時沿用前一營業日。',
    );
  } else {
    lines.push(
      '匯率：市場中間價，每日更新一次。',
      '　　　這不是任何銀行的實際換匯價，和券商顯示的損益',
      '　　　會有落差。',
    );
  }

  lines.push('');
  if (costInTwd) {
    lines.push(
      '為什麼成本不換匯：',
      '　　　券商的成本固定在買進當天的交割匯率，那個匯率',
      '　　　事後查不到，重算只會失真。所以成本直接採用',
      '　　　你輸入的數字。',
      '　　　請填券商顯示的「總成本」（已含買進手續費），',
      '　　　兩邊才會完全對得起來。',
    );
  } else {
    lines.push(
      '注意：成本以外幣記錄時，這裡是用當前報價日的匯率換算，',
      '　　　但券商是固定在買進當天的交割匯率，兩者長期會',
      '　　　愈差愈多。建議改以台幣輸入券商的「總成本」。',
    );
  }

  return lines.join('\n');
}
