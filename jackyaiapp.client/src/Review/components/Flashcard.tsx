import VisibilityIcon from '@mui/icons-material/Visibility';
import VisibilityOffIcon from '@mui/icons-material/VisibilityOff';
import VolumeUpIcon from '@mui/icons-material/VolumeUp';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import Divider from '@mui/material/Divider';
import IconButton from '@mui/material/IconButton';
import Stack from '@mui/material/Stack';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import { ReviewWordItem } from '@/apis/reviewApis/types';
import { playBrowserTts, playVoiceTube } from '@/Dictionary/utils/audio';

interface FlashcardProps {
  word: ReviewWordItem;
  cardNumber: number;
  totalCards: number;
  onRate: (quality: number) => void;
}

function Flashcard({ word, cardNumber, totalCards, onRate }: FlashcardProps) {
  const [revealed, setRevealed] = useState(false);

  return (
    <Card sx={{ maxWidth: 600, mx: 'auto', boxShadow: 3, borderRadius: 3 }}>
      <CardContent sx={{ p: 3 }}>
        {/* Header */}
        <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 2 }}>
          <Typography variant="body2" color="text.secondary">
            {cardNumber} / {totalCards}
          </Typography>
          <Chip
            label={`Reviewed ${word.reviewCount}×`}
            size="small"
            variant="outlined"
            color="info"
          />
        </Box>

        {/* Word */}
        <Box
          sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', gap: 1, mb: 1 }}
        >
          <Typography
            variant="h3"
            sx={{
              fontWeight: 'bold',
              textAlign: 'center',
            }}
          >
            {word.wordText}
          </Typography>
          <Tooltip title="Play pronunciation">
            <IconButton
              onClick={() => playVoiceTube(word.wordText)}
              color="primary"
              size="medium"
              aria-label={`Play pronunciation of ${word.wordText}`}
            >
              <VolumeUpIcon />
            </IconButton>
          </Tooltip>
        </Box>
        <Typography
          variant="body1"
          color="text.secondary"
          sx={{
            textAlign: 'center',
            mb: 3,
          }}
        >
          {word.kkPhonics}
        </Typography>

        {/* Toggle answer button */}
        <Box
          sx={{
            textAlign: 'center',
            mb: 2,
          }}
        >
          <Button
            variant={revealed ? 'outlined' : 'contained'}
            size="large"
            startIcon={revealed ? <VisibilityOffIcon /> : <VisibilityIcon />}
            onClick={() => setRevealed((r) => !r)}
            sx={{ borderRadius: 3, px: 4 }}
          >
            {revealed ? 'Hide Answer' : 'Show Answer'}
          </Button>
        </Box>

        {/* Definitions — only shown when revealed */}
        {revealed && (
          <>
            <Divider sx={{ mb: 2 }} />
            {word.meanings.map((meaning, mIdx) => (
              <Box key={mIdx} sx={{ mb: 2 }}>
                <Chip label={meaning.partOfSpeech} size="small" color="primary" sx={{ mb: 1 }} />
                {meaning.definitions.map((def, dIdx) => (
                  <Box key={dIdx} sx={{ ml: 1, mb: 1 }}>
                    <Typography variant="body1">{def.english}</Typography>
                    <Typography variant="body2" color="text.secondary">
                      {def.chinese}
                    </Typography>
                  </Box>
                ))}
                {meaning.exampleSentences.length > 0 && (
                  <Box sx={{ ml: 1, mt: 1, p: 1, borderRadius: 1, bgcolor: 'action.hover' }}>
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
                      <Typography
                        variant="body2"
                        sx={{
                          fontStyle: 'italic',
                        }}
                      >
                        {meaning.exampleSentences[0].english}
                      </Typography>
                      <Tooltip title="Play example sentence">
                        <IconButton
                          size="small"
                          onClick={() => playBrowserTts(meaning.exampleSentences[0].english)}
                          aria-label="Play example sentence"
                        >
                          <VolumeUpIcon fontSize="small" />
                        </IconButton>
                      </Tooltip>
                    </Box>
                    <Typography variant="caption" color="text.secondary">
                      {meaning.exampleSentences[0].chinese}
                    </Typography>
                  </Box>
                )}
              </Box>
            ))}
          </>
        )}

        <Divider sx={{ my: 2 }} />

        {/* Rating buttons — always visible */}
        <Typography
          variant="body2"
          color="text.secondary"
          sx={{
            textAlign: 'center',
            mb: 1.5,
          }}
        >
          How well did you know this?
        </Typography>
        <Stack
          direction="row"
          spacing={1}
          sx={{
            justifyContent: 'center',
            flexWrap: 'wrap',
          }}
        >
          <Button
            variant="contained"
            color="error"
            onClick={() => onRate(1)}
            sx={{ minWidth: 100 }}
          >
            😵 Forgot
          </Button>
          <Button
            variant="contained"
            color="warning"
            onClick={() => onRate(3)}
            sx={{ minWidth: 100 }}
          >
            🤔 Hard
          </Button>
          <Button
            variant="contained"
            color="success"
            onClick={() => onRate(4)}
            sx={{ minWidth: 100 }}
          >
            👍 Good
          </Button>
          <Button variant="contained" color="info" onClick={() => onRate(5)} sx={{ minWidth: 100 }}>
            ⚡ Easy
          </Button>
        </Stack>
      </CardContent>
    </Card>
  );
}

export default Flashcard;
