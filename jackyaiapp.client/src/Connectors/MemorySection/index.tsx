import { DeleteOutlined, Psychology } from '@mui/icons-material';
import { Alert, Box, Button, Chip, Paper, Stack, Typography } from '@mui/material';
import { useState } from 'react';

import type { AssistantMemory } from '@/apis/assistantMemoryApis/types';

import {
  useForgetAssistantMemoryMutation,
  useGetAssistantMemoriesQuery,
} from '@/apis/assistantMemoryApis';
import { getApiErrorMessage, getCaughtErrorMessage } from '@/apis/utils/errorMessage';

// One string, so no line break in the source turns into a space between Chinese words.
const INTRO =
  '跟 Studio AI 說「記住我在準備多益」，它確認後就會記下來，之後每個對話都會參考（例如短文的情境、簡報的重點），' +
  '資料分析的 AI 助理也會參考（只讀）。這裡列出它記得的每一則，不需要的可以刪掉。';

interface MemorySectionProps {
  notify: (message: string, severity: 'success' | 'error') => void;
}

/**
 * What the reader asked Studio AI to remember (docs/ai-native/design/08): every note goes into Studio AI's every
 * conversation, so they can see each one here and forget it (a second press, as revoking a token). Keeping one is
 * Studio AI's 「記住…」, asked on a confirmation card in the chat.
 */
export default function MemorySection({ notify }: MemorySectionProps) {
  const { data, isLoading, error } = useGetAssistantMemoriesQuery();
  const [forgetMemory, { isLoading: forgetting }] = useForgetAssistantMemoryMutation();
  const [confirming, setConfirming] = useState<string | null>(null);

  const forget = async (memory: AssistantMemory) => {
    try {
      await forgetMemory(memory.id).unwrap();
      notify(`Studio AI 已忘掉「${memory.text}」`, 'success');
    } catch (e) {
      notify(getCaughtErrorMessage(e), 'error');
    }
    setConfirming(null);
  };

  const list = data?.data;
  const memories = list?.memories ?? [];

  return (
    <Paper
      component="section"
      aria-labelledby="memory-title"
      variant="outlined"
      sx={{ p: { xs: 2, sm: 3 }, mb: 5 }}
    >
      {/* One line on a phone too: the title is a little smaller there rather than wrapping beside the count. */}
      <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', mb: 1 }}>
        <Psychology color="primary" />
        <Typography
          id="memory-title"
          variant="h6"
          component="h2"
          sx={{
            flex: 1,
            minWidth: 0,
            fontSize: { xs: '1rem', sm: undefined },
            whiteSpace: 'nowrap',
          }}
        >
          Studio AI 記得的事
        </Typography>
        {list && (
          <Chip
            size="small"
            variant="outlined"
            label={`${memories.length} / ${list.limit}`}
            sx={{ flexShrink: 0 }}
          />
        )}
      </Stack>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        {INTRO}
      </Typography>

      {isLoading ? null : error ? (
        <Alert severity="info">記憶目前無法載入（{getApiErrorMessage(error)}）。</Alert>
      ) : memories.length === 0 ? (
        <Typography variant="body2" color="text.secondary">
          Studio AI 還沒有記得任何事。
        </Typography>
      ) : (
        <Stack spacing={1}>
          {memories.map((m) => (
            <Box
              key={m.id}
              data-testid="memory"
              sx={{
                display: 'flex',
                alignItems: 'center',
                gap: 1.5,
                flexWrap: 'wrap',
                p: 1.5,
                border: 1,
                borderColor: 'divider',
                borderRadius: 1,
              }}
            >
              <Typography variant="body1" sx={{ flex: 1, minWidth: 200, overflowWrap: 'anywhere' }}>
                {m.text}
              </Typography>
              {confirming === m.id ? (
                <Stack direction="row" spacing={1}>
                  <Button size="small" onClick={() => setConfirming(null)}>
                    取消
                  </Button>
                  <Button
                    size="small"
                    color="error"
                    variant="contained"
                    disabled={forgetting}
                    onClick={() => forget(m)}
                  >
                    確定刪除
                  </Button>
                </Stack>
              ) : (
                <Button
                  size="small"
                  color="error"
                  startIcon={<DeleteOutlined />}
                  onClick={() => setConfirming(m.id)}
                >
                  刪除
                </Button>
              )}
            </Box>
          ))}
        </Stack>
      )}
    </Paper>
  );
}
