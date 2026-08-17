# tgdl-bot 开源合规 + i18n + 安装向导 + Bot 内配置管理 设计方案

- **状态**：已定稿（待评审）
- **日期**：2026-08-17
- **决策者**：architect
- **关联 ADR**：`docs/adr/0001-i18n-架构.md`、`docs/adr/0002-bot-配置管理.md`、`docs/adr/0003-install-交互式安装向导.md`
- **分支**：feat/oss-i18n（基线 main dd8e557，含 agent 体系）

---

## 0. 现状盘点（设计前置事实）

| 项 | 现状 |
| --- | --- |
| 文案 | `src/TGBot/Texts/UserTexts.cs` 39 条 `const string`（中文，部分含 `{0}` 占位符），全仓库直接引用；另有散落硬编码中文：`MessageRouter.cs`（"该链接正在处理中…"×2）、`CommandHandler.cs`（"更新完成："、cookie 状态行、版本/字节格式化）、`DownloadCoordinator.cs`（"部分会话失败…"）、`CaptionBuilder.cs`（"标题：/来源："）、`ConfigParser.cs`/`ConfigLoader.cs`/`AppHost.cs` 启动错误与日志 |
| 配置 | `AppConfig` 26 个可配置键 + `SourcePath`；`ConfigParser` 静态解析，`Validate()` 为 private；`RequiredKeys` 5 项；错误消息硬编码中文 |
| 持久化 | 容器卷 `tgdl-data` 挂 `/opt/tgdl-bot/api-data`（cookie 目录 `api-data/cookies` 在此卷内，v2.3.1 起）；`compose.yaml` `restart: unless-stopped`；`docker-entrypoint.sh` **仅在 config.conf 不存在时**由 TGDL_* 生成，已存在则直接使用 |
| 重启链路 | bot 进程退出（entrypoint `wait` 捕获）→ 容器退出 → `unless-stopped` 自动拉起 → entrypoint 重新执行（config.conf 已存在则不改写） |
| 访问控制 | `AccessControlService` 构造时接收两个集合（AllowedUserIds / TargetChannelIds），纯内存 `HashSet` |
| 测试 | `ConfigParserTests.cs` 仅断言异常类型不断言消息文本；`FormatUnavailableTests.cs`/`CookieTests.cs` 用 `UserTexts.*` 构造 `DownloadException.UserMessage`（i18n 后需适配） |
| install.sh | 非交互：下载 `.env.example` 作模板 → 提示手动填写 → compose/docker run 启动 |

关键结论：

1. **entrypoint 不会覆盖已存在的 config.conf** → bot 写回 config.conf 在"容器重启"场景不丢；但**用户改 .env 后重建容器**时，若 config.conf 已存在，entrypoint 不会重新生成，用户的 .env 改动会"看似不生效"——这是现有机制本身的坑，overlay 设计必须绕开。
2. 测试对错误消息无文本断言 → 双语化 ConfigParser 错误不破坏现有测试（新增测试需注意断言策略）。
3. `restart: unless-stopped` 已就绪 → 配置改动"进程退出→容器重启"链路零改动（只需 bot 优雅退出）。

---

## 1. 开源合规（GPL v3）

### 1.1 LICENSE 文件

- 新增根目录 `LICENSE`：GPL-3.0 全文（官方标准文本 "GNU GENERAL PUBLIC LICENSE Version 3, 29 June 2007"，由 devops 从 https://www.gnu.org/licenses/gpl-3.0.txt 获取，勿手写）。
- 配套新增 `NOTICE`（或 `THIRD-PARTY-NOTICES`）：声明项目内引用的第三方组件及其许可：
  - yt-dlp（The Unlicense）
  - ffmpeg 静态构建（LGPL/GPL，含 `--enable-gpl` 配置差异说明）
  - telegram-bot-api（GPL-3.0，fork `linkcccp/telegram-bot-api`）
  - .NET Runtime（MIT）、Telegram.Bot（MIT）、docfx 等构建/文档工具
  - 进程级调用（`Process.ArgumentList`）不构成衍生作品，NOTICE 仅作透明性声明。

### 1.2 源文件头版权注释：**建议加 SPDX 头**

| 方案 | 优点 | 缺点 |
| --- | --- | --- |
| A：仅 LICENSE + README 徽章 | 改动最小；GPL-3.0 第 5 节仅要求"适当的版权声明" | 单个文件来源不透明，审查/贡献者体验差；与 REUSE 规范不兼容 |
| B：全源文件加 SPDX 头 + LICENSE | 机器可读（`SPDX-License-Identifier`），开源审查友好；逐文件版权归属清晰 | 一次性批量改动所有源文件（脚本可完成）；文件头稍长 |

