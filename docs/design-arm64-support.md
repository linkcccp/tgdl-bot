# arm64 支持设计

- **日期**：2026-08-17
- **状态**：已采纳（见 `docs/adr/0004-多架构镜像发布与架构感知更新.md`）
- **范围**：CI 多架构镜像发布 + `/update` 架构感知下载。只做设计，不实现。

## 1. 目标与范围

### 目标

1. CI（`v*` tag）产出 **multi-arch manifest 镜像**：`ghcr.io/linkcccp/tgdl-bot:{ver}` 与 `:latest` 同时包含 `linux/amd64`（标准平台串，amd64 即 x64）与 `linux/arm64`；`docker pull` / `docker compose pull` 自动按宿主架构拉取。
2. **代码层面保证 `/update` 在 arm64 上可用**：arm64 容器内 `/update` 下载的 ffmpeg/yt-dlp 为 arm64 版，可执行、可校验、可原子替换。
3. `scripts/install.sh` **零改动**（multi-arch manifest 天然覆盖）。

### 验收标准

| # | 验收项 | 验证方式 |
| --- | --- | --- |
| 1 | CI 发版产出双平台 manifest | `docker buildx imagetools inspect ghcr.io/linkcccp/tgdl-bot:{ver}` 显示 `linux/amd64`（标准平台串）+ `linux/arm64` 两个 platform |
| 2 | `docker pull` 自动按架构拉取 | 本机 `docker pull --platform arm64 ghcr.io/linkcccp/tgdl-bot:{ver}` 可拉取（不要求本机运行）；install.sh 内容无 diff |
| 3 | `/update` arm64 可用 | 单测覆盖 URL 选择逻辑；arm64 镜像**构建成功**即视为 CI 侧验收；真实运行由用户日后在 ARM 设备实测（本机为 x64） |

### 范围边界

- **做**：linux/arm64（aarch64）构建、发布、运行期更新下载。
- **不做**：armv7/32 位；Windows/macOS 宿主；非 Docker 部署路径的架构适配；QEMU 模拟冒烟测试 arm64 镜像。

## 2. 架构命名约定（防混淆，全员遵守）

`amd64` 与 `arm64` 视觉上极易看错。统一规则：**人为可读的自命名一律 `x64` / `arm64`**；**Docker 标准平台串**（`linux/amd64`、`linux/arm64`、`TARGETARCH` 注入值）与**第三方官方资产命名/URL** 原样保留，提及处注明对应关系。

### 架构映射表（实现时以此为准）

| 概念 | x64 | arm64 | 说明 |
| --- | --- | --- | --- |
| dist 目录 | `docker/dist/x64/` | `docker/dist/arm64/` | 自命名，CI matrix 经 `distarch` 驱动 |
| dotnet RID | `linux-x64` | `linux-arm64` | .NET 官方 RID |
| tba 资产 | `telegram-bot-api-linux-x64` | `telegram-bot-api-linux-arm64` | fork 官方资产命名，不改 |
| ffmpeg URL | `https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz` | `https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-arm64-static.tar.xz` | **上游官方命名（amd64）不可改**，注释注明对应 x64 |
| yt-dlp 资产 | `yt-dlp`（官方 x86_64 命名） | `yt-dlp_linux_aarch64`（官方命名） | 上游官方命名，不改 |
| deno 资产 | `deno-x86_64-unknown-linux-gnu.zip` | `deno-aarch64-unknown-linux-gnu.zip` | 上游官方命名，不改 |
| buildx 平台串 | `linux/amd64` | `linux/arm64` | **Docker 标准平台串，不可改**；amd64 即 x64 |
| TARGETARCH 注入值 | `amd64` | `arm64` | Docker 标准值，buildx 自动注入，不可改 |
| 镜像 per-arch tag | `{ver}-x64` | `{ver}-arm64` | 自命名 |
| runner | `ubuntu-latest` | `ubuntu-24.04-arm` | GitHub 官方 runner 标签 |

### 三条规则

