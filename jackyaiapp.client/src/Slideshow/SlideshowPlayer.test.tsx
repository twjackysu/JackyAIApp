import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import SlideshowPlayer from './SlideshowPlayer';

import type { PhotoFile } from './photoFiles';
import type { SlideshowSettings } from './settings';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

// ── Stand-ins for what jsdom does not have ──
// Image.decode(): a file whose name has "bad" in it cannot be decoded; one listed in `held` waits
// until the test lets it go.
const held = new Map<string, { resolve: () => void; reject: () => void }>();
class FakeImage {
  src = '';
  decode() {
    const name = this.src.replace('blob:', '');
    if (held.has(name))
      return new Promise<void>((resolve, reject) =>
        held.set(name, { resolve, reject: () => reject(new Error('cannot decode')) }),
      );
    if (this.src.includes('bad')) return Promise.reject(new Error('cannot decode'));
    return Promise.resolve();
  }
}
const created: string[] = [];
const revoked: string[] = [];
const original = {
  create: URL.createObjectURL,
  revoke: URL.revokeObjectURL,
  requestFullscreen: HTMLElement.prototype.requestFullscreen,
};
let fullscreenElement: Element | null = null;
const exitFullscreen = vi.fn(async () => {
  fullscreenElement = null;
  document.dispatchEvent(new Event('fullscreenchange'));
});
const requestFullscreen = vi.fn(async () => {
  fullscreenElement = document.documentElement;
  document.dispatchEvent(new Event('fullscreenchange'));
});

const photo = (name: string): PhotoFile => ({
  file: new File(['x'], name),
  name,
  path: `假期/${name}`,
});
const settingsOf = (patch: Partial<SlideshowSettings> = {}): SlideshowSettings => ({
  interval: 5,
  loop: true,
  shuffle: false,
  fit: 'contain',
  ...patch,
});

let container: HTMLDivElement;
let root: Root;
const onSettingsChange = vi.fn();
const onExit = vi.fn();
let photos: PhotoFile[];

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
  vi.stubGlobal('Image', FakeImage);
  held.clear();
  created.length = 0;
  revoked.length = 0;
  URL.createObjectURL = (file: Blob) => {
    const url = `blob:${(file as File).name}`;
    created.push(url);
    return url;
  };
  URL.revokeObjectURL = (url: string) => void revoked.push(url);
  fullscreenElement = null;
  Object.defineProperty(document, 'fullscreenElement', {
    configurable: true,
    get: () => fullscreenElement,
  });
  document.exitFullscreen = exitFullscreen;
  HTMLElement.prototype.requestFullscreen = requestFullscreen;
  photos = [photo('a.jpg'), photo('b.jpg'), photo('c.jpg')];
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.clearAllMocks();
  URL.createObjectURL = original.create;
  URL.revokeObjectURL = original.revoke;
  HTMLElement.prototype.requestFullscreen = original.requestFullscreen;
});

/** Renders the player and lets the first decode land. */
async function render(settings: SlideshowSettings = settingsOf()) {
  act(() =>
    root.render(
      <SlideshowPlayer
        photos={photos}
        settings={settings}
        onSettingsChange={onSettingsChange}
        onExit={onExit}
      />,
    ),
  );
  await settle();
}
/** Lets pending decodes and the renders they cause run. */
const settle = () => act(async () => {});
const tick = async (ms: number) => {
  await act(async () => {
    vi.advanceTimersByTime(ms);
  });
  await settle();
};
const key = async (k: string, target: EventTarget = window) => {
  let event!: KeyboardEvent;
  await act(async () => {
    event = new KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true });
    target.dispatchEvent(event);
  });
  await settle();
  return event;
};
const counter = () =>
  [...container.querySelectorAll('.MuiTypography-caption')].find((t) =>
    /^\d+ \/ \d+$/.test(t.textContent ?? ''),
  )?.textContent;
const shown = () => container.querySelector('img[alt]:not([alt=""])') as HTMLImageElement | null;
const images = () => [...container.querySelectorAll('img')] as HTMLImageElement[];
const buttonLabelled = (label: string) =>
  container.querySelector(`button[aria-label="${label}"]`) as HTMLButtonElement;
const click = async (el: Element) => {
  await act(async () => (el as HTMLElement).click());
  await settle();
};
const checkbox = (label: string) =>
  [...container.querySelectorAll('label')]
    .find((l) => l.textContent === label)!
    .querySelector('input') as HTMLInputElement;
