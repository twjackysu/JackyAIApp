import ArticleIcon from '@mui/icons-material/Article';
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutlineOutlined';
import TableChartIcon from '@mui/icons-material/TableChart';
import TextSnippetIcon from '@mui/icons-material/TextSnippet';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';

import { ATTACHMENT_TRUNCATED_NOTE } from '@/apis/chatbotApis/chatErrors';
import { ChatAttachment } from '@/apis/chatbotApis/types';

const KIND_ICON: Record<ChatAttachment['kind'], typeof ArticleIcon> = {
  word: ArticleIcon,
  excel: TableChartIcon,
  csv: TableChartIcon,
  text: TextSnippetIcon,
};

interface AttachmentChipProps {
  fileName: string;
  /** Unknown until the upload is done. */
  kind?: ChatAttachment['kind'];
  uploading?: boolean;
  failed?: boolean;
  truncated?: boolean;
  /** Shown on a coloured bubble (the reader's own message) rather than on the page. */
  onBubble?: boolean;
  onDelete?: () => void;
}

/** One attached file: in the chat box before it is sent, and in the message bubble after. */
function AttachmentChip({
  fileName,
  kind,
  uploading,
  failed,
  truncated,
  onBubble,
  onDelete,
}: AttachmentChipProps) {
  const Icon = failed ? ErrorOutlineIcon : KIND_ICON[kind ?? 'text'];
  return (
    <Chip
      size="small"
      variant="outlined"
      color={failed ? 'error' : 'default'}
      icon={uploading ? <CircularProgress size={14} sx={{ ml: '6px' }} /> : <Icon />}
      label={fileName}
      title={truncated ? `${fileName}（${ATTACHMENT_TRUNCATED_NOTE}）` : fileName}
      onDelete={onDelete}
      sx={{
        maxWidth: 220,
        ...(onBubble && {
          color: 'inherit',
          borderColor: 'rgba(255,255,255,0.6)',
          '& .MuiChip-icon': { color: 'inherit' },
        }),
      }}
    />
  );
}

export default AttachmentChip;
