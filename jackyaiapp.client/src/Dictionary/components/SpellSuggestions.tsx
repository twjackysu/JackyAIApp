import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Typography from '@mui/material/Typography';

import { useGetWordSuggestionsQuery } from '@/apis/dictionaryApis';

interface SpellSuggestionsProps {
  word: string;
  onSelectSuggestion: (word: string) => void;
}

/** Shown when a dictionary lookup fails — offers Hunspell-based "did you mean" corrections. */
function SpellSuggestions({ word, onSelectSuggestion }: SpellSuggestionsProps) {
  const { data } = useGetWordSuggestionsQuery(word, { skip: !word });
  const suggestions = data?.data.suggestions ?? [];

  if (suggestions.length === 0) {
    return null;
  }

  return (
    <Box sx={{ mt: 2, textAlign: 'center' }}>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        你是不是要找：
      </Typography>
      <Box sx={{ display: 'flex', gap: 1, justifyContent: 'center', flexWrap: 'wrap' }}>
        {suggestions.map((suggestion) => (
          <Chip
            key={suggestion}
            label={suggestion}
            variant="outlined"
            onClick={() => onSelectSuggestion(suggestion)}
          />
        ))}
      </Box>
    </Box>
  );
}

export default SpellSuggestions;
