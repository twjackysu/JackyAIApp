import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';

// Mix of starter and realistic "what a sales/finance team would actually ask"
// questions — the complex ones exercise multi-dimension group-bys, filters,
// NumericBin tiers, and weekly DateTrunc with relative dates.
// Values and tier boundaries verified against the live dbo.Orders data
// (Revenue spans ~1K–10K; categories are Consulting/Hardware/Software/
// Support/Training; statuses are Completed/Pending/Cancelled).
const NL_EXAMPLES = [
  '各地區總營收',
  '每月訂單數量趨勢',
  'EMEA 地區企業客戶各銷售管道的季營收',
  '依成交金額級距（<3K、3K–6K、>6K）統計訂單數與營收',
  '今年 Software 類別已完成訂單的每週營收',
];

/** Clickable example questions — shown in the input panel and in the "no answer" / "no data"
 * guidance, so a user whose question matched nothing can fall back to a known-good query. */
export function ExampleChips({
  onPick,
  spacing,
}: {
  onPick: (question: string) => void;
  /** Space around the chips, which differs between the panel and the guidance. */
  spacing: { mt?: number; mb?: number };
}) {
  return (
    <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.75, ...spacing }}>
      {NL_EXAMPLES.map((ex) => (
        <Chip
          key={ex}
          label={ex}
          size="small"
          variant="outlined"
          clickable
          onClick={() => onPick(ex)}
        />
      ))}
    </Box>
  );
}