const picture = () => container.firstElementChild!.firstElementChild as HTMLElement;
const topBar = () => container.firstElementChild!.children[1] as HTMLElement;
const progressBar = () =>
  [...container.firstElementChild!.children].find((el) => getComputedStyle(el).height === '3px') as
    HTMLElement | undefined;

describe('SlideshowPlayer — showing photos', () => {
  it('opens on the first photo, playing, with its path and the count', async () => {
    await render();
    expect(shown()?.alt).toBe('a.jpg');
    expect(shown()?.src).toBe('blob:a.jpg');
    expect(topBar().textContent).toContain('假期/a.jpg');
    expect(counter()).toBe('1 / 3');
    expect(buttonLabelled('暫停 (Space)')).not.toBeNull();
    expect(progressBar()).toBeDefined();
  });

  it('fades the next photo in over the last one: the old one stays under, unnamed', async () => {
    await render();
    await click(buttonLabelled('下一張 (→)'));
    expect(images().map((i) => [i.src, i.alt])).toEqual([
      ['blob:a.jpg', ''],
      ['blob:b.jpg', 'b.jpg'],
    ]);
    expect(getComputedStyle(images()[1]).animation).toContain('slideshow-fade');
    expect(getComputedStyle(images()[0]).animation).toBe('none');
  });

  it('shows the photo whole, or cropped to fill, as set', async () => {
    await render();
    expect(getComputedStyle(shown()!).objectFit).toBe('contain');
    await render(settingsOf({ fit: 'cover' }));
    expect(getComputedStyle(shown()!).objectFit).toBe('cover');
  });

  it('holds blob URLs only for this photo and the ones either side, and lets them all go on leaving', async () => {
    photos = ['a', 'b', 'c', 'd', 'e'].map((n) => photo(`${n}.jpg`));
    await render();
    expect(created.sort()).toEqual(['blob:a.jpg', 'blob:b.jpg', 'blob:e.jpg']);
    await click(buttonLabelled('下一張 (→)'));
    await click(buttonLabelled('下一張 (→)'));
    expect(revoked.sort()).toEqual(['blob:a.jpg', 'blob:e.jpg']);
    act(() => root.unmount());
    root = createRoot(container);
    expect(revoked.sort()).toEqual([
      'blob:a.jpg',
      'blob:b.jpg',
      'blob:c.jpg',
      'blob:d.jpg',
      'blob:e.jpg',
    ]);
  });

  it('a photo still decoding when the next is asked for never shows over it', async () => {
    held.set('b.jpg', { resolve: () => {}, reject: () => {} });
    await render();
    await click(buttonLabelled('下一張 (→)')); // b waits
    await click(buttonLabelled('下一張 (→)')); // c shows
    expect(shown()?.alt).toBe('c.jpg');
    await act(async () => held.get('b.jpg')!.resolve());
    await settle();
    expect(shown()?.src).toBe('blob:c.jpg');
    expect(counter()).toBe('3 / 3');
  });
});

describe('SlideshowPlayer — photos that cannot be shown', () => {
  it('skips one the way it was going: forwards, and backwards from ←', async () => {
    photos = [photo('a.jpg'), photo('bad.heic'), photo('c.jpg'), photo('d.jpg')];
    await render();
    await click(buttonLabelled('下一張 (→)'));
    expect(counter()).toBe('3 / 4');
    expect(shown()?.alt).toBe('c.jpg');
    await click(buttonLabelled('上一張 (←)'));
    expect(counter()).toBe('1 / 4');
    expect(shown()?.alt).toBe('a.jpg');
  });

  it('a photo that fails to decode after it was left behind does not move the slideshow', async () => {
    photos = [photo('a.jpg'), photo('bad-slow.heic'), photo('c.jpg'), photo('d.jpg')];
    held.set('bad-slow.heic', { resolve: () => {}, reject: () => {} });
    await render();
    await click(buttonLabelled('下一張 (→)')); // still decoding
    await click(buttonLabelled('下一張 (→)'));
    await click(buttonLabelled('下一張 (→)'));
    expect(counter()).toBe('4 / 4');
    await act(async () => held.get('bad-slow.heic')!.reject());
    await settle();
    expect(counter()).toBe('4 / 4');
  });

  it('one that cannot be shown among ones that can never stops the slideshow, however often it comes round', async () => {
    photos = [photo('a.jpg'), photo('bad.heic'), photo('c.jpg')];
    await render();
    for (let lap = 0; lap < 4; lap++) {
      await click(buttonLabelled('下一張 (→)')); // skips the bad one to c
      await click(buttonLabelled('下一張 (→)')); // a
    }
    expect(container.textContent).not.toContain('一張都顯示不了');
    expect(buttonLabelled('暫停 (Space)')).not.toBeNull();
  });

  it('none in the folder can be shown: says why and stops', async () => {
    photos = [photo('bad1.heic'), photo('bad2.heic')];
    await render();
    await settle();
    expect(container.textContent).toContain('這個資料夾裡的照片瀏覽器一張都顯示不了。');
    expect(container.textContent).toContain('iPhone 的 .heic');
    expect(buttonLabelled('播放 (Space)')).not.toBeNull();
  });
});