1. **自命名统一 `x64` / `arm64`**：dist 目录名、镜像 per-arch tag、CI matrix 变量名（`distarch`）、步骤名/注释/echo 文本、设计文档与 ADR 正文。**`amd64` 不出现在任何自命名位置**。
2. **Docker 标准平台串保留原样**：buildx `platforms:` 的 `linux/amd64`/`linux/arm64`、`docker pull --platform linux/amd64`、`imagetools inspect` 输出、`TARGETARCH` 注入值（amd64/arm64）均为构建工具硬性要求，不可改；文档/注释中提及这些串时加注「amd64 即 x64」或「标准平台串」。
3. **第三方官方资产命名/URL 不改**：ffmpeg 的 `amd64` 后缀、yt-dlp 的 `x86_64`/`aarch64` 命名、deno 的 `x86_64`/`aarch64` 命名、tba 的 `x64`/`arm64` 命名均为上游官方命名，只在代码注释与文档中注明对应关系（如「x86_64 即 x64」）。

## 3. 已确认事实（依赖资产，可直接引用）

| 依赖 | x64 资产 | arm64 资产 | 状态 |
| --- | --- | --- | --- |
| telegram-bot-api | `telegram-bot-api-linux-x64`（44.9MB） | `telegram-bot-api-linux-arm64`（46.7MB，2026-08-15 发布） | fork `linkcccp/telegram-bot-api` latest Release 两者均存在 |
| ffmpeg | `https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz`（官方命名 amd64，即 x64） | `https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-arm64-static.tar.xz` | 官方静态构建两者均存在 |
| yt-dlp | `/releases/latest/download/yt-dlp`（官方 x86_64 命名，即 x64） | `/releases/latest/download/yt-dlp_linux_aarch64`（2026.07.04 release 实测存在） | 官方资产按架构命名，**须区分** |
| deno | `deno-x86_64-unknown-linux-gnu.zip` | `deno-aarch64-unknown-linux-gnu.zip` | Dockerfile 第 24 行已有 `TARGETARCH` 分支，**无需改动** |

> 关键断言：yt-dlp 官方资产按架构区分（`yt-dlp` = 官方 x86_64 命名，即 x64；`yt-dlp_linux_aarch64` = arm64）。因此镜像内 seed-bin 的 yt-dlp **必须按架构放置**，不能跨架构共用单文件。

## 4. CI 构建策略（核心决策）

### 方案对比

| 方案 | 做法 | 优点 | 缺点 |
| ---- | ---- | ---- | ---- |
| **A（推荐）：matrix 双 job 原生构建** | `build` job 用 matrix 拆 x64（`ubuntu-latest`）+ arm64（`ubuntu-24.04-arm` 官方免费原生 ARM runner）；各自下载对应架构资产、`dotnet publish` 对应 RID、构建对应平台镜像并推 **per-arch tag**（`{ver}-x64` / `{ver}-arm64`）；最后 `manifest` job 用 `docker buildx imagetools create` 合并出 `{ver}` 与 `latest` multi-arch tag | 原生编译/下载校验快（无 QEMU 3-5 倍损耗）；下载的 arm64 二进制可在 runner 上**原生执行验证**（`yt-dlp --version` / `ffmpeg -version`）；arm runner 对 public 仓库免费，成本为零；per-arch tag 天然隔离两 job 互不阻塞 | 需两条 runner 执行；manifest 合并多一个 job；arm64 镜像无法在 x64 runner 上做运行时冒烟（本设计明确不做，交给用户实测） |
| B：单 x64 job + QEMU | `docker/setup-qemu-action` + buildx 一次 `--platform linux/amd64,linux/arm64` | 无需新 runner；流程与现状最接近 | QEMU 模拟编译/执行慢 3-5 倍；下载的 arm64 二进制无法在 runner 上原生校验（只能构建期验证）；QEMU 偶发兼容性/网络问题，可靠性低 |

### 决策：方案 A

理由：arm64 runner 是 GitHub 官方免费标准功能（public 仓库），原生构建速度与可靠性全面优于 QEMU；且 arm64 job 内可**原生执行**下载的 arm64 yt-dlp/ffmpeg 做资产校验（方案 B 做不到）；成本为零。代价仅是 workflow 多一个 manifest 合并 job。

### 具体设计（供 devops 实现）

`build` job matrix 定义（**变量名约定：`distarch` 为自命名 x64/arm64，`platform` 为 Docker 标准平台串**）：

