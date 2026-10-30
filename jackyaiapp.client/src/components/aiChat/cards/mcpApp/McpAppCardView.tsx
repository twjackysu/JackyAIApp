import { Box, Button, CircularProgress, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';

import { hairline } from '../../style';

import { hostContextChanged, messageRefused, stepsFor, toolNotifications } from './appBridge';
import { useAppDraft } from './appDraft';
import { APP_SANDBOX, APP_START_HEIGHT, appCsp, appSrcDoc, frameHeight } from './appFrame';
import {
  BUSY_MESSAGE,
  MAX_CALLS_IN_FLIGHT,
  OPEN_GATE,
  PAUSED_MESSAGE,
  PRESS_GUARD_MS,
  REFUSED_MESSAGE,
  TOO_MANY_CALLS_MESSAGE,
  allowAgain,
  askingAbout,
  decide,
  forNewDocument,
  hold,
  isAllowed,
  isRefused,
  needsReader,
  shownToolName,
  stopAllowing,
} from './appToolGate';
import { line, parseMcpAppCard, resultText } from './mcpAppCard';
import { useOnScreen } from './useOnScreen';

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
  // An allowance is for the app the reader sees: out of sight, its calls of allowed tools pause (D-209).
  const seenRef = useOnScreen(frameRef);
  const readyRef = useRef(false);
  const loadsRef = useRef(0);
  const [height, setHeight] = useState(APP_START_HEIGHT);
  // The app asked to open a link (ui/open-link): one at a time, like ui/message, so it is never swapped under the reader's
  // press (D-232).
  const [offeredLink, setOfferedLinkState] = useState<string | null>(null);
  const offeredLinkRef = useRef<string | null>(null);
  const setOfferedLink = useCallback((url: string | null) => {
    offeredLinkRef.current = url;
    setOfferedLinkState(url);
  }, []);
  // The app asked to put words in the box (ui/message): shown here, in the box only once the reader presses (D-204).
  const [offeredMessage, setOfferedMessageState] = useState<string | null>(null);
  const offeredMessageRef = useRef<string | null>(null);
  const setOfferedMessage = useCallback((text: string | null) => {
    offeredMessageRef.current = text;
    setOfferedMessageState(text);
  }, []);
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
  // How many of this card's calls the server is answering now: at most MAX_CALLS_IN_FLIGHT (D-239).
  const inFlightRef = useRef(0);
  const runCall = useCallback(
    (call: WaitingCall, readerAllowed: boolean) => {
      if (readerAllowed && !seenRef.current) return fail(call.id, PAUSED_MESSAGE);
      if (inFlightRef.current >= MAX_CALLS_IN_FLIGHT) return fail(call.id, TOO_MANY_CALLS_MESSAGE);
      inFlightRef.current += 1;
      const settled = () => {
        inFlightRef.current -= 1;
      };
      callTool({
        installationId: data.installationId,
        tool: call.name,
        arguments: call.arguments,
        readerAllowed,
      })
        .unwrap()
        .then(
          (r) => {
            settled();
            post({ jsonrpc: '2.0', id: call.id, result: r.result });
          },
          (e: unknown) => {
            settled();
            throw e;
          },
        )
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
    [callTool, data.installationId, post, fail, setGate, seenRef],
  );
  // When the question last appeared or the app last changed its size: a press sooner than PRESS_GUARD_MS after is not taken.
  const shiftedAtRef = useRef(0);
  const pressCounts = () => performance.now() - shiftedAtRef.current >= PRESS_GUARD_MS;
  const decideTool = (name: string, allow: boolean) => {
    if (!pressCounts()) return;
    const { gate: next, released } = decide(gateRef.current, name, allow);
    setGate(next);
    for (const call of released) {
      if (allow) runCall(call, true);
      else fail(call.id, REFUSED_MESSAGE);
    }
  };
  const asking = askingAbout(gate);
  // Before the browser paints (a layout effect): the question appears on a message or an answer, not on the reader's
  // input, so a press landing between that paint and a later passive effect would otherwise count (D-239).
  useLayoutEffect(() => {
    if (asking !== null) shiftedAtRef.current = performance.now();
  }, [asking]);
  // 改為允許 appears where 不用 was: a second press of 不用 must not land on it.
  const refusedKey = gate.refused.join('\n');
  useLayoutEffect(() => {
    if (refusedKey) shiftedAtRef.current = performance.now();
  }, [refusedKey]);
  // The document starts in the light or dark of the moment it is drawn; a later change is told, never drawn again, so
  // the app keeps its state and the calls the reader allowed (D-210).
  const [startScheme] = useState(mode);
  const srcDoc = useMemo(() => appSrcDoc(html, csp, startScheme), [html, csp, startScheme]);
  // The light or dark the document's color-scheme has now: the start's, then each one the app was told.
  const schemeRef = useRef(startScheme);
  // A new document is a new app: not ready, in its starting scheme, the old one's calls not answered into it; the
  // reader's decisions stay.
  useEffect(() => {
    readyRef.current = false;
    schemeRef.current = startScheme;
    setGate(forNewDocument(gateRef.current));
  }, [srcDoc, startScheme, setGate]);
  // Told only once it is ready (nothing is sent before `initialized`); it learned the theme of that moment from ui/initialize.
  const tellScheme = useCallback(() => {
    if (!readyRef.current || schemeRef.current === mode) return;
    schemeRef.current = mode;
    post(hostContextChanged(mode));
  }, [mode, post]);
  useEffect(tellScheme, [tellScheme]);

  useEffect(() => {
    const onMessage = (event: MessageEvent) => {
      // Only this frame's app: its origin is opaque ("null"), so the window is what tells it from any other.
      if (!frameRef.current || event.source !== frameRef.current.contentWindow) return;
      for (const step of stepsFor(event.data, { theme: mode, takesMessages })) {
        if (step.type === 'reply') post(step.message);
        else if (step.type === 'ready') {
          if (readyRef.current) continue;
          readyRef.current = true;
          toolNotifications(data.input, data.result).forEach(post);
          tellScheme();
        } else if (step.type === 'resize') {
          const h = frameHeight(step.height);
          if (h !== null) {
            shiftedAtRef.current = performance.now();
            setHeight(h);
          }
        } else if (step.type === 'offerLink') {
          // One at a time, and a press just after it appeared is not taken: the app cannot swap the address, or put a
          // fresh one, under a press meant for another (D-232).
          if (offeredLinkRef.current !== null) post(messageRefused(step.id));
          else {
            shiftedAtRef.current = performance.now();
            setOfferedLink(step.url);
            post({ jsonrpc: '2.0', id: step.id, result: {} });
          }
        } else if (step.type === 'offerMessage') {
          // One at a time: a new one never replaces the text the reader is reading (they could press on what they did not
          // read), and the app is told this one was not shown rather than that it was (D-211). A press just after it
          // appeared is not taken either (D-232).
          if (offeredMessageRef.current !== null) post(messageRefused(step.id));
          else {
            shiftedAtRef.current = performance.now();
            setOfferedMessage(step.text);
            post({ jsonrpc: '2.0', id: step.id, result: {} });
          }
        } else if (step.type === 'callTool') {
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
  }, [
    data,
    srcDoc,
    mode,
    takesMessages,
    post,
    fail,
    runCall,
    setGate,
    tellScheme,
    setOfferedMessage,
    setOfferedLink,
  ]);

  return (
    <Box>
      {/* Above the app, which sets its own height: nothing the app does moves this question (D-207). */}
      {asking !== null && (
        <Box sx={{ mb: 0.75 }} role="status">
          <Typography variant="caption" color="text.secondary" component="div">
            介面想執行「<bdi>{shownToolName(asking)}</bdi>
            」。這個工具沒有標示為只讀取，可能會變更你在「
            {data.connector}」的資料；允許後，這張卡片上的介面再執行它就不再問。
          </Typography>
          <Box sx={{ display: 'flex', gap: 0.5, mt: 0.5 }}>
            <Button size="small" onClick={() => decideTool(asking, true)}>
              允許
            </Button>
            <Button size="small" color="inherit" onClick={() => decideTool(asking, false)}>
              不用
            </Button>
          </Box>
        </Box>
      )}
      {/* Also above the app, and guarded like the question: a press here lets the app run the tool (D-209). */}
      {gate.refused.map((name) => (
        <Box
          key={name}
          sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 0.5, flexWrap: 'wrap' }}
        >
          <Typography variant="caption" color="text.secondary">
            沒有允許介面執行「<bdi>{shownToolName(name)}</bdi>」
          </Typography>
          <Button
            size="small"
            color="inherit"
            onClick={() => {
              if (pressCounts()) setGate(allowAgain(gateRef.current, name));
            }}
          >
            改為允許
          </Button>
        </Box>
      ))}
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
                if (!pressCounts()) return;
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
                if (!pressCounts()) return;
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
