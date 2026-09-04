// 跨平台文档构建工具（替代 build-docs.sh）。
// 编排：① 检测 docfx → ② 清理增量缓存 → ③ dotnet publish（生成最新 XML 文档）→ ④ docfx metadata（生成 api/*.yml + api/toc.yml）→ ⑤ 自动生成 index.md → ⑥ docfx build（0 警告硬门槛）→ ⑦ 内联 sidetoc。
// 用法：dotnet run --project TGBot.Docfx [--help | --skip-publish | --keep-cache]
// 设计依据：docs/design-doc-builder.md（§3 实现细节）与 docs/adr/0005-跨平台文档构建工具.md。
// 入口说明：Main 直接作为程序入口（不用顶层语句包装——顶层语句与本类 Main 并存会触发 CS7022 警告）。

using System.ComponentModel;
using System.Diagnostics;

/// <summary>
/// 文档构建编排器。内部开发工具（不产文档、不进发布镜像），无 XML 文档生成要求，仅保持编译 0 警告。
/// </summary>
internal static class DocBuilder
{
    /// <summary>
    /// 工具入口：解析参数、定位仓库根，并按固定顺序编排 docfx 检测 → 清缓存 → publish → docfx build。
    /// </summary>
    /// <param name="args">命令行参数：--help/-h、--skip-publish、--keep-cache，其余视为未知参数。</param>
    /// <returns>进程退出码：0 成功；1 参数/环境错误；其余透传子进程退出码。</returns>
    internal static int Main(string[] args)
    {
        bool showHelp = false;
        bool skipPublish = false;
        bool keepCache = false;

        // 手写参数解析（零第三方依赖），未知参数报错并提示 --help。
        foreach (string arg in args)
        {
            switch (arg)
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                case "--skip-publish":
                    skipPublish = true;
                    break;
                case "--keep-cache":
                    keepCache = true;
                    break;
                default:
                    Console.Error.WriteLine($"错误：未知参数：{arg}");
                    Console.Error.WriteLine("提示：运行 --help 查看用法。");
                    return 1;
            }
        }

        // --help 不依赖仓库根，任何目录下均可查看。
        if (showHelp)
        {
            PrintHelp();
            return 0;
        }