describe('SlideshowPlayer — moving through', () => {
  it('previous and next wrap around the ends', async () => {
    await render();
    await click(buttonLabelled('上一張 (←)'));
    expect(counter()).toBe('3 / 3');
    await click(buttonLabelled('下一張 (→)'));
    expect(counter()).toBe('1 / 3');
  });

  it('plays on by itself every interval; at the end it starts over when looping', async () => {
    await render(settingsOf({ interval: 2 }));
    await tick(1999);
    expect(counter()).toBe('1 / 3');
    await tick(1);
    expect(counter()).toBe('2 / 3');
    await tick(2000);
    await tick(2000);
    expect(counter()).toBe('1 / 3');
  });

  it('without looping it stops on the last photo', async () => {
    await render(settingsOf({ interval: 1, loop: false }));
    await tick(1000);
    await tick(1000);
    expect(counter()).toBe('3 / 3');
    await tick(1000);
    expect(counter()).toBe('3 / 3');
    expect(buttonLabelled('播放 (Space)')).not.toBeNull();
    expect(progressBar()).toBeUndefined();
  });

  it('paused, it stays; the play button and a click on the photo itself toggle it', async () => {
    await render(settingsOf({ interval: 1 }));
    await click(buttonLabelled('暫停 (Space)'));
    await tick(3000);
    expect(counter()).toBe('1 / 3');
    await click(picture());
    expect(buttonLabelled('暫停 (Space)')).not.toBeNull();
    await click(topBar());
    expect(buttonLabelled('暫停 (Space)')).not.toBeNull(); // the bars are not the photo
    await click(shown()!);
    expect(buttonLabelled('暫停 (Space)')).not.toBeNull(); // only the photo's own frame counts
  });
});

describe('SlideshowPlayer — keyboard', () => {
  it('→ / PageDown next, ← / PageUp previous, Home first, End last', async () => {
    photos = ['a', 'b', 'c', 'd'].map((n) => photo(`${n}.jpg`));
    await render();
    await key('ArrowRight');
    await key('PageDown');
    expect(counter()).toBe('3 / 4');
    await key('ArrowLeft');
    expect(counter()).toBe('2 / 4');
    await key('PageUp');
    expect(counter()).toBe('1 / 4');
    await key('End');
    expect(counter()).toBe('4 / 4');
    await key('Home');
    expect(counter()).toBe('1 / 4');
  });

  it('Space plays and pauses without scrolling the page', async () => {
    await render();
    expect((await key(' ')).defaultPrevented).toBe(true);
    expect(buttonLabelled('播放 (Space)')).not.toBeNull();
    await key(' ');
    expect(buttonLabelled('暫停 (Space)')).not.toBeNull();
  });

  it('+ / = and − change the interval by a second, within 1 to 30', async () => {
    await render(settingsOf({ interval: 30 }));
    await key('+');
    expect(onSettingsChange).toHaveBeenLastCalledWith({ interval: 30 });
    await key('-');
    expect(onSettingsChange).toHaveBeenLastCalledWith({ interval: 29 });
    await render(settingsOf({ interval: 1 }));
    await key('-');
    expect(onSettingsChange).toHaveBeenLastCalledWith({ interval: 1 });
    await key('=');
    expect(onSettingsChange).toHaveBeenLastCalledWith({ interval: 2 });
  });

  it('F toggles full screen; Esc leaves the player, but not while in full screen', async () => {
    await render();
    await key('f');
    expect(requestFullscreen).toHaveBeenCalledTimes(1);
    expect(buttonLabelled('離開全螢幕 (F)')).not.toBeNull();
    await key('Escape');
    expect(onExit).not.toHaveBeenCalled();
    await key('F');
    expect(exitFullscreen).toHaveBeenCalledTimes(1);
    expect(buttonLabelled('全螢幕 (F)')).not.toBeNull();
    await key('Escape');
    expect(onExit).toHaveBeenCalledTimes(1);
  });

  it('keys typed into a field are left to the field', async () => {
    await render();
    const input = document.createElement('input');
    document.body.appendChild(input);
    await key('ArrowRight', input);
    expect(counter()).toBe('1 / 3');
    input.remove();
  });
});

