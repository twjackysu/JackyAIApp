import type { ToolEffect, ToolPermission } from '@/apis/connectorsApis/types';

/*
 * What the AI may do with one connector tool (docs/ai-native/design/03 §2.2): run it on its own,
 * ask the reader first, or not see it at all. The server holds the rule (and has the last word);
 * the page offers only what it would accept.
 */

export type { ToolEffect, ToolPermission };

export const PERMISSION_LABELS: Record<ToolPermission, string> = {
  auto: '自動',
  confirm: '需確認',
  disabled: '停用',
};

export const EFFECT_LABELS: Record<ToolEffect, string> = {
  read: '讀取',
  write: '寫入',
  destructive: '無法復原',
};

/**
 * The choices a tool's permission can take: one that cannot be undone never runs unasked, nor one the server says
 * always asks (`alwaysAsks`: what it writes steers every later conversation, D-101).
 */
export const allowedPermissions = (effect: ToolEffect, alwaysAsks = false): ToolPermission[] =>
  effect === 'destructive' || alwaysAsks
    ? ['confirm', 'disabled']
    : ['auto', 'confirm', 'disabled'];

/**
 * A tool's permission before the reader changes it: reading runs on its own (for an outside MCP
 * tool only when the server marks it read-only), anything else asks first.
 */
export const defaultPermission = (effect: ToolEffect): ToolPermission =>
  effect === 'read' ? 'auto' : 'confirm';

/** How many of the tools have each permission, for a connector's card. */
export const permissionCounts = (
  permissions: readonly ToolPermission[],
): Record<ToolPermission, number> => {
  const counts: Record<ToolPermission, number> = { auto: 0, confirm: 0, disabled: 0 };
  for (const p of permissions) counts[p] += 1;
  return counts;
};
