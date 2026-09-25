import { act, useCallback, useState } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// eslint-disable-next-line @typescript-eslint/no-explicit-any -- react-dom/test-utils' documented global flag, no typed export
(globalThis as any).IS_REACT_ACT_ENVIRONMENT = true;

import {
  useFinanceAnalysis,
  shouldShowComprehensiveError,
  TAB_QUANTITATIVE,
  TAB_COMPARISON,
} from './useFinanceAnalysis';

// `useFinanceAnalysis` calls RTK Query hooks that normally need a real Redux store. Mocked here
// so the hook can run standalone; `useGetComprehensiveAnalysisMutation` is faithfully reproduced
// (not stubbed away) because the bug under test — TASK-016 — is specifically about RTK Query's
// real "one shared trigger reflects only the last settled call's error/isLoading" behavior.
const { comprehensiveImplRef } = vi.hoisted(() => {
  const ref: { current: (arg: unknown) => Promise<unknown> } = { current: async () => ({}) };
  return { comprehensiveImplRef: ref };
});

vi.mock('@/apis/accountApis', () => ({
  useGetUserInfoQuery: () => ({ data: undefined }),
}));

vi.mock('@/apis/financeApis', () => ({
  useGetMarketDigestQuery: () => ({
    data: undefined,
    isLoading: false,
    isError: false,
    error: undefined,
    refetch: vi.fn(),
    isFetching: false,
  }),
  useAnalyzeStockMutation: () => [vi.fn(), { isLoading: false, error: undefined, reset: vi.fn() }],
  useGetComprehensiveAnalysisMutation: () => {
    const [state, setState] = useState<{ isLoading: boolean; error: unknown }>({
      isLoading: false,
      error: undefined,
    });
    const trigger = useCallback((arg: unknown) => {
      setState({ isLoading: true, error: undefined });
      const promise = comprehensiveImplRef
        .current(arg)
        .then((data: unknown) => {
          setState({ isLoading: false, error: undefined });
          return { data };
        })
        .catch((err: unknown) => {
          setState({ isLoading: false, error: err });
          throw err;
        });
      return { unwrap: () => promise };
    }, []);
    const reset = useCallback(() => setState({ isLoading: false, error: undefined }), []);
    return [trigger, { ...state, reset }];
  },
}));

function Harness({ onReady }: { onReady: (hook: ReturnType<typeof useFinanceAnalysis>) => void }) {
  onReady(useFinanceAnalysis());
  return null;
}

describe('useFinanceAnalysis — comparison tab error isolation (TASK-016)', () => {
  let container: HTMLDivElement;
  let root: Root;
  let latest: ReturnType<typeof useFinanceAnalysis>;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.appendChild(container);
    root = createRoot(container);
  });

  afterEach(() => {
    act(() => root.unmount());
    container.remove();
    vi.clearAllMocks();
  });

  it('a failing comparison code sets the shared comprehensiveError while still on the comparison tab, but the quantitative-tab Alert condition stays false', async () => {
    comprehensiveImplRef.current = async (arg: unknown) => {
      const { stockCode } = arg as { stockCode: string };
      if (stockCode === 'BADCODE') {
        throw new Error('找不到股票代碼');
      }
      return { stockCode };
    };

    act(() => {
      root.render(
        <MemoryRouter>
          <Harness
            onReady={(hook) => {
              latest = hook;
            }}
          />
        </MemoryRouter>,
      );
    });

    act(() => {
      latest.handleTabChange(null, TAB_COMPARISON);
    });
    act(() => {
      latest.setComparisonInput('BADCODE');
    });
    act(() => {
      latest.handleAddComparisonCode();
    });
    await act(async () => {
      await latest.runComparison();
    });

    // The underlying RTK Query race actually happened: the shared mutation hook's error is set...
    expect(latest.analysisTab).toBe(TAB_COMPARISON);
    expect(latest.comprehensiveError).toBeTruthy();
    // ...but the comparison tab's own per-slot error tracking is what should be trusted here.
    expect(latest.comparisonSlots).toEqual([
      expect.objectContaining({ code: 'BADCODE', status: 'error' }),
    ]);

    // This is the actual regression guard: without TASK-016's fix, this reads `true` on the
    // comparison tab too (Finance/index.tsx:268 used to render on any tab).
    expect(
      shouldShowComprehensiveError(
        latest.analysisTab,
        latest.comprehensiveError,
        latest.isComprehensiveLoading,
      ),
    ).toBe(false);

    // The Alert must still work correctly on the tab it actually belongs to — no regression.
    expect(
      shouldShowComprehensiveError(
        TAB_QUANTITATIVE,
        latest.comprehensiveError,
        latest.isComprehensiveLoading,
      ),
    ).toBe(true);
  });

  it('switching from a failed quantitative search into the comparison tab clears the stale error', async () => {
    comprehensiveImplRef.current = async () => {
      throw new Error('找不到股票代碼');
    };

    act(() => {
      root.render(
        <MemoryRouter>
          <Harness
            onReady={(hook) => {
              latest = hook;
            }}
          />
        </MemoryRouter>,
      );
    });

    await act(async () => {
      latest.setStockSearchTerm('2330');
    });
    await act(async () => {
      await latest.handleStockSearch();
    });

    expect(latest.comprehensiveError).toBeTruthy();
    expect(
      shouldShowComprehensiveError(
        latest.analysisTab,
        latest.comprehensiveError,
        latest.isComprehensiveLoading,
      ),
    ).toBe(true);

    act(() => {
      latest.handleTabChange(null, TAB_COMPARISON);
    });

    expect(latest.comprehensiveError).toBeFalsy();
    expect(
      shouldShowComprehensiveError(
        latest.analysisTab,
        latest.comprehensiveError,
        latest.isComprehensiveLoading,
      ),
    ).toBe(false);
  });
});
