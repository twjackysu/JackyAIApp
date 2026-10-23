import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import ExpandLessIcon from '@mui/icons-material/ExpandLess';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import HistoryIcon from '@mui/icons-material/History';
import LoginIcon from '@mui/icons-material/Login';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Collapse from '@mui/material/Collapse';
import Paper from '@mui/material/Paper';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import { AI_DATA_NOTICE } from '../../constants';

import { ExampleChips } from './ExampleChips';

interface AskAiPanelProps {
  query: string;
  onQueryChange: (query: string) => void;
  recentQueries: string[];
  isLoggedIn: boolean;
  nlIsLoading: boolean;
  /** Asks the question in `query` — or, logged out, goes to sign in. */
  onGenerate: () => void;
}

/** AI mode: the question box, example and recent questions, and the generate button. */
export function AskAiPanel({
  query,
  onQueryChange,
  recentQueries,
  isLoggedIn,
  nlIsLoading,
  onGenerate,
}: AskAiPanelProps) {
  const [recentOpen, setRecentOpen] = useState(false);
  return (
    <Paper
      variant="outlined"
      sx={{
        p: 2.5,
        mb: 3,
        borderColor: 'rgba(94, 106, 210, 0.35)',
        backgroundColor: 'rgba(94, 106, 210, 0.05)',
      }}
    >
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        用一般文字描述你想看的內容，AI 會把你的需求轉換成查詢並回傳結果。
      </Typography>

      <TextField
        multiline
        minRows={2}
        maxRows={5}
        fullWidth
        placeholder="例如：「EMEA 地區各產品類別的營收」"
        value={query}
        onChange={(e) => onQueryChange(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter' && (e.metaKey || e.ctrlKey) && query.trim()) {
            onGenerate();
          }
        }}
        helperText={AI_DATA_NOTICE}
        sx={{ mb: 1.5 }}
      />

      <ExampleChips onPick={onQueryChange} spacing={{ mb: 2 }} />

      {recentQueries.length > 0 && (
        <Box sx={{ mb: 2 }}>
          {/* Collapsed by default so recents don't blend into the examples above */}
          <Button
            size="small"
            startIcon={<HistoryIcon sx={{ fontSize: 16 }} />}
            endIcon={
              recentOpen ? (
                <ExpandLessIcon sx={{ fontSize: 16 }} />
              ) : (
                <ExpandMoreIcon sx={{ fontSize: 16 }} />
              )
            }
            onClick={() => setRecentOpen((o) => !o)}
            sx={{ color: 'text.secondary', px: 1, minWidth: 0 }}
          >
            最近查詢 ({recentQueries.length})
          </Button>
          <Collapse in={recentOpen}>
            <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.75, mt: 1, pl: 0.5 }}>
              {recentQueries.map((q) => (
                <Chip
                  key={q}
                  label={q}
                  size="small"
                  onClick={() => onQueryChange(q)}
                  clickable
                  sx={{ maxWidth: 360 }}
                />
              ))}
            </Box>
          </Collapse>
        </Box>
      )}

      <Button
        variant="contained"
        startIcon={isLoggedIn ? <AutoAwesomeIcon /> : <LoginIcon />}
        onClick={onGenerate}
        disabled={isLoggedIn && (nlIsLoading || !query.trim())}
      >
        {!isLoggedIn ? '登入以使用 AI' : nlIsLoading ? '生成中…' : '生成'}
      </Button>
    </Paper>
  );
}