```yaml
strategy:
  matrix:
    include:
      - distarch: x64                  # 自命名（dist 目录/per-arch tag），统一 x64
        platform: linux/amd64          # Docker 标准平台串（buildx 硬性要求，amd64 即 x64）
        runs-on: ubuntu-latest
        dotnet-rid: linux-x64
        tba-asset: telegram-bot-api-linux-x64
        ffmpeg-url: https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz  # 上游官方命名（amd64），不可改
        yt-dlp-url: https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp  # 官方 x86_64 命名资产（即 x64）
        per-arch-tag: ${{ env.VERSION }}-x64
      - distarch: arm64
        platform: linux/arm64          # Docker 标准平台串
        runs-on: ubuntu-24.04-arm
        dotnet-rid: linux-arm64
        tba-asset: telegram-bot-api-linux-arm64
        ffmpeg-url: https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-arm64-static.tar.xz
        yt-dlp-url: https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux_aarch64  # 官方 aarch64 命名资产
        per-arch-tag: ${{ env.VERSION }}-arm64
```

每个 matrix job 步骤（沿用现有 release.yml 的步骤结构与校验模式，全部 URL/资产名/目标目录改由 matrix 参数驱动）：

1. Checkout → Setup .NET → Resolve version（现步骤不变）。
2. Download telegram-bot-api：按 `tba-asset` 选资产；**资产缺失 → 硬失败**（沿用现有 `exit 1` 模式，**不静默降级到 x64**）；ELF 魔数校验 + 5 次重试保留；落盘 `docker/dist/${{ matrix.distarch }}/telegram-bot-api`。
3. Publish：`dotnet publish ... -r ${{ matrix.dotnet-rid }} ... -o docker/dist/${{ matrix.distarch }}`（`-p:Version=${VERSION}` 保留）。
4. Download ffmpeg：按 `ffmpeg-url`；xz 魔数校验 + 重试保留；解压取 ffmpeg 落盘 `docker/dist/${{ matrix.distarch }}/ffmpeg`，**执行 `ffmpeg -version` 原生校验**（arm64 job 上即为 arm64 二进制真实验证）。
5. Download yt-dlp：按 `yt-dlp-url`；落盘 `docker/dist/${{ matrix.distarch }}/yt-dlp`；**执行 `yt-dlp --version` 原生校验**（同上）。
6. Setup Docker Buildx → Login to GHCR（现步骤不变）。
7. Build & push：`platforms: ${{ matrix.platform }}`，tags 仅推 **per-arch tag**（`ghcr.io/linkcccp/tgdl-bot:${{ env.VERSION }}-${{ matrix.distarch }}`）；**不推 `latest`/裸版本号**（避免两 job 竞争同一 tag）。
8. （仅 x64 job）运行时冒烟：现有 "Verify image (deno JS runtime)" 步骤保留在 x64 job 内（arm64 镜像无法在 x64 runner 执行）。

> **DISTARCH/TARGETARCH 边界**（实现时勿混淆）：
> - `TARGETARCH`：Docker/buildx 预定义 ARG，构建时自动注入标准值 `amd64`/`arm64`——**由 buildx 自动注入，无需显式传**；Dockerfile 内仅用于 deno 分支判断（第 24 行）。
> - `DISTARCH`：自命名 ARG（x64/arm64，对应 dist 目录名），buildx 不会自动注入，**必须显式传**，值为 `${{ matrix.distarch }}`。示意：`docker build --build-arg TARGETARCH=amd64 --build-arg DISTARCH=${{ matrix.distarch }} ...`（`TARGETARCH` 处实际由 buildx 自动注入，仅为可读性展示）。

`manifest` job（`needs: build`，单独 job 收口）：

1. Setup Docker Buildx（`imagetools` 需要）→ Login to GHCR。
2. 合并：
   - `docker buildx imagetools create -t ghcr.io/linkcccp/tgdl-bot:${{ env.VERSION }} ghcr.io/linkcccp/tgdl-bot:${{ env.VERSION }}-x64 ghcr.io/linkcccp/tgdl-bot:${{ env.VERSION }}-arm64`
   - 同上再创建 `:latest`。
