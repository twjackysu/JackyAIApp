import { Box, Button, Popover, Stack, Typography } from '@mui/material';
import { useState } from 'react';

interface DifficultySelectorProps {
  onSelectDifficulty: (level: number) => void;
  disabled?: boolean;
}

const DIFFICULTY_OPTIONS = [
  { level: 1, label: '初級 (Beginner)', desc: '簡單日常對話、基礎詞彙' },
  { level: 2, label: '初中級 (Elementary)', desc: '基礎生活對話、常用句型' },
  { level: 3, label: '中級 (Intermediate)', desc: '一般情境對話、多樣話題' },
  { level: 4, label: '中高級 (Upper-Intermediate)', desc: '複雜話題討論、抽象概念' },
  { level: 5, label: '高級 (Advanced)', desc: '專業深度對話、學術討論' },
];

function DifficultySelector({ onSelectDifficulty, disabled = false }: DifficultySelectorProps) {
  const [anchorEl, setAnchorEl] = useState<HTMLButtonElement | null>(null);
  const [selectedDifficulty, setSelectedDifficulty] = useState<number>(3);

  const handleOpenSelector = (event: React.MouseEvent<HTMLButtonElement>) => {
    setAnchorEl(event.currentTarget);
  };

  const handleCloseSelector = () => {
    setAnchorEl(null);
  };

  const handleSelectDifficulty = (level: number) => {
    setSelectedDifficulty(level);
    handleCloseSelector();
    onSelectDifficulty(level);
  };

  return (
    <>
      <Button
        variant="contained"
        color="primary"
        size="large"
        onClick={handleOpenSelector}
        disabled={disabled}
      >
        開始對話
      </Button>

      <Popover
        open={Boolean(anchorEl)}
        anchorEl={anchorEl}
        onClose={handleCloseSelector}
        anchorOrigin={{
          vertical: 'top',
          horizontal: 'center',
        }}
        transformOrigin={{
          vertical: 'bottom',
          horizontal: 'center',
        }}
      >
        <Box sx={{ p: 3, minWidth: 320, maxWidth: 360 }}>
          <Typography variant="h6" gutterBottom align="center">
            🎯 選擇對話難度
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 3, textAlign: 'center' }}>
            選擇適合你英文程度的對話難度
          </Typography>
          <Stack spacing={1.5}>
            {DIFFICULTY_OPTIONS.map((option) => (
              <Button
                key={option.level}
                variant={selectedDifficulty === option.level ? 'contained' : 'outlined'}
                onClick={() => handleSelectDifficulty(option.level)}
                sx={{
                  justifyContent: 'flex-start',
                  textAlign: 'left',
                  py: 1.5,
                  px: 2,
                  '&:hover': {
                    transform: 'translateY(-1px)',
                    boxShadow: 2,
                  },
                  transition: 'all 0.2s ease-in-out',
                }}
              >
                <Box sx={{ width: '100%' }}>
                  <Typography variant="body1" fontWeight="bold" gutterBottom>
                    {option.label}
                  </Typography>
                  <Typography variant="body2" color="text.secondary">
                    {option.desc}
                  </Typography>
                </Box>
              </Button>
            ))}
          </Stack>
        </Box>
      </Popover>
    </>
  );
}

export default DifficultySelector;