**决策：选 B**。理由：项目即将开源面向国际贡献者，SPDX 头是事实标准（REUSE），成本仅一个批量脚本。格式：

```csharp
// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp
```

- shell 脚本（`docker/*.sh`、`scripts/*.sh`）用 `# SPDX-License-Identifier: GPL-3.0-only`。
- **GPL-3.0-only**（非 or-later）：与 LICENSE 文件精确一致，避免"未来版本条款"歧义。
- 排除：`third_party/`（上游代码，保持其自身许可）、构建产物、docs 输出。

### 1.3 README 免责声明（新章节「法律与合规声明」）

在 README 开头（快速开始之前）以醒目 blockquote 放置简短声明，文末放完整章节：

- 下载器仅用于**个人合法用途**；各国版权法差异巨大，使用者须自行确认其司法辖区合法性
- 用户须**遵守目标站点服务条款（ToS）**与 robots 要求；绕过访问控制可能违反法律
- **侵权风险自负**：本项目不存储任何媒体内容，不提供任何内容检索/分发服务
- 版权方可通过仓库 Issue/SECURITY.md 渠道要求移除（项目本身不托管内容，一般无可移除对象，声明仍保留以示流程）
- 参考链接：yt-dlp legality 说明（https://github.com/yt-dlp/yt-dlp#legal）

### 1.4 SECURITY.md

新增根目录 `SECURITY.md`（GitHub 自动识别）：

- **受支持版本**：最新 release（`v*` tag）
- **上报渠道（优先）**：GitHub Security Advisory（仓库 → Security → Report a vulnerability）
- **后备**：邮件 `linkzengyaoxiang@outlook.com`（与 git 作者一致），注明 "SECURITY"
- **预期响应**：5 个工作日内确认；确认后 90 天内修复并发布
- **不接收**：bot 滥用举报（走免责声明流程）、第三方站点版权投诉

### 1.5 Telegram Bot API 条款与站点合规说明（README 章节规划）

README「法律与合规声明」下设小节：

