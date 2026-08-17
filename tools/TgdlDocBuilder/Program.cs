// 跨平台文档构建工具（替代 build-docs.sh）。
// 编排：① 检测 docfx → ② 清理增量缓存 → ③ dotnet publish（生成最新 XML 文档）→ ④ docfx build（0 警告硬门槛）。
// 用法：dotnet run --project tools/TgdlDocBuilder [--help | --skip-publish | --keep-cache]
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

        // 定位仓库根（从 cwd 向上找 docfx/docfx.json，允许在仓库任意子目录运行）。
        string? repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            Console.Error.WriteLine("错误：未找到 docfx/docfx.json，请在仓库根目录或其子目录运行本工具。");
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

        // ④ docfx build（工作目录=仓库根，--warningsAsErrors 保持 0 警告硬门槛，退出码透传）。
        return BuildDocs(repoRoot, docfxCmd);
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
    /// 清理 docfx 增量缓存目录 src/TGBot/obj/docfx（§4.2）。
    /// </summary>
    /// <param name="repoRoot">仓库根目录。</param>
    internal static void CleanDocfxCache(string repoRoot)
    {
        string cacheDir = Path.Combine(repoRoot, "src", "TGBot", "obj", "docfx");
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
            Console.WriteLine($">>> dotnet publish src/TGBot/TGBot.csproj -c Release -o {tmpDir}");
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = repoRoot,
            };
            psi.ArgumentList.Add("publish");
            psi.ArgumentList.Add("src/TGBot/TGBot.csproj");
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
        Console.WriteLine($">>> {docfxCmd} build docfx/docfx.json --warningsAsErrors");
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
        psi.ArgumentList.Add("docfx/docfx.json");
        psi.ArgumentList.Add("--warningsAsErrors");

        // 不重定向 stdout/stderr（默认继承控制台）：输出实时透传、保留 docfx 颜色、无管道死锁风险。
        int code = RunProcess(psi);
        if (code == 0)
        {
            Console.WriteLine(">>> 文档已生成到 docs/，打开 docs/index.html 查看。");
        }

        return code;
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
    /// 从当前目录向上逐级查找 docfx/docfx.json 以定位仓库根（§4.7）。
    /// </summary>
    /// <returns>仓库根绝对路径；未找到返回 null。</returns>
    private static string? FindRepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(Environment.CurrentDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docfx", "docfx.json")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// 打印帮助信息（含 docfx 安装指引与 DOTNET_ROOT 说明）。
    /// </summary>
    private static void PrintHelp()
    {
        Console.WriteLine("""
            TgdlDocBuilder —— 跨平台 API 文档构建工具（替代 build-docs.sh）

            用法：dotnet run --project tools/TgdlDocBuilder [参数]

            执行流程（固定顺序）：
              ① 检测 docfx（docfx 命令；缺失时回退 dotnet docfx）
              ② 清理 docfx 增量缓存（src/TGBot/obj/docfx）
              ③ dotnet publish src/TGBot（生成最新 XML 文档，供 docfx metadata 使用）
              ④ docfx build docfx/docfx.json --warningsAsErrors（0 警告硬门槛）

            输出：
              docs/         docfx 构建出的站点（打开 docs/index.html 查看）
              docfx/api/    docfx metadata 生成的 API yml

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