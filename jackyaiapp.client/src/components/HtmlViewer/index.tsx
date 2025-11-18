import { Code, Preview } from '@mui/icons-material';
import { Box, ToggleButton, ToggleButtonGroup, Typography, Paper } from '@mui/material';
import hljs from 'highlight.js';
import { useState, useEffect, useRef } from 'react';

interface HtmlViewerProps {
  content: string;
  isComplete?: boolean;
  title?: string;
}

type ViewMode = 'preview' | 'code';

function HtmlViewer({ content, isComplete = false, title }: HtmlViewerProps) {
  // 根據 isComplete 初始化模式，但不會強制切換
  const [viewMode, setViewMode] = useState<ViewMode>(() => (isComplete ? 'preview' : 'code'));

  const codeScrollRef = useRef<HTMLPreElement>(null);
  const previewScrollRef = useRef<HTMLDivElement>(null);

  // 當內容更新時自動滾動到底部（特別是 streaming 時）
  useEffect(() => {
    if (!isComplete) {
      // 只在 streaming 時自動滾動
      const targetElement = viewMode === 'code' ? codeScrollRef.current : previewScrollRef.current;
      if (targetElement) {
        // 直接滾動到底部，避免動畫干擾 streaming 效果
        targetElement.scrollTop = targetElement.scrollHeight;
      }
    }
  }, [content, viewMode, isComplete]);

  const handleModeChange = (_: React.MouseEvent<HTMLElement>, newMode: ViewMode | null) => {
    if (newMode !== null) {
      setViewMode(newMode);
    }
  };

  // 使用 highlight.js 高亮代碼
  const getHighlightedCode = () => {
    try {
      const highlighted = hljs.highlight(content, { language: 'html' });
      return highlighted.value;
    } catch (error) {
      console.warn('Failed to highlight code:', error);
      return content;
    }
  };

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      {/* Header */}
      <Box
        sx={{
          p: 2,
          borderBottom: 1,
          borderColor: 'divider',
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
        }}
      >
        <Typography variant="h6" sx={{ fontSize: '0.9rem' }}>
          {title || 'HTML Preview'}
        </Typography>

        <ToggleButtonGroup value={viewMode} exclusive onChange={handleModeChange} size="small">
          <ToggleButton value="preview" disabled={!isComplete} sx={{ px: 1.5 }}>
            <Preview sx={{ mr: 0.5, fontSize: '1rem' }} />
            Preview
          </ToggleButton>
          <ToggleButton value="code" sx={{ px: 1.5 }}>
            <Code sx={{ mr: 0.5, fontSize: '1rem' }} />
            Code
          </ToggleButton>
        </ToggleButtonGroup>
      </Box>

      {/* Content Area */}
      <Box sx={{ flex: 1, overflow: 'hidden' }}>
        {viewMode === 'preview' ? (
          <Box
            ref={previewScrollRef}
            sx={{
              height: '100%',
              overflow: 'auto',
              p: 2,
              '& > *': {
                maxWidth: '100%',
              },
            }}
          >
            {isComplete ? (
              <div dangerouslySetInnerHTML={{ __html: content }} />
            ) : (
              <Typography color="text.secondary" sx={{ textAlign: 'center', mt: 4 }}>
                Preview will be available when HTML is complete...
              </Typography>
            )}
          </Box>
        ) : (
          <Box sx={{ height: '100%', overflow: 'hidden' }}>
            <Paper
              elevation={0}
              sx={{
                height: '100%',
                bgcolor: 'transparent',
              }}
            >
              <pre
                ref={codeScrollRef}
                style={{
                  margin: 0,
                  padding: '16px',
                  height: '100%',
                  overflow: 'auto',
                  fontSize: '0.875rem',
                  lineHeight: 1.5,
                  fontFamily: 'monospace',
                  whiteSpace: 'pre-wrap',
                  wordBreak: 'break-all',
                }}
              >
                <code
                  className="hljs language-html"
                  dangerouslySetInnerHTML={{ __html: getHighlightedCode() }}
                />
              </pre>
            </Paper>
          </Box>
        )}
      </Box>
    </Box>
  );
}

export default HtmlViewer;
