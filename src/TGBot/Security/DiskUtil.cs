using System.Runtime.InteropServices;

namespace TGBot.Security;

/// <summary>
/// 磁盘工具：查询目录所在文件系统的可用空间。
/// </summary>
public static class DiskUtil
{
    /// <summary>
    /// 获取路径所在文件系统的可用字节数。
    /// </summary>
    /// <param name="path">任意路径。</param>
    /// <returns>可用字节数；无法获取时返回 <see langword="null"/>。</returns>
    public static long? GetFreeSpaceBytes(string path)
    {
        try
        {
            var root = new DirectoryInfo(Path.GetFullPath(path)).Root.FullName;
            var info = new DriveInfo(root);
            return info.AvailableFreeSpace;
        }
        catch
        {
            return null;
        }
    }
}
