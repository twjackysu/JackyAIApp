import { afterEach, describe, expect, it, vi } from 'vitest';

import {
  fittedSize,
  MAX_IMAGE_EDGE,
  MAX_IMAGE_PIXELS,
  outputTypeOf,
  prepareChatImage,
  renamedFor,
  UnreadableImageError,
} from './prepareChatImage';

describe('fittedSize', () => {
  it('keeps an image that already fits', () => {
    expect(fittedSize(300, 200)).toEqual({ width: 300, height: 200 });
  });

  it('caps the long edge', () => {
    const size = fittedSize(4000, 400);
    expect(size.width).toBe(MAX_IMAGE_EDGE);
    expect(size.height).toBe(156);
  });

  it('caps the area, keeping the shape, for a phone photo', () => {
    const size = fittedSize(4032, 3024);
    expect(size.width * size.height).toBeLessThanOrEqual(MAX_IMAGE_PIXELS);
    expect(size.width * size.height).toBeGreaterThan(MAX_IMAGE_PIXELS * 0.99);
    expect(size.width / size.height).toBeCloseTo(4 / 3, 2);
  });

  it('never goes below one pixel', () => {
    expect(fittedSize(100_000, 1)).toEqual({ width: MAX_IMAGE_EDGE, height: 1 });
  });
});

describe('outputTypeOf and renamedFor', () => {
  it('keeps a PNG a PNG and turns anything else into a JPEG', () => {
    expect(outputTypeOf(new File([], 'a.png', { type: 'image/png' }))).toBe('image/png');
    expect(outputTypeOf(new File([], 'a.gif', { type: 'image/gif' }))).toBe('image/jpeg');
    expect(outputTypeOf(new File([], 'a.webp', { type: 'image/webp' }))).toBe('image/jpeg');
    expect(outputTypeOf(new File([], 'a.jpeg', { type: 'image/jpeg' }))).toBe('image/jpeg');
  });

  it('names the file for what it now is', () => {
    expect(renamedFor('動畫.gif', 'image/jpeg')).toBe('動畫.jpg');
    expect(renamedFor('截圖.PNG', 'image/png')).toBe('截圖.png');
    expect(renamedFor('no-extension', 'image/jpeg')).toBe('no-extension.jpg');
  });
});

describe('prepareChatImage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  const drawing = () => {
    const context = {
      fillRect: vi.fn(),
      drawImage: vi.fn(),
      fillStyle: '',
      imageSmoothingQuality: '',
    };
    const canvas = { width: 0, height: 0, getContext: () => context, toBlob: vi.fn() };
    canvas.toBlob.mockImplementation((done: (blob: Blob | null) => void, type: string) =>
      done(new Blob(['encoded'], { type })),
    );
    const original = document.createElement.bind(document);
    vi.spyOn(document, 'createElement').mockImplementation((tag: string) =>
      tag === 'canvas' ? (canvas as unknown as HTMLCanvasElement) : original(tag),
    );
    return { canvas, context };
  };

  it('draws a large photo down to the caps, upright, as a JPEG on white', async () => {
    const close = vi.fn();
    const decode = vi.fn().mockResolvedValue({ width: 4032, height: 3024, close });
    vi.stubGlobal('createImageBitmap', decode);
    const { canvas, context } = drawing();

    const prepared = await prepareChatImage(new File(['raw'], '相片.webp', { type: 'image/webp' }));

    expect(decode).toHaveBeenCalledWith(expect.any(File), { imageOrientation: 'from-image' });
    expect(canvas.width * canvas.height).toBeLessThanOrEqual(MAX_IMAGE_PIXELS);
    expect(context.fillRect).toHaveBeenCalledWith(0, 0, canvas.width, canvas.height);
    expect(canvas.toBlob).toHaveBeenCalledWith(expect.any(Function), 'image/jpeg', 0.85);
    expect([prepared.name, prepared.type]).toEqual(['相片.jpg', 'image/jpeg']);
    expect(close).toHaveBeenCalled();
  });

  it('keeps a PNG a PNG, with its transparency', async () => {
    vi.stubGlobal(
      'createImageBitmap',
      vi.fn().mockResolvedValue({ width: 800, height: 600, close: vi.fn() }),
    );
    const { canvas, context } = drawing();

    const prepared = await prepareChatImage(new File(['raw'], '截圖.png', { type: 'image/png' }));

    expect([canvas.width, canvas.height]).toEqual([800, 600]);
    expect(context.fillRect).not.toHaveBeenCalled();
    expect([prepared.name, prepared.type]).toEqual(['截圖.png', 'image/png']);
  });

  it('says so when the browser cannot read the image', async () => {
    vi.stubGlobal(
      'createImageBitmap',
      vi.fn().mockRejectedValue(new DOMException('bad', 'InvalidStateError')),
    );

    await expect(
      prepareChatImage(new File(['junk'], '壞掉.jpg', { type: 'image/jpeg' })),
    ).rejects.toBeInstanceOf(UnreadableImageError);
  });

  it('leaves a file that is not an image as it is', async () => {
    const decode = vi.fn();
    vi.stubGlobal('createImageBitmap', decode);
    const pdf = new File(['%PDF'], '報告.pdf', { type: 'application/pdf' });

    expect(await prepareChatImage(pdf)).toBe(pdf);
    expect(decode).not.toHaveBeenCalled();
  });

  it('sends the image as it is where the browser cannot draw one', async () => {
    vi.stubGlobal('createImageBitmap', undefined);
    const png = new File(['png'], 'a.png', { type: 'image/png' });

    expect(await prepareChatImage(png)).toBe(png);
  });
});