3. **校验 manifest 内容**（防合并遗漏，失败即 job 失败）：
   - `docker buildx imagetools inspect ghcr.io/linkcccp/tgdl-bot:${{ env.VERSION }}` 输出必须同时含 `linux/amd64`（标准平台串）与 `linux/arm64`（grep 断言）。
4. Create GitHub Release（softprops/action-gh-release，**只在此 job 建一次**）：body 更新为双平台说明（注明 `linux/amd64` 即 x64）+ arm64 真实运行待用户实测的提示。

要点：

- `latest` 不按架构推（避免产生悬空 `latest-x64`）；由 manifest job 统一创建 multi-arch `latest`。
- 每次发版在 GHCR 遗留 2 个 per-arch tag（`{ver}-x64`/`{ver}-arm64`），作为 manifest 组成来源保留（不可变资产，可接受）。
- arm64 验证上限 = 构建成功 + arm64 资产在原生 runner 上执行校验 + manifest 内容断言；**不做 QEMU 冒烟**（任务约束：真实运行由用户 ARM 设备实测）。

## 5. dist 目录布局

最终布局（每架构自包含，目录名为自命名 x64/arm64）：

```
docker/dist/
├── x64/
│   ├── tgdl-bot            # dotnet publish -r linux-x64（单文件）
│   ├── telegram-bot-api    # fork latest, asset telegram-bot-api-linux-x64
│   ├── ffmpeg              # johnvansickle amd64-static 解压产物（上游官方命名，对应 x64）
│   └── yt-dlp              # yt-dlp release asset "yt-dlp"（官方 x86_64 命名，即 x64）
└── arm64/
    ├── tgdl-bot            # dotnet publish -r linux-arm64（单文件）
    ├── telegram-bot-api    # fork latest, asset telegram-bot-api-linux-arm64
    ├── ffmpeg              # johnvansickle arm64-static 解压产物
    └── yt-dlp              # yt-dlp release asset "yt-dlp_linux_aarch64"
```

**Dockerfile 必改点**（第 8 行后新增 ARG；第 42-45 行四条 COPY 统一改 `${DISTARCH}`；现状第 42/43/45 行已用 `${TARGETARCH}`、第 44 行 yt-dlp 硬编码）：

```dockerfile
ARG TARGETARCH          # Docker 标准注入值：amd64/arm64（供 deno 分支判断，保留）
ARG DISTARCH=x64        # 人可读架构名（dist 目录名）：x64/arm64，CI 经 --build-arg 传入
COPY dist/${DISTARCH}/tgdl-bot /opt/tgdl-bot/tgdl-bot
COPY dist/${DISTARCH}/telegram-bot-api /opt/tgdl-bot/api/telegram-bot-api
COPY dist/${DISTARCH}/yt-dlp /opt/tgdl-bot/seed-bin/yt-dlp
COPY dist/${DISTARCH}/ffmpeg /opt/tgdl-bot/seed-bin/ffmpeg
```

> 注：**COPY 指令不能用 shell 替换，必须显式 ARG**。`TARGETARCH` 保留给 deno 分支判断（第 24 行 `aarch64-unknown-linux-gnu` / `x86_64-unknown-linux-gnu`，其中 x86_64 即 x64），`DISTARCH` 只用于 COPY 路径，两者职责分离。

理由：yt-dlp 资产按架构命名（见第 3 节断言），且 per-arch 目录与其余三个二进制一致、自文档化——即使 matrix job workspace 天然隔离使共用路径在"当前实现"下碰巧正确，per-arch 布局也能防止未来引入 artifact 共享/缓存合并时踩坑。

deno：Dockerfile 第 24 行 `TARGETARCH` 分支已覆盖，**不改**（分支内 `x86_64` 即 x64 官方命名）。

## 6. /update 架构检测设计（源码侧）

### 为什么只有 /update 需要检测

镜像**按架构构建**：`seed-bin/{yt-dlp,ffmpeg}` 在构建期即与镜像架构一致，entrypoint 的种子复制（`cp -a "$SEED_DIR"/. "$BIN_DIR"/`）只做文件拷贝，**不感知架构、无需改动**。唯一在**运行期**发生"下载外部二进制"的是 `/update`，因此只有 `ToolSource` 需要在运行时判断宿主架构。

