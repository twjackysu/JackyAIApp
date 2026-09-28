import { describe, expect, it } from 'vitest';

import { ObjectUrlWindow, type ObjectUrlFactory } from './objectUrlWindow';

/**
 * 記錄每一次 create / revoke，並追蹤還活著的有哪些。
 *
 * 這裡出錯在畫面上完全看不出來 —— 照片照常播，只是記憶體一路往上，而照片資料夾正是最
 * 容易有幾千個檔案的那種，所以只能靠測試盯著。
 *
 * URL 每次建立都帶一個流水號，跟真的 `createObjectURL` 一樣：同一個檔案先後建立兩次會拿
 * 到兩個不同的 URL，release 掉其中一個不代表另一個也沒了。
 */
function trackingFactory() {
  const created: string[] = [];
  const revoked: string[] = [];
  const alive = new Set<string>();
  let seq = 0;

  const factory: ObjectUrlFactory = {
    create: (file) => {
      const url = `blob:${file.name}#${++seq}`;
      created.push(url);
      alive.add(url);
      return url;
    },
    revoke: (url) => {
      revoked.push(url);
      alive.delete(url);
    },
  };

  /** 去掉流水號，讓斷言讀得出是哪一張。 */
  const names = (urls: string[]) => urls.map((u) => u.slice('blob:'.length).split('#')[0]);

  return { factory, created, revoked, alive, names };
}

const files = (count: number): File[] =>
  Array.from({ length: count }, (_, i) => ({ name: `${i}.jpg` }) as unknown as File);

describe('ObjectUrlWindow', () => {
  it('只為目前這張和前後各一張建立 URL', () => {
    const { factory, created } = trackingFactory();
    const w = new ObjectUrlWindow(1, factory);

    const list = files(100);
    w.sync(list, 50);

    expect(created).toHaveLength(3);
    expect(w.get(list[49])).toBeDefined();
    expect(w.get(list[50])).toBeDefined();
    expect(w.get(list[51])).toBeDefined();
    expect(w.get(list[52])).toBeUndefined();
  });

  it('往前播時把離開視窗的那張釋放掉，留在視窗內的不重建', () => {
    const { factory, revoked, created, names } = trackingFactory();
    const w = new ObjectUrlWindow(1, factory);
    const list = files(100);

    w.sync(list, 50);
    w.sync(list, 51);

    expect(names(revoked)).toEqual(['49.jpg']);
    expect(names(created)).toEqual(['49.jpg', '50.jpg', '51.jpg', '52.jpg']);
    expect(w.size).toBe(3);
  });

  it('連續播一整輪之後同時存活的 URL 仍然只有三個', () => {
    // 這才是真正要防的那件事：一次 createObjectURL 全部，一千張就同時釘住一千個檔案。
    const { factory, alive } = trackingFactory();
    const w = new ObjectUrlWindow(1, factory);
    const list = files(500);

    for (let i = 0; i < 500; i++) w.sync(list, i);

    expect(alive.size).toBe(3);
  });

  it('留在原地重複 sync 不會重新建立', () => {
    const { factory, created } = trackingFactory();
    const w = new ObjectUrlWindow(1, factory);
    const list = files(10);

    w.sync(list, 5);
    w.sync(list, 5);
    w.sync(list, 5);

    expect(created).toHaveLength(3);
  });

  it('視窗在頭尾會繞回去，循環播放的接縫處才不會卡一下', () => {
    const { factory } = trackingFactory();
    const w = new ObjectUrlWindow(1, factory);

    const list = files(10);
    w.sync(list, 9);

    expect(w.get(list[8])).toBeDefined();
    expect(w.get(list[9])).toBeDefined();
    expect(w.get(list[0])).toBeDefined(); // 循環時的下一張
  });

  it('清單比視窗還小時不會替同一張重複建立', () => {
    const { factory, created } = trackingFactory();
    const w = new ObjectUrlWindow(1, factory);

    w.sync(files(2), 0);

    // 前一張和下一張都繞回這兩張身上，每張仍然只有一個 URL。
    expect(created).toHaveLength(2);
    expect(w.size).toBe(2);
  });

  it('dispose 之後不留任何 URL', () => {
    const { factory, alive } = trackingFactory();
    const w = new ObjectUrlWindow(1, factory);

    w.sync(files(10), 5);
    w.dispose();

    expect(alive.size).toBe(0);
    expect(w.size).toBe(0);
  });

  it('換成空清單等於全部釋放', () => {
    // 換到一個沒有圖片的資料夾時，舊資料夾的檔案不該繼續被釘著。
    const { factory, alive } = trackingFactory();
    const w = new ObjectUrlWindow(1, factory);

    w.sync(files(10), 5);
    w.sync([], 0);

    expect(alive.size).toBe(0);
  });

  it('換播放順序時拿到的是同一張照片的 URL，不是同一個位置的', () => {
    // 開關隨機播放會讓「第 5 個」變成另一張照片。以位置當 key 會把舊照片的 URL 交給
    // 新照片，畫面就顯示錯圖。
    const { factory, names } = trackingFactory();
    const w = new ObjectUrlWindow(1, factory);
    const list = files(10);
    w.sync(list, 5);
    const urlOfPhoto5 = w.get(list[5]);

    const shuffled = [
      list[5],
      list[2],
      list[8],
      list[0],
      list[9],
      list[1],
      list[3],
      list[4],
      list[6],
      list[7],
    ];
    w.sync(shuffled, 0);

    expect(w.get(list[5])).toBe(urlOfPhoto5); // 同一張照片沿用，不重建
    expect(names([w.get(shuffled[1])!])).toEqual(['2.jpg']); // 位置 1 是照片 2，不是舊的照片 6
    expect(w.size).toBe(3);
  });

  it('換播放順序後舊視窗裡用不到的照片會被釋放', () => {
    const { factory, alive } = trackingFactory();
    const w = new ObjectUrlWindow(1, factory);
    const list = files(10);
    w.sync(list, 5); // 視窗：4、5、6

    w.sync([list[5], list[0], list[9], ...list.slice(1, 5), ...list.slice(6, 9)], 0); // 新視窗：8（繞回最後）、5、0

    expect(alive.size).toBe(3);
  });
});
