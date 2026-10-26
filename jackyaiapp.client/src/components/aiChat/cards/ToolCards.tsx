import { Box } from '@mui/material';
import { useTheme } from '@mui/material/styles';

import { useAiChatConfig } from '../config';
import { hairline } from '../style';

import LinkCardView from './LinkCardView';
import TableCardActions from './TableCardActions';
import TableCardView from './TableCardView';

import type { CustomCard, TableCard, ToolCard } from './toolCard';
import type { ToolStep } from '../utils/toolActivity';

/**
 * The cards an answer's tools returned, in the order they ran, above its text: from the answer's
 * tool steps while it streams, or the cards stored with it when a conversation is reopened. The
 * chat draws tables and links; any other kind is drawn by the app's renderer for it, or not at all.
 */
export default function ToolCards({
  steps,
  cards,
}: {
  steps?: readonly ToolStep[];
  cards?: readonly ToolCard[];
}) {
  const theme = useTheme();
  const { cardRenderers, cardTables } = useAiChatConfig();
  const items = steps
    ? steps.map((s) => ({ key: s.key, card: s.card }))
    : (cards ?? []).map((card, i) => ({ key: `saved-${i}`, card }));
  const drawn = items
    .map((s) => ({
      key: s.key,
      view: s.card ? cardView(s.card, cardRenderers, !steps) : null,
      table: s.card ? tableOf(s.card, cardTables) : null,
    }))
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
          {c.table && c.table.rows.length > 0 && <TableCardActions table={c.table} />}
        </Box>
      ))}
    </Box>
  );
}

/** The card as a table to copy or download (D-150): a table card itself, or an app's card it says how to read as one. */
function tableOf(
  card: ToolCard,
  cardTables: ReturnType<typeof useAiChatConfig>['cardTables'],
): TableCard | null {
  if (card.kind === 'table' && 'columns' in card) return card;
  return 'data' in card ? (cardTables?.[card.kind]?.(card as CustomCard) ?? null) : null;
}

function cardView(
  card: ToolCard,
  renderers: ReturnType<typeof useAiChatConfig>['cardRenderers'],
  saved: boolean,
): React.ReactNode {
  if (card.kind === 'table' && 'columns' in card) return <TableCardView card={card} />;
  if (card.kind === 'link' && 'href' in card) return <LinkCardView card={card} />;
  const Renderer = 'data' in card ? renderers?.[card.kind] : undefined;
  return Renderer && 'data' in card ? <Renderer card={card} saved={saved} /> : null;
}