describe('SlideshowPlayer — controls', () => {
  it('the close button leaves; the full screen button toggles it', async () => {
    await render();
    await click(buttonLabelled('回到選擇資料夾 (Esc)'));
    expect(onExit).toHaveBeenCalledTimes(1);
    await click(buttonLabelled('全螢幕 (F)'));
    expect(requestFullscreen).toHaveBeenCalledTimes(1);
  });

  it('shows the interval; the slider, 循環 and 填滿裁切 change the settings', async () => {
    await render();
    expect(container.textContent).toContain('5s');
    await click(checkbox('循環'));
    expect(onSettingsChange).toHaveBeenLastCalledWith({ loop: false });
    await click(checkbox('填滿裁切'));
    expect(onSettingsChange).toHaveBeenLastCalledWith({ fit: 'cover' });
    await render(settingsOf({ fit: 'cover' }));
    await click(checkbox('填滿裁切'));
    expect(onSettingsChange).toHaveBeenLastCalledWith({ fit: 'contain' });
    expect(container.querySelector('[aria-label="每張秒數"]')).not.toBeNull();
  });

  it('隨機播放 keeps this photo on screen and plays the rest in a new order; off, back to file order from here', async () => {
    photos = ['a', 'b', 'c', 'd'].map((n) => photo(`${n}.jpg`));
    vi.spyOn(Math, 'random').mockReturnValue(0);
    await render();
    await click(buttonLabelled('下一張 (→)')); // b
    await click(checkbox('隨機播放'));
    expect(onSettingsChange).toHaveBeenLastCalledWith({ shuffle: true });
    expect(counter()).toBe('1 / 4');
    expect(shown()?.alt).toBe('b.jpg');
    // With random() = 0, Fisher–Yates over [a, c, d] gives [c, d, a]: b, c, d, a.
    await click(buttonLabelled('下一張 (→)'));
    expect(shown()?.alt).toBe('c.jpg');

    await render(settingsOf({ shuffle: true }));
    await click(checkbox('隨機播放'));
    expect(onSettingsChange).toHaveBeenLastCalledWith({ shuffle: false });
    expect(counter()).toBe('3 / 4');
    expect(shown()?.alt).toBe('c.jpg');
  });

  it('opened with 隨機播放 on, it starts from a random photo in a random order', async () => {
    photos = ['a', 'b', 'c', 'd'].map((n) => photo(`${n}.jpg`));
    vi.spyOn(Math, 'random').mockReturnValue(0.6); // floor(0.6 × 4) = 2: c first
    await render(settingsOf({ shuffle: true }));
    expect(shown()?.alt).toBe('c.jpg');
    expect(counter()).toBe('1 / 4');
  });

  it('the controls fade out after 2.5 s without the mouse, and come back when it moves', async () => {
    await render();
    await tick(2499);
    expect(getComputedStyle(topBar()).opacity).toBe('1');
    await tick(1);
    expect(getComputedStyle(topBar()).opacity).toBe('0');
    expect(getComputedStyle(container.firstElementChild!).cursor).toBe('none');
    await act(async () => window.dispatchEvent(new MouseEvent('mousemove')));
    expect(getComputedStyle(topBar()).opacity).toBe('1');
    expect(getComputedStyle(container.firstElementChild!).cursor).toBe('default');
  });

  it('the progress bar runs for the interval and starts over for each photo', async () => {
    await render(settingsOf({ interval: 7 }));
    expect(getComputedStyle(progressBar()!).animation).toContain('7s');
    const first = progressBar();
    await click(buttonLabelled('下一張 (→)'));
    expect(progressBar()).not.toBe(first);
  });
});
