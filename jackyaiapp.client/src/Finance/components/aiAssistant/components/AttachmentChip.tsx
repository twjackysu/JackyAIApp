import ArticleIcon from '@mui/icons-material/Article';
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutlineOutlined';
import ImageIcon from '@mui/icons-material/Image';
import PictureAsPdfIcon from '@mui/icons-material/PictureAsPdf';
import TableChartIcon from '@mui/icons-material/TableChart';
import TextSnippetIcon from '@mui/icons-material/TextSnippet';
import Avatar from '@mui/material/Avatar';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';

import { ATTACHMENT_TRUNCATED_NOTE } from '@/apis/chatbotApis/chatErrors';
import { ChatAttachment } from '@/apis/chatbotApis/types';

const KIND_ICON: Record<ChatAttachment['kind'], typeof ArticleIcon> = {
  word: ArticleIcon,
  excel: TableChartIcon,
  csv: TableChartIcon,
  text: TextSnippetIcon,
  image: ImageIcon,
  pdf: PictureAsPdfIcon,
};

interface AttachmentChipProps {
  fileName: string;
  /** Unknown until the upload is done. */
  kind?: ChatAttachment['kind'];
  uploading?: boolean;
  failed?: boolean;
  truncated?: boolean;
  pageCount?: number | null;
  /** A picture of the image itself, shown in place of the icon. */
  thumbnailUrl?: string;
  /** Opens the file (an image or a PDF) in a new tab when the chip is clicked. */
  href?: string;
  /** Shown on a coloured bubble (the reader's own message) rather than on the page. */
  onBubble?: boolean;
  onDelete?: () => void;
}

const describe = (fileName: string, truncated?: boolean, pageCount?: number | null) =>
  truncated
    ? `${fileName}（${ATTACHMENT_TRUNCATED_NOTE}）`
    : pageCount
      ? `${fileName}（${pageCount} 頁）`
      : fileName;

/** One attached file: in the chat box before it is sent, and in the message bubble after. */
function AttachmentChip({
  fileName,
  kind,
  uploading,
  failed,
  truncated,
  pageCount,
  thumbnailUrl,
  href,
  onBubble,
  onDelete,
}: AttachmentChipProps) {
  const Icon = failed ? ErrorOutlineIcon : KIND_ICON[kind ?? 'text'];
  const visual = uploading ? (
    <CircularProgress size={14} sx={{ ml: '6px' }} />
  ) : thumbnailUrl && !failed ? (
    <Avatar src={thumbnailUrl} alt="" variant="rounded" />
  ) : (
    <Icon />
  );
  const link =
    href && !failed
      ? { component: 'a' as const, href, target: '_blank', rel: 'noopener', clickable: true }
      : {};

  return (
    <Chip
      size="small"
      variant="outlined"
      color={failed ? 'error' : 'default'}
      {...(thumbnailUrl && !uploading && !failed ? { avatar: visual } : { icon: visual })}
      label={fileName}
      title={describe(fileName, truncated, pageCount)}
      onDelete={onDelete}
      {...link}
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
