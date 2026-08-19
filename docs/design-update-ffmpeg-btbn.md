# /update ffmpeg 换源 BtbN 设计

- **日期**：2026-08-18
- **状态**：已采纳（见 `docs/adr/0006-用户态update的ffmpeg源切换为BtbN.md`）
- **范围**：用户态 `/update` 的 ffmpeg 版本发现与下载换源（johnvansickle.com → BtbN/FFmpeg-Builds）。只做设计，不实现。
- **配套事实**：CI 已于 `05dd661` 完成同源切换（`docker/dist/{x64,arm64}/ffmpeg` 下载 BtbN 资产）。

## 1. 目标与范围

### 目标

1. `/update` 的 ffmpeg 版本发现与下载切换到 BtbN/FFmpeg-Builds（GitHub Releases 托管），与 CI 源一致，摆脱 johnvansickle.com 不稳定问题。
2. 保留"已是最新则短路"的现状语义（本地版本可比较时不做无谓下载）。
3. 保持错误分类与用户提示不变（`UpdateFailureReason` 四个分类 + 现有 i18n 文案，无新增）。
4. 不加新配置键；不重构 Update 模块整体架构；不动镜像内置 ffmpeg。

### 验收标准

| # | 验收项 | 验证方式 |
| --- | --- | --- |
| 1 | 版本发现走 GitHub API 且解析出 autobuild 时间 | 单测覆盖 `UriVersionParser.ParseGitHubApiPublishedAt`；真实环境 `GetLatestVersionAsync` 返回形如 `2026.08.17.13.29.26` 的版本 |
| 2 | 下载 URL 为 BtbN latest 固定 URL（x64/arm64） | `ToolArchTests` URL 断言更新后全绿 |
| 3 | 连续两次 `/update`，第二次 ffmpeg 显示"已是最新" | 容器内手动验证（marker 文件生效） |
| 4 | 下载产物非 xz 时失败且报下载失败 | 单测：魔数校验分支（stub 返回非 xz 内容 → `DownloadFailed` 语义的异常） |
| 5 | 构建与测试 0 警告 | `dotnet build -c Release`、`dotnet test` |

### 范围边界

- **做**：`ToolArch` URL 替换、`FfmpegToolSource` 版本发现改 API、`UriVersionParser` 解析器替换、ffmpeg 本地版本 marker 文件、xz 魔数校验、相关单测更新。
- **不做**：镜像内置 ffmpeg 的运行时更新功能扩展（镜像 seed 无 marker，首次 `/update` 下载一次属预期）；配置键新增；yt-dlp 源改动；Update 模块重构；GitHub API token 配置（匿名 60/hr 足够）。

## 2. 已确认事实（2026-08-18 实测）

> 与任务简报的假设有两处不符，均已实测校正，见 3.1。