### 检测机制

- 使用 .NET 内置 `System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture`（进程架构，arm64 容器内返回 `Architecture.Arm64`），**不依赖环境变量/宿主探测**。
- 新建 `src/TGBot/Update/ToolArch.cs`（静态工具类，纯函数、可单测）：

```csharp
public static class ToolArch
{
    /// <summary>按运行架构返回 johnvansickle ffmpeg 静态构建 URL（URL 为上游官方命名，amd64 即 x64）。</summary>
    public static string FfmpegReleaseUrl(Architecture arch) => arch switch
    {
        Architecture.X64   => "https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz",
        Architecture.Arm64 => "https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-arm64-static.tar.xz",
        _ => throw new InvalidOperationException($"不支持的运行架构 {arch}：/update 仅支持 x64 与 arm64"),
    };

    /// <summary>按运行架构返回 yt-dlp 官方 release 资产 URL（官方命名：x64 为 yt-dlp，arm64 为 yt-dlp_linux_aarch64）。</summary>
    public static string YtDlpReleaseUrl(Architecture arch) => arch switch
    {
        Architecture.X64   => "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp",
        Architecture.Arm64 => "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux_aarch64",
        _ => throw new InvalidOperationException($"不支持的运行架构 {arch}：/update 仅支持 x64 与 arm64"),
    };
}
```

- `YtDlpToolSource`：删除 `const string LatestUrl` 常量，改为 `Func<Architecture> _archProvider` 字段，URL 在**调用点动态解析**（`ToolArch.YtDlpReleaseUrl(_archProvider())`）；`FfmpegToolSource` 同样删除 `const string ReleaseUrl`（仅保留版本页 `HomePageUrl` 常量），URL 同样在调用点经 `ToolArch.FfmpegReleaseUrl(_archProvider())` 解析。相较"构造时解析为实例字段"，调用点动态解析使 **URL 单一数据源集中在 `ToolArch`**（构造、探测、下载各环节取 URL 都实时求值，测试注入的架构在每次调用时生效，注入更灵活）。版本探测逻辑（yt-dlp HEAD 重定向解析版本号、ffmpeg 解析 johnvansickle 主页）**不变**——两架构资产走同一探测机制。

### 可测试性（契约）

两个 ToolSource 构造函数追加**可选**参数，默认取真实进程架构，**AppHost.cs 第 85-86/242 行无需改动**（源码兼容）：

```csharp
public YtDlpToolSource(HttpClient http, Func<Architecture>? archProvider = null)   // 默认 () => RuntimeInformation.ProcessArchitecture
public FfmpegToolSource(HttpClient http, IProcessRunner runner, Func<Architecture>? archProvider = null)
```

单测通过 `() => Architecture.Arm64` 注入，不依赖本机（x64）真实架构。

### 非 x64/arm64 架构的回退决策

**快速失败（抛异常），不做 x64 静默回退**。理由：

1. 错误架构的二进制在 `Updater.VerifyBinaryAsync`（下载后执行 `--version` 校验）必然失败——静默回退只是把错误推迟到下载完成之后，浪费流量且报错信息更绕。
2. 本项目仅发布 x64/arm64 镜像，出现其他架构 = 部署错误，应在 `/update` 入口立即暴露。
3. 异常经 `Updater` 现有包装路径映射为 `UpdateFailureReason.DownloadFailed`，错误文案已包含架构信息，用户可据此反馈。

## 7. 测试策略

| 测试 | 位置 | 内容 | 执行环境 |
| --- | --- | --- | --- |
| `ToolArchTests`（新增） | `tests/TGBot.Tests/ToolArchTests.cs` | `[Theory]` 参数化：`X64` → x64 资产 URL（官方 amd64 命名）、`Arm64` → arm64 URL（断言**完整 URL 字符串**）；`Arm`/`X86`/未知枚举 → 断言抛 `InvalidOperationException` 且消息含架构名 | 本机 x64 正常跑 |
| ToolSource 构造测试（建议） | `tests/TGBot.Tests/` | 注入 `() => Architecture.Arm64` 构造 `YtDlpToolSource`/`FfmpegToolSource`，用 stub `HttpMessageHandler` 断言实际请求的 URL 为 arm64 资产 | 本机 |
| `UpdaterIntegrationTests` | 现有文件 | 真实下载逻辑**不变**；arm64 runner 上运行 CI 时自然覆盖 arm64 资产；本机/无网环境沿用静默跳过 | CI / 本机（跳过） |
| 用户实测 | 用户 ARM 设备 | arm64 容器内 `/update` 真实下载、校验、替换 | 发布后由用户执行（README 记录待验证项） |

