# 0006-用户态update的ffmpeg源切换为BtbN

- **状态**：已采纳
- **日期**：2026-08-18
- **决策者**：architect / 用户

## 背景

CI 已将 ffmpeg 静态构建源从 johnvansickle.com 换成 BtbN/FFmpeg-Builds（GitHub Releases 托管，`05dd661`），但用户态 `/update` 仍从 johnvansickle.com 解析版本并下载（该站不稳定，已知问题）。用户指示 `/update` 同步换源。

换源牵涉三个决策点：版本如何识别（johnvansickle 是语义版本 `7.0.2`，BtbN 无稳定语义版本号）、下载 URL 取法、以及本地/远端版本标度不匹配时的比较语义。

**实测校正（2026-08-18）**：任务简报假设"GitHub API 返回 `tag_name` 为 autobuild 日期"——**不成立**。BtbN 的 `latest` 是真实存在的滚动 release tag（`tag_name` 恒为 `latest`，资产名固定不含日期），`releases/download/latest/<asset>` 直接 302 到 CDN，Location 头无版本号（yt-dlp 的 HEAD 重定向解析模式**不可复用**）；版本信息在 API 响应的 `published_at`（ISO 8601 UTC，如 `2026-08-17T13:29:26Z`）。

## 方案对比

### 版本识别

| 方案 | 优点 | 缺点 |
| ---- | ---- | ---- |
| **A：GitHub API `releases/tags/latest` + `published_at`** | 机器格式无时区歧义；`tags/latest` 精确命中滚动 release；单调递增可数值比较 | 每次 `/update` 消耗 1 次匿名 API 请求（限额 60/hr，充足） |
| B：API + `name` 字段日期（`Latest Auto-Build (2026-08-17 13:05)`） | 人类可读 | BtbN 构建机本地时区与 published_at 不一致（13:05 vs 13:29Z），自然语言格式不稳定 |
| C：固定 URL HEAD 重定向（yt-dlp 同构） | 0 次 API 请求 | **不可行**：`latest` 是真实 tag，302 直达 CDN，Location 无版本号 |
| D：下载后解析包内版本 | 无 API 依赖 | 失去"先比较后下载"短路语义；122MB 白下 |

### 下载 URL

| 方案 | 优点 | 缺点 |
| ---- | ---- | ---- |
| **A：固定 `latest` URL**（资产名恒定） | 少一次 API 请求；与 CI 逐字符一致；URL 永不变化 | 无（资产名不变的前提下） |
| B：API assets 列表取 `browser_download_url` | 更"规范" | 资产名恒定无收益；多一次请求 |

### 本地/远端版本标度不匹配的处置

BtbN master 二进制 `ffmpeg -version` 输出 git 提交计数（`N-118503` → 解析 `[118503]`），远端为日期 `[2026,...]`——数值比较必然误判（`118503 > 2026` → 永远"已是最新"，`/update` 永不更新 ffmpeg）。yt-dlp 无此问题（本地/远端同为日期标度）。

| 方案 | 优点 | 缺点 |
| ---- | ---- | ---- |
| **A：marker 文件**（`<ffmpegPath>.autobuild` 记录上次安装的 autobuild 时间，比较时优先读） | 保留"已是最新"短路现状语义；比较正确；无 marker 时更新一次自愈 | 新增 1 个状态文件；Updater 小改（不重构） |
| B：总是更新 | 实现最简单 | 每次 `/update` 下载 122MB；"已是最新"永不出现 |
| C：本地非日期标度一律视为需更新 | 实现简单 | 行为同 B（本地永远是 git 计数/语义版本），永无短路 |

## 决策

1. **版本识别**：`GET https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/tags/latest`（请求级设 User-Agent，GitHub API 必需），解析 `published_at` → 归一化 `2026.08.17.13.29.26` → `ToolVersion`。选 A 理由：机器格式稳定无歧义、精确命中滚动 release、限流充足。
2. **下载**：固定 `latest` URL（`ffmpeg-master-latest-linux64-gpl.tar.xz` / `ffmpeg-master-latest-linuxarm64-gpl.tar.xz`），放入 `ToolArch` 纯 URL 映射。选 A 理由：资产名恒定、与 CI 一致、少一次请求。
3. **比较语义**：引入 marker 文件方案（A），`IsDateLike`（首分量 ∈ [2000,2100]）标度一致才短路；无 marker 回退二进制解析（仅展示不短路）。
4. **失败处理**：沿用现有 `UpdateFailureReason` 四分类与 i18n 文案（零新增）；新增 xz 魔数校验（`fd377a585a00`，与 CI 一致），下载后解压前校验，失败归 `DownloadFailed`。不做 SHA-256 全量校验（二进制执行校验已兜底完整性）。
5. **职责划分**：`ToolArch`（URL 映射）、`FfmpegToolSource`（发现+下载）、`UriVersionParser`（纯函数解析）、`ToolVersion`（+`IsDateLike`）职责保留；新增 `FfmpegVersionMarker`；`Updater` 小改（marker 读写 + 短路条件）。不重构 Update 模块。

## 后果

- 正面：
  - 摆脱 johnvansickle.com 不稳定源，与 CI 源一致（同一 URL、同一魔数校验）。
  - 保留"已是最新"短路语义（marker 生效后），`/update` 不无谓下载。
  - 错误分类与用户提示零改动，用户可见行为只有版本号格式变化（语义版本 → autobuild 日期）。
- 负面：
  - 每次 `/update` 多 1 次 GitHub API 请求（60/hr 限额，单实例低频，充足）。
  - 首次 `/update`（含镜像内置 seed，无 marker）需下载 122MB（BtbN gpl 包比 johnvansickle 约 40MB 大），之后短路。
  - ffmpeg 版本号显示为 autobuild 时间（如 `2026.08.17.13.29.26`）而非语义版本（如 `7.1.1`）——BtbN 无稳定语义版本号，属源特性。
- 迁移/实现注意：
  - 共享 `HttpClient` 未设 User-Agent，API 请求须请求级设置（勿污染默认头）。
  - `ToolArchTests.FfmpegToolSource_Arm64Injected_RequestsArm64Asset` 的异常断言随魔数校验前置而变（空内容 stub → 报"非 xz 格式"而非"解压失败"）。
  - 写 marker 失败忽略（下次 `/update` 重下，无害）；用户手动替换二进制导致 marker 失真时，删 marker 文件即可自愈。
  - 详见设计文档 `docs/design-update-ffmpeg-btbn.md`（含实现清单与测试影响清单）。