| # | 事实 | 证据 |
| --- | --- | --- |
| F1 | **BtbN 的 `latest` 是真实存在的滚动 release tag**（`tag_name: "latest"`，name 形如 `Latest Auto-Build (2026-08-17 13:05)`），资产名固定不含日期（`ffmpeg-master-latest-linux64-gpl.tar.xz` / `ffmpeg-master-latest-linuxarm64-gpl.tar.xz`） | `GET /repos/BtbN/FFmpeg-Builds/releases/tags/latest` |
| F2 | `releases/download/latest/<asset>` **直接 302 到 CDN 签名 URL**（`release-assets.githubusercontent.com`），Location 头**不含版本号** | `curl -sI https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-linux64-gpl.tar.xz` |
| F3 | 与 yt-dlp 对比：yt-dlp 的 `releases/latest/download/yt-dlp` 是 GitHub **虚拟别名**，第一步 302 到 `/releases/download/2026.07.04/yt-dlp`（含版本）→ 现有 `ParseGitHubRedirectLocation` 可解析 | 同上命令对 yt-dlp URL；现有 `YtDlpToolSource.GetLatestVersionAsync` |
| F4 | API `published_at` 为 ISO 8601 UTC（如 `2026-08-17T13:29:26Z`），单调递增，可作版本标识；`tag_name` 恒为 `latest` **无日期，不可用** | `GET /repos/BtbN/FFmpeg-Builds/releases/tags/latest` |
| F5 | API `name` 含构建日期（`Latest Auto-Build (2026-08-17 13:05)`）但为 BtbN 构建机本地时区（13:05 与 published_at 的 13:29Z 不一致），有歧义 | F4 同响应对比 |
| F6 | 本地 BtbN master 二进制的 `ffmpeg -version` 首行形如 `ffmpeg version N-118503-g2b46d3311f`，现有 `BinaryVersionParser.ParseFfmpeg` 解析为 `[118503]`（git 提交计数）——**与远端 autobuild 日期标度不同** | BtbN 资产命名规律 + `BinaryVersionParser.ParseFfmpeg` 正则行为推演 |
| F7 | GitHub API 匿名限流 60 次/小时；**要求请求带 User-Agent**（缺失返回 403） | GitHub API 文档；`AppHost` 共享 `HttpClient`（`new HttpClient { Timeout = 110s }`）**未设置 UA** |
| F8 | 资产体积：`ffmpeg-master-latest-linux64-gpl.tar.xz` 约 **122MB**（johnvansickle 静态包约 40MB）；CI 已在使用同一 URL 且验证通过 | API assets 列表 |
| F9 | 现有 Updater 对下载产物**无格式校验**（解压失败/二进制执行失败可兜底）；CI 用 xz 魔数 `fd377a585a00` 校验 | `FfmpegToolSource.DownloadBinaryAsync`；`release.yml` |

## 3. 方案设计

### 3.1 版本发现（核心决策）：GitHub API `releases/tags/latest` 的 `published_at`

**任务简报假设 `tag_name` 是 autobuild 日期——实测不成立**（F1/F4：`tag_name` 恒为 `latest`）。版本标识改用 `published_at`（F4），语义一致：autobuild 日期时间，单调递增，可数值比较。

```
GET https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/tags/latest
（请求级设置 User-Agent，见 F7）
→ 提取 "published_at": "2026-08-17T13:29:26Z"
→ 归一化 "2026.08.17.13.29.26" → ToolVersion [2026,8,17,13,29,26]
```

| 方案 | 做法 | 优点 | 缺点 |
| ---- | ---- | ---- | ---- |
| **A（推荐）：API `releases/tags/latest` + `published_at`** | 每次 `/update` 1 次匿名 API 请求，解析 `published_at` | 机器格式无时区歧义（F5）；`tags/latest` 精确命中滚动 release（不受未来"每构建一 release"模式影响）；与 yt-dlp 的日期版本模型一致 | 1 次 API 请求/update；依赖 GitHub API 可达 |
| B：API + `name` 字段 | 解析 `Latest Auto-Build (2026-08-17 13:05)` | 人类可读 | BtbN 构建机本地时区，与 published_at 不一致（F5），格式为自然语言不稳定 |
| C：固定 URL HEAD 重定向（yt-dlp 模式） | 复用 `ParseGitHubRedirectLocation` | 0 次 API 请求、与 yt-dlp 完全同构 | **不可行**：BtbN 的 `latest` 是真实 tag，302 直达 CDN，Location 无版本（F2/F3） |
| D：下载后解析包内版本 | 下载 122MB 再 `ffmpeg -version` 得 git 计数 | 无 API | 无法"先比较后下载"，失去短路语义；122MB 白下 |

**限流评估**：每次 `/update` 消耗 1 次（60/hr 限额）。bot 为自托管实例、用户手动触发 `/update`，频率远低于限额；匿名限额按出口 IP 计，自部署各实例独立出口。与 yt-dlp 的"0 次 API"相比多 1 次，属 BtbN 源特性所致（F3），非实现选择。

### 3.2 下载：固定 latest URL（不放 API 资产列表）

```
x64:   https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-linux64-gpl.tar.xz
arm64: https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-linuxarm64-gpl.tar.xz
```

理由：资产名恒定（F1）、tag 恒为 `latest` → URL 永不变化；少一次 API 请求；与 CI（`release.yml` matrix `ffmpeg-url`）逐字符一致。从 API assets 列表取 `browser_download_url` 无收益（资产名不会变），多一次请求，否决。

