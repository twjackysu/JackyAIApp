/**
 * 把 `<input webkitdirectory>` 給的一整包 File 篩成照片清單並排序。
 *
 * 這是這個頁面存在的理由：靜態 HTML 用 `file://` 開啟時無法列出資料夾內容，只能用
 * `new Image()` 逐一試 `1.jpg`、`2.jpg`… 看載不載得起來，所以檔名被迫是連號數字。
 * 換成瀏覽器的資料夾選擇器之後，檔名和副檔名都是瀏覽器直接給的，什麼名字都可以。
 */

export interface PhotoFile {
  file: File;
  /** 檔名，含副檔名。 */
  name: string;
  /** 相對於使用者選的那層資料夾的路徑，子資料夾也會有。 */
  path: string;
}

/**
 * 判斷是不是照片時 `File.type` 不能單獨信任 —— Windows 上沒註冊的副檔名（.heic、.avif、
 * 某些 .webp）`type` 會是空字串，只看 type 會整批漏掉。所以副檔名和 MIME 任一符合就算。
 */
const IMAGE_EXTENSIONS: ReadonlySet<string> = new Set([
  'jpg',
  'jpeg',
  'png',
  'gif',
  'webp',
  'bmp',
  'avif',
  'jfif',
  'heic',
  'heif',
  'svg',
]);

export function extensionOf(name: string): string {
  const dot = name.lastIndexOf('.');
  return dot <= 0 ? '' : name.slice(dot + 1).toLowerCase();
}

function isImage(file: File): boolean {
  return file.type.startsWith('image/') || IMAGE_EXTENSIONS.has(extensionOf(file.name));
}

/**
 * 自然排序：`img2.jpg` 要排在 `img10.jpg` 前面。預設的字串比較會給出 10 < 2，那正是原本
 * 的數字檔名限制想避開、但換個形式又踩回去的坑。
 */
const naturalCompare = new Intl.Collator(undefined, {
  numeric: true,
  sensitivity: 'base',
}).compare;

/**
 * 篩出照片並依檔名排序。以完整相對路徑排序而非只看檔名，子資料夾才會整叢待在一起，
 * 而不是被打散成各層檔名交錯。
 *
 * 只有這一種順序；隨機播放是播放器在這個順序上另外洗牌，關掉時才回得到原本的順序。
 */
export function collectPhotos(files: readonly File[]): PhotoFile[] {
  return files
    .filter(isImage)
    .map((file) => ({
      file,
      name: file.name,
      // webkitRelativePath 只有透過資料夾選擇器進來才有值；拖曳單檔進來時是空字串。
      path: file.webkitRelativePath || file.name,
    }))
    .sort((a, b) => naturalCompare(a.path, b.path));
}

/**
 * 隨機播放順序：回傳 0..count-1 的一個排列，first 固定在最前面。
 *
 * first 固定是為了播到一半打開隨機時，畫面上這張不要突然換掉 —— 使用者勾的是「接下來
 * 隨機」，不是「現在跳到別張」。
 */
export function shuffledOrder(
  count: number,
  first: number,
  random: () => number = Math.random,
): number[] {
  const rest: number[] = [];
  for (let i = 0; i < count; i++) if (i !== first) rest.push(i);
  // Fisher–Yates
  for (let i = rest.length - 1; i > 0; i--) {
    const j = Math.floor(random() * (i + 1));
    [rest[i], rest[j]] = [rest[j], rest[i]];
  }
  return count > 0 ? [first, ...rest] : [];
}
