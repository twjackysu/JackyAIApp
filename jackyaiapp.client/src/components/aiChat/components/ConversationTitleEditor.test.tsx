import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import ConversationTitleEditor from './ConversationTitleEditor';

let container: HTMLDivElement;
let root: Root;
let onSave: ReturnType<typeof vi.fn<(title: string) => void>>;
let onClose: ReturnType<typeof vi.fn<() => void>>;

beforeEach(() => {
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
  onSave = vi.fn<(title: string) => void>();
  onClose = vi.fn<() => void>();
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
});

const render = (title: string) =>
  act(() =>
    root.render(<ConversationTitleEditor title={title} onSave={onSave} onClose={onClose} />),
  );
const box = () => container.querySelector<HTMLInputElement>('input[aria-label="對話標題"]')!;
const type = (text: string) =>
  act(() => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(box(), text);
    box().dispatchEvent(new Event('input', { bubbles: true }));
  });
const key = (init: KeyboardEventInit) =>
  act(() => {
    box().dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, ...init }));
  });
const blur = () =>
  act(() => {
    box().dispatchEvent(new FocusEvent('focusout', { bubbles: true }));
  });

describe('ConversationTitleEditor (D-144)', () => {
  it('leaving the box saves what was typed', () => {
    render('舊標題');
    type('新標題');
    blur();
    expect(onSave).toHaveBeenCalledWith('新標題');
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('Enter then the blur that follows saves once', () => {
    render('舊標題');
    type('新標題');
    key({ key: 'Enter' });
    blur();
    expect(onSave).toHaveBeenCalledTimes(1);
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('a title that changed while the box was open (the AI named it) is not put back over by an untouched box', () => {
    render('台積電最近一個月的走勢如何？技術面和籌…');
    render('台積電近一個月走勢');
    blur();
    expect(onSave).not.toHaveBeenCalled();
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('the Enter that picks a Chinese input method candidate does not save, nor its Escape close', () => {
    render('舊標題');
    type('新標題');
    key({ key: 'Enter', keyCode: 229 });
    key({ key: 'Escape', isComposing: true });
    expect(onSave).not.toHaveBeenCalled();
    expect(onClose).not.toHaveBeenCalled();
  });

  it('Escape keeps the title and goes no further', () => {
    const outside = vi.fn();
    document.body.addEventListener('keydown', outside);
    render('舊標題');
    type('新標題');
    key({ key: 'Escape' });
    expect(onSave).not.toHaveBeenCalled();
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(outside).not.toHaveBeenCalled();
    document.body.removeEventListener('keydown', outside);
  });
});
