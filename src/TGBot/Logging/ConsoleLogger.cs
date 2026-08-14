using System.Text;

namespace TGBot.Logging;

/// <summary>
/// 写入控制台（stdout）并可选追加到日志文件的日志实现。
/// <para>线程安全：使用独立锁串行化写入，避免交错输出。</para>
/// </summary>
public sealed class ConsoleLogger : IAppLogger
{
    private readonly object _sync = new();
    private readonly LogLevel _minLevel;
    private readonly TextWriter _writer;
    private readonly StreamWriter? _fileWriter;

    /// <summary>
    /// 初始化 <see cref="ConsoleLogger"/>。
    /// </summary>
    /// <param name="minLevel">最小输出级别，低于该级别的日志被忽略。</param>
    /// <param name="logFilePath">可选的文件日志路径；为 <see langword="null"/> 时不写文件。</param>
    /// <exception cref="IOException">日志文件无法创建或写入时抛出。</exception>
    public ConsoleLogger(LogLevel minLevel, string? logFilePath)
    {
        _minLevel = minLevel;
        _writer = Console.Out;
        if (!string.IsNullOrWhiteSpace(logFilePath))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(logFilePath));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _fileWriter = new StreamWriter(logFilePath, append: true, new UTF8Encoding(false)) { AutoFlush = true };
        }
    }

    /// <inheritdoc />
    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        if (level < _minLevel)
        {
            return;
        }

        var line = Format(level, message, exception);
        lock (_sync)
        {
            _writer.WriteLine(line);
            _fileWriter?.WriteLine(line);
        }
    }

    /// <inheritdoc />
    public void Trace(string message) => Log(LogLevel.Trace, message);

    /// <inheritdoc />
    public void Debug(string message) => Log(LogLevel.Debug, message);

    /// <inheritdoc />
    public void Info(string message) => Log(LogLevel.Info, message);

    /// <inheritdoc />
    public void Warn(string message) => Log(LogLevel.Warn, message);

    /// <inheritdoc />
    public void Error(string message, Exception? exception = null) => Log(LogLevel.Error, message, exception);

    private static string Format(LogLevel level, string message, Exception? exception)
    {
        var ts = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
        var levelText = level.ToString().ToUpperInvariant().PadRight(5);
        var sb = new StringBuilder();
        sb.Append('[').Append(ts).Append("] [").Append(levelText).Append("] ").Append(message);
        if (exception is not null)
        {
            sb.Append(" => ").Append(exception);
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_sync)
        {
            _fileWriter?.Dispose();
        }
    }
}
