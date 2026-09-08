import Box from '@mui/material/Box';
import Typography, { TypographyProps } from '@mui/material/Typography';
import { ReactNode } from 'react';

interface StatBlockProps {
  /** The small grey caption above the value. */
  label: ReactNode;
  /** The value itself. Ignored when `children` is given. */
  value?: ReactNode;
  /**
   * Renders in place of the value Typography, for stats whose value is a Chip or other element.
   * Nullish children fall back to `value`, so a stat that only sometimes has a Chip (privacy mode
   * masks it) can pass both without repeating the label.
   */
  children?: ReactNode;
  /** Value colour; omitted means "inherit", i.e. normal body text. */
  color?: string;
  /**
   * Value size. The Portfolio cards deliberately differ here — a benchmark return headlines its
   * card (`h6`), the waterfall's contributor lines sit under a chart (`body2`) — so it stays a
   * per-call-site choice rather than being flattened to one size.
   */
  valueVariant?: TypographyProps['variant'];
  /** `center` for the holding drawer's grid; the card grids keep the default left alignment. */
  align?: 'left' | 'center';
}

/**
 * The Portfolio module's "caption label over a bold value" readout, shared by the summary grids in
 * DrawdownChart / ConcentrationCard / BenchmarkChart / HoldingDetailDrawer / the contribution
 * waterfall. Each of those had grown its own near-identical copy of this markup, which is how they
 * ended up disagreeing on details (a `<span>` here, a `text.primary` fallback there) that were
 * never meant to be different.
 */
export function StatBlock({ label, value, children, color, valueVariant, align }: StatBlockProps) {
  return (
    <Box sx={{ textAlign: align }}>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
        {label}
      </Typography>
      {children ?? (
        <Typography
          variant={valueVariant ?? 'body1'}
          component="span"
          sx={{ display: 'block', fontWeight: 700, color }}
        >
          {value}
        </Typography>
      )}
    </Box>
  );
}
