import { describe, expect, it } from 'vitest';

import { refreshAfterTool } from './refreshAfterTool';

const tagsOf = (tool: string) =>
  refreshAfterTool(tool).map((a) => ({ type: a.type, tags: a.payload }));

describe('refreshAfterTool', () => {
  it('reads the portfolio again after a holding was updated', () => {
    expect(tagsOf('update_holding')).toEqual([
      { type: 'portfolioApis/invalidateTags', tags: ['Holdings', 'Summary'] },
    ]);
  });

  it('reads the repository and the review queue again after a word was added', () => {
    expect(tagsOf('add_word_to_repository')).toEqual([
      {
        type: 'api/invalidateTags',
        tags: ['PersonalWord', 'DueReviews', 'DueCount', 'LearningStats'],
      },
    ]);
  });

  it('refreshes nothing after a read', () => {
    expect(refreshAfterTool('portfolio_summary')).toEqual([]);
    expect(refreshAfterTool('look_up_word')).toEqual([]);
  });
});