### 3.3 版本比较语义：本地版本 marker 文件（核心难点）

**问题（F6）**：本地 BtbN master 二进制解析为 git 提交计数 `[118503]`，远端为日期 `[2026,8,17,...]`——标度不同，`CompareTo` 必然误判：

- `118503 > 2026` → 永远误判"已是最新"，**/update 永不更新 ffmpeg**（回归性 bug）；
- 旧 johnvansickle 语义版本 `7.0.2 < 2026` → 永远"有更新"，每次重下 122MB。

yt-dlp 无此问题：本地 `--version` 与远端 tag 同为日期标度，可直接比较。

| 方案 | 做法 | 优点 | 缺点 |
| ---- | ---- | ---- | ---- |
| **A（推荐）：marker 文件** | ffmpeg 更新成功后，把安装的 autobuild 时间写入 `<ffmpegPath>.autobuild`；比较时优先读 marker（日期标度）与远端同标度比较 | 保留"已是最新"短路语义（现状语义不变）；比较正确；无 marker（首次/旧安装）→ 更新一次后自愈 | 新增 1 个状态文件；Updater 需小改（读 marker + 短路标度条件 + 写 marker），不重构架构 |
| B：总是更新（无短路） | ffmpeg 不做"已是最新"判断 | 实现最简单 | 每次 `/update` 下载 122MB（F8），浪费带宽与时间，"已是最新"永不出现 |
| C：标度不一致→更新 | 本地非日期标度一律视为需更新 | 实现简单 | 行为同 B：本地永远是 git 计数/语义版本，永无短路 |

**决策：方案 A**，规则如下：

1. `GetLocalVersionAsync("ffmpeg")`：优先读 `<installPath>.autobuild`（内容为上次安装的归一化 autobuild 版本串，如 `2026.08.17.13.29.26`）→ 解析为 ToolVersion；文件缺失/损坏 → 回退现有二进制解析（结果仅作展示，不参与短路）。
2. 短路条件收紧为**标度一致**：`localVersion.CompareTo(latest) >= 0 && localVersion.IsDateLike == latest.IsDateLike`，其中 `IsDateLike` = 首分量 ∈ [2000, 2100]（日期年份区间）。
   - 生效矩阵：marker 日期 vs 远端日期 → 正常比较短路 ✓；git 计数/语义版本 vs 日期 → 不短路，更新 ✓；yt-dlp 日期 vs 日期 → 行为不变 ✓。
3. 替换成功后写 marker（ffmpeg 分支）：值 = `latest.ToString()`；写失败忽略（下次 `/update` 会重下，无害，不阻塞更新成功）。

### 3.4 失败处理

| 失败场景 | 分类（现有，不变） | 用户提示（现有 i18n，不变） |
| --- | --- | --- |
| API 不可达 / 非 200 / published_at 缺失 / 解析失败 | `LatestVersionUnavailable` | `UpdateFailedLatestVersion` |
| 下载 HTTP 错误 / 非 xz 魔数 / 解压失败 | `DownloadFailed` | `UpdateFailedDownload` |
| 二进制校验失败 / 原子替换失败 | `ReplaceFailed` | `UpdateFailedReplace` |
| 本地版本不可读 | `LocalVersionUnavailable` | `UpdateFailedLocalVersion` |

**新增 xz 魔数校验（推荐，随源切换一并做）**：`DownloadBinaryAsync` 下载完成后、解压前读前 6 字节，须为 `fd377a585a00`，否则抛异常（归 `DownloadFailed`）。理由：与 CI（F9）防护一致，200 坏响应早失败、错误分类清晰；成本约 3 行。现有"解压失败/二进制执行失败"兜底保留（防御纵深）。不做 SHA-256 全量校验（BtbN 有 `checksums.sha256` 资产，但需额外请求且二进制执行校验已覆盖完整性语义）。

> 备注（2026-08-19 线上修复）：解压走 `tar -xf`，GNU tar 解 `.tar.xz` 需调用外部 `xz` 命令——Dockerfile apt 层须安装 **`xz-utils`**（tar/gzip 为 Debian required 包，slim 自带），非 Docker 部署需系统安装 `xz-utils`。

