// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Collections.Concurrent;
using System.Text.Json;
using TGBot.Logging;

namespace TGBot.Texts.I18n;

/// <summary>
/// per-user 语言显式选择存储：<c>&lt;StateDir&gt;/languages.json</c>（userId → 语言）。
/// <para>仅存储显式选择（/language 或首次弹窗选择），auto/映射结果不落盘；
/// 内存 <see cref="ConcurrentDictionary{TKey,TValue}"/> + 原子写（临时文件 + rename）保证并发安全。</para>
/// </summary>
public sealed class UserLanguageStore
{
    /// <summary>语言状态文件名。</summary>
    public const string FileName = "languages.json";

    private readonly string _filePath;
    private readonly IAppLogger? _logger;
    private readonly ConcurrentDictionary<long, string> _languages = new();
    private readonly object _writeLock = new();

    /// <summary>
    /// 初始化 <see cref="UserLanguageStore"/>。
    /// </summary>
    /// <param name="stateDir">状态目录（D6 起为 <c>StateDir</c> 配置键）。</param>
    /// <param name="logger">日志器（可空）。</param>
    public UserLanguageStore(string stateDir, IAppLogger? logger = null)
    {
        _filePath = Path.Combine(Path.GetFullPath(stateDir), FileName);
        _logger = logger;
        // 保证状态目录存在（幂等；Load/Persist 均依赖）。
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
    }

    /// <summary>
    /// 从磁盘加载已持久化的语言选择（启动时调用一次；文件缺失或损坏时静默降级为空）。
    /// </summary>
    public void Load()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict is null)
            {
                return;
            }

            foreach (var (rawId, rawLang) in dict)
            {
                if (long.TryParse(rawId, out var userId) &&
                    LanguageCatalog.NormalizeLanguageCode(rawLang) is { } lang)
                {
                    _languages[userId] = lang;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.Warn($"加载语言状态失败，将重新开始记录：{ex.Message}");
        }
    }

    /// <summary>
    /// 读取用户显式选择的语言。
    /// </summary>
    /// <param name="userId">用户 ID。</param>
    /// <returns>语言代码；未显式选择返回 <see langword="null"/>。</returns>
    public string? Get(long userId)
        => _languages.TryGetValue(userId, out var lang) ? lang : null;

    /// <summary>
    /// 判断用户是否已有显式语言选择。
    /// </summary>
    /// <param name="userId">用户 ID。</param>
    /// <returns>已有选择返回 <see langword="true"/>。</returns>
    public bool Has(long userId) => _languages.ContainsKey(userId);

    /// <summary>
    /// 设置用户语言并原子写盘（tmp + rename，单次写全量）。
    /// </summary>
    /// <param name="userId">用户 ID。</param>
    /// <param name="lang">语言代码（en/zh 等）。</param>
    /// <exception cref="ArgumentException"><paramref name="lang"/> 无法归一化时抛出。</exception>
    public void Set(long userId, string lang)
    {
        var normalized = LanguageCatalog.NormalizeLanguageCode(lang)
            ?? throw new ArgumentException($"非法语言代码：{lang}", nameof(lang));
        _languages[userId] = normalized;
        Persist();
    }

    private void Persist()
    {
        lock (_writeLock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var tmp = _filePath + ".tmp";
                var json = JsonSerializer.Serialize(_languages);
                File.WriteAllText(tmp, json);
                if (!OperatingSystem.IsWindows())
                {
                    // 状态文件 0600，防同机其他用户读取（对齐 CookieStore 先例）。
                    File.SetUnixFileMode(tmp, (UnixFileMode)((int)UnixFileMode.UserRead | (int)UnixFileMode.UserWrite));
                }

                File.Move(tmp, _filePath, overwrite: true);
            }
            catch (Exception ex)
            {
                _logger?.Warn($"语言状态写入失败：{ex.Message}");
            }
        }
    }
}