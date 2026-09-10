import { driver, type Config, type DriveStep } from 'driver.js';
import 'driver.js/dist/driver.css';
import { useEffect, useRef } from 'react';

import { readString, writeString } from '@/utils/storage';

// Three separate tours, each with its own once-only flag:
// - empty: brand-new user with zero holdings — the main page is blank, so all we can usefully
//   show is where to start (the 建立持倉 button). Runs on first visit with no data.
// - editor: first time the holdings dialog opens — walks each asset section + the snapshot
//   concept. This is where a new user actually needs guidance.
// - main: the dashboard walkthrough (net worth / allocation / chart / positions) — only
//   meaningful once holdings exist, so it fires on the first visit WITH data (i.e. typically
//   the visit right after the user saves their first snapshot).
const SEEN_KEYS = {
  empty: 'portfolio_tour_empty_seen_v1',
  editor: 'portfolio_tour_editor_seen_v1',
  main: 'portfolio_tour_seen_v1',
} as const;

type TourKind = keyof typeof SEEN_KEYS;

// Elements are targeted via data-tour attributes (see index.tsx/HoldingsEditor.tsx) rather than
// MUI classnames — those are generated/unstable, data-tour is a stable contract this file owns.
const EMPTY_STEPS: DriveStep[] = [
  {
    element: '[data-tour="portfolio-tour-empty"]',
    popover: {
      title: '歡迎使用 Portfolio',
      description:
        '這裡用「持倉快照」追蹤你的總資產：股票、現金、基金、負債、不動產，統一換算成台幣淨值。現在還沒有任何紀錄。',
      side: 'top',
      align: 'center',
    },
  },
  {
    element: '[data-tour="portfolio-tour-edit"]',
    popover: {
      title: '從這裡開始',
      description: '點「建立持倉」輸入你目前的資產。存檔後就能看到淨值、配置、走勢等完整儀表板。',
      side: 'bottom',
      align: 'end',
    },
  },
];

const EDITOR_STEPS: DriveStep[] = [
  {
    element: '[data-tour="portfolio-tour-editor-date"]',
    popover: {
      title: '生效日期',
      description:
        '這批持倉從這天起算。這是「快照」模式：記錄某天你手上有什麼，不是逐筆交易紀錄；下次資產有變動再存一份新的即可。',
      side: 'bottom',
      align: 'start',
    },
  },
  {
    element: '[data-tour="portfolio-tour-editor-tw"]',
    popover: {
      title: '台股',
      description:
        '輸入代號（如 2330）會自動帶出名稱與現價，填股數即可；成本可填可不填（填了才有損益）。',
      side: 'bottom',
      align: 'start',
    },
  },
  {
    element: '[data-tour="portfolio-tour-editor-us"]',
    popover: {
      title: '美股',
      description: '同台股，輸入代號（如 AAPL）自動帶出資訊，以美元計價、自動換算台幣。',
      side: 'bottom',
      align: 'start',
    },
  },
  {
    element: '[data-tour="portfolio-tour-editor-cash"]',
    popover: {
      title: '現金',
      description: '活存、定存等，可分幣別（TWD/USD）記錄。',
      side: 'top',
      align: 'start',
    },
  },
  {
    element: '[data-tour="portfolio-tour-editor-liability"]',
    popover: {
      title: '負債',
      description:
        '信貸、房貸等欠款，會從淨值中扣除。注意：若是房貸，建議連同下方「不動產」一起記，曝險比例才不會失真。',
      side: 'top',
      align: 'start',
    },
  },
  {
    element: '[data-tour="portfolio-tour-editor-realestate"]',
    popover: {
      title: '不動產',
      description: '自住房等，手動估值（市價或買價扣折舊）。計入淨值但不算市場曝險。',
      side: 'top',
      align: 'start',
    },
  },
  {
    element: '[data-tour="portfolio-tour-editor-fund"]',
    popover: {
      title: '基金',
      description: '搜尋基金名稱（基富通資料），填單位數，淨值會自動更新。',
      side: 'top',
      align: 'start',
    },
  },
  {
    element: '[data-tour="portfolio-tour-editor-save"]',
    popover: {
      title: '存成快照',
      description:
        '填好後按這裡儲存。同一天重複儲存會覆蓋當天的紀錄，不同天則各自保留、形成淨值歷史。',
      side: 'top',
      align: 'end',
    },
  },
];