### 3.5 职责划分（不重构）

| 组件 | 职责 | 改动 |
| --- | --- | --- |
| `ToolArch` | 纯 URL 映射（保留） | `FfmpegReleaseUrl` 换 BtbN URL，注释注明 BtbN 命名 `linux64`/`linuxarm64` 对应 x64/arm64 |
| `FfmpegToolSource` | 版本发现 + 下载（保留） | `GetLatestVersionAsync` 改调 API（请求级设 User-Agent）；`DownloadBinaryAsync` 加魔数校验；常量 `HomePageUrl` → `ApiUrl` |
| `UriVersionParser` | 纯函数解析（保留） | 删 `ParseJohnVanSickleReleasePage`；新增 `ParseGitHubApiPublishedAt`（输入 API JSON 字符串） |
| `FfmpegVersionMarker`（新增，`Update` 命名空间） | marker 读写 | `TryRead(installPath, out ToolVersion)` / `Write(installPath, version)`，纯文件操作 |
| `Updater` | 更新执行（小改，不动架构） | `GetLocalVersionAsync` ffmpeg 分支优先读 marker；短路条件加 `IsDateLike` 标度一致性；替换成功后写 marker |
| `ToolVersion` | 版本模型（小改） | 新增只读属性 `IsDateLike`（首分量 ∈ [2000,2100]） |

### 3.6 实现清单（developer 可直接执行）

1. `src/TGBot/Update/ToolArch.cs`：`FfmpegReleaseUrl` 两分支换 URL（见 3.2），XML 注释更新（BtbN 命名对应关系、`latest` 滚动 tag 说明）。
2. `src/TGBot/Update/ToolSource.cs`：
   - `FfmpegToolSource`：常量改为 `ApiUrl = "https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/tags/latest"`；`GetLatestVersionAsync` 用 `HttpRequestMessage` GET（请求级 `Headers.UserAgent`，勿污染共享 HttpClient 默认头）→ 非 200 返回 `null` → `GetStringAsync` 结果交 `UriVersionParser.ParseGitHubApiPublishedAt`；
   - `DownloadBinaryAsync`：下载后 `FileStream` 读前 6 字节，与 `fd 37 7a 58 5a 00` 比较，不符抛 `InvalidOperationException("ffmpeg 下载内容非 xz 格式")`（自然归 `DownloadFailed`）。
3. `src/TGBot/Update/UriVersionParser.cs`：删 `ParseJohnVanSickleReleasePage`；新增 `ParseGitHubApiPublishedAt(string? json)`：正则 `"published_at"\s*:\s*"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})"` 提取 → `-`/`T`/`:` 替换为 `.` → `ToolVersion.TryParse`；解析失败返回 `null`。
4. `src/TGBot/Update/ToolVersion.cs`：新增 `public bool IsDateLike => _components.Length > 0 && _components[0] is >= 2000 and <= 2100;`（XML 注释：首分量在 2000-2100 视为日期标度，如 autobuild 年份）。
5. 新增 `src/TGBot/Update/FfmpegVersionMarker.cs`（static class）：marker 路径 = `Path.GetFullPath(installPath) + ".autobuild"`；`TryRead` 读文本 → `ToolVersion.TryParse`，缺失/损坏返回 false；`Write` 先写 `{path}.tmp` 再 `File.Move(overwrite: true)`（原子性）。
6. `src/TGBot/Update/Updater.cs`：
   - `GetLocalVersionAsync`：`name == "ffmpeg"` 时先 `FfmpegVersionMarker.TryRead(installPath, out var marked)`，命中直接返回 marked；
   - 短路条件（第 129 行附近）改为：`localVersion is not null && localVersion.CompareTo(latest) >= 0 && localVersion.IsDateLike == latest.IsDateLike`；
   - 替换成功后（`AtomicFileReplacer.Replace` 之后、返回前）ffmpeg 分支 `FfmpegVersionMarker.Write(installPath, latest)`，包 try/catch 忽略失败。

## 4. 测试影响清单

### 需修改

