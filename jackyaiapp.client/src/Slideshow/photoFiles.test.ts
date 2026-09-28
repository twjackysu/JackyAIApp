import { describe, expect, it } from 'vitest';

import { collectPhotos, extensionOf } from './photoFiles';

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

    expect(collectPhotos(files, 'name').map((p) => p.name)).toEqual([
      'img1.jpg',
      'img2.jpg',
      'img10.jpg',
    ]);
  });

  it('依完整路徑排序，子資料夾會整叢待在一起', () => {
    const files = [
      fakeFile('trip/b-folder/1.jpg'),
      fakeFile('trip/a-folder/2.jpg'),
      fakeFile('trip/a-folder/1.jpg'),
    ];

    expect(collectPhotos(files, 'name').map((p) => p.path)).toEqual([
      'trip/a-folder/1.jpg',
      'trip/a-folder/2.jpg',
      'trip/b-folder/1.jpg',
    ]);
  });

  it('中文檔名也排得動，不會被丟掉', () => {
    const files = [fakeFile('旅行/合照.jpg'), fakeFile('旅行/日出.jpg')];

    expect(collectPhotos(files)).toHaveLength(2);
  });

  it('依修改時間排序', () => {
    const files = [
      fakeFile('trip/c.jpg', '', 300),
      fakeFile('trip/a.jpg', '', 100),
      fakeFile('trip/b.jpg', '', 200),
    ];

    expect(collectPhotos(files, 'modified').map((p) => p.name)).toEqual([
      'a.jpg',
      'b.jpg',
      'c.jpg',
    ]);
  });

  it('修改時間相同時退回檔名，順序才是穩定的', () => {
    // 相機連拍或整包複製過來的檔案時間戳會一樣，沒有次要排序的話每次結果都不同。
    const files = [fakeFile('trip/b.jpg', '', 100), fakeFile('trip/a.jpg', '', 100)];

    expect(collectPhotos(files, 'modified').map((p) => p.name)).toEqual(['a.jpg', 'b.jpg']);
  });

  it('隨機排序會用完全部照片，不會掉張也不會重複', () => {
    const files = ['1.jpg', '2.jpg', '3.jpg', '4.jpg'].map((n) => fakeFile(`trip/${n}`));

    const names = collectPhotos(files, 'random', () => 0.5)
      .map((p) => p.name)
      .sort();

    expect(names).toEqual(['1.jpg', '2.jpg', '3.jpg', '4.jpg']);
  });

  it('不會就地改動傳進來的陣列', () => {
    // 呼叫端拿同一份 File 清單重新排序，原始順序不該被上一次排序污染。
    const files = [fakeFile('trip/b.jpg'), fakeFile('trip/a.jpg')];
    const before = files.map((f) => f.name);

    collectPhotos(files, 'name');

    expect(files.map((f) => f.name)).toEqual(before);
  });

  it('沒有 webkitRelativePath 時用檔名當路徑', () => {
    const file = { name: 'a.jpg', type: 'image/jpeg', lastModified: 0 } as unknown as File;

    expect(collectPhotos([file])[0].path).toBe('a.jpg');
  });
});
