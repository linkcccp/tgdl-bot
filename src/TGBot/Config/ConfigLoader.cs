namespace TGBot.Config;

/// <summary>
/// 配置文件加载失败异常（文件不存在或不可读）。
/// </summary>
public sealed class ConfigLoadException : Exception
{
    /// <summary>
    /// 初始化 <see cref="ConfigLoadException"/>。
    /// </summary>
    /// <param name="message">中文错误提示。</param>
    public ConfigLoadException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// 负责定位并读取 config.conf。
/// <para>查找顺序：<c>--config &lt;path&gt;</c> 参数 → 环境变量 <c>TGDL_CONFIG</c> →
/// 与程序二进制同目录 → 当前工作目录。若仍找不到则抛出 <see cref="ConfigLoadException"/>。</para>
/// </summary>
public static class ConfigLoader
{
    /// <summary>环境变量名，用于指定配置文件路径。</summary>
    public const string EnvVarName = "TGDL_CONFIG";

    /// <summary>配置文件默认文件名。</summary>
    public const string DefaultFileName = "config.conf";

    /// <summary>
    /// 定位配置文件路径。
    /// </summary>
    /// <param name="explicitPath">命令行指定的路径，可为 <see langword="null"/>。</param>
    /// <returns>配置文件的绝对路径。</returns>
    /// <exception cref="ConfigLoadException">无法找到配置文件时抛出。</exception>
    public static string Locate(string? explicitPath = null)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            candidates.Add(explicitPath);
        }

        var envPath = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            candidates.Add(envPath);
        }

        var baseDir = Environment.ProcessPath is not null
            ? Path.GetDirectoryName(Environment.ProcessPath)
            : null;
        if (!string.IsNullOrEmpty(baseDir))
        {
            candidates.Add(Path.Combine(baseDir, DefaultFileName));
        }

        candidates.Add(Path.Combine(Environment.CurrentDirectory, DefaultFileName));

        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                return Path.GetFullPath(c);
            }
        }

        throw new ConfigLoadException(
            $"错误：找不到配置文件 {DefaultFileName}。请创建该文件，或通过 --config <路径> / 环境变量 {EnvVarName} 指定。");
    }

    /// <summary>
    /// 加载并解析配置。
    /// </summary>
    /// <param name="explicitPath">命令行指定的配置路径，可为 <see langword="null"/>。</param>
    /// <returns>解析结果。</returns>
    /// <exception cref="ConfigLoadException">配置文件不存在或不可读时抛出。</exception>
    /// <exception cref="ConfigParseException">配置内容缺失或格式错误时抛出。</exception>
    public static ConfigParseResult Load(string? explicitPath = null)
    {
        var path = Locate(explicitPath);
        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConfigLoadException($"错误：无法读取配置文件 {path}：{ex.Message}");
        }

        if (content.Length > 128 * 1024)
        {
            throw new ConfigParseException($"错误：配置文件 {path} 过大（超过 128KB）。");
        }

        return ConfigParser.Parse(content, path);
    }
}
