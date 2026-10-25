import { api } from '@/apis/api';
import { portfolioApis } from '@/apis/portfolioApis';

type RefreshAction =
  ReturnType<typeof api.util.invalidateTags> | ReturnType<typeof portfolioApis.util.invalidateTags>;

/**
 * What to read again once one of Studio AI's site tools changed something on the server (the server's names,
 * `SiteToolSet`): the page under the drawer — the portfolio, the repository, the review queue — then shows the
 * change at once, as if the reader had made it there. A tool that changed nothing the pages show refreshes nothing.
 */
export function refreshAfterTool(tool: string): RefreshAction[] {
  switch (tool) {
    case 'update_holding':
      return [portfolioApis.util.invalidateTags(['Holdings', 'Summary'])];
    case 'add_word_to_repository':
      // What adding a word on the repository page refreshes (repositoryApis).
      return [api.util.invalidateTags(['PersonalWord', 'DueReviews', 'DueCount', 'LearningStats'])];
    default:
      return [];
  }
}
