import VolumeUpIcon from '@mui/icons-material/VolumeUp';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';

import { useGetWordQuery } from '@/apis/dictionaryApis';
import { playVoiceTube } from '@/Dictionary/utils/audio';

interface WordRecapProps {
  word: string;
}

/** Compact word-card shown after answering, so the exam doubles as a quick review. */
function WordRecap({ word }: WordRecapProps) {
  const { data, isFetching } = useGetWordQuery(word, { skip: !word });
  const wordData = data?.data;

  if (isFetching) {
    return (
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mt: 2 }}>
        <CircularProgress size={16} />
        <Typography variant="caption" color="text.secondary">
          載入單字卡...
        </Typography>
      </Box>
    );
  }

  if (!wordData) return null;

  const firstMeaning = wordData.meanings[0];

  return (
    <Box
      sx={{
        mt: 2,
        p: 1.5,
        borderRadius: 1,
        border: '1px solid',
        borderColor: 'divider',
      }}
    >
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
        <Typography variant="subtitle1" sx={{ fontWeight: 700 }}>
          {wordData.word}
        </Typography>
        <Typography variant="body2" color="text.secondary">
          {wordData.kkPhonics}
        </Typography>
        <IconButton
          size="small"
          onClick={() => playVoiceTube(wordData.word)}
          aria-label={`播放 ${wordData.word} 發音`}
        >
          <VolumeUpIcon fontSize="small" />
        </IconButton>
        {firstMeaning && <Chip label={firstMeaning.partOfSpeech} size="small" variant="outlined" />}
      </Box>
      {firstMeaning?.definitions.slice(0, 2).map((def) => (
        <Box key={def.english} sx={{ mt: 0.5 }}>
          <Typography variant="body2">{def.english}</Typography>
          <Typography variant="caption" color="text.secondary">
            {def.chinese}
          </Typography>
        </Box>
      ))}
    </Box>
  );
}

export default WordRecap;