| 测试 | 改动 |
| --- | --- |
| `ToolArchTests.FfmpegReleaseUrl_MatchesArchAsset` | 期望值改 BtbN URL（x64 `.../latest/ffmpeg-master-latest-linux64-gpl.tar.xz`、arm64 `.../latest/ffmpeg-master-latest-linuxarm64-gpl.tar.xz`） |
| `ToolArchTests.FfmpegToolSource_Arm64Injected_RequestsArm64Asset` | `LastRequestUrl` 断言改 BtbN arm64 URL；**注意**：stub 返回空内容，若已实现魔数校验则异常信息变为"非 xz 格式"而非"解压失败"——断言相应调整（或保留解压失败断言则需 stub 返回含 xz 魔数的最小内容，见 4 新增项） |
| `UpdaterTests.UriVersionParserTests.ParseJohnVanSickleReleasePage_*`（2 个） | 删除，替换为 `ParseGitHubApiPublishedAt` 用例 |

### 需新增

| 测试 | 内容 |
| --- | --- |
| `UriVersionParserTests.ParseGitHubApiPublishedAt` 系列 | 标准 JSON（`"published_at":"2026-08-17T13:29:26Z"` → 与 `ToolVersion.Parse("2026.08.17.13.29.26")` 相等）；缺字段/坏格式/`null` → `null` |
| `ToolVersionTests.IsDateLike` 系列 | `2026.08.17...` → true；`7.1.1`/`118503` → false；并入 `ToolVersionTests` |
| `FfmpegVersionMarkerTests`（新文件） | 写后读回相等；缺失返回 false；损坏内容返回 false；跨目录路径含空格正常 |
| `FfmpegToolSource` 魔数校验 | stub 返回 6 字节 `fd377a585a00` + 任意尾部 → 走到解压（`FailingProcessRunner` 抛"解压失败"）；stub 返回非 xz 内容 → 抛"非 xz 格式"（`ToolArchTests` 内已有 `StubHttpHandler` 可扩展返回内容，或新增带内容 stub） |

### 无影响（回归确认）

- `ToolVersionTests.CompareTo/TryParse`、`BinaryVersionParserTests`：语义不变（`ParseFfmpeg` 对 `N-118503` 的解析行为不变，仅短路判断不再依赖它）。
- `UpdaterTests` 其余（`AtomicFileReplacer`）、`YtDlpToolSource` 相关、`MessageRouterTests` fakes：不动。
- `UpdaterIntegrationTests`：可加 `FfmpegToolSource` 版本发现+下载集成用例（真实网络，不可达静默跳过），与 `UpdateYtDlp_DownloadsAndParsesVersion` 对称——可选。

### 集成验证（容器内手动）

1. 首次 `/update`：ffmpeg 下载 BtbN 122MB 资产、解压、替换成功；`<ffmpegPath>.autobuild` 生成。
2. 再次 `/update`：ffmpeg 显示"已是最新（2026.08.17...）"，无下载。
3. 断网/API 不可达时 `/update`：ffmpeg 提示"无法获取最新版本"，yt-dlp 不受影响。

## 5. 遗留与风险

| 项 | 说明 | 处置 |
| --- | --- | --- |
| 首次 `/update`（无 marker）下载 122MB | 含镜像内置 seed（无 marker）场景 | 接受：一次成本，之后短路；如需消除可让 CI 构建镜像时写入 seed ffmpeg 的 marker——**本次不做**（镜像侧改动，超出本次范围） |
| BtbN 若恢复"每构建一 release"模式 | `tags/latest` 仍指向滚动 `latest` release（资产命名不变） | 设计天然兼容，无需改 |
| API 限流 60/hr | 单实例、`/update` 低频，远低于限额 | 接受；失败时有 `LatestVersionUnavailable` 清晰提示 |
| 下载体积增大（~40MB → 122MB） | BtbN gpl 静态包更大 | 与 CI 一致；gpl 版含完整组件，功能不受影响 |
| marker 与二进制不一致（用户手动替换二进制） | 比较可能失真 | 罕见场景；marker 文件可手动删除自愈（下次 `/update` 更新） |
| GitHub API 403（缺 User-Agent） | 共享 HttpClient 无 UA（F7） | 请求级设置 UA（实现清单第 2 项），不动共享 client |