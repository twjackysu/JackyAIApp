import BookmarkAddIcon from '@mui/icons-material/BookmarkAdd';
import BookmarkAddedIcon from '@mui/icons-material/BookmarkAdded';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import HighlightOffIcon from '@mui/icons-material/HighlightOff';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import Typography from '@mui/material/Typography';

import { DailyChallengeQuestion } from '@/apis/dailyChallengeApis/types';
import { noSelectionAsk } from '@/components/aiChat/selection/readSelection';

const TYPE_LABELS: Record<
  string,
  { label: string; color: 'primary' | 'secondary' | 'success' | 'warning' | 'info' }
> = {
  VocabularyDefinition: { label: '📖 Definition', color: 'primary' },
  FillInTheBlank: { label: '✏️ Fill in the Blank', color: 'secondary' },
  Synonym: { label: '🔄 Synonym', color: 'success' },
  Antonym: { label: '↔️ Antonym', color: 'warning' },
  Translation: { label: '🌐 Translation', color: 'info' },
};

interface QuestionCardProps {
  question: DailyChallengeQuestion;
  questionNumber: number;
  totalQuestions: number;
  selectedIndex: number | null;
  onSelect: (index: number) => void;
  showResult: boolean;
  /** Save the question's source word to the repository (review mode only). */
  onSaveWord?: (wordId: string, word: string) => void;
  /** Whether the source word has already been saved this session. */
  isSaved?: boolean;
  /** Whether a save request is in flight for this word. */
  isSaving?: boolean;
}

function QuestionCard({
  question,
  questionNumber,
  totalQuestions,
  selectedIndex,
  onSelect,
  showResult,
  onSaveWord,
  isSaved,
  isSaving,
}: QuestionCardProps) {
  const typeInfo = TYPE_LABELS[question.type] ?? {
    label: question.type,
    color: 'primary' as const,
  };

  return (
    <Card
      sx={{
        maxWidth: 600,
        mx: 'auto',
        boxShadow: 3,
        borderRadius: 3,
      }}
      // Until it is answered, selecting the question offers no AI help (it would give the answer away).
      {...noSelectionAsk(!showResult)}
    >
      <CardContent sx={{ p: 3 }}>
        {/* Header */}
        <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 2 }}>
          <Typography variant="body2" color="text.secondary">
            Question {questionNumber} / {totalQuestions}
          </Typography>
          <Chip label={typeInfo.label} size="small" color={typeInfo.color} variant="outlined" />
        </Box>

        {/* Prompt */}
        <Typography variant="h6" sx={{ mb: 3, lineHeight: 1.5 }}>
          {question.prompt}
        </Typography>

        {/* Options */}
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
          {question.options.map((option, index) => {
            let variant: 'outlined' | 'contained' = 'outlined';
            let color: 'primary' | 'success' | 'error' | 'inherit' = 'inherit';

            if (showResult) {
              if (index === question.correctIndex) {
                variant = 'contained';
                color = 'success';
              } else if (index === selectedIndex && index !== question.correctIndex) {
                variant = 'contained';
                color = 'error';
              }
            } else if (index === selectedIndex) {
              variant = 'contained';
              color = 'primary';
            }

            return (
              <Button
                key={index}
                variant={variant}
                color={color}
                onClick={() => !showResult && onSelect(index)}
                disabled={showResult}
                sx={{
                  justifyContent: 'flex-start',
                  textTransform: 'none',
                  py: 1.5,
                  px: 2,
                  fontSize: '0.95rem',
                  textAlign: 'left',
                }}
                startIcon={
                  showResult && index === question.correctIndex ? (
                    <CheckCircleIcon />
                  ) : showResult && index === selectedIndex && index !== question.correctIndex ? (
                    <HighlightOffIcon />
                  ) : undefined
                }
              >
                {option}
              </Button>
            );
          })}
        </Box>

        {/* Explanation (after submit) */}
        {showResult && (
          <Box
            sx={{
              mt: 2,
              p: 2,
              borderRadius: 2,
              bgcolor: selectedIndex === question.correctIndex ? 'success.dark' : 'error.dark',
              opacity: 0.9,
            }}
          >
            <Typography variant="body2" color="white">
              {question.explanation}
            </Typography>
          </Box>
        )}

        {/* Save the source word to the repository — the natural moment to capture an unknown word */}
        {showResult && question.wordId && question.word && onSaveWord && (
          <Box sx={{ mt: 1.5, display: 'flex', justifyContent: 'flex-end' }}>
            {isSaved ? (
              <Button size="small" startIcon={<BookmarkAddedIcon />} color="success" disabled>
                已加入「{question.word}」
              </Button>
            ) : (
              <Button
                size="small"
                startIcon={<BookmarkAddIcon />}
                onClick={() => onSaveWord(question.wordId!, question.word!)}
                disabled={isSaving}
              >
                收藏「{question.word}」到儲存庫
              </Button>
            )}
          </Box>
        )}
      </CardContent>
    </Card>
  );
}

export default QuestionCard;