        // 定位仓库根（从 cwd 向上找 TGBot.Docfx/docfx.json，允许在仓库任意子目录运行）。
        string? repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            Console.Error.WriteLine("错误：未找到 TGBot.Docfx/docfx.json，请在仓库根目录或其子目录运行本工具。");
            return 1;
        }

        // ① 检测 docfx（回退链：docfx → dotnet docfx），缺失时输出安装指引并退出 1。
        string? docfxCmd = ResolveDocfxCommand();
        if (docfxCmd is null)
        {
            Console.Error.WriteLine("错误：未找到 docfx。请先安装：");
            Console.Error.WriteLine("  dotnet tool install --global docfx");
            Console.Error.WriteLine("若已安装仍提示找不到，请确认 ~/.dotnet/tools（macOS/Linux）已加入 PATH。");
            return 1;
        }

        // ② 清理 docfx 增量缓存（--keep-cache 跳过），失败仅警告不中断。
        if (!keepCache)
        {
            CleanDocfxCache(repoRoot);
        }

        // ③ publish 生成最新 XML 文档（不带 --no-restore；--skip-publish 跳过）。
        if (!skipPublish)
        {
            int publishCode = Publish(repoRoot);
            if (publishCode != 0)
            {
                return publishCode;
            }
        }

        // ④ docfx metadata（工作目录=仓库根，生成 api/*.yml 和 api/toc.yml，供 index.md 生成与 build 使用）。
        int metadataCode = RunMetadata(repoRoot, docfxCmd);
        if (metadataCode != 0)
        {
            return metadataCode;
        }

        // ⑤ 自动生成 index.md（模块概览表）：从 toc.yml 提取命名空间，查表填充描述，build 前须存在。
        GenerateIndexMd(repoRoot);

        // ⑥ docfx build（工作目录=仓库根，--warningsAsErrors 保持 0 警告硬门槛，退出码透传）。
        int buildCode = BuildDocs(repoRoot, docfxCmd);
        if (buildCode != 0)
        {
            return buildCode;
        }

        // ⑦ 内联 sidetoc：将 toc.html 内容注入每个 API 页面的 <div id="sidetoc">，解决 file:// 协议下 AJAX 加载失败问题。
        InlineSidetoc(repoRoot);

        return 0;
    }

    /// <summary>
    /// 解析 docfx 可执行命令（§4.1 回退链）。
    /// </summary>
    /// <returns>可用命令（"docfx" 或 "dotnet docfx"），两者均无法启动时返回 null。</returns>
    internal static string? ResolveDocfxCommand()
    {
        // 直接执行 docfx（PATH 解析跨平台可用）。
        if (CanStartProcess("docfx", "docfx", "--version"))
        {
            return "docfx";
        }

        // 回退 dotnet docfx：dotnet CLI 全局工具分发机制，与 PATH 无关，
        // 覆盖"全局工具已装但 ~/.dotnet/tools 未加入 PATH"的场景。
        if (CanStartProcess("dotnet docfx", "dotnet", "docfx", "--version"))
        {
            return "dotnet docfx";
        }

        return null;
    }

    /// <summary>
    /// 清理 docfx 增量缓存目录 TGBot/obj/docfx（§4.2）。
    /// </summary>
    /// <param name="repoRoot">仓库根目录。</param>
    internal static void CleanDocfxCache(string repoRoot)
    {
        string cacheDir = Path.Combine(repoRoot, "TGBot", "obj", "docfx");
        if (!Directory.Exists(cacheDir))
        {
            return;
        }

        try
        {
            // Windows 上 Directory.Delete(recursive) 遇只读文件会抛 IOException，先递归清除只读属性。
            foreach (string file in Directory.EnumerateFiles(cacheDir, "*", SearchOption.AllDirectories))
            {
                FileAttributes attrs = File.GetAttributes(file);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
                }
            }

            Directory.Delete(cacheDir, recursive: true);
            Console.WriteLine($"已清理 docfx 增量缓存：{cacheDir}");
        }
        catch (Exception ex)
        {
            // 删除失败（如文件被 docfx 进程占用）降级为警告：docfx 会自行重建缓存，流程继续。
            Console.Error.WriteLine($"警告：清理 docfx 增量缓存失败（{ex.Message}），docfx 将自行重建缓存，流程继续。");
        }
    }

    /// <summary>
    /// 执行 dotnet publish（§4.3）：生成最新 XML 文档供 docfx metadata 使用，产物输出到临时目录，用后清理。
    /// </summary>
    /// <param name="repoRoot">仓库根目录（作为 publish 工作目录）。</param>
    /// <returns>publish 进程退出码。</returns>
    internal static int Publish(string repoRoot)
    {
        // 唯一临时目录，避免与历史残留混合；finally 中递归删除（失败忽略，OS 临时目录可清理）。
        string tmpDir = Path.Combine(Path.GetTempPath(), $"tgdl-docfx-{Guid.NewGuid():N}");
        try
        {
            Console.WriteLine($">>> dotnet publish TGBot/TGBot.csproj -c Release -o {tmpDir}");
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = repoRoot,
            };
            psi.ArgumentList.Add("publish");
            psi.ArgumentList.Add("TGBot/TGBot.csproj");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("Release");
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(tmpDir);
            // 注意：不带 --no-restore。docfx metadata 随后会 restore 并覆盖 project.assets.json，
            // publish 是最后一步依赖 assets.json 的 MSBuild 操作，先 publish 可避免 NETSDK1047。
            return RunProcess(psi);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tmpDir))
                {
                    Directory.Delete(tmpDir, recursive: true);
                }
            }
            catch
            {
                // 临时目录清理失败不影响结果。
            }
        }
    }

    /// <summary>
    /// 执行 docfx build（§4.4）。
    /// </summary>
    /// <param name="repoRoot">仓库根目录（作为 docfx 工作目录）。</param>
    /// <param name="docfxCmd">解析得到的命令："docfx" 或 "dotnet docfx"。</param>
    /// <returns>docfx 进程退出码（非零透传，可诊断性优于统一返回 1）。</returns>
    internal static int BuildDocs(string repoRoot, string docfxCmd)
    {
        Console.WriteLine($">>> {docfxCmd} build TGBot.Docfx/docfx.json --warningsAsErrors");
        string[] parts = docfxCmd.Split(' ', 2);
        var psi = new ProcessStartInfo
        {
            FileName = parts[0],
            WorkingDirectory = repoRoot,
        };
        ApplyDocfxEnvironment(psi);
        if (parts.Length > 1)
        {
            psi.ArgumentList.Add(parts[1]);
        }

        psi.ArgumentList.Add("build");
        psi.ArgumentList.Add("TGBot.Docfx/docfx.json");
        psi.ArgumentList.Add("--warningsAsErrors");

        // 不重定向 stdout/stderr（默认继承控制台）：输出实时透传、保留 docfx 颜色、无管道死锁风险。
        int code = RunProcess(psi);
        if (code == 0)
        {
            Console.WriteLine(">>> 文档已生成到 TGBot.Docfx/_site/，打开 TGBot.Docfx/_site/index.html 查看。");
        }

        return code;
    }

    /// <summary>
    /// 执行 docfx metadata（§4.4.1）：从 XML 文档生成 api/*.yml 和 api/toc.yml，供 index.md 生成与 docfx build 使用。
    /// </summary>
    /// <param name="repoRoot">仓库根目录（作为 docfx 工作目录）。</param>
    /// <param name="docfxCmd">解析得到的命令："docfx" 或 "dotnet docfx"。</param>
    /// <returns>docfx 进程退出码（非零透传）。</returns>
    internal static int RunMetadata(string repoRoot, string docfxCmd)
    {
        Console.WriteLine($">>> {docfxCmd} metadata TGBot.Docfx/docfx.json");
        string[] parts = docfxCmd.Split(' ', 2);
        var psi = new ProcessStartInfo
        {
            FileName = parts[0],
            WorkingDirectory = repoRoot,
        };
        ApplyDocfxEnvironment(psi);
        if (parts.Length > 1)
        {
            psi.ArgumentList.Add(parts[1]);
        }

        psi.ArgumentList.Add("metadata");
        psi.ArgumentList.Add("TGBot.Docfx/docfx.json");

        return RunProcess(psi);
    }

    /// <summary>
    /// 尝试启动进程并等待退出，验证命令是否可用（仅捕获启动异常，不做输出重定向）。
    /// </summary>
    /// <param name="display">用于日志显示的命令名。</param>
    /// <param name="fileName">可执行文件路径。</param>
    /// <param name="args">启动参数。</param>
    /// <returns>启动成功返回 true；抛 Win32Exception/FileNotFoundException（命令不存在）返回 false。</returns>
    private static bool CanStartProcess(string display, string fileName, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo { FileName = fileName };
            ApplyDocfxEnvironment(psi);
            foreach (string a in args)
            {
                psi.ArgumentList.Add(a);
            }

            using Process proc = Process.Start(psi) ?? throw new InvalidOperationException("进程启动失败");
            proc.WaitForExit();
            // 启动成功但命令执行失败（如 dotnet docfx 的全局工具缺失）同样视为不可用，继续回退。
            if (proc.ExitCode != 0)
            {
                return false;
            }

            Console.WriteLine($">>> 检测 docfx：使用 {display}");
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// 清理 dotnet CLI 注入的架构后缀环境变量（DOTNET_ROOT_&lt;ARCH&gt;，如 DOTNET_ROOT_X64）。
    /// 该变量在 apphost 的运行时查找中优先级高于用户显式设置的 DOTNET_ROOT（dotnet run 会把
    /// DOTNET_ROOT_X64 指向 CLI 自身安装根，遮蔽自定义安装如 $HOME/dotnet，导致 docfx 找不到
    /// aspnetcore 运行时）。当 DOTNET_ROOT 已设置时将其移除，确保 docfx 使用用户指定的运行时根；
    /// 不自动设置任何变量（§4.5：尊重用户设置、不干预）。
    /// </summary>
    /// <param name="psi">docfx 子进程的启动配置。</param>
    private static void ApplyDocfxEnvironment(ProcessStartInfo psi)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_ROOT")))
        {
            return;
        }

        foreach (string key in psi.Environment.Keys
                     .Where(k => k.StartsWith("DOTNET_ROOT_", StringComparison.Ordinal))
                     .ToList())
        {
            psi.Environment.Remove(key);
        }
    }

    /// <summary>
    /// 启动进程并等待退出，返回退出码。
    /// </summary>
    /// <param name="psi">进程启动配置（输出不重定向，继承控制台）。</param>
    /// <returns>子进程退出码。</returns>
    private static int RunProcess(ProcessStartInfo psi)
    {
        using Process proc = Process.Start(psi) ?? throw new InvalidOperationException("进程启动失败");
        proc.WaitForExit();
        return proc.ExitCode;
    }

    /// <summary>
    /// 从当前目录向上逐级查找 TGBot.Docfx/docfx.json 以定位仓库根（§4.7）。
    /// </summary>
    /// <returns>仓库根绝对路径；未找到返回 null。</returns>
    private static string? FindRepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(Environment.CurrentDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TGBot.Docfx", "docfx.json")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// 内联 sidetoc：读取 TGBot.Docfx/_site/api/toc.html 的 TOC 内容，注入每个 API 页面的 &lt;div id="sidetoc"&gt;&lt;/div&gt;，
    /// 解决 file:// 协议下 jQuery AJAX 加载 toc.html 被 CORS 阻止导致侧边栏为空的问题。
    /// </summary>
    /// <param name="repoRoot">仓库根目录。</param>
    internal static void InlineSidetoc(string repoRoot)
    {
        string tocHtmlPath = Path.Combine(repoRoot, "TGBot.Docfx", "_site", "api", "toc.html");
        if (!File.Exists(tocHtmlPath))
        {
            Console.Error.WriteLine("警告：TGBot.Docfx/_site/api/toc.html 不存在，跳过 sidetoc 内联。");
            return;
        }

        string tocContent = File.ReadAllText(tocHtmlPath);

        // 提取 <div id="sidetoggle"> 到对应的闭合 </div> 之间的内容（含自身）。
        // toc.html 结构：<div id="sidetoggle"><div>...toc...</div></div>
        string sidetoggleStart = "<div id=\"sidetoggle\">";
        int startIdx = tocContent.IndexOf(sidetoggleStart, StringComparison.Ordinal);
        if (startIdx < 0)
        {
            Console.Error.WriteLine("警告：toc.html 中未找到 <div id=\"sidetoggle\">，跳过 sidetoc 内联。");
            return;
        }

        // 从 startIdx 开始，找到匹配的闭合 </div>（嵌套计数）。
        string html = tocContent[startIdx..];
        int depth = 0;
        int endIdx = -1;
        int i = 0;
        while (i < html.Length)
        {
            int divOpen = html.IndexOf("<div", i, StringComparison.OrdinalIgnoreCase);
            int divClose = html.IndexOf("</div>", i, StringComparison.OrdinalIgnoreCase);

            if (divClose < 0)
            {
                break;
            }

            if (divOpen >= 0 && divOpen < divClose)
            {
                // 检查是否是自闭合或新标签（简单计数）。
                depth++;
                i = divOpen + 4;
            }
            else
            {
                depth--;
                if (depth == 0)
                {
                    endIdx = divClose + "</div>".Length;
                    break;
                }

                i = divClose + "</div>".Length;
            }
        }

        if (endIdx < 0)
        {
            Console.Error.WriteLine("警告：toc.html 中 <div id=\"sidetoggle\"> 闭合标签不匹配，跳过 sidetoc 内联。");
            return;
        }

        string sidetoggleHtml = html[..endIdx];
        string placeholder = "<div id=\"sidetoc\"></div>";
        string apiDir = Path.Combine(repoRoot, "TGBot.Docfx", "_site", "api");
        int processedCount = 0;

        foreach (string htmlFile in Directory.EnumerateFiles(apiDir, "*.html"))
        {
            string content = File.ReadAllText(htmlFile);
            if (!content.Contains(placeholder, StringComparison.Ordinal))
            {
                continue;
            }

            string updated = content.Replace(placeholder, sidetoggleHtml, StringComparison.Ordinal);
            File.WriteAllText(htmlFile, updated);
            processedCount++;
        }

        Console.WriteLine($">>> 已将 sidetoc 内联到 {processedCount} 个 API 页面。");
    }

    /// <summary>
    /// 自动生成 index.md（模块概览表）：从 docfx metadata 生成的 api/toc.yml 提取命名空间条目，
    /// 根据内置描述字典生成带 xref 链接的 markdown 表格（§4.8）。
    /// </summary>
    /// <param name="repoRoot">仓库根目录。</param>
    internal static void GenerateIndexMd(string repoRoot)
    {
        string tocPath = Path.Combine(repoRoot, "TGBot.Docfx", "api", "toc.yml");
        if (!File.Exists(tocPath))
        {
            Console.Error.WriteLine("警告：TGBot.Docfx/api/toc.yml 不存在，跳过 index.md 生成。");
            return;
        }

        string[] namespaces = ParseNamespacesFromToc(tocPath);
        if (namespaces.Length == 0)
        {
            Console.Error.WriteLine("警告：toc.yml 中未找到命名空间条目，跳过 index.md 生成。");
            return;
        }

        var descriptions = new Dictionary<string, string>
        {
            ["TGBot.Config"] = "config.conf 解析与校验（中文错误提示）",
            ["TGBot.Config.Overlay"] = "访问列表覆盖/合并策略",
            ["TGBot.Logging"] = "分级日志（控制台 + 可选文件）",
            ["TGBot.Security"] = "URL/SSRF 校验、路径净化、临时目录、磁盘检查",
            ["TGBot.Access"] = "双重白名单访问控制",
            ["TGBot.Download"] = "yt-dlp 下载器抽象、并发闸门、任务注册表",
            ["TGBot.Update"] = "ffmpeg/yt-dlp 原子自更新",
            ["TGBot.Messaging"] = "Telegram 客户端抽象、上传服务、文案构建",
            ["TGBot.Application"] = "消息路由、下载协调、指令处理、Bot 宿主",
            ["TGBot.Cookie"] = "多站点 Cookie 解析与上传",
            ["TGBot.Texts"] = "面向用户的中文提示文案",
            ["TGBot.Texts.I18n"] = "多语言国际化支持",
        };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# TGBot API 文档");
        sb.AppendLine();
        sb.AppendLine("`tgdl-bot` 是一个基于 .NET 10 的 Telegram 下载 Bot 控制台程序，支持 yt-dlp 下载与本地 Bot API Server（--local 模式）上传，内存占用 < 100MB。");
        sb.AppendLine();
        sb.AppendLine("## 模块概览");
        sb.AppendLine();
        sb.AppendLine("| 命名空间 | 职责 |");
        sb.AppendLine("| --- | --- |");

        foreach (string ns in namespaces)
        {
            descriptions.TryGetValue(ns, out string? desc);
            desc ??= "待补充";
            sb.AppendLine($"| <xref:{ns}> | {desc} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 构建文档");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine("dotnet tool install --global docfx");
        sb.AppendLine("dotnet run --project TGBot.Docfx");
        sb.AppendLine("# 输出到 TGBot.Docfx/_site/ 目录，用浏览器打开 TGBot.Docfx/_site/index.html（--help 查看参数）");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("本文档由 DocFX 从 XML 文档注释自动生成。");

        string indexPath = Path.Combine(repoRoot, "TGBot.Docfx", "index.md");
        File.WriteAllText(indexPath, sb.ToString());
        Console.WriteLine($">>> 已自动生成 index.md（{namespaces.Length} 个命名空间）。");
    }

    /// <summary>
    /// 从 docfx 生成的 toc.yml 中解析所有 type: Namespace 的条目（简单行级 YAML 解析，零第三方依赖）。
    /// toc.yml 由 docfx metadata 生成，结构固定：items: → - uid/name/type → items: → …
    /// 简化解法：直接扫描所有行，匹配 "  type: Namespace" 模式，向上查找最近的 "uid:" 行。
    /// </summary>
    /// <param name="tocPath">toc.yml 文件路径。</param>
    /// <returns>命名空间 uid 列表（保持 toc.yml 中的出现顺序）。</returns>
    internal static string[] ParseNamespacesFromToc(string tocPath)
    {
        var namespaces = new List<string>();
        string[] lines = File.ReadAllLines(tocPath);

        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed != "type: Namespace")
            {
                continue;
            }

            // 向上回溯，找最近的 uid: 行
            for (int j = i - 1; j >= 0; j--)
            {
                string uidLine = lines[j].TrimStart();
                if (uidLine.StartsWith("- uid: ", StringComparison.Ordinal))
                {
                    namespaces.Add(uidLine[7..]);
                    break;
                }

                if (uidLine.StartsWith("uid: ", StringComparison.Ordinal))
                {
                    namespaces.Add(uidLine[5..]);
                    break;
                }
            }
        }

        return namespaces.ToArray();
    }

    /// <summary>
    /// 打印帮助信息（含 docfx 安装指引与 DOTNET_ROOT 说明）。
    /// </summary>
    private static void PrintHelp()
    {
        Console.WriteLine("""
            TGBot.Docfx —— 跨平台 API 文档构建工具（替代 build-docs.sh）

            用法：dotnet run --project TGBot.Docfx [参数]

            执行流程（固定顺序）：
              ① 检测 docfx（docfx 命令；缺失时回退 dotnet docfx）
              ② 清理 docfx 增量缓存（TGBot/obj/docfx）
              ③ dotnet publish TGBot（生成最新 XML 文档，供 docfx metadata 使用）
              ④ docfx metadata TGBot.Docfx/docfx.json（生成 api/*.yml 和 api/toc.yml）
              ⑤ 自动生成 index.md（从 toc.yml 提取命名空间，生成模块概览表，build 前须存在）
              ⑥ docfx build TGBot.Docfx/docfx.json --warningsAsErrors（0 警告硬门槛）
              ⑦ 内联 sidetoc（将 toc.html 注入 API 页面，解决 file:// 下侧边栏为空问题）

            输出：
              TGBot.Docfx/_site/  docfx 构建出的站点（打开 TGBot.Docfx/_site/index.html 查看）
              TGBot.Docfx/api/ docfx metadata 生成的 API yml

            参数：
              --help / -h       打印本帮助并退出
              --skip-publish    跳过 publish（XML 文档已最新时迭代 docfx 配置用）
              --keep-cache      跳过缓存清理（docfx 配置调试用）

            前置条件：
              - 安装 docfx：dotnet tool install --global docfx
              - macOS/Linux 使用自定义 dotnet 安装（如 $HOME/dotnet）时，docfx 需要 aspnetcore 运行时，
                请先执行：export DOTNET_ROOT=$HOME/dotnet （Windows 通常无需此步）
            """);
    }
}