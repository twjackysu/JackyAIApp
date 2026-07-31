import { driver, type Config } from 'driver.js';
import 'driver.js/dist/driver.css';
import { useEffect, useRef } from 'react';

const STORAGE_KEY = 'portfolio_tour_seen_v1';

// Elements are targeted via data-tour attributes (see index.tsx/HoldingsEditor.tsx) rather than
// MUI classnames — those are generated/unstable, data-tour is a stable contract this file owns.
const STEPS: NonNullable<Config['steps']> = [
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
      description:
        '在這裡新增/編輯股票、現金、基金、負債、不動產，並存成當天的持倉快照（不是逐筆交易紀錄）。',
      side: 'bottom',
      align: 'end',
    },
  },
];

function allStepTargetsPresent(): boolean {
  return STEPS.every((s) => typeof s.element === 'string' && document.querySelector(s.element));
}

function createTourDriver() {
  return driver({
    showProgress: true,
    allowClose: true,
    overlayOpacity: 0.65,
    nextBtnText: '下一步',
    prevBtnText: '上一步',
    doneBtnText: '完成',
    steps: STEPS,
    // Fires on both "完成" and the close (skip) button — either way the user has seen it once,
    // don't auto-launch again.
    onDestroyed: () => {
      localStorage.setItem(STORAGE_KEY, '1');
    },
  });
}

/// Auto-launches the tour once per browser (localStorage flag) the first time `ready` becomes
/// true AND every target element already exists in the DOM — `ready` alone isn't enough because
/// data-tour elements only render once the summary has loaded and hasHoldings is true. Also
/// returns `startTour` for a manual "replay" entry point that ignores the seen-flag.
export function usePortfolioTour(ready: boolean) {
  const startedRef = useRef(false);

  useEffect(() => {
    if (!ready || startedRef.current) return;
    if (localStorage.getItem(STORAGE_KEY)) return;
    if (!allStepTargetsPresent()) return;
    startedRef.current = true;
    createTourDriver().drive();
  }, [ready]);

  const startTour = () => {
    if (!allStepTargetsPresent()) return;
    createTourDriver().drive();
  };

  return { startTour };
}
