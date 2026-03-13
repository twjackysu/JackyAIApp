import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import HighlightOffIcon from '@mui/icons-material/HighlightOff';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import Typography from '@mui/material/Typography';

import { DailyChallengeQuestion } from '@/apis/dailyChallengeApis/types';

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
}

function QuestionCard({
  question,
  questionNumber,
  totalQuestions,
  selectedIndex,
  onSelect,
  showResult,
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
      </CardContent>
    </Card>
  );
}

export default QuestionCard;
