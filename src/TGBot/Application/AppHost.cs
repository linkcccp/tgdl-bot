namespace TGBot.Application;

/// <summary>
/// 应用宿主：负责解析命令行参数、加载配置、装配各模块并启动 Bot 服务。
/// </summary>
public static class AppHost
{
    /// <summary>
    /// 应用入口。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>进程退出码。</returns>
    public static Task<int> RunAsync(string[] args)
    {
        Console.Error.WriteLine("尚未实现应用宿主。");
        return Task.FromResult(1);
    }
}
