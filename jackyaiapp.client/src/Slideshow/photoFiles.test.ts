import { describe, expect, it } from 'vitest';

import { collectPhotos, extensionOf, shuffledOrder } from './photoFiles';

/** 只需要 name / type / lastModified / webkitRelativePath，不必真的做出一個 File。 */
function fakeFile(path: string, type = '', lastModified = 0): File {
  const name = path.split('/').pop() ?? path;
  return { name, type, lastModified, webkitRelativePath: path } as unknown as File;
}

describe('extensionOf', () => {
  it('取副檔名並轉小寫', () => {
    expect(extensionOf('Photo.JPG')).toBe('jpg');
  });

  it('多個點只取最後一段', () => {
    expect(extensionOf('2026.01.15 生日.png')).toBe('png');
  });

  it('沒有副檔名或是隱藏檔時回空字串', () => {
    // 「.gitignore」的點在開頭，那是檔名不是副檔名。
    expect(extensionOf('README')).toBe('');
    expect(extensionOf('.gitignore')).toBe('');
  });
});

describe('collectPhotos', () => {
  it('挑出圖片、丟掉其他檔案', () => {
    const files = [
      fakeFile('trip/a.jpg', 'image/jpeg'),
      fakeFile('trip/notes.txt', 'text/plain'),
      fakeFile('trip/b.png', 'image/png'),
      fakeFile('trip/clip.mp4', 'video/mp4'),
    ];

    expect(collectPhotos(files).map((p) => p.name)).toEqual(['a.jpg', 'b.png']);
  });

  it('MIME type 是空字串時仍靠副檔名認出圖片', () => {
    // Windows 上沒註冊的副檔名 File.type 會是空的，只看 type 會整批漏掉。
    const files = [fakeFile('trip/IMG_1234.HEIC'), fakeFile('trip/shot.avif')];

    expect(collectPhotos(files)).toHaveLength(2);
  });

  it('副檔名不認得但 MIME 是 image/* 也收', () => {
    expect(collectPhotos([fakeFile('trip/weird.xyz', 'image/tiff')])).toHaveLength(1);
  });

  it('自然排序：img2 排在 img10 前面', () => {
    // 這正是原本靜態版被迫用連號數字檔名的那個坑，用一般字串排序會得到 10 < 2。
    const files = ['img10.jpg', 'img2.jpg', 'img1.jpg'].map((n) => fakeFile(`trip/${n}`));

    expect(collectPhotos(files).map((p) => p.name)).toEqual(['img1.jpg', 'img2.jpg', 'img10.jpg']);
  });

  it('依完整路徑排序，子資料夾會整叢待在一起', () => {
    const files = [
      fakeFile('trip/b-folder/1.jpg'),
      fakeFile('trip/a-folder/2.jpg'),
      fakeFile('trip/a-folder/1.jpg'),
    ];

    expect(collectPhotos(files).map((p) => p.path)).toEqual([
      'trip/a-folder/1.jpg',
      'trip/a-folder/2.jpg',
      'trip/b-folder/1.jpg',
    ]);
  });

  it('中文檔名也排得動，不會被丟掉', () => {
    const files = [fakeFile('旅行/合照.jpg'), fakeFile('旅行/日出.jpg')];

    expect(collectPhotos(files)).toHaveLength(2);
  });

  it('不會就地改動傳進來的陣列', () => {
    // 呼叫端手上的 File 清單是瀏覽器給的原始順序，不該被排序副作用改掉。
    const files = [fakeFile('trip/b.jpg'), fakeFile('trip/a.jpg')];
    const before = files.map((f) => f.name);

    collectPhotos(files);

    expect(files.map((f) => f.name)).toEqual(before);
  });

  it('沒有 webkitRelativePath 時用檔名當路徑', () => {
    const file = { name: 'a.jpg', type: 'image/jpeg', lastModified: 0 } as unknown as File;

    expect(collectPhotos([file])[0].path).toBe('a.jpg');
  });
});

describe('shuffledOrder', () => {
  it('是 0..n-1 的一個排列：每張都在，沒有重複', () => {
    const order = shuffledOrder(10, 3);

    expect([...order].sort((a, b) => a - b)).toEqual([0, 1, 2, 3, 4, 5, 6, 7, 8, 9]);
  });

  it('指定的那張排在第一個', () => {
    // 播到一半打開隨機時，畫面上這張不能突然換掉。
    expect(shuffledOrder(10, 7)[0]).toBe(7);
  });

  it('真的有打亂，不是原本的順序', () => {
    // 固定的亂數讓結果可重現；Math.random 在測試裡會讓失敗無法重跑。
    let seed = 1;
    const random = () => {
      seed = (seed * 16807) % 2147483647;
      return seed / 2147483647;
    };

    const order = shuffledOrder(20, 0, random);

    expect(order.slice(1)).not.toEqual(Array.from({ length: 19 }, (_, i) => i + 1));
  });

  it('只有一張或沒有照片也不會出錯', () => {
    expect(shuffledOrder(1, 0)).toEqual([0]);
    expect(shuffledOrder(0, 0)).toEqual([]);
  });
});