> 注意：`ToolArchTests` 是本机（x64）能覆盖 arm64 分支的**唯一途径**，必须通过参数注入而非真实进程架构，否则 arm64 分支永远走不到。

## 8. 影响面

| 文件 | 改动 |
| --- | --- |
| `.github/workflows/release.yml` | 按第 4 节重构：matrix 双 job（`distarch` 自命名 + `platform` 标准串）+ manifest job（devops 实现，本次仅设计） |
| `docker/Dockerfile` | 新增 `ARG DISTARCH=x64`；第 42-45 行 4 条 COPY 改 `dist/${DISTARCH}/`（yt-dlp 第 44 行由硬编码改 DISTARCH）；`TARGETARCH` 保留（deno 分支，第 24 行） |
| `src/TGBot/Update/ToolSource.cs` | 两处硬编码 URL 改为按架构解析；构造函数加可选 `archProvider` |
| `src/TGBot/Update/ToolArch.cs` | 新增（URL 映射纯函数） |
| `tests/TGBot.Tests/ToolArchTests.cs` | 新增（参数化 URL 选择测试） |
| `README.md` | 已知限制：架构章节改为"支持 linux/amd64（即 x64）与 linux/arm64，暂不支持 armv7/32 位；arm64 真实运行待用户实测"；架构概览/发布流程/本地构建示例（第 222-228 行注释中 `当前仅 x86_64` 提示）同步更新 |
| `AGENTS.md` | 架构章节：CI matrix 描述、dist 布局（`x64/` + `arm64/`）、Dockerfile DISTARCH 与 yt-dlp 路径、ToolArch 说明 |
| `CHANGELOG.md` | 新版本条目 |
| `scripts/install.sh` | **零改动**（multi-arch manifest 下 `docker pull`/`docker compose pull` 自动按宿主架构拉取） |
| `docker/docker-entrypoint.sh` | **零改动**（seed-bin 与镜像同架构，entrypoint 不感知架构；见第 6 节） |
| `docker/.env.example` / `docker/config.conf.example` | **零改动**（无新增配置键） |
| `docs/history/` | 实现阶段由 scribe 记录 |

## 9. 不做的事（明确排除）

- armv7 / 32 位（用户确认不做）。
- Windows/macOS 宿主支持。
- 非 Docker 部署路径的架构适配（本地直接运行 `tgdl-bot` 时 `/update` 仍会按进程架构下载正确二进制——`RuntimeInformation` 天然正确，但不在本次验证范围）。
- QEMU 冒烟测试 arm64 镜像（真实运行由用户 ARM 设备实测）。

## 10. 实施与验证顺序（供 orchestrator 派单）

1. **developer**：`ToolArch.cs` + `ToolSource.cs` 改造 + `ToolArchTests`；`dotnet build -c Release`（0 警告）+ `dotnet test` 全绿。
2. **devops**：`release.yml` matrix 重构（`distarch` 自命名 + `platform` 标准串）+ Dockerfile DISTARCH 改造 + AGENTS.md/README/CHANGELOG 文档侧改动；以测试 tag（如 `v2.5.0-rc1`，发版动作需用户确认）验证：manifest 双平台、per-arch tag（`{ver}-x64`/`{ver}-arm64`）、Release body。
3. **qa**：单测 + `docker buildx imagetools inspect` 断言双平台（`linux/amd64` 与 `linux/arm64` 标准平台串）；`docker pull --platform arm64` 拉取验证（不运行）。
4. **docs**：README 已知限制与架构说明核对（与 AGENTS.md 一致）。
5. **用户**：发布后在 ARM 设备实测 `/update` 与下载功能，结果反馈后由 docs 更新 README 待验证项。