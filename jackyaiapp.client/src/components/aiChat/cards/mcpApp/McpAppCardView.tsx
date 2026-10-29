import { Box, Button, CircularProgress, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';

import { hairline } from '../../style';

import { stepsFor, toolNotifications } from './appBridge';
import { useAppDraft } from './appDraft';
import { APP_SANDBOX, APP_START_HEIGHT, appCsp, appSrcDoc, frameHeight } from './appFrame';
import {
  BUSY_MESSAGE,
  OPEN_GATE,
  REFUSED_MESSAGE,
  askingAbout,
  decide,
  hold,
  isAllowed,
  isRefused,
  needsReader,
  shownToolName,
  stopAllowing,
} from './appToolGate';
import { line, parseMcpAppCard, resultText } from './mcpAppCard';

import type { ToolGate, WaitingCall } from './appToolGate';
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
  // The app asked to put words in the box (ui/message): shown here, in the box only once the reader presses (D-204).
  const [offeredMessage, setOfferedMessage] = useState<string | null>(null);
  const putInDraft = useAppDraft();
  const takesMessages = putInDraft !== null;
  const [callTool] = useCallFromMcpAppMutation();
  // The tools for the app alone the reader allowed or refused on this card, and the calls waiting on them (D-207).
  const [gate, setGateState] = useState<ToolGate>(OPEN_GATE);
  const gateRef = useRef<ToolGate>(OPEN_GATE);
  const setGate = useCallback((next: ToolGate) => {
    gateRef.current = next;
    setGateState(next);
  }, []);
  const post = useCallback(
    (message: unknown) => frameRef.current?.contentWindow?.postMessage(message, '*'),
    [],
  );
  const fail = useCallback(
    (id: string | number, message: string) =>
      post({ jsonrpc: '2.0', id, error: { code: -32000, message } }),
    [post],
  );
  // The server decides whether the app may (a tool that only reads and runs on its own, or one for the app alone the reader
  // allowed here); the app gets its answer. One the server says waits on the reader is held, for the card to ask.
  const runCall = useCallback(
    (call: WaitingCall, readerAllowed: boolean) => {
      callTool({
        installationId: data.installationId,
        tool: call.name,
        arguments: call.arguments,
        readerAllowed,
      })
        .unwrap()
        .then((r) => post({ jsonrpc: '2.0', id: call.id, result: r.result }))
        .catch((e: unknown) => {
          if (!readerAllowed && needsReader(e)) {
            const current = gateRef.current;
            if (isAllowed(current, call.name)) return runCall(call, true);
            if (isRefused(current, call.name)) return fail(call.id, REFUSED_MESSAGE);
            const held = hold(current, call);
            if (held) setGate(held);
            else fail(call.id, BUSY_MESSAGE);
            return;
          }
          fail(call.id, getCaughtErrorMessage(e));
        });
    },
    [callTool, data.installationId, post, fail, setGate],
  );
  const decideTool = (allow: boolean) => {
    const { gate: next, released } = decide(gateRef.current, allow);
    setGate(next);
    for (const call of released) {
      if (allow) runCall(call, true);
      else fail(call.id, REFUSED_MESSAGE);
    }
  };
  const asking = askingAbout(gate);
  // A change of light or dark draws the app again (the scheme is the document's); it is told the theme as it starts.
  const srcDoc = useMemo(() => appSrcDoc(html, csp, mode), [html, csp, mode]);

  useEffect(() => {
    readyRef.current = false;
    const onMessage = (event: MessageEvent) => {
      // Only this frame's app: its origin is opaque ("null"), so the window is what tells it from any other.
      if (!frameRef.current || event.source !== frameRef.current.contentWindow) return;
      for (const step of stepsFor(event.data, { theme: mode, takesMessages })) {
        if (step.type === 'reply') post(step.message);
        else if (step.type === 'ready') {
          if (readyRef.current) continue;
          readyRef.current = true;
          toolNotifications(data.input, data.result).forEach(post);
        } else if (step.type === 'resize') {
          const h = frameHeight(step.height);
          if (h !== null) setHeight(h);
        } else if (step.type === 'offerLink') setOfferedLink(step.url);
        else if (step.type === 'offerMessage')
          // One at a time: a new one never replaces the text the reader is reading (they could press on what they did not read).
          setOfferedMessage((shown) => shown ?? step.text);
        else if (step.type === 'callTool') {
          const call = { id: step.id, name: step.name, arguments: step.arguments };
          const current = gateRef.current;
          if (isRefused(current, call.name)) fail(call.id, REFUSED_MESSAGE);
          else if (isAllowed(current, call.name)) runCall(call, true);
          else if (current.waiting.some((c) => c.name === call.name)) {
            // Already being asked about: this one waits for the same answer.
            const held = hold(current, call);
            if (held) setGate(held);
            else fail(call.id, BUSY_MESSAGE);
          } else runCall(call, false);
        }
      }
    };
    window.addEventListener('message', onMessage);
    return () => window.removeEventListener('message', onMessage);
  }, [data, srcDoc, mode, takesMessages, post, fail, runCall, setGate]);

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
      {asking !== null && (
        <Box sx={{ mt: 0.75 }} role="status">
          <Typography variant="caption" color="text.secondary" component="div">
            介面想執行「<bdi>{shownToolName(asking)}</bdi>
            」。這個工具沒有標示為只讀取，可能會變更你在「
            {data.connector}」的資料；允許後，這張卡片上的介面再執行它就不再問。
          </Typography>
          <Box sx={{ display: 'flex', gap: 0.5, mt: 0.5 }}>
            <Button size="small" onClick={() => decideTool(true)}>
              允許
            </Button>
            <Button size="small" color="inherit" onClick={() => decideTool(false)}>
              不用
            </Button>
          </Box>
        </Box>
      )}
      {gate.allowed.map((name) => (
        <Box
          key={name}
          sx={{ display: 'flex', alignItems: 'center', gap: 1, mt: 0.5, flexWrap: 'wrap' }}
        >
          <Typography variant="caption" color="text.secondary">
            已允許介面執行「<bdi>{shownToolName(name)}</bdi>」
          </Typography>
          <Button
            size="small"
            color="inherit"
            onClick={() => setGate(stopAllowing(gateRef.current, name))}
          >
            停止允許
          </Button>
        </Box>
      ))}
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
      {offeredMessage && putInDraft && (
        <Box sx={{ mt: 0.75 }} role="status">
          <Typography variant="caption" color="text.secondary" component="div">
            介面想把這段話放進輸入框（由你決定要不要送出）：
          </Typography>
          <Typography
            variant="body2"
            component="div"
            sx={{
              whiteSpace: 'pre-wrap',
              overflowWrap: 'anywhere',
              maxHeight: 120,
              overflowY: 'auto',
              borderLeft: `3px solid ${hairline(theme)}`,
              pl: 1,
              my: 0.5,
            }}
          >
            {offeredMessage}
          </Typography>
          <Box sx={{ display: 'flex', gap: 0.5 }}>
            <Button
              size="small"
              onClick={() => {
                putInDraft(offeredMessage);
                setOfferedMessage(null);
              }}
            >
              放進輸入框
            </Button>
            <Button size="small" color="inherit" onClick={() => setOfferedMessage(null)}>
              不用
            </Button>
          </Box>
        </Box>
      )}
    </Box>
  );
}
