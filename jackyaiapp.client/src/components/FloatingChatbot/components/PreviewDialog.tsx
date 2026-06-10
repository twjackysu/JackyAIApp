import CloseIcon from '@mui/icons-material/Close';
import { Box, Dialog, IconButton, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';

import HtmlViewer from '@/components/HtmlViewer';
import MarkdownMessage from '@/components/MarkdownMessage';
import { ParsedBlock } from '@/hooks/useStreamingHtmlParser';

interface PreviewDialogProps {
  open: boolean;
  onClose: () => void;
  block: ParsedBlock | null;
}

/**
 * A spacious, standalone modal for previewing a single content block.
 * - HTML  → reuses {@link HtmlViewer} (with its Preview / Code toggle).
 * - Markdown → renders the markdown in a wide, comfortable reading column.
 */
const PreviewDialog = ({ open, onClose, block }: PreviewDialogProps) => {
  const theme = useTheme();

  const isHtml = block?.contentType === 'html';
  const isComplete = block?.isComplete ?? true;

  return (
    <Dialog
      open={open && block !== null}
      onClose={onClose}
      maxWidth={false}
      slotProps={{
        paper: {
          sx: {
            width: { xs: '100vw', sm: 'min(1100px, 96vw)' },
            height: { xs: '100dvh', sm: 'min(88vh, 880px)' },
            m: { xs: 0, sm: 2 },
            borderRadius: { xs: 0, sm: 3 },
            display: 'flex',
            flexDirection: 'column',
            overflow: 'hidden',
            bgcolor: 'background.default',
          },
        },
      }}
    >
      {isHtml ? (
        <HtmlViewer
          content={block?.content ?? ''}
          isComplete={isComplete}
          title="HTML 預覽"
          onClose={onClose}
        />
      ) : (
        <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
          {/* Header */}
          <Box
            sx={{
              px: 2.5,
              py: 1.5,
              borderBottom: 1,
              borderColor: 'divider',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              flexShrink: 0,
            }}
          >
            <Typography variant="subtitle1" sx={{ fontWeight: 600 }}>
              Markdown 預覽
            </Typography>
            <IconButton size="small" onClick={onClose} aria-label="關閉預覽">
              <CloseIcon fontSize="small" />
            </IconButton>
          </Box>

          {/* Content */}
          <Box
            sx={{
              flex: 1,
              overflowY: 'auto',
              px: { xs: 2.5, sm: 5 },
              py: 3,
              '& > *': { maxWidth: 860, mx: 'auto' },
            }}
          >
            <MarkdownMessage content={block?.content ?? ''} isOwnMessage={false} theme={theme} />
          </Box>
        </Box>
      )}
    </Dialog>
  );
};

export default PreviewDialog;
