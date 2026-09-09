import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Divider from '@mui/material/Divider';
import Typography from '@mui/material/Typography';
import { ReactNode } from 'react';

interface ChartSectionCardProps {
  /** Section heading shown above the divider. */
  title: string;
  children: ReactNode;
  /** Anchor id for the onboarding tour (see onboardingTour.ts); only a couple of sections need one. */
  'data-tour'?: string;
}

/**
 * The outlined card every Portfolio chart section sits in: rounded border, a bold heading, a
 * divider, then the chart. Extracted because the same shell was repeated verbatim for each of
 * the seven sections — a spacing or border tweak meant editing all seven in lockstep.
 *
 * The positions table at the bottom of the page deliberately keeps its own card: it has no
 * heading, tighter padding, and no bottom margin, so folding it in here would mean three
 * one-caller props.
 */
export function ChartSectionCard({ title, children, ...rest }: ChartSectionCardProps) {
  return (
    <Card
      {...rest}
      elevation={0}
      sx={{ borderRadius: 3, border: '1px solid', borderColor: 'divider', mb: 3 }}
    >
      <CardContent sx={{ p: 3 }}>
        <Typography variant="h6" sx={{ fontWeight: 700, mb: 2 }}>
          {title}
        </Typography>
        <Divider sx={{ mb: 2 }} />
        {children}
      </CardContent>
    </Card>
  );
}
