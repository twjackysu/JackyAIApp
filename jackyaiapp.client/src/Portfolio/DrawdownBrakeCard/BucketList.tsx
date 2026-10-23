import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import { ReactNode } from 'react';

import { Advice, StrategyPrices, StrategySymbol } from '../drawdownBrake';
import {
  CASH_BUCKET_NOTE,
  describeCashRowAdvice,
  describeCoreRowAdvice,
  describeLev2RowAdvice,
  fmtMultiple,
  LEV2_ROW_TITLE,
  OTHERS_ADVICE,
  OTHERS_REMINDER,
} from '../drawdownBrakeCopy';
import { BucketTotals, CORE_LEVERAGED_2X, ScopeClassification } from '../drawdownBrakeScope';
import { usePrivacy } from '../privacy';

import { StatItem } from './StatItem';

import { fmtNum } from '@/utils/format';

interface BucketRowProps {
  title: string;
  bucket: BucketTotals;
  scopeAssets: number;
  /** 核心與現金類才有目標市值。 */
  target?: number;
  /** 建議欄；其他持股固定為「不動（由您自行決定）」。 */
  adviceText: string;
  /** 列內附屬的內容（台股正 2 類的各檔明細）。 */
  children?: ReactNode;
}

/** 分桶表的一列。手機寬度下不用寬表格，改成堆疊式：標題一行，數字在可換行的格子裡，建議獨占一行。 */
function BucketRow({ title, bucket, scopeAssets, target, adviceText, children }: BucketRowProps) {
  const { value } = usePrivacy();
  return (
    <Box
      component="li"
      sx={{
        listStyle: 'none',
        border: '1px solid',
        borderColor: 'divider',
        borderRadius: 2,
        p: 1.5,
        minWidth: 0,
      }}
    >
      <Typography variant="body2" sx={{ fontWeight: 700, mb: 0.5 }}>
        {title}
      </Typography>
      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(120px, 1fr))',
          gap: 1,
        }}
      >
        <StatItem label="目前市值" value={`NT$${value(fmtNum(bucket.value))}`} />
        <StatItem
          label="目前曝險貢獻"
          value={`${fmtMultiple(scopeAssets > 0 ? bucket.exposure / scopeAssets : 0)} 倍`}
        />
        {target != null && <StatItem label="目標市值" value={`NT$${value(fmtNum(target))}`} />}
      </Box>
      <Typography variant="body2" sx={{ fontWeight: 600, mt: 0.75, overflowWrap: 'anywhere' }}>
        {`建議：${adviceText}`}
      </Typography>
      {children}
    </Box>
  );
}

interface BucketListProps {
  scope: ScopeClassification;
  advice: Advice;
  prices: StrategyPrices;
}

/** 台股正 2 類之下，縮排列出各檔的現值與建議股數（只列有持有或有建議的）。類別列給整組一個結論與金額，
 * 股數怎麼分到各檔由 `splitLev2Order` 決定。 */
function Lev2MemberRows({ scope, advice, prices }: BucketListProps) {
  const { value } = usePrivacy();
  const fmtAmount = (n: number) => value(fmtNum(n));
  const members = CORE_LEVERAGED_2X.flatMap((symbol) => {
    const holding = scope.lev2Holdings.find((h) => h.symbol === symbol);
    const order = advice.orders.find((o) => o.symbol === symbol);
    return holding || order ? [{ symbol, holding, order }] : [];
  });
  if (members.length === 0) return null;

  return (
    <Box
      component="ul"
      aria-label={`${LEV2_ROW_TITLE}各檔`}
      sx={{ m: 0, mt: 1, p: 0, display: 'grid', gap: 1 }}
    >
      {members.map(({ symbol, holding, order }) => (
        <Box
          component="li"
          key={symbol}
          sx={{
            listStyle: 'none',
            minWidth: 0,
            pl: 1.5,
            borderLeft: '2px solid',
            borderColor: 'divider',
          }}
        >
          <Typography variant="body2" sx={{ fontWeight: 700 }}>
            {symbol}
          </Typography>
          <StatItem label="目前市值" value={`NT$${fmtAmount(holding?.value ?? 0)}`} />
          <Typography variant="body2" sx={{ fontWeight: 600, mt: 0.5, overflowWrap: 'anywhere' }}>
            {`建議：${describeCoreRowAdvice(order, prices[symbol] ?? 0, fmtAmount)}`}
          </Typography>
        </Box>
      ))}
    </Box>
  );
}

/** 12b 分桶表：0050、台股正 2（00631L／00685L，其下列各檔）、其他一倍商品、其他倍數商品（有才顯示）、
 * 現金類。沒有的桶不顯示。 */
export function BucketList({ scope, advice, prices }: BucketListProps) {
  const { value } = usePrivacy();
  const fmtAmount = (n: number) => value(fmtNum(n));
  const orderOf = (symbol: StrategySymbol) => advice.orders.find((o) => o.symbol === symbol);
  const { scopeAssets, targets } = advice;

  const showCore = (bucket: BucketTotals, target: number) => bucket.count > 0 || target > 0;
  const hasOthers = scope.otherOne.count > 0 || scope.otherMulti.count > 0;

  return (
    <>
      <Box
        component="ul"
        aria-label="分桶配置與建議"
        sx={{ m: 0, mt: 1.5, p: 0, display: 'grid', gap: 1 }}
      >
        {showCore(scope.core0050, targets['0050']) && (
          <BucketRow
            title="0050"
            bucket={scope.core0050}
            scopeAssets={scopeAssets}
            target={targets['0050']}
            adviceText={describeCoreRowAdvice(orderOf('0050'), prices['0050'] ?? 0, fmtAmount)}
          />
        )}
        {showCore(scope.coreLev2, targets.lev2) && (
          <BucketRow
            title={LEV2_ROW_TITLE}
            bucket={scope.coreLev2}
            scopeAssets={scopeAssets}
            target={targets.lev2}
            adviceText={describeLev2RowAdvice(advice.lev2Change, fmtAmount)}
          >
            <Lev2MemberRows scope={scope} advice={advice} prices={prices} />
          </BucketRow>
        )}
        {scope.otherOne.count > 0 && (
          <BucketRow
            title="其他一倍商品"
            bucket={scope.otherOne}
            scopeAssets={scopeAssets}
            adviceText={OTHERS_ADVICE}
          />
        )}
        {scope.otherMulti.count > 0 && (
          <BucketRow
            title="其他倍數商品"
            bucket={scope.otherMulti}
            scopeAssets={scopeAssets}
            adviceText={OTHERS_ADVICE}
          />
        )}
        {showCore(scope.cash, targets.cash) && (
          <BucketRow
            title="現金類"
            bucket={scope.cash}
            scopeAssets={scopeAssets}
            target={targets.cash}
            adviceText={describeCashRowAdvice(advice.cashChange, fmtAmount)}
          />
        )}
      </Box>
      {hasOthers && (
        <Typography
          variant="caption"
          color="text.secondary"
          sx={{ display: 'block', mt: 1, overflowWrap: 'anywhere' }}
        >
          {OTHERS_REMINDER}
        </Typography>
      )}
      {(scope.cash.count > 0 || targets.cash > 0) && (
        <Typography
          variant="caption"
          color="text.secondary"
          sx={{ display: 'block', mt: 0.5, overflowWrap: 'anywhere' }}
        >
          {CASH_BUCKET_NOTE}
        </Typography>
      )}
    </>
  );
}
