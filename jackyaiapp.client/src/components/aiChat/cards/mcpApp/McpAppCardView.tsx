import { Box, Button, CircularProgress, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { useEffect, useMemo, useRef, useState } from 'react';

import { hairline } from '../../style';

import { stepsFor, toolNotifications } from './appBridge';
import { APP_SANDBOX, APP_START_HEIGHT, appCsp, appSrcDoc, frameHeight } from './appFrame';
import { line, parseMcpAppCard, resultText } from './mcpAppCard';

import type { McpAppCardData } from './mcpAppCard';
import type { CustomCard } from '../toolCard';

import { useCallFromMcpAppMutation, useGetMcpAppQuery } from '@/apis/connectorsApis/connectorsApis';
import { getApiErrorMessage, getCaughtErrorMessage } from '@/apis/utils/errorMessage';

/**
 * A connector's MCP App in an answer (docs/ai-native/design/09, D-201): the server's own interface for the tool's
 * result, in a sandboxed frame, under a line naming whose it is (the app cannot draw over it). Without the app (it
 * cannot be read, or is still loading) the result's text stands in.
 */
export default function McpAppCardView({ card }: { card: CustomCard; saved?: boolean }) {
  const data = useMemo(() => parseMcpAppCard(card), [card]);
  return data ? <McpAppCard data={data} /> : null;
}

function McpAppCard({ data }: { data: McpAppCardData }) {
  const theme = useTheme();
  // The app navigated its frame away from what the site wrote (its CSP gone with it): drawn as text from then on.
  const [left, setLeft] = useState(false);
  const {
    data: app,
    error,
    isLoading,
  } = useGetMcpAppQuery({
    installationId: data.installationId,
    uri: data.resourceUri,
  });

  return (
    <Box data-testid="mcp-app-card">
      {/* Whose it is first, the tool's own title last and isolated: the title is the server's words. */}
      <Typography variant="caption" color="text.secondary" component="div" sx={{ mb: 0.75 }}>
        「{line(app?.connector, 60) ?? data.connector}」的介面{app ? ` · 來自 ${app.host}` : ''} ·{' '}
        <bdi>{data.title}</bdi>
      </Typography>
      {app && !left ? (
        <AppFrame
          key={theme.palette.mode}
          onLeft={() => setLeft(true)}
          data={data}
          html={app.html}
          csp={appCsp(app.connectDomains, app.resourceDomains)}
          border={app.prefersBorder !== false}
        />
      ) : (
        <Box>
          {isLoading && (
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 0.5 }}>
              <CircularProgress size={14} />
              <Typography variant="caption" color="text.secondary">
                正在載入介面…
              </Typography>
            </Box>
          )}
          {error && (
            <Typography variant="caption" color="text.secondary" component="div" sx={{ mb: 0.5 }}>
              介面沒有載入：{getApiErrorMessage(error)}
            </Typography>
          )}
          {left && (
            <Typography variant="caption" color="text.secondary" component="div" sx={{ mb: 0.5 }}>
              這個介面想換成別的網頁，已停止顯示。
            </Typography>
          )}
          {resultText(data.result) && (
            <Typography
              variant="body2"
              sx={{
                whiteSpace: 'pre-wrap',
                border: `1px solid ${hairline(theme)}`,
                borderRadius: 1,
                p: 1,
              }}
            >
              {resultText(data.result)}
            </Typography>
          )}
        </Box>
      )}
    </Box>
  );
}

function AppFrame({
  data,
  html,
  csp,
  border,
  onLeft,
}: {
  data: McpAppCardData;
  html: string;
  csp: string;
  border: boolean;
  onLeft: () => void;
}) {
  const theme = useTheme();
  const mode = theme.palette.mode;
  const frameRef = useRef<HTMLIFrameElement>(null);
  const readyRef = useRef(false);
  const loadsRef = useRef(0);
  const [height, setHeight] = useState(APP_START_HEIGHT);
  const [offeredLink, setOfferedLink] = useState<string | null>(null);
  const [callTool] = useCallFromMcpAppMutation();
  // A change of light or dark draws the app again (the scheme is the document's); it is told the theme as it starts.
  const srcDoc = useMemo(() => appSrcDoc(html, csp, mode), [html, csp, mode]);

  useEffect(() => {
    readyRef.current = false;
    const post = (message: unknown) => frameRef.current?.contentWindow?.postMessage(message, '*');
    const onMessage = (event: MessageEvent) => {
      // Only this frame's app: its origin is opaque ("null"), so the window is what tells it from any other.
      if (!frameRef.current || event.source !== frameRef.current.contentWindow) return;
      for (const step of stepsFor(event.data, { theme: mode })) {
        if (step.type === 'reply') post(step.message);
        else if (step.type === 'ready') {
          if (readyRef.current) continue;
          readyRef.current = true;
          toolNotifications(data.input, data.result).forEach(post);
        } else if (step.type === 'resize') {
          const h = frameHeight(step.height);
          if (h !== null) setHeight(h);
        } else if (step.type === 'offerLink') setOfferedLink(step.url);
        else if (step.type === 'callTool') {
          // The server decides whether the app may (a tool that only reads and runs on its own); the app gets its answer.
          const { id, name } = step;
          callTool({ installationId: data.installationId, tool: name, arguments: step.arguments })
            .unwrap()
            .then((r) => post({ jsonrpc: '2.0', id, result: r.result }))
            .catch((e: unknown) =>
              post({
                jsonrpc: '2.0',
                id,
                error: { code: -32000, message: getCaughtErrorMessage(e) },
              }),
            );
        }
      }
    };
    window.addEventListener('message', onMessage);
    return () => window.removeEventListener('message', onMessage);
  }, [data, srcDoc, mode, callTool]);

  return (
    <Box>
      <Box
        component="iframe"
        ref={frameRef}
        title={`${data.title}（${data.connector}）`}
        sandbox={APP_SANDBOX}
        srcDoc={srcDoc}
        // A srcdoc frame loads once; a second load is the app going somewhere else, with none of our CSP.
        onLoad={() => {
          loadsRef.current += 1;
          if (loadsRef.current > 1) onLeft();
        }}
        referrerPolicy="no-referrer"
        sx={{
          display: 'block',
          width: '100%',
          height,
          border: border ? `1px solid ${hairline(theme)}` : 'none',
          borderRadius: 1,
          bgcolor: 'transparent',
          colorScheme: mode,
        }}
      />
      {offeredLink && (
        <Box
          sx={{ display: 'flex', alignItems: 'center', gap: 1, mt: 0.75, flexWrap: 'wrap' }}
          role="status"
        >
          <Typography variant="caption" color="text.secondary">
            介面想開啟 {new URL(offeredLink).host}
          </Typography>
          <Box sx={{ display: 'flex', gap: 0.5 }}>
            <Button
              size="small"
              onClick={() => {
                window.open(offeredLink, '_blank', 'noopener,noreferrer');
                setOfferedLink(null);
              }}
            >
              開啟
            </Button>
            <Button size="small" color="inherit" onClick={() => setOfferedLink(null)}>
              不用
            </Button>
          </Box>
        </Box>
      )}
    </Box>
  );
}
