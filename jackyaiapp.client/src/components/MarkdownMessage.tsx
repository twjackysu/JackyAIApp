import { Box, Typography } from '@mui/material';
import { Theme } from '@mui/material/styles';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';

import { readFencedCode } from './markdownCode';
import { highlightToHtml } from './syntaxHighlight';

interface MarkdownMessageProps {
  content: string;
  isOwnMessage: boolean;
  theme: Theme;
}

const MarkdownMessage = ({ content, isOwnMessage, theme }: MarkdownMessageProps) => {
  // 行內與區塊共用的外觀，差別只在 padding / 字級 / display。
  const codeSurface = {
    backgroundColor: isOwnMessage
      ? 'rgba(255, 255, 255, 0.1)'
      : theme.palette.mode === 'dark'
        ? 'rgba(255, 255, 255, 0.05)'
        : 'rgba(0, 0, 0, 0.05)',
    borderRadius: 1,
    fontFamily: 'Monaco, Consolas, "Courier New", monospace',
    color: 'inherit',
  } as const;

  return (
    <ReactMarkdown
      remarkPlugins={[remarkGfm]}
      components={{
        // 自定義樣式
        p: ({ children }) => (
          <Typography
            variant="body1"
            component="div"
            sx={{
              margin: 0,
              marginBottom: 1,
              '&:last-child': { marginBottom: 0 },
              color: 'inherit',
              lineHeight: 1.5,
            }}
          >
            {children}
          </Typography>
        ),
        h1: ({ children }) => (
          <Typography
            variant="h5"
            component="h1"
            sx={{
              marginBottom: 1,
              color: 'inherit',
              fontWeight: 600,
            }}
          >
            {children}
          </Typography>
        ),
        h2: ({ children }) => (
          <Typography
            variant="h6"
            component="h2"
            sx={{
              marginBottom: 1,
              color: 'inherit',
              fontWeight: 600,
            }}
          >
            {children}
          </Typography>
        ),
        h3: ({ children }) => (
          <Typography
            variant="subtitle1"
            component="h3"
            sx={{
              marginBottom: 1,
              color: 'inherit',
              fontWeight: 600,
            }}
          >
            {children}
          </Typography>
        ),
        // fenced code block（`pre > code`）整段由 pre 負責：內容直接從 hast node 取，
        // 所以底下的 code renderer 永遠只會收到行內 code，不需要（也無法）自己判斷。
        // 捲動放在 pre 上而不是 code 上 —— 會溢出的是整個區塊，先前那份程式碼把
        // overflow 掛在 code 上，加上判斷失效，長行完全無法橫向捲動。
        pre: ({ node, children }) => {
          const fenced = readFencedCode(node);
          if (!fenced) return <Box component="pre">{children}</Box>;

          // 只有標了語言（`language-xxx`）且該語言有註冊時才上色，其餘原樣輸出。
          // highlight.js 的輸出已做過 HTML escape，未上色的路徑則交給 React 自己 escape。
          const highlighted = highlightToHtml(fenced.text, fenced.className);
          return (
            <Box component="pre" sx={{ margin: '8px 0', overflow: 'auto', maxWidth: '100%' }}>
              <Box
                component="code"
                className={
                  highlighted
                    ? ['hljs', fenced.className].filter(Boolean).join(' ')
                    : fenced.className
                }
                sx={{
                  ...codeSurface,
                  padding: '8px 12px',
                  fontSize: '0.875rem',
                  display: 'block',
                }}
                {...(highlighted ? { dangerouslySetInnerHTML: { __html: highlighted } } : {})}
              >
                {highlighted ? null : fenced.text}
              </Box>
            </Box>
          );
        },
        code: ({ children, className, ...props }) => {
          // eslint-disable-next-line @typescript-eslint/no-unused-vars
          const { ref, key, node, ...cleanProps } = props;
          return (
            <Box
              component="code"
              className={className}
              sx={{
                ...codeSurface,
                padding: '2px 4px',
                fontSize: '0.85em',
                display: 'inline',
              }}
              {...cleanProps}
            >
              {children}
            </Box>
          );
        },
        blockquote: ({ children }) => (
          <Box
            sx={{
              borderLeft: `4px solid ${
                isOwnMessage ? 'rgba(255, 255, 255, 0.3)' : theme.palette.primary.main
              }`,
              paddingLeft: 2,
              margin: '8px 0',
              color: 'inherit',
              fontStyle: 'italic',
            }}
          >
            {children}
          </Box>
        ),
        ul: ({ children }) => (
          <Box component="ul" sx={{ paddingLeft: 2, margin: '8px 0', color: 'inherit' }}>
            {children}
          </Box>
        ),
        ol: ({ children }) => (
          <Box component="ol" sx={{ paddingLeft: 2, margin: '8px 0', color: 'inherit' }}>
            {children}
          </Box>
        ),
        li: ({ children }) => (
          <Typography component="li" sx={{ marginBottom: 0.5, color: 'inherit' }}>
            {children}
          </Typography>
        ),
        strong: ({ children }) => (
          <Typography component="strong" sx={{ fontWeight: 600, color: 'inherit' }}>
            {children}
          </Typography>
        ),
        em: ({ children }) => (
          <Typography component="em" sx={{ fontStyle: 'italic', color: 'inherit' }}>
            {children}
          </Typography>
        ),
        a: ({ href, children }) => (
          <Typography
            component="a"
            href={href}
            target="_blank"
            rel="noopener noreferrer"
            sx={{
              color: isOwnMessage ? 'rgba(255, 255, 255, 0.9)' : theme.palette.primary.main,
              textDecoration: 'underline',
            }}
          >
            {children}
          </Typography>
        ),
        table: ({ children }) => (
          <Box
            sx={{
              overflowX: 'auto',
              margin: '8px 0',
              border: `1px solid ${theme.palette.divider}`,
              borderRadius: 1,
            }}
          >
            <Box component="table" sx={{ width: '100%', borderCollapse: 'collapse' }}>
              {children}
            </Box>
          </Box>
        ),
        thead: ({ children }) => (
          <Box
            component="thead"
            sx={{
              backgroundColor:
                theme.palette.mode === 'dark' ? 'rgba(255, 255, 255, 0.05)' : 'rgba(0, 0, 0, 0.05)',
            }}
          >
            {children}
          </Box>
        ),
        th: ({ children }) => (
          <Box
            component="th"
            sx={{
              padding: 1,
              textAlign: 'left',
              borderBottom: `1px solid ${theme.palette.divider}`,
              fontWeight: 600,
              color: 'inherit',
            }}
          >
            {children}
          </Box>
        ),
        td: ({ children }) => (
          <Box
            component="td"
            sx={{
              padding: 1,
              borderBottom: `1px solid ${theme.palette.divider}`,
              color: 'inherit',
            }}
          >
            {children}
          </Box>
        ),
      }}
    >
      {content}
    </ReactMarkdown>
  );
};

export default MarkdownMessage;
