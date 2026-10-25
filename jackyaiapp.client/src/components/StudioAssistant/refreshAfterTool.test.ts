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

  it('reads what Studio AI remembers again after it kept or forgot a note', () => {
    expect(tagsOf('remember')).toEqual([{ type: 'api/invalidateTags', tags: ['AssistantMemory'] }]);
    expect(tagsOf('forget')).toEqual([{ type: 'api/invalidateTags', tags: ['AssistantMemory'] }]);
  });

  it('refreshes nothing after a read', () => {
    expect(refreshAfterTool('portfolio_summary')).toEqual([]);
    expect(refreshAfterTool('look_up_word')).toEqual([]);
  });
});
