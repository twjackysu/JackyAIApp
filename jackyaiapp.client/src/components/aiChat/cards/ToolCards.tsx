import { Box } from '@mui/material';
import { useTheme } from '@mui/material/styles';

import { useAiChatConfig } from '../config';
import { hairline } from '../style';

import LinkCardView from './LinkCardView';
import TableCardView from './TableCardView';

import type { ToolCard } from './toolCard';
import type { ToolStep } from '../utils/toolActivity';

/**
 * The cards an answer's tools returned, in the order they ran, above its text. The chat draws
 * tables and links; any other kind is drawn by the app's renderer for it, or not at all.
 */
export default function ToolCards({ steps }: { steps: readonly ToolStep[] }) {
  const theme = useTheme();
  const { cardRenderers } = useAiChatConfig();
  const drawn = steps
    .map((s) => ({ key: s.key, view: s.card ? cardView(s.card, cardRenderers) : null }))
    .filter((c) => c.view !== null);
  if (drawn.length === 0) return null;

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1, mb: 1.5 }}>
      {drawn.map((c) => (
        <Box
          key={c.key}
          data-testid="tool-card"
          sx={{
            border: `1px solid ${hairline(theme)}`,
            borderRadius: 2,
            p: 1.5,
            overflow: 'hidden',
          }}
        >
          {c.view}
        </Box>
      ))}
    </Box>
  );
}

function cardView(
  card: ToolCard,
  renderers: ReturnType<typeof useAiChatConfig>['cardRenderers'],
): React.ReactNode {
  if (card.kind === 'table' && 'columns' in card) return <TableCardView card={card} />;
  if (card.kind === 'link' && 'href' in card) return <LinkCardView card={card} />;
  const Renderer = 'data' in card ? renderers?.[card.kind] : undefined;
  return Renderer && 'data' in card ? <Renderer card={card} /> : null;
}
