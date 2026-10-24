import type { SiteAccessToken } from '@/apis/connectorsApis/types';

const DAY_MS = 24 * 60 * 60 * 1000;

/** A date as readers here write it (2026/10/24), in their own time zone. */
export const shortDate = (iso: string) => {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}/${pad(d.getMonth() + 1)}/${pad(d.getDate())}`;
};

/** The server's UTC timestamps come without a zone; read them as UTC. */
const time = (iso: string) =>
  new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(iso) ? iso : `${iso}Z`).getTime();

export interface TokenView {
  expired: boolean;
  /** "2026/12/24 到期" / "3 天後到期" / "已過期". */
  expires: string;
  /** "從未使用" / "最後使用 2026/10/24". */
  lastUsed: string;
}

/** What a token's row says about it at `now`. */
export function tokenView(token: SiteAccessToken, now: number): TokenView {
  const left = time(token.expiresAt) - now;
  const expired = left <= 0;
  const expires = expired
    ? '已過期'
    : left < 7 * DAY_MS
      ? `${Math.max(1, Math.ceil(left / DAY_MS))} 天後到期`
      : `${shortDate(token.expiresAt)} 到期`;
  const lastUsed = token.lastUsedAt ? `最後使用 ${shortDate(token.lastUsedAt)}` : '從未使用';
  return { expired, expires, lastUsed };
}

/** The lifetimes the server takes, in days, with what the reader is shown. */
export const LIFETIMES = [
  { days: 30, label: '30 天' },
  { days: 90, label: '90 天' },
  { days: 365, label: '1 年' },
] as const;
