import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';

import { SummarizeTab } from './SummarizeTab';

const noop = () => {};

function render(isLoggedIn: boolean) {
  return renderToStaticMarkup(
    <SummarizeTab
      fieldTransforms={[]}
      onTransformAdd={noop}
      onTransformUpdate={noop}
      onTransformRemove={noop}
      viewByEntries={[]}
      onViewByAdd={noop}
      onViewByUpdate={noop}
      onViewByRemove={noop}
      measures={[]}
      onMeasureAdd={noop}
      onMeasureRemove={noop}
      onMeasureUpdate={noop}
      summarizeFilters={[]}
      onSummarizeFiltersChange={noop}
      onRunSummarize={noop}
      isLoading={false}
      error={null}
      summarizeRows={[]}
      summarizeHeaders={[]}
      onRunNlSummarize={noop}
      nlIsLoading={false}
      nlError={null}
      nlRows={[]}
      nlResultVersion={0}
      summarizeVersion={0}
      summarizeLastRequest={null}
      canAddToDashboard={false}
      isLoggedIn={isLoggedIn}
      fields={[]}
    />,
  );
}

describe('SummarizeTab AI mode data disclosure', () => {
  // AI is the default mode, so a plain render is the AI panel.
  it.each([
    ['logged in', true],
    ['logged out', false],
  ])('tells the user what is sent to OpenAI when %s', (_label, isLoggedIn) => {
    const html = render(isLoggedIn);
    expect(html).toContain('OpenAI');
    expect(html).toContain('欄位名稱');
  });

  it('shows the notice as visible helper text tied to the input, not in a tooltip', () => {
    const html = render(true);
    const helper = /<p[^>]*id="([^"]*-helper-text)"[^>]*>([^<]*OpenAI[^<]*)<\/p>/.exec(html);
    expect(helper).not.toBeNull();
    // aria-describedby on the textarea points at the helper text, so screen readers read it.
    expect(html).toMatch(new RegExp(`<textarea[^>]*aria-describedby="${helper![1]}"`));
  });
});
