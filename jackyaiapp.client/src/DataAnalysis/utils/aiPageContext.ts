/**
 * What the Data Analysis page tells its assistant about the data on screen
 * (`AiChatConfig.pageContext`; the server reads it as `DataAnalysisPage`): an uploaded file's
 * session, or else the source and table being queried. Nothing at all means the built-in default
 * source, as for the page's own queries.
 */
export function dataAnalysisPageContext({
  sourceId,
  tableName,
  sessionId,
}: {
  sourceId?: string | null;
  tableName?: string | null;
  sessionId?: string | null;
}): Record<string, string> {
  if (sessionId) return { sessionId };
  const context: Record<string, string> = {};
  if (sourceId) context.sourceId = sourceId;
  if (tableName) context.tableName = tableName;
  return context;
}
