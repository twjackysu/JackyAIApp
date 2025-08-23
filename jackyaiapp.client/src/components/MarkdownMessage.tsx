import { Box, Typography } from '@mui/material';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import rehypeHighlight from 'rehype-highlight';

interface MarkdownMessageProps {
  content: string;
  isOwnMessage: boolean;
  theme: any;
}

const MarkdownMessage = ({ content, isOwnMessage, theme }: MarkdownMessageProps) => {
  return (
    <ReactMarkdown
      remarkPlugins={[remarkGfm]}
      rehypePlugins={[rehypeHighlight]}
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
        code: ({ inline, children, ...props }) => (
          <Box
            component={inline ? 'code' : 'pre'}
            sx={{
              backgroundColor: isOwnMessage 
                ? 'rgba(255, 255, 255, 0.1)' 
                : theme.palette.mode === 'dark' 
                  ? 'rgba(255, 255, 255, 0.05)' 
                  : 'rgba(0, 0, 0, 0.05)',
              padding: inline ? '2px 4px' : '8px 12px',
              borderRadius: 1,
              fontFamily: 'Monaco, Consolas, "Courier New", monospace',
              fontSize: inline ? '0.85em' : '0.875rem',
              display: inline ? 'inline' : 'block',
              margin: inline ? 0 : '8px 0',
              overflow: inline ? 'visible' : 'auto',
              color: 'inherit',
            }}
            {...props}
          >
            {children}
          </Box>
        ),
        blockquote: ({ children }) => (
          <Box
            sx={{
              borderLeft: `4px solid ${isOwnMessage 
                ? 'rgba(255, 255, 255, 0.3)' 
                : theme.palette.primary.main}`,
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
              color: isOwnMessage 
                ? 'rgba(255, 255, 255, 0.9)' 
                : theme.palette.primary.main,
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
              backgroundColor: theme.palette.mode === 'dark' 
                ? 'rgba(255, 255, 255, 0.05)' 
                : 'rgba(0, 0, 0, 0.05)' 
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