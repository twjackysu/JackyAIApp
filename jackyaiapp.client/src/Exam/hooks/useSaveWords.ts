import { useCallback, useState } from 'react';

import { usePutRepositoryWordMutation } from '@/apis/repositoryApis';

export type SaveStatus = 'saving' | 'saved' | 'error';

/** The backend answers 400 "already been added" when the word is already in the user's repository. */
function isAlreadyInRepository(error: unknown): boolean {
  return typeof error === 'object' && error !== null && 'status' in error && error.status === 400;
}

/**
 * Adds words to the user's repository and tracks per-word status. Reuses the existing PUT
 * mutation (which invalidates the repository cache). A word that is already in the
 * repository counts as saved rather than as a failure.
 */
export function useSaveWords() {
  const [putRepositoryWord] = usePutRepositoryWordMutation();
  const [statuses, setStatuses] = useState<Record<string, SaveStatus>>({});

  const save = useCallback(
    async (wordId: string): Promise<boolean> => {
      setStatuses((prev) => ({ ...prev, [wordId]: 'saving' }));
      try {
        await putRepositoryWord(wordId).unwrap();
        setStatuses((prev) => ({ ...prev, [wordId]: 'saved' }));
        return true;
      } catch (error) {
        const ok = isAlreadyInRepository(error);
        setStatuses((prev) => ({ ...prev, [wordId]: ok ? 'saved' : 'error' }));
        return ok;
      }
    },
    [putRepositoryWord],
  );

  return { statuses, save };
}