1. **Telegram Bot API 条款**：bot 开发者须遵守 [Telegram Bot API 服务条款](https://telegram.org/tos/bots) 与 [Telegram 服务条款](https://telegram.org/tos)；本项目仅提供下载工具，bot 的部署与使用行为由部署者自行负责
2. **站点合规**：yt-dlp 生态声明（尊重目标站点 ToS/robots）；`/cookie` 上传仅用于访问用户已授权内容，不得用于规避付费墙
3. **滥用风险提示**：高频下载可能触发 Telegram 风控（封号）或站点 IP 封禁，部署者自行承担

---

## 2. i18n 架构（中英双语，可扩展）

### 2.1 资源组织：自定义字典 + 嵌入式 JSON + 注册 API

| 方案 | 优点 | 缺点 |
| --- | --- | --- |
| A：.resx + ResourceManager | .NET 标准；VS 工具支持；强类型访问可生成 | 自包含单文件（`PublishSingleFile`）下 satellite assembly 需 `IncludeAllContentForSelfExtract` 等额外处理；`InvariantGlobalization=true` 下文化行为需验证；加语言要重新编译并涉及资源管线；不支持运行时注册 |
| B：自定义字典 + 嵌入式 JSON + `LanguageCatalog.Register` | 无资源管线依赖，单文件发布零障碍；注册式 API 天然支持第三方加语言（满足"不允许硬编码 switch"）；JSON 键值直观、diff 友好；测试简单 | 无 VS 工具支持（本项目无 UI，无影响）；需自写加载与格式化 |
| C：外部 JSON 文件目录（热加载） | 加语言不重新编译 | 多一个配置键（语言目录路径）+ 文件系统安全面；启动期 IO 依赖；本项目无插件体系，收益低 |

**决策：选 B**。内置 `en`/`zh` 两个嵌入式 JSON 资源；对外提供注册 API（`LanguageCatalog.Register(lang, IReadOnlyDictionary<string,string>)`），其他开发者可用一行代码注册新语言（或提 PR 增加嵌入式资源）。拒绝 C：无插件需求，避免膨胀（若未来需要，可在 B 之上加目录加载器，接口不变）。

### 2.2 键与调用契约

- `UserTexts` 类**保留**，但语义从"文案常量"变为**键常量容器**：`public const string Queued = "Queued";`（39 条逐一转为键名，消除魔法字符串）。
- 新接口（`src/TGBot/Texts/I18n/`）：

```csharp
public interface II18n
{
    string Get(string lang, string key, params object[] args); // 键缺失 → 回退 en → 回退键名本身
    string T(string key, params object[] args);                // 当前上下文语言的便捷方法
}

public static class LanguageCatalog
{
    public static void Register(string lang, IReadOnlyDictionary<string, string> entries); // 覆盖/新增语言
    public static IReadOnlyCollection<string> SupportedLanguages { get; }                  // 内置 "en","zh" + 已注册
    public static string NormalizeLanguageCode(string? bc47);                              // zh-CN/zh-TW→zh, en-US→en, 其余→null
    public static string FallbackLanguage { get; }                                         // 恒 "en"
}
```

- `{0}` 占位符：`Get` 内部 `string.Format`（`InvariantGlobalization` 下数字格式固定为 invariant，占位符替换语义确定）。
- 语言有效值枚举：`BotLanguage`（`en`/`zh`），解析非法值报配置错误。

### 2.3 语言解析链（优先级从高到低）

```
私聊用户：
  1. per-user 显式选择（UserLanguageStore，/language 或首次弹窗选择）
  2. Telegram user.language_code（BCP 47）→ LanguageCatalog.NormalizeLanguageCode
  3. 全局默认 TgdlLanguage（auto 时跳到 4）
  4. 内建 fallback："en"

群组/频道触发（无用户 language_code）：
  1. 全局默认 TgdlLanguage（auto → 2）
  2. 内建 fallback："en"

启动期/无消息上下文（ConfigParser 错误、日志）：
  1. TgdlLanguage（auto → "en"）
```

- **auto 语义**：跟随用户语言（私聊）/跟随全局（群组）。默认 `auto` → 中文用户（language_code=zh-*）升级后无感，国际用户默认英文。**auto 的最终解析语言不落盘**（只存显式选择），避免 language_code 过期污染。

### 2.4 per-user 语言存储

- 位置：`StateDir/languages.json`（`StateDir` 见 §5，容器内为 tgdl-data 卷内 → 跨重建持久）。
- 格式：`{"123456789": "zh", "987654321": "en"}`（userId → 语言，仅存**显式选择**）。
- 实现：启动加载入内存 `ConcurrentDictionary<long,string>`；写入原子化（临时文件 + `File.Move(overwrite)`），单次写全量（规模：数百用户 < 数十 KB，可接受）。
- 新增 `UserLanguageStore`（`src/TGBot/Texts/I18n/UserLanguageStore.cs`）：`Get(long userId)` / `Set(long userId, string lang)`。

### 2.5 首次私聊自动弹语言选择

- 触发：私聊收到**任意**消息（含指令），且 `UserLanguageStore` 无该用户记录。
- 行为：先按 language_code 映射处理当前消息（不阻塞），同时附加语言选择键盘（`lang:zh` / `lang:en` 回调）。用户点击 → 写入存储 → 回执确认（双语）。
- 回调数据格式 `lang:<code>`，在 `MessageRouter` 中与 `dl:` 并列处理；仅允许点击者本人生效（沿用 `PendingChoice` 的校验模式）。
- 超时（2 分钟）未点：仅丢弃回调，语言沿用映射值，不打扰。

### 2.6 语言上下文传播（关键设计）

**语言在消息入口解析一次，随消息流动**：

- `InboundMessage` 新增 `public string Language { get; init; } = "en";`（入口由 `UserLanguageResolver` 填充）。
- 所有回复/通知使用 `msg.Language` 渲染；后台任务（下载完成通知）沿用**触发消息的语言**（`DownloadCoordinator.ProcessJobAsync` 已持有 `InboundMessage msg`，直接透传）。
- 不使用 AsyncLocal 隐式上下文：显式传参可测试、可追溯、无泄漏风险。

受影响模块（developer 逐文件迁移，详见 §7.1）：

| 文件 | 改动 |
| --- | --- |
| `Texts/UserTexts.cs` | const 文案 → 键常量；文案移入 `Texts/I18n/Resources/`（`en.json`、`zh.json`） |
| `Texts/I18n/`（新增） | `II18n`、`I18nService`、`LanguageCatalog`、`UserLanguageStore`、`UserLanguageResolver`、`BotLanguage` |
| `Messaging/ITelegramClient.cs` | `InboundMessage.Language` 字段 |
| `Messaging/TelegramClientWrapper.cs` | 从 `User.language_code`/`Chat` 填充 Language |
| `Messaging/CaptionBuilder.cs` | `Build(title, sourceUrl, lang)` 或注入 II18n；"标题/来源"文案入资源 |
| `Application/MessageRouter.cs` | 硬编码文案替换；首次语言弹窗；回调处理 |
| `Application/CommandHandler.cs` | 硬编码文案替换；新增 `/language` 命令 |
| `Application/DownloadCoordinator.cs` | `string.Format(UserTexts.X,…)` → `i18n.Get(msg.Language, …)`；硬编码后缀替换 |
| `Access/AccessControlService.cs` | 拒绝文案按调用方语言（`Evaluate` 增加 lang 参数或返回键） |
| `Cookie/CookieService.cs` | 结果消息按 `msg.Language` |
| `Config/ConfigParser.cs` | 错误消息双语化（见 2.7） |
| `Application/AppHost.cs` | 装配 II18n；ConfigParser 语言参数 |

### 2.7 ConfigParser 错误提示双语化

- 场景特殊性：启动期**无用户上下文**，用户也无法通过 bot 命令自救（配置加载失败 → 进程起不来）。
- **决策：错误消息双行并列**（中文行 + 英文行），例如：

  ```
  配置错误：配置项 MaxConcurrentDownloads 必须在 1 到 16 之间。
  Config error: MaxConcurrentDownloads must be between 1 and 16.
  ```

- 理由：任何语言用户都能读懂其一；不依赖语言探测（避免启动期鸡生蛋问题）；`ConfigLoader`/`AppHost` 前缀提示（"配置错误：/Config error:"）同步双行。
- 测试策略：断言包含中文或英文片段其一（如 `Assert.Contains(ex.Message, "MaxConcurrentDownloads")` 不断言全文本）。

### 2.8 /language 命令

- `/language` → 发送语言选择键盘（与首次弹窗同款）；`/language en`、`/language zh` 直接设置（方便脚本化）。
- 仅私聊、仅白名单用户（沿用现有指令入口检查）。
- 帮助文本（`/help`）与 BotCommand 菜单（`TelegramClientWrapper.SetCommandsAsync`）同步新增 `/language` 与 `/config`、`/access`（见 §4.6）。

---

## 3. install.sh 交互式安装向导

### 3.1 总体决策

| 决策点 | 方案 |
| --- | --- |
| 交互输入源 | **`/dev/tty` 专用**（`read -r ... < /dev/tty`）。`curl … \| sudo bash` 时 bash 的 stdin 是管道（curl 输出），直接 `read` 会误读脚本内容 |
| 无 TTY 降级 | `[[ -r /dev/tty ]]` 失败 → 保持现有非交互路径（下载 .env.example 模板 + 提示手动填写），输出明确提示"检测到非交互环境" |
| 幂等 | 重复执行：.env 已存在且所有必填项非空 → 跳过向导直接启动；否则进入向导（重问缺失项） |
| 语言选择 | 第一步菜单（1. 中文 / 2. English）：决定向导提示语言（`WIZ_LANG`），并**同时写入 `TGDL_LANGUAGE`**（建议：向导语言 = 部署者语言 → 作为 bot 全局默认语言，语义一致；用户后续可用 /language 或改 .env 调整） |

### 3.2 校验规则（就地重问，单项超 5 次跳过）

| 项 | 规则 | 失败提示示例 |
| --- | --- | --- |
| TGDL_BOT_TOKEN | 正则 `^\d+:[A-Za-z0-9_-]+$`，长度 ≤ 200 | Token 格式应为 `123456:ABC…`（数字:字母数字下划线） |
| TGDL_TARGET_CHANNELS | 逗号分隔整数（允许负数，如 `-100123…`），至少 1 个，去重 | 频道 ID 应为整数，多个用英文逗号分隔 |
| TGDL_ALLOWED_USERS | 逗号分隔**正整数**，至少 1 个，去重 | 用户 ID 应为正整数 |
| TGDL_API_ID | 正整数（int 范围） | API ID 应为正整数（my.telegram.org 获取） |
| TGDL_API_HASH | 正则 `^[0-9a-fA-F]{32}$` | API Hash 应为 32 位十六进制 |

- 单项连续失败 ≥ 5 次 → 跳过（`SKIPPED+=("TGDL_BOT_TOKEN")`），其余项继续；全部结束后汇总提示：**"以下项未填写，请稍后手动编辑 /opt/tgdl-bot/.env 后执行 docker compose up -d 重启"**，脚本仍继续启动（缺必填项时 entrypoint 会失败并给出明确错误，符合"跳过提示稍后手动填写"语义）。
- 校验失败重问时回显当前值（`${VAR:-(未填写)}`）便于对照。

### 3.3 写 .env 与启动

- 保留 `curl -fsSL …/.env.example` 模板下载（非交互降级路径仍用）；交互路径：下载模板 → `sed` 替换 5 个必填项（占位值 → 用户输入，注意转义）→ 追加 `TGDL_LANGUAGE=<向导语言>`（仅交互路径写，默认 auto 时省略）→ `chmod 600`。
- 用户确认（y/N）后执行：`docker compose up -d`（存在 compose 时）→ 失败降级 `docker run`（沿用现有兜底）；`docker image prune -f` 保留。
- 启动后输出"修改配置的后续步骤"提示（编辑 .env → compose up -d）。

### 3.4 流程伪代码

```
main:
  root 检查（沿用现有 die 提示）
  docker 检测/自动安装（沿用现有逻辑）
  if ! [[ -r /dev/tty ]]; then
      warn "非交互环境，跳过向导；将生成 .env 模板供手动填写"
      执行现有非交互路径（下载模板/拉镜像/启动/提示）；exit 0
  fi
  WIZ_LANG = 菜单选择(中文/English)          # 1..2，非法重问（≤5 次，默认 1）
  for key in BOT_TOKEN TARGET_CHANNELS ALLOWED_USERS API_ID API_HASH:
      ok = false
      for attempt in 1..5:
          prompt(key, 回显当前值); read -r val < /dev/tty
          if validate(key, val): ok=true; break
          else: err(校验规则); continue
      ok ? 写入内存 : SKIPPED += key
  if SKIPPED 非空: warn(汇总，提示稍后手动填写)
  模板下载（如 .env 不存在）→ sed 替换必填项 → 追加 TGDL_LANGUAGE=$WIZ_LANG → chmod 600
  pull 镜像 → compose up -d（docker run 兜底）→ image prune
  输出使用提示（含 SKIPPED 项手动填写说明）
```

---

## 4. Bot 内配置管理

### 4.1 可改范围

- **可改**：`AppConfig` 除 `BotToken`、`StateDir` 外的全部键（TGDL_API_ID / TGDL_API_HASH **不在** bot 配置内——它们只被 docker-entrypoint.sh 用于 tba，bot 无感知、不可改、无需改）。
- **不可改（需求约束）**：`AllowedUserIds`、`TargetChannelIds`（安装配置的白名单）；bot 通过 `/access` 维护**独立新增列表**。
- **不可改（锁键）**：`StateDir`——overlay/languages/pending-notify 文件自身存放于 StateDir，若允许覆盖会造成"状态目录漂移、overlay 读取位置不一致"的鸡生蛋问题；实现列为 `ConfigParser.LockedKeys`，`/config set StateDir` 拒绝且不重启（overlay 始终以 config.conf 推导的目录读取）。
- **风险提示类**：`LocalApiBaseUrl`（改错 → 连不上 tba，重启后 bot 起不来，需手动改 .env/删 overlay 恢复）、`YtDlpPath`/`FfmpegPath`/`DownloadTempDir`/`CookieStoreDir`/`LogFile`（路径类）。允许改，但 `/config set` 对路径类回显警告。

可改清单（以 `AppConfig` 源码为准，可改 24 项，按类分组；另含锁键 4 项与内部辅助属性 `SourcePath`）：

| 类别 | 键 |
| --- | --- |
| 常规 | LogLevel、MaxConcurrentDownloads、DownloadRetries、UploadRetries、DownloadTimeoutSeconds、MaxMediaSizeBytes、TgdlDefaultMode、TgdlLanguage |
| 行为开关 | ExtractAudio、AlsoSendMediaToRequester、AllowPrivateUrls、AllowPlaylists、UpdateYtDlp、UpdateFfmpeg |
| 下载/合并 | MergeFormat、YtDlpProxy、YtDlpExtraArgs、YtDlpYoutubePlayerClients |
| 路径类（警告） | LocalApiBaseUrl、DownloadTempDir、YtDlpPath、FfmpegPath、CookieStoreDir、LogFile |
| 不可改（锁键） | BotToken、AllowedUserIds、TargetChannelIds、StateDir |

### 4.2 存储：独立 overlay 文件（vs 写回 config.conf）

| 方案 | 优点 | 缺点 |
| --- | --- | --- |
| A：写回 config.conf | 单一文件，语义统一 | ① entrypoint 仅"文件不存在"时生成，但**用户改 .env 期望重建 config.conf 时不生效**（config.conf 已存在）——bot 写入会与 .env 形成双写冲突源；② 用户可能挂载自有 config.conf（权限只读）→ 写失败；③ ConfigParser 不保留注释，整文件重写会丢用户注释；④ 升级/重置场景易丢 |
| B：独立 overlay 文件 | 与安装配置**物理隔离**，互不覆盖；entrypoint 不触碰新文件；tgdl-data 卷持久，pull 重建不丢；原子写简单；天然实现"安装配置不可改、bot 配置可改"的边界 | 多一个文件；装配时需二次合并（成本低，AppHost 一处） |

**决策：选 B**。overlay 目录 = `StateDir`（§5），文件：

- `StateDir/config-overlay.json`：`{"MaxConcurrentDownloads": "4", "TgdlDefaultMode": "audio"}`（键 → **字符串**值，与 config.conf 值格式一致）
- `StateDir/access-overlay.json`：`{"extraAllowedUsers": [123, 456], "extraTargetChannels": [-1001]}`

### 4.3 合并生效机制

- **装配期合并（AppHost）**：
  - 配置：`ConfigLoader.Load(configPath)` → 基础 `AppConfig` → `OverlayApplier.Apply(config, config-overlay.json)` → 最终 `AppConfig`（`with` 语义，**显式逐键映射**，禁用反射——键集合小且可控，编译期安全）。
  - 访问控制：`EffectiveAllowed = config.AllowedUserIds ∪ overlay.extraAllowedUsers`；`EffectiveTargets = config.TargetChannelIds ∪ overlay.extraTargetChannels` → 传入 `AccessControlService`（其内部结构不变，保持可测试）。
- **重启生效**（统一语义）：所有 `/config set`/`reset` 与 `/access add|del` 仅写 overlay；`AppConfig` 是 init-only 不可变 → 运行期热改会造成状态不一致（如并发数、gate）。故：**改动 → 持久化 → 触发重启**。
- **重启链路（零基础设施改动）**：bot 收到重启指令 → 写入 `pending-notify.json`（§4.5）→ 触发优雅退出（`AppHost` 的 `cts.Cancel()` 走 BotService 正常退出路径，进程 exit 0）→ entrypoint `wait` 返回 → 容器退出 → `restart: unless-stopped` 拉起 → 新进程加载 overlay → 生效。重启耗时 ≈ 数秒。

### 4.4 权限

- `/config`、`/access` 仅私聊 + 白名单用户（沿用 `MessageRouter.HandleCommandAsync` 现有双重检查，零新增逻辑）。
- 重启通知对象 = 发起命令的用户 chatId。

### 4.5 重启后通知（待通知状态，防重复）

- 持久化：`StateDir/pending-notify.json`：`{"chatId": 123456789, "textKey": "ConfigApplied", "args": ["MaxConcurrentDownloads"], "lang": "zh", "createdAt": "…"}`。
- 发送时机：新进程启动完成（BotService 就绪后、开始轮询前）→ 读取 → 发送（按 `lang` 渲染）→ **发送成功删除**（原子：先写临时文件再 rename，或发送后删除；失败保留至下次启动重试，上限 3 次后丢弃并记日志）。
- 防重复：文件读取后立即 rename 为 `.sending` 再发送（进程崩溃也不重发）；成功后删除 `.sending`。
- 文本：`/config` 用 "配置已更新，将在重启后生效：X（已自动重启）"；`/access` 用对应键。

### 4.6 命令设计：命令式（推荐）

| 方案 | 优点 | 缺点 |
| --- | --- | --- |
| A：命令式 `/config list\|set\|reset` + `/access add\|del\|list` | 幂等、可脚本化、可测试；与现有 `/cookie` 风格一致；值校验可复用 ConfigParser 规则；list 输出即"配置表" | 用户需记命令语法（help 内列出） |
| B：单一向导 `/settings`（按钮流） | 新手友好 | 回调状态机复杂（多步 wizard + 超时 + 并发）；无法批量操作；与现有模式不一致；测试成本高 |

**决策：选 A**。理由：目标用户是部署者（已会编辑 .env），命令式语义清晰、实现与测试成本低、无状态（每次调用独立，无回调状态管理）。

命令语法：

```
/config list                       # 列出全部可改键：当前生效值 + 来源（config/overlay/默认）
/config set <Key> <value>          # 校验（复用 ConfigParser 规则）→ 写 overlay → 确认 + 重启
/config reset <Key>                # 删除 overlay 覆盖 → 重启（恢复 config.conf/默认值）
/config reset-all                  # 清空整个 config-overlay.json → 重启
/access add <user|channel> <id>    # 追加到 overlay（去重）
/access del <user|channel> <id>    # 从 overlay 删除（config 中的项提示不可删）
/access list                       # 合并列表 + 标注来源（config/overlay）
/language [en|zh]                  # 查看/设置我的语言（§2.8）
```

- 校验复用：`ConfigParser` 暴露 `public static string? ValidateValue(string canonicalKey, string value)`（重构现有 `Validate`/`GetInt`/`GetBool` 等为可复用校验器），返回错误文本（null = 合法）。**不得在 CommandHandler 重复实现校验**（单点规则）。
- 未知名键：`/config list` 输出可改键表供参考。
- BotCommand 菜单（`SetCommandsAsync`）新增：`language`、`config`、`access`（简短描述，双语随全局默认语言）。

---

## 5. 新配置键完整清单（走完整链路）

| # | AppConfig 属性 | 类型 | 默认值 | TGDL_ 环境变量 | config.conf 键 | 说明 |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `TgdlLanguage` | string | `auto` | `TGDL_LANGUAGE` | `TgdlLanguage` | bot 全局默认语言：`auto`（跟随用户 language_code）/ `en` / `zh`；别名 `Language`；非法值报错（双行消息） |
| 2 | `StateDir` | string | 空 → 自动推导 | `TGDL_STATE_DIR` | `StateDir` | bot 运行时状态目录：`languages.json`、`config-overlay.json`、`access-overlay.json`、`pending-notify.json`。空值时：容器内默认 `/opt/tgdl-bot/api-data`（entrypoint 生成 config.conf 时**显式写入**，保证在 tgdl-data 卷内持久）；native 运行时默认 `DownloadTempDir` 父目录 |

链路同步清单（每键 6 处）：

1. `src/TGBot/Config/AppConfig.cs`（属性 + XML 注释）
2. `src/TGBot/Config/ConfigParser.cs`（Alias 表 + 解析 + 校验；`StateDir` 长度 ≤512；`TgdlLanguage` 枚举校验）
3. `docker/docker-entrypoint.sh`（生成 config.conf 时：`StateDir = $API_DATA_DIR` 固定写入；`TGDL_LANGUAGE` 条件写入）
4. `docker/.env.example`（注释示例）
5. `docker/config.conf.example`（注释示例）
6. `README.md` 配置表 + `README.en.md` 同步

---

## 6. README 双语结构

| 方案 | 优点 | 缺点 |
| --- | --- | --- |
| A：README.md（中文）+ README.en.md（英文），顶部互链 | GitHub 默认展示 README.md（中文为主，符合现有用户）；双语文件独立、diff/维护清晰；docs agent 可对照同步 | 双份维护成本（结构需保持一致） |
| B：单文件双语分节 | 单文件 | 篇幅翻倍、锚点混乱、贡献者易改乱；GitHub 不提供语言切换 |
| C：README.md（英文）+ docs/README.zh.md | 国际优先 | 与现有中文用户群不符；中文入口深藏 |

**决策：选 A**。

- `README.md` 顶部：`[English](README.en.md) | [中文](README.md)`；`README.en.md` 顶部互链反向。
- 结构同步规则：两文件章节标题一一对应（docs agent 用标题级对比核对）；新章节（法律与合规声明、i18n、bot 配置管理）同步进两版。
- 免责声明：中文版放 README.md 文首 blockquote（醒目），英文版对应位置同。

---

## 7. 实施拆解（可执行任务清单）

### 7.1 developer（src/TGBot + tests）

| # | 任务 | 涉及文件 | 验证 |
| --- | --- | --- | --- |
| D1 | 建立 i18n 基础：`II18n`/`I18nService`/`LanguageCatalog`/`BotLanguage` + 嵌入式资源 `en.json`/`zh.json`（39 键从 UserTexts 迁移，含占位符）；`UserTexts` 转键常量 | `src/TGBot/Texts/I18n/*`、`src/TGBot/Texts/UserTexts.cs`、`src/TGBot/TGBot.csproj`（EmbeddedResource） | `dotnet build -c Release`（0 警告）；单测：缺键回退、占位符、注册覆盖 |
| D2 | `UserLanguageStore`（加载/原子写/并发安全）+ `UserLanguageResolver`（§2.3 解析链） | `src/TGBot/Texts/I18n/` | 单测：解析链优先级、原子写 |
| D3 | `InboundMessage.Language` + Wrapper 填充（`User.language_code` → Normalize） | `Messaging/ITelegramClient.cs`、`Messaging/TelegramClientWrapper.cs` | 单测：Fake 消息带 language_code |
| D4 | 迁移全部文案调用点（MessageRouter/CommandHandler/DownloadCoordinator/AccessControlService/CookieService/CaptionBuilder），删除硬编码中文；`CaptionBuilder.Build` 增加语言 | 上表 §2.6 各文件 | `dotnet test`（现有测试适配：FormatUnavailableTests/CookieTests 的 UserTexts 引用改键）；Fake 断言改键 |
| D5 | 首次私聊语言弹窗 + `lang:` 回调 + `/language` 命令 | `MessageRouter.cs`、`CommandHandler.cs`、`BotService.cs`（SetCommands） | 单测：回调校验、首次弹窗触发条件 |
| D6 | ConfigParser 错误消息双语双行（含 ConfigLoader/AppHost 前缀）；`TgdlLanguage`/`StateDir` 解析与校验 | `Config/ConfigParser.cs`、`Config/ConfigLoader.cs`、`Application/AppHost.cs` | `dotnet test`（ConfigParserTests 断言策略：含键名不断言全文本）；新增非法值测试 |
| D7 | overlay 存储与合并：`OverlayStore`（config/access 读写、原子写）、`OverlayApplier`、`AccessListMerge`；AppHost 装配 | `src/TGBot/Config/Overlay/`（新增）、`AppHost.cs` | 单测：合并去重、来源标注、原子写 |
| D8 | `ConfigParser.ValidateValue(key, value)` 重构暴露 + `/config`、`/access` 命令 + 重启触发（优雅退出）+ `pending-notify` 发送（启动期消费） | `ConfigParser.cs`、`CommandHandler.cs`、`AppHost.cs`、`BotService.cs` | 单测：命令解析/校验拒绝/防重复通知；`dotnet test` 全绿 |
| D9 | 文案资源补全（新命令文案键入 en/zh.json，如 ConfigApplied/AccessAdded/LanguagePrompt…） | `Texts/I18n/Resources/*.json` | 键覆盖单测（en/zh 键集合一致） |

### 7.2 devops（docker / scripts / LICENSE）

| # | 任务 | 涉及文件 | 验证 |
| --- | --- | --- | --- |
| O1 | LICENSE（GPL-3.0 官方全文）+ NOTICE（第三方许可清单） | `LICENSE`、`NOTICE` | `head LICENSE` 含 "Version 3, 29 June 2007"；NOTICE 列出 5 项 |
| O2 | SPDX 头批量脚本（一次性，覆盖 `src/**/*.cs`、`docker/*.sh`、`scripts/*.sh`；跳过 third_party/） | 脚本 + 全源文件 | `rg -L "SPDX-License-Identifier" src docker scripts` 无输出 |
| O3 | `docker-entrypoint.sh`：生成块写入 `StateDir = $API_DATA_DIR` 与 `TGDL_LANGUAGE` | `docker/docker-entrypoint.sh` | `docker compose build` 后起容器，`cat /opt/tgdl-bot/config.conf` 含 StateDir |
| O4 | `.env.example`/`config.conf.example` 新增两键注释 | 两个 example 文件 | diff 检查与 README 表一致 |
| O5 | install.sh 交互向导（§3 伪代码；/dev/tty、校验、5 次跳过、降级、写 .env、启动） | `scripts/install.sh` | shellcheck 0 告警；`bash -n`；TTY 下手工冒烟（本机或容器） |
| O6 | 发布链路核对：README 徽章（GPL）、SECURITY.md 入库 | `README.md`、`SECURITY.md` | 仓库页面徽章渲染 |

### 7.3 docs（README / 文档一致性）

| # | 任务 | 涉及文件 | 验证 |
| --- | --- | --- | --- |
| M1 | README 新增章节：法律与合规声明（§1.3/1.5 内容）、配置表新增 `TGDL_LANGUAGE`/`TGDL_STATE_DIR`、命令表新增 `/language`/`/config`/`/access` | `README.md` | 与 .env.example 键一一对应 |
| M2 | 新增 `README.en.md`（与 README.md 章节一一对应，含免责声明英文版） | `README.en.md` | 标题级 diff 核对；`./build-docs.sh` 0 警告 |
| M3 | 核对 ADR/设计文档引用编号正确（0001/0002/0003） | `docs/adr/*.md` | `git grep "0001-" docs` 等 |

---

## 8. 验证总览（验收命令）

```bash
dotnet build -c Release          # 0 警告（硬门槛）
dotnet test                      # 全绿（IntegrationTests 静默跳过）
bash -n scripts/install.sh && shellcheck scripts/install.sh docker/docker-entrypoint.sh
./build-docs.sh                  # docs agent 环境（DOTNET_ROOT=$HOME/dotnet）
```

---

## 9. 遗留问题与风险

1. **`/config set LocalApiBaseUrl` 等路径/连接类键**：改错将导致重启后无法连接（bot 起不来）。缓解：路径类警告提示；必要时用户可删 `config-overlay.json` 中该键恢复。不改需求（不在排除列表）。
2. **`zh-TW`/`zh-Hant` 用户映射到 `zh`（简体）**：v1 语言集仅 en/zh，接受；未来加 `zh-hant` 语言时映射表扩展即可（`NormalizeLanguageCode` 单点）。
3. **`/access` 删除不可逆**：config 来源的成员不可删（设计如此），但 overlay 来源删除后不可恢复（除非重新 add）——接受。
4. **首次弹窗与并发的竞态**：用户连发多条消息时可能多次弹窗；缓解：弹窗前内存 `TryAdd` 去重（D5 实现时注意）。
5. **pending-notify 与进程退出时序**：退出前必须同步写完文件再触发 cts.Cancel（D8 实现时注意顺序）。
6. **README.en.md 维护漂移**：依赖 docs agent 流程（M2/M3 每次改动核对）。