const MAIN_STEPS: DriveStep[] = [
  {
    element: '[data-tour="portfolio-tour-networth"]',
    popover: {
      title: '總資產淨值',
      description: '這裡顯示你的淨值、單日/未實現/含息損益，以及年化報酬率(XIRR)。',
      side: 'bottom',
      align: 'start',
    },
  },
  {
    element: '[data-tour="portfolio-tour-allocation"]',
    popover: {
      title: '資產配置與市場曝險',
      description:
        '各類資產（台股/美股/現金/基金/不動產）佔比，以及槓桿曝險比例——正2 ETF 曝險 ×2，債券/現金/負債/不動產算 0。',
      side: 'left',
      align: 'start',
    },
  },
  {
    element: '[data-tour="portfolio-tour-chart"]',
    popover: {
      title: '資產淨值走勢',
      description: '可切換資產類型/產業/子類別/個股明細等分組方式，觀察淨值隨時間的變化。',
      side: 'top',
      align: 'start',
    },
  },
  {
    element: '[data-tour="portfolio-tour-positions"]',
    popover: {
      title: '持倉明細',
      description: '目前所有持倉的細項，點擊個股可看更多資訊（走勢、股息等）。',
      side: 'top',
      align: 'start',
    },
  },
  {
    element: '[data-tour="portfolio-tour-edit"]',
    popover: {
      title: '更新持倉',
      description: '之後資產有變動（買賣、入帳），就再從這裡更新一份新的快照，淨值走勢會自動累積。',
      side: 'bottom',
      align: 'end',
    },
  },
];

const STEPS: Record<TourKind, DriveStep[]> = {
  empty: EMPTY_STEPS,
  editor: EDITOR_STEPS,
  main: MAIN_STEPS,
};

function targetsPresent(steps: DriveStep[]): boolean {
  return steps.every((s) => typeof s.element === 'string' && document.querySelector(s.element));
}

function createTourDriver(kind: TourKind, extra?: Partial<Config>) {
  return driver({
    showProgress: true,
    allowClose: true,
    overlayOpacity: 0.65,
    nextBtnText: '下一步',
    prevBtnText: '上一步',
    doneBtnText: '完成',
    steps: STEPS[kind],
    // Fires on both "完成" and the close (skip) button — either way the user has seen it once,
    // don't auto-launch again.
    onDestroyed: () => {
      writeString(SEEN_KEYS[kind], '1');
    },
    ...extra,
  });
}

function launchOnce(kind: TourKind) {
  if (readString(SEEN_KEYS[kind])) return false;
  if (!targetsPresent(STEPS[kind])) return false;
  createTourDriver(kind).drive();
  return true;
}

/// Auto-launches the matching dashboard tour once per browser the first time `ready` becomes
/// true: the empty-state intro when there are no holdings yet, the full walkthrough once
/// holdings exist. `hasHoldings` also picks which tour the returned `startTour` (manual replay,
/// ignores seen-flags) runs. Targets are re-checked against the DOM because the data-tour
/// elements only render after loading finishes.
export function usePortfolioTour(ready: boolean, hasHoldings: boolean) {
  // Per-kind, not a single boolean: a brand-new user sees the empty-state tour, then saves their
  // first snapshot without ever leaving the page — hasHoldings flips to true and the main tour
  // should still fire. A shared "already started something" flag would suppress it until reload.
  const startedRef = useRef<Partial<Record<TourKind, boolean>>>({});

  useEffect(() => {
    if (!ready) return;
    const kind: TourKind = hasHoldings ? 'main' : 'empty';
    if (startedRef.current[kind]) return;
    if (launchOnce(kind)) startedRef.current[kind] = true;
  }, [ready, hasHoldings]);

  const startTour = () => {
    const kind: TourKind = hasHoldings ? 'main' : 'empty';
    if (!targetsPresent(STEPS[kind])) return;
    createTourDriver(kind).drive();
  };

  return { startTour };
}

/// First time the holdings editor dialog opens, walk through every section. Delayed slightly:
/// MUI Dialog mounts its content in a portal with an entrance transition, so the data-tour
/// targets don't exist (or are mid-animation) at the exact moment `open` flips true.
export function useEditorTour(open: boolean) {
  useEffect(() => {
    if (!open) return;
    if (readString(SEEN_KEYS.editor)) return;
    const timer = setTimeout(() => launchOnce('editor'), 400);
    return () => clearTimeout(timer);
  }, [open]);
}
