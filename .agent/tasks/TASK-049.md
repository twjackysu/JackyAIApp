# TASK-049：部署工作流程——不得讓「較舊的 commit」覆蓋正式環境上較新的版本

## Owner
Backend Engineer（CI／部署；只動 `.github/workflows/BuildAndDeployToAzureAppService.yml` 與文件）

## Status
READY

## Priority
HIGH（已發生一次：2026-10-11 約 13:54–14:46 UTC，正式環境被回滾到不含 TASK-047 的舊版，Boss 看到「00685L 沒併入、NTSD 沒計入」）

## Objective
無論多個 PR 以什麼順序、多接近的時間合併，部署流程最後留在正式環境的一定是 `master` 的最新內容，不會回到較舊的版本。

## 事故經過（事實，非推測）
| 時間（UTC） | 事件 |
|---|---|
| 13:15 | #444（TASK-047）合併；run 532 部署成功（正式環境含 TASK-047） |
| 13:50:05 | #445 合併（master＝`1de4bb5`，含 #443 全部提交＋#444）；run 533 **13:50:08 開始、13:53:13 完成** |
| 13:50:07 | #443 也被標為 merged（它的提交是 #445 的祖先，所以 GitHub 視為已合併）；`merge_commit_sha` ＝ **`f77ac7b`（#443 的分支頭，從更舊的 master `24d4c08` 切出，不含 #444／TASK-047）**；run 534 **13:50:10 開始、13:54:10 完成** |
| 13:54 | run 534 比 533 **晚完成**，它部署的是 `f77ac7b` → **正式環境被回滾到沒有 TASK-047 的版本** |
| 14:43 | #446 合併（master＝`31bcbf8`）；run 535 於 14:46:42 完成 → 正式環境回到 master 最新 |

根因＝兩件事疊加：(1) 工作流程沒有 `concurrency`，兩個部署同時跑、誰後完成誰贏；(2) `pull_request: closed` 部署的是該 PR 的 `merge_commit_sha`，而「因祖先關係被標為已合併」的 PR，其 `merge_commit_sha` 是它自己的（可能很舊的）分支頭，不是 master 最新。

## Requirements
1. 在 workflow（或 job）加 `concurrency: { group: production-deploy, cancel-in-progress: false }`：同一時間只有一個部署在跑；GitHub 對排隊中的 run 只保留最新一個（被取代的排隊 run 會顯示 cancelled，這是預期行為，不是失敗）。
2. 由 PR 觸發的 run，checkout 改為 **`master` 在該 run 開始執行當下的最新 commit**（`ref: master`），不再用 `github.event.pull_request.merge_commit_sha`。`workflow_dispatch` 維持 `github.ref`（用來手動部署分支／補部署，行為不變）。
   - 理由：`merged == true` 的 PR，其內容必然已在 master 上；部署 master 最新只會更新、不會回滾。與第 1 點合起來，每個 run 的 checkout 時間都晚於前一個 run 的結束時間，部署順序單調。
   - 保留既有的 `merged == true` 條件（放棄的 PR 不得部署）。
3. 更新根 `CLAUDE.md` 的 Deployment 段：把「It deploys `pull_request.merge_commit_sha`」改為新規則，並把 Gotcha 改成「多個 PR 連續合併時，被取代的排隊 run 會顯示 cancelled；最後一個 run 會部署包含全部內容的 master 最新」。
4. `ARCHITECTURE.md` 不動（由 Tech Lead 負責）。

## Acceptance Criteria
- YAML 語法正確（以 `actionlint` 或等價工具檢查；沙箱沒有的話，PR CI 與 GitHub 對 workflow 的解析即為驗證）。
- 在 PR 描述附上推演：兩個 PR 在 5 秒內先後合併（含「後者的 `merge_commit_sha` 是舊 commit」的情況），逐步說明最後部署的是 master 最新。
- **不得**為了驗證而對正式環境做會回滾的部署；手動 `workflow_dispatch` 只能從 master 執行。
- 合併前請 Boss 看過第 2 點（它改變 CLAUDE.md 記載的既有部署規則）；Boss 不同意則退回，改採「只加 `concurrency`」的最小版本（仍有 `merge_commit_sha` 舊 commit 的風險，需 Boss 以「不要合併已被包含的 PR，直接 close」的操作規則補上）。

## Technical Notes
- 為什麼不只加 `concurrency`：只加排隊的話，#443 的 run 仍會在 #445 的 run 之後執行並部署 `f77ac7b`，一樣回滾。第 2 點才是治本。
- 為什麼不用「檢查 checkout 是否為 master 祖先後才部署」：舊 commit 也是 master 的祖先，擋不住。
- 設計取捨：部署內容可能比該 PR 合併當下**更新**（因為中間又有別的 PR 合併）。這是想要的。
- 範圍外：不改測試步驟、不改 publish 設定、不引入 staging 環境（若要，另案並由 Boss 決定成本）。

## Dependencies
無。

## PR
（待 Engineer 填）

## Result
（待填）

## Review
（待填）
