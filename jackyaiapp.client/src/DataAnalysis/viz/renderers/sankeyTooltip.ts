import { escapeHtml } from '../../../components/HtmlViewer/escapeHtml';

/** Strips the level prefix (`"1␟Asia"` → `"Asia"`) for display. */
export const stripLevel = (name: string, sep: string): string => name.split(sep).pop() ?? name;

/**
 * A node's or a flow's tooltip. ECharts writes a function formatter's string as HTML: the names are the data's own (a
 * source anyone may fill, a dashboard anyone may be shown), so each is escaped.
 */
export const sankeyTooltip = (p: unknown, sep: string): string => {
  const params = p as {
    dataType?: string;
    name?: string;
    data?: { source?: string; target?: string };
    value?: number;
  };
  if (params.dataType === 'edge' && params.data) {
    return `${escapeHtml(stripLevel(params.data.source ?? '', sep))} → ${escapeHtml(stripLevel(params.data.target ?? '', sep))}: ${escapeHtml(String(params.value))}`;
  }
  return escapeHtml(stripLevel(params.name ?? '', sep));
};
