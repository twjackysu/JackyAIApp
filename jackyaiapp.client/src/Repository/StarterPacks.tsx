import AddIcon from '@mui/icons-material/Add';
import CheckIcon from '@mui/icons-material/Check';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import CircularProgress from '@mui/material/CircularProgress';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import type { AddWordPackResult } from '@/apis/repositoryApis/types';

import { useAddWordPackMutation, useGetWordPacksQuery } from '@/apis/repositoryApis';

interface StarterPacksProps {
  /** Called after a pack is added so the parent can refresh its word list. */
  onAdded?: () => void;
}

/**
 * One-click topic word packs to seed an empty repository — the fix for the cold-start
 * problem (no words → nothing to review/quiz → no reason to return).
 */
function StarterPacks({ onAdded }: StarterPacksProps) {
  const { data, isLoading } = useGetWordPacksQuery();
  const [addWordPack] = useAddWordPackMutation();
  const [busyId, setBusyId] = useState<string | null>(null);
  const [results, setResults] = useState<Record<string, AddWordPackResult>>({});

  const packs = data?.data ?? [];

  const handleAdd = async (id: string) => {
    setBusyId(id);
    try {
      const res = await addWordPack(id).unwrap();
      setResults((prev) => ({ ...prev, [id]: res.data }));
      onAdded?.();
    } catch {
      /* surfaced via the row staying actionable; ignore */
    } finally {
      setBusyId(null);
    }
  };

  if (isLoading || packs.length === 0) {
    return null;
  }

  return (
    <Box sx={{ maxWidth: 560 }}>
      <Typography variant="subtitle1" sx={{ fontWeight: 600, mb: 0.5 }}>
        Start with a word pack
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        Add a themed set of words in one tap, then review and test yourself on them. Free.
      </Typography>

      <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
        {packs.map((pack) => {
          const result = results[pack.id];
          const busy = busyId === pack.id;
          return (
            <Card key={pack.id} variant="outlined">
              <CardContent
                sx={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 2,
                  '&:last-child': { pb: 2 },
                }}
              >
                <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                  <Typography variant="subtitle2" sx={{ fontWeight: 600 }}>
                    {pack.name}{' '}
                    <Typography component="span" variant="caption" color="text.secondary">
                      · {pack.wordCount} words
                    </Typography>
                  </Typography>
                  <Typography variant="body2" color="text.secondary">
                    {pack.description}
                  </Typography>
                </Box>
                {result ? (
                  <Button size="small" startIcon={<CheckIcon />} color="success" disabled>
                    Added {result.added}
                  </Button>
                ) : (
                  <Button
                    size="small"
                    variant="outlined"
                    startIcon={busy ? <CircularProgress size={16} /> : <AddIcon />}
                    onClick={() => handleAdd(pack.id)}
                    disabled={busy}
                  >
                    {busy ? 'Adding…' : 'Add'}
                  </Button>
                )}
              </CardContent>
            </Card>
          );
        })}
      </Box>

      {Object.values(results).some((r) => r.added > 0) && (
        <Alert severity="success" sx={{ mt: 2 }}>
          Words added! Head to <strong>Review</strong> or <strong>Exam</strong> to start practising.
        </Alert>
      )}
    </Box>
  );
}

export default StarterPacks;
