/**
 * 只為「目前這張前後幾張」持有 blob URL，其餘立刻釋放。
 *
 * 為什麼需要這個而不是一次 `createObjectURL` 全部：每個 blob URL 都會讓瀏覽器釘住那個
 * 檔案直到 revoke，一個幾千張照片的資料夾會同時釘住幾千個。照片資料夾正是最容易很大的
 * 那種資料夾，而輪播任何時刻只看得到一張。
 *
 * 以 File 物件本身當 key，而不是它在清單裡的位置：開關隨機播放會把同一個位置換成另一張
 * 照片，用位置當 key 就會把舊照片的 URL 當成新照片拿出來用。
 *
 * 分成獨立模組是因為釋放錯了不會有任何徵兆 —— 畫面照常播，只是記憶體一路往上，所以這裡
 * 的行為要能被測試直接驗。
 */
export interface ObjectUrlFactory {
  create: (file: File) => string;
  revoke: (url: string) => void;
}

const browserFactory: ObjectUrlFactory = {
  create: (file) => URL.createObjectURL(file),
  revoke: (url) => URL.revokeObjectURL(url),
};

export class ObjectUrlWindow {
  private readonly urls = new Map<File, string>();

  /**
   * @param radius 目前這張的前後各保留幾張。1 = 上一張／這張／下一張，足夠讓下一張先
   *   解碼好、按上一張時也不用重讀。
   */
  constructor(
    private readonly radius = 1,
    private readonly factory: ObjectUrlFactory = browserFactory,
  ) {}

  /**
   * 把持有的 URL 對齊到「files 裡以 center 為中心」的那幾張，多的釋放、缺的建立。
   * files 是播放順序，所以預載的一定是接下來真的會播的那張。
   */
  sync(files: readonly File[], center: number): void {
    if (files.length === 0) {
      this.dispose();
      return;
    }

    const wanted = new Set<File>();
    for (let offset = -this.radius; offset <= this.radius; offset++) {
      // 循環播放時最後一張的下一張是第一張，預載要跟著繞回去，否則每繞一圈就卡一次。
      wanted.add(files[(((center + offset) % files.length) + files.length) % files.length]);
    }

    for (const [file, url] of this.urls) {
      if (!wanted.has(file)) {
        this.factory.revoke(url);
        this.urls.delete(file);
      }
    }

    for (const file of wanted) {
      if (!this.urls.has(file)) {
        this.urls.set(file, this.factory.create(file));
      }
    }
  }

  get(file: File | undefined): string | undefined {
    return file ? this.urls.get(file) : undefined;
  }

  /** 全部釋放。離開頁面或換資料夾時必須呼叫，否則舊資料夾的檔案會一直被釘著。 */
  dispose(): void {
    for (const url of this.urls.values()) this.factory.revoke(url);
    this.urls.clear();
  }

  /** 測試用：目前持有幾個 URL。 */
  get size(): number {
    return this.urls.size;
  }
}
