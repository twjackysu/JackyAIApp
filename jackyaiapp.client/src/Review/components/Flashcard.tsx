import VisibilityIcon from '@mui/icons-material/Visibility';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import Divider from '@mui/material/Divider';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import { ReviewWordItem } from '@/apis/reviewApis/types';

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
        <Typography variant="h3" fontWeight="bold" textAlign="center" sx={{ mb: 1 }}>
          {word.wordText}
        </Typography>
        <Typography variant="body1" color="text.secondary" textAlign="center" sx={{ mb: 3 }}>
          {word.kkPhonics}
        </Typography>

        {/* Reveal button or definitions */}
        {!revealed ? (
          <Box textAlign="center">
            <Button
              variant="contained"
              size="large"
              startIcon={<VisibilityIcon />}
              onClick={() => setRevealed(true)}
              sx={{ borderRadius: 3, px: 4 }}
            >
              Show Answer
            </Button>
          </Box>
        ) : (
          <>
            <Divider sx={{ mb: 2 }} />

            {/* Definitions */}
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
                    <Typography variant="body2" fontStyle="italic">
                      {meaning.exampleSentences[0].english}
                    </Typography>
                    <Typography variant="caption" color="text.secondary">
                      {meaning.exampleSentences[0].chinese}
                    </Typography>
                  </Box>
                )}
              </Box>
            ))}

            <Divider sx={{ my: 2 }} />

            {/* Rating buttons */}
            <Typography variant="body2" color="text.secondary" textAlign="center" sx={{ mb: 1.5 }}>
              How well did you know this?
            </Typography>
            <Stack direction="row" spacing={1} justifyContent="center" flexWrap="wrap">
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
              <Button
                variant="contained"
                color="info"
                onClick={() => onRate(5)}
                sx={{ minWidth: 100 }}
              >
                ⚡ Easy
              </Button>
            </Stack>
          </>
        )}
      </CardContent>
    </Card>
  );
}

export default Flashcard;
