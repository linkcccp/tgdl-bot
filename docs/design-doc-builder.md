# 跨平台文档构建工具设计（C# 替代 build-docs.sh）

- **状态**：已采纳（用户选定方案 B，见 `docs/adr/0005-跨平台文档构建工具.md`）
- **日期**：2026-08-17
- **决策者**：architect / 用户

## 1. 目标与范围

### 1.1 目标

用 C# 编写的独立工具 `TGBot.Docfx/` 替代 `build-docs.sh`（bash），实现 **Windows / macOS / Linux 三平台同一条命令**生成 API 文档：

```bash
dotnet run --project TGBot.Docfx
```

- 输出目录不变：`docs/`（docfx build 输出）+ `TGBot.Docfx/api/`（docfx metadata 输出）。
- 保持 0 警告硬门槛（`--warningsAsErrors`）。
- 解决既有四个已知坑（见 §2）。

### 1.2 验收标准

1. 三平台（Windows / macOS / Linux）各自执行 `dotnet run --project tools/TGBot.Docfx`，退出码 0，`docs/index.html` 生成，docfx 输出 0 警告。
2. docfx 未安装时，工具输出安装指引并以**非零退出码**结束。
3. 连续执行两次（增量场景）结果一致，`TGBot.Docfx/api/` 下 yml 时间戳正确刷新（无增量缓存残留问题）。
4. `build-docs.sh` 删除后，仓库内无任何指向它的引用残留（README / README.en / CONTRIBUTING / AGENTS.md / .opencode/* 同步完成）。

### 1.3 范围

| 做 | 不做 |
| --- | --- |
| `tools/TGBot.Docfx` 工具项目（publish + 缓存清理 + docfx build 编排） | `scripts/install.sh` 平台化（保持 Linux 部署脚本） |
| `build-docs.sh` 删除 + 文档命令同步（§7 文件清单） | `/update` 资产平台化 |
| docfx 缺失时的安装指引与退出码 | CI 接入文档 job（文档构建不在 release.yml 流程内，本次不引入 CI 改动） |
| ADR 0005 决策记录 | 工具单测项目（§8） |

## 2. 已知坑与解决编排

| # | 坑 | 根因 | 工具内的解决 |
| --- | --- | --- | --- |
| 1 | `dotnet publish --no-restore` 报 NETSDK1047 | docfx metadata 会 restore 项目并覆盖 `project.assets.json`（不含 linux-x64 RID） | **编排顺序：先 publish（不带 `--no-restore`，正常 restore），再跑 docfx**。publish 之后不再有任何依赖 assets.json 的 MSBuild 操作，docfx 后续的 restore 覆盖无副作用 |
| 2 | docfx 增量缓存导致 yml 旧时间戳不更新 | docfx 缓存于 `src/TGBot/obj/docfx` | **每次运行先清理 `src/TGBot/obj/docfx`**（`Directory.Delete(recursive:true)`，Windows 只读文件先清属性，失败降级为警告不中断） |
| 3 | docfx 未安装 | 无 | 检测失败时输出 `dotnet tool install --global docfx` 指引，非零退出（§4.1） |
| 4 | 本机 docfx 需 aspnetcore 运行时 `DOTNET_ROOT=$HOME/dotnet` | 自定义 dotnet 安装布局 | **文档说明，工具不干预**（§4.4 决策） |

## 3. 工具设计

### 3.1 项目结构

```
TGBot.Docfx/
├── TGBot.Docfx.csproj
└── Program.cs          # 入口：参数解析 + 四步编排（含 internal 辅助类）
```

工具逻辑薄（约 120–180 行），**单文件 `Program.cs` 承载**（顶层语句 + internal static 类），不拆多文件。

#### `TGBot.Docfx.csproj`（全文）

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <InvariantGlobalization>true</InvariantGlobalization>
    <!-- 内部开发工具：不生成 API 文档、不进发布镜像，故不开 GenerateDocumentationFile（无 CS1591 约束） -->
  </PropertyGroup>

  <!-- 零第三方依赖：进程编排仅用 BCL（System.Diagnostics / System.IO） -->

</Project>
```

关键决策：

- **`GenerateDocumentationFile` 不开启**：0 警告硬门槛（`GenerateDocumentationFile` + CS1591）的动机是 **docfx 从 TGBot 项目生成 API 文档的质量**；工具项目自身不产文档、不进发布产物与镜像，XML 注释收益为零。工具项目仍须**编译 0 警告**（Nullable/ImplicitUsings 与主项目同风格）。此界定将同步写入 AGENTS.md（§7）。
- `InvariantGlobalization=true` 与主项目一致（工具无本地化需求，避免环境差异）。
- 无 `PublishSingleFile`（工具不发布，仅 `dotnet run`）。
- 无任何 `PackageReference`（手写参数解析，不引 System.CommandLine）。

#### `Program.cs` 结构（契约，供 developer 实现）

```
顶层语句：
  int exitCode = DocBuilder.Main(args);
  return exitCode;

internal static class DocBuilder：
  static int Main(string[] args)
    - 解析参数：--help/-h、--skip-publish、--keep-cache（手写循环，未知参数报错+提示 --help，返回 1）
    - FindRepoRoot()：从 Environment.CurrentDirectory 向上逐级查找 TGBot.Docfx/docfx.json
        · 找到 → 仓库根
        · 找不到 → stderr 输出"请在仓库根目录或子目录运行"，返回 1
    - 若 --help：打印用法（含 docfx 安装指引与 DOTNET_ROOT 说明），返回 0

  static string? ResolveDocfxCommand()          // §4.1
  static void CleanDocfxCache(string repoRoot)  // §4.2（--keep-cache 跳过）
  static int Publish(string repoRoot)           // §4.3（--skip-publish 跳过）
  static int BuildDocs(string repoRoot, string docfxCmd) // §4.4
```

### 3.2 执行流程编排（四步）

```
┌─ 解析参数 / 定位仓库根（向上找 TGBot.Docfx/docfx.json）
├─ ① 检查 docfx 可用性 → ResolveDocfxCommand()：缺失 → 安装指引 + 退出码 1
├─ ② 清理增量缓存 → rm -rf src/TGBot/obj/docfx（跨平台；--keep-cache 跳过）
├─ ③ publish 生成 XML 文档 → dotnet publish … -o <临时目录>（不带 --no-restore；--skip-publish 跳过）
└─ ④ docfx build → docfx build TGBot.Docfx/docfx.json --warningsAsErrors（工作目录=仓库根，实时透传）
    成功 → 打印"文档已生成到 docs/，打开 docs/index.html 查看"，返回 0
```

## 4. 跨平台细节与决策

### 4.1 docfx 发现策略（回退链）

1. 直接执行 `docfx --version`（`ProcessStartInfo.FileName = "docfx"`，PATH 解析跨平台 OK）。
2. 若启动失败（`Win32Exception`/`FileNotFoundException`，即"未找到命令"）→ 回退尝试 `dotnet docfx --version`。
   - **决策**：`dotnet docfx` 是 dotnet CLI 的全局工具分发机制，**与 PATH 无关**，天然覆盖"全局工具已安装但 `~/.dotnet/tools` 未加入 PATH"的场景（Windows 上 `%USERPROFILE%\.dotnet\tools` 由安装器自动配置，无需处理；macOS/Linux 的 `~/.dotnet/tools` 依赖用户 PATH 配置）。**不做自动修改 PATH**——环境变量修改属隐性副作用，且回退链已覆盖，无需引入。
3. 两次均失败 → stderr 输出安装指引并返回 1：

   ```
   错误：未找到 docfx。请先安装：
     dotnet tool install --global docfx
   若已安装仍提示找不到，请确认 ~/.dotnet/tools（macOS/Linux）已加入 PATH。
   ```

   - 后续步骤使用解析到的命令名（`docfx` 或 `dotnet docfx`）。

### 4.2 增量缓存清理（Windows 只读文件）

- 目标：`<repoRoot>/src/TGBot/obj/docfx`，`Directory.Delete(path, recursive: true)`。
- **Windows 只读文件处理**：`Directory.Delete(recursive:true)` 在 Windows 上遇只读文件会抛 `IOException`。删除前先递归遍历清 `FileAttributes.ReadOnly`（`File.SetAttributes(f, FileAttributes.Normal)`）。
- **失败降级**：删除抛异常（文件被占用等）时**警告不中断**——docfx 会自行重建缓存，工具继续流程（避免 docfx 进程占用缓存时无谓失败）。仅打印 warning 到 stderr。
- `--keep-cache` 参数跳过本步（docfx 配置迭代调试用）。

### 4.3 publish 编排（NETSDK1047 解法）

- 命令：`dotnet publish src/TGBot/TGBot.csproj -c Release -o <tmp>`，**不带 `--no-restore`**（允许 restore，publish 产物包含最新 XML 文档，供 docfx metadata 使用）。
- 临时目录：`Path.GetTempPath()/tgdl-docfx-<GUID>`（唯一，避免与历史残留混合）；`finally` 中递归删除，删除失败忽略（OS 临时目录可清理）。
- publish 退出码非零 → 透传退出码终止。
- 顺序保证：publish 是最后一步依赖 `project.assets.json` 的操作；docfx 在第 ④ 步的 restore 覆盖 assets.json 后不再有 MSBuild 操作，NETSDK1047 不再出现。

### 4.4 docfx build 进程调用

- `ProcessStartInfo`：
  - `FileName` = 解析到的命令（`docfx` 或 `dotnet docfx`）
  - `ArgumentList`：`build`、`TGBot.Docfx/docfx.json`、`--warningsAsErrors`（`ArgumentList` 自动处理含空格路径，无注入面）
  - `WorkingDirectory` = 仓库根
  - **不重定向 stdout/stderr**（`UseShellExecute=false` 默认继承控制台）：输出实时透传、保留 docfx 颜色、无管道死锁风险。这是比"重定向 + 异步读"更简单可靠的实时方案。
- 退出码：非零 → 工具返回 docfx 的退出码（可诊断性优于统一返回 1）；零 → 打印成功提示。

### 4.5 DOTNET_ROOT 处理（决策：文档说明，工具不干预）

- **决策**：工具**不自动设置** `DOTNET_ROOT`。理由：
  - 它是本机 aspnetcore 运行时布局问题（`$HOME/dotnet` 自定义安装），仅影响特定开发机，与工具逻辑无关；
  - 工具内设置会掩盖环境问题、且 Windows 无此问题（引入平台分支）。
- 在 `--help` 输出与 AGENTS.md / README 文档中说明：macOS/Linux 自定义 dotnet 安装（如 `$HOME/dotnet`）的用户需 `export DOTNET_ROOT=$HOME/dotnet` 后运行。

> **实现补充（2026-08-17，developer）**：实测发现 `dotnet run` 会为 apphost 子进程注入
> `DOTNET_ROOT_<ARCH>`（如 `DOTNET_ROOT_X64`），其运行时查找优先级**高于**用户显式设置的
> `DOTNET_ROOT`，会遮蔽自定义安装导致 docfx 找不到 aspnetcore 运行时。工具在启动 docfx 子进程时，
> 若 `DOTNET_ROOT` 已设置则移除继承环境中的 `DOTNET_ROOT_<ARCH>` 变量（仍**不设置任何变量**，
> 不违背本决策）；`DOTNET_ROOT` 未设置时不干预（保留系统默认行为）。另实测 docfx 2.78.5 的
> 增量缓存为**内容级比对**（yml 内容不变则不重写，时间戳不刷新属正常增量行为），
> `src/TGBot/obj/docfx` 目录在 2.78.5 不存在——§4.2 清理逻辑保留为兼容旧版 docfx 的无害操作
> （目录不存在时 no-op）。

### 4.6 参数设计（简单优先）

| 参数 | 行为 |
| --- | --- |
| `--help` / `-h` | 打印用法（含安装指引与 DOTNET_ROOT 说明），退出码 0 |
| `--skip-publish` | 跳过 publish（XML 已最新时迭代 docfx 配置用） |
| `--keep-cache` | 跳过缓存清理（docfx 配置调试用） |

- 不提供 `--clean` 开关：**默认即全流程干净**（每次清缓存 + publish + build），`--clean` 语义与默认行为重复。
- 未知参数：stderr 提示 + 返回 1。

### 4.7 仓库根定位

- 从 `Environment.CurrentDirectory` 向上逐级查找 `TGBot.Docfx/docfx.json`，命中即仓库根。
- 与 build-docs.sh 的 `cd "$(dirname "$0")"`（定位脚本自身）不同：C# 工具的 `AppContext.BaseDirectory` 是编译输出目录（不可靠），向上查找实现简单且允许从仓库任意子目录运行。
- 找不到时报错并提示"请在仓库根目录或子目录运行"，返回 1。

## 5. tools/ 是否加入 TGBot.slnx —— 决策：**不入**

理由：

1. **隔离主构建/测试/CI**：`TGBot.slnx` 是 `dotnet build` / `dotnet test` / CI 的入口；工具入 slnx 后每次主构建都会被牵连（新目标框架、警告门槛、构建时长），与"不影响主链路"的目标相悖。
2. **零依赖自包含**：工具无 PackageReference，不需要 slnx 管理依赖图。
3. **工具只在文档构建场景使用**：`dotnet run --project TGBot.Docfx` 直接指定项目路径即可，IDE 可见性非必需。

代价与缓解：

- 工具编译错误不会被主 CI 捕获。缓解：文档 agent 每次运行工具即触发编译（`dotnet run` 含 build）；dev 合流验证时 developer 顺手 `dotnet build TGBot.Docfx` 确认编译 0 警告（写入 §9 实现要点）。

## 6. CI 影响

- **无**。release.yml 不含文档 job；工具不影响镜像构建/发布链路。
- 可选（本次不做）：未来在 CI 增加"文档构建 0 警告"校验 job（需自托管/容器内置 docfx），仅作备注。

## 7. build-docs.sh 处置与文档同步（developer/devops 实现工作）

- **删除** `build-docs.sh`（单一入口，避免双脚本漂移）。
- 同步文件清单（命令替换为 `dotnet run --project TGBot.Docfx`）：

| 文件 | 位置 | 改动 |
| --- | --- | --- |
| `README.md` | 开发章节（约 L253–254） | 命令替换 |
| `README.en.md` | 开发章节（约 L263–264） | 命令替换 |
| `CONTRIBUTING.md` | 命令表（L38） | 命令替换 |
| `AGENTS.md` | 命令段（L23） | 命令替换 + DOTNET_ROOT 说明 + 新增 tools/ 说明（0 警告门槛界定：编译 0 警告；XML 注释不强制） |
| `.opencode/agent/docs.md` | description（L2）+ 正文（L12） | 命令替换 |
| `.opencode/command/doc.md` | L6 | 命令替换 |
| `TGBot.Docfx/index.md` | "构建文档"（L22–23） | 替换为工具命令（docfx 站点内指引保持一致） |

- **不改**：`CHANGELOG.md`（历史记录）、`docs/design-internationalization.md`（历史设计文档）、`docs/history/*`（工作日志）。

## 8. 测试策略

**决策：不建工具单测项目。** 理由：

1. 工具是薄编排（进程调用 + 目录删除），核心风险在"编排顺序"与"环境差异"，单测需 mock 进程/文件系统，成本高于收益；
2. 主项目 0 警告门槛与测试基建（xunit 分析器）是为 TGBot 发布产物服务的，工具不适用；
3. 三平台人工验收（下述）已覆盖核心风险面。
4. 若未来工具逻辑复杂化（如解析 docfx 输出、多参数矩阵），再补 `TGBot.Docfx.Tests`。

### 人工验收步骤（每平台执行）

| 步骤 | 命令 | 预期 |
| --- | --- | --- |
| 1. 干净环境 | `dotnet run --project TGBot.Docfx` | 退出码 0；`docs/index.html` 存在；无 docfx 警告输出 |
| 2. 增量重跑 | 再次执行同一命令 | 退出码 0；`TGBot.Docfx/api/TGBot.*.yml` 时间戳刷新 |
| 3. 缺 docfx | `PATH` 剔除 docfx 后执行 | 安装指引输出；退出码非 0 |
| 4. 参数 | `--help` / `--skip-publish` / `--keep-cache` | 各自按 §4.6 行为工作 |
| 5. 文档一致性 | 对照 §7 清单 | 无 build-docs.sh 引用残留（`grep -r build-docs` 空） |

## 9. 给 developer 的实现要点

1. 新建 `TGBot.Docfx/TGBot.Docfx.csproj`（§3.1 全文）+ `Program.cs`（§3.1 结构契约）。
2. 四步编排严格按 §3.2 顺序：**docfx 检测 → 清缓存 → publish → docfx build**；publish 不带 `--no-restore`。
3. 进程调用一律 `ProcessStartInfo.ArgumentList`（无 shell、无注入）；输出不重定向（继承控制台）。
4. docfx 回退链 `docfx` → `dotnet docfx`（§4.1）；缺失时输出安装指引返回 1。
5. 缓存清理：先递归清 `ReadOnly` 属性再 `Directory.Delete(recursive:true)`，失败仅警告（§4.2）。
6. 工具编译须 0 警告；不要求 XML 注释（§3.1）。
7. 完成后执行 §7 文件清单同步并删除 `build-docs.sh`；`grep -r "build-docs"` 确认无残留。
8. 提交前本机验证 §8 步骤 1–2（含 `dotnet build TGBot.Docfx` 编译确认）。

## 10. 不做的边界（明确）

- `scripts/install.sh` 平台化：不做（部署保持 Linux）。
- `/update` 资产平台化：不做。
- CI 文档校验 job：不做（仅备注可选）。
- 工具单测项目：不建（§8）。
- docfx 全局工具打包/内置：不做（保持 `dotnet tool install --global docfx` 安装方式，与现状一致）。
- 自动设置 DOTNET_ROOT / 修改 PATH：不做（§4.1 / §4.5）。