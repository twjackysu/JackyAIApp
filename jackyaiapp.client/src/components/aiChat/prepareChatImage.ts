/**
 * Chat images are made ready in the browser, not on the server: the server never decodes an image it is
 * given (a decoder is where image libraries' security bugs are), it only reads the header of what arrives
 * and keeps it as it is (AttachmentFileProcessor.PrepareImage). So the downsizing happens here.
 */

/** The size the server takes an image at (AttachmentFileProcessor.MaxImageEdge / MaxImagePixels). */
export const MAX_IMAGE_EDGE = 1568;
export const MAX_IMAGE_PIXELS = 1_150_000;

const JPEG_QUALITY = 0.85;

export const isImageFile = (file: File) => /\.(png|jpe?g|gif|webp)$/i.test(file.name);

/** An image the browser could not read. */
export class UnreadableImageError extends Error {}

/** The size an image is drawn at: scaled down to fit both caps, keeping its shape; never scaled up. */
export const fittedSize = (width: number, height: number) => {
  const scale = Math.min(
    1,
    MAX_IMAGE_EDGE / Math.max(width, height),
    Math.sqrt(MAX_IMAGE_PIXELS / (width * height)),
  );
  return {
    width: Math.max(1, Math.floor(width * scale)),
    height: Math.max(1, Math.floor(height * scale)),
  };
};

/** A PNG stays a PNG, so text in a screenshot stays sharp; anything else becomes a JPEG. */
export const outputTypeOf = (file: File) =>
  file.type === 'image/png' || /\.png$/i.test(file.name) ? 'image/png' : 'image/jpeg';

/** The file's name with the extension of what it now is. */
export const renamedFor = (name: string, type: string) =>
  name.replace(/\.[^.]*$/, '') + (type === 'image/png' ? '.png' : '.jpg');

/**
 * An image made ready for the chat: downsized to the caps above, drawn upright (its EXIF orientation
 * applied), an animated GIF as its first frame, saved as a PNG or a JPEG. Any other file comes back as
 * it is — and so does an image in a browser that cannot do this, for the server to take if it fits.
 */
export async function prepareChatImage(file: File): Promise<File> {
  if (!isImageFile(file) || typeof createImageBitmap !== 'function') return file;

  let bitmap: ImageBitmap;
  try {
    bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
  } catch {
    throw new UnreadableImageError(`Could not read ${file.name} as an image.`);
  }
  try {
    const { width, height } = fittedSize(bitmap.width, bitmap.height);
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    const context = canvas.getContext('2d');
    if (!context) throw new UnreadableImageError('No 2D canvas to draw the image on.');

    const type = outputTypeOf(file);
    if (type === 'image/jpeg') {
      // A JPEG has no transparency: put transparent areas on white, not on black.
      context.fillStyle = '#ffffff';
      context.fillRect(0, 0, width, height);
    }
    context.imageSmoothingQuality = 'high';
    context.drawImage(bitmap, 0, 0, width, height);

    const blob = await new Promise<Blob | null>((resolve) =>
      canvas.toBlob(resolve, type, JPEG_QUALITY),
    );
    if (!blob) throw new UnreadableImageError(`Could not save ${file.name}.`);
    return new File([blob], renamedFor(file.name, type), { type });
  } finally {
    bitmap.close();
  }
}
