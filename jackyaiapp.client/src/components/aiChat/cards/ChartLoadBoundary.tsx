import { Component, type ReactNode } from 'react';

/**
 * Around 畫成圖's lazily loaded chart (D-153): a chart that cannot be loaded (a tab left open over a deploy asks for a
 * chunk that is gone) or drawn shows `fallback` — the table, with a line saying so — instead of an error taking the whole
 * page down. React has no hook for this, so it is a class.
 */
export default class ChartLoadBoundary extends Component<
  { fallback: ReactNode; children: ReactNode },
  { failed: boolean }
> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  render() {
    return this.state.failed ? this.props.fallback : this.props.children;
  }
}
