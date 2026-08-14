using TGBot.Logging;

namespace TGBot.Security;

/// <summary>
/// 下载临时目录管理器：创建 0700 权限的任务子目录、清理遗留目录。
/// </summary>
public sealed class TempDirManager
{
    private readonly string _root;
    private readonly IAppLogger _logger;

    /// <summary>
    /// 初始化 <see cref="TempDirManager"/>。
    /// </summary>
    /// <param name="root">临时根目录（绝对路径）。</param>
    /// <param name="logger">日志器。</param>
    public TempDirManager(string root, IAppLogger logger)
    {
        _root = Path.GetFullPath(root);
        _logger = logger;
    }

    /// <summary>
    /// 根目录（绝对路径）。
    /// </summary>
    public string Root => _root;

    /// <summary>
    /// 初始化根目录并清理所有遗留的任务子目录。
    /// </summary>
    /// <exception cref="IOException">目录无法创建时抛出。</exception>
    public void Initialize()
    {
        Directory.CreateDirectory(_root);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_root, (UnixFileMode)((int)UnixFileMode.UserRead | (int)UnixFileMode.UserWrite | (int)UnixFileMode.UserExecute));
        }

        foreach (var dir in Directory.EnumerateDirectories(_root))
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.Warn($"清理遗留临时目录失败：{dir}，{ex.Message}");
            }
        }
    }

    /// <summary>
    /// 创建唯一的任务子目录（0700 权限）。
    /// </summary>
    /// <returns>子目录绝对路径。</returns>
    public string CreateJobDirectory()
    {
        var dir = Path.Combine(_root, Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(dir, (UnixFileMode)((int)UnixFileMode.UserRead | (int)UnixFileMode.UserWrite | (int)UnixFileMode.UserExecute));
        }

        return dir;
    }

    /// <summary>
    /// 删除任务目录（忽略文件权限问题，尽力删除）。
    /// </summary>
    /// <param name="jobDir">任务目录绝对路径。</param>
    public void CleanupJobDirectory(string jobDir)
    {
        if (!PathSanitizer.IsWithinDirectory(_root, jobDir))
        {
            _logger.Warn($"拒绝清理越界路径：{jobDir}");
            return;
        }

        try
        {
            Directory.Delete(jobDir, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.Warn($"清理任务临时目录失败：{jobDir}，{ex.Message}");
        }
    }
}
