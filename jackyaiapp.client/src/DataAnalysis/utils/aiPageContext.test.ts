import { describe, expect, it } from 'vitest';

import { dataAnalysisPageContext } from './aiPageContext';

describe('dataAnalysisPageContext', () => {
  it('names an uploaded file by its session only', () => {
    expect(
      dataAnalysisPageContext({ sourceId: 's1', tableName: 'dbo.Orders', sessionId: 'x' }),
    ).toEqual({
      sessionId: 'x',
    });
  });

  it('names a saved source and its table', () => {
    expect(dataAnalysisPageContext({ sourceId: 's1', tableName: 'dbo.Orders' })).toEqual({
      sourceId: 's1',
      tableName: 'dbo.Orders',
    });
  });

  it('is empty for the default source', () => {
    expect(dataAnalysisPageContext({ sourceId: null, tableName: undefined })).toEqual({});
  });
});
