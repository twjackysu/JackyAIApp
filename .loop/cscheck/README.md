# cscheck — 離線 C# 型別檢查（迴圈專用，合併前整個 `.loop/` 刪掉）

雲端 session 連不到 NuGet，`dotnet build` 不能用；GitHub Actions 又因帳單停擺（D-016）。這個工具用 SDK 內附的 Roslyn
直接編譯 `JackyAIApp.Server` 與 `JackyAIApp.Server.Tests` 的原始碼，抓出 CI 會抓的大部分編譯錯誤與警告（含 nullable、
`CS1574` cref）。

```bash
sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0   # 約 1 分鐘
CSCHECK_OUT=/tmp/cscheck .loop/cscheck/run.sh                     # 約 45 秒；第一次會 clone 第三方原始碼
```

- 對 `origin/master`（或第一個參數指定的 ref）與目前工作目錄各編譯一次，**只列出新出現的診斷**（以 檔案＋代碼＋訊息 比較，行號另附）。
- 有原始碼的第三方套件當參考編譯：Anthropic SDK（tag `Anthropic-v12.60.0`，跟 csproj 一致）、xunit 的 Assert、Moq（v4.20.72）、EF Core（v10.0.13 的 EFCore／Relational／SqlServer／InMemory，部分型別仍綁不到，例如 migration 的屬性）；`stubs/Xunit.cs` 是 Fact／Theory 等屬性的樁。
- 其餘套件（Betalgo、TWStockLib、ModelContextProtocol…）沒有型別，相關用法會變成「找不到名稱」；那些名稱只彙總成一行，自己判斷是不是打錯本站的型別。用到這些套件的程式碼要特別小心，仍以 CI 為準。
- 也就是說：輸出空白 ≠ 保證 CI 綠，但大部分打錯字、型別不合、nullable 警告、cref 錯誤都抓得到。
