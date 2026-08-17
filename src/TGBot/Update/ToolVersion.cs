// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.Globalization;

namespace TGBot.Update;

/// <summary>
/// 工具版本模型（yt-dlp/ffmpeg），支持数值化比较。
/// </summary>
public sealed class ToolVersion : IComparable<ToolVersion>
{
    private readonly int[] _components;

    /// <summary>
    /// 初始化 <see cref="ToolVersion"/>。
    /// </summary>
    /// <param name="components">数值化版本分量。</param>
    /// <param name="suffix">版本后缀（如 -1、beta），可为空。</param>
    public ToolVersion(IReadOnlyList<int> components, string? suffix = null)
    {
        _components = components.ToArray();
        Suffix = suffix ?? string.Empty;
    }

    /// <summary>
    /// 原始版本字符串。
    /// </summary>
    public string Raw { get; private init; } = string.Empty;

    /// <summary>
    /// 版本后缀。
    /// </summary>
    public string Suffix { get; }

    /// <summary>
    /// 尝试从任意字符串解析版本号，如 <c>2025.01.26</c>、<c>n9.0.1</c>、<c>7.1.1-1</c>。
    /// </summary>
    /// <param name="raw">原始字符串。</param>
    /// <param name="version">解析结果。</param>
    /// <returns>解析成功返回 <see langword="true"/>。</returns>
    public static bool TryParse(string? raw, out ToolVersion version)
    {
        version = new ToolVersion(Array.Empty<int>());
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        raw = raw.Trim();

        var start = -1;
        for (var i = 0; i < raw.Length; i++)
        {
            if (char.IsDigit(raw[i]))
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            return false;
        }

        var components = new List<int>();
        var i2 = start;
        while (i2 < raw.Length && char.IsDigit(raw[i2]))
        {
            var numStart = i2;
            while (i2 < raw.Length && char.IsDigit(raw[i2]))
            {
                i2++;
            }

            if (int.TryParse(raw[numStart..i2], NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                components.Add(n);
            }

            if (i2 < raw.Length && raw[i2] == '.')
            {
                i2++;
            }
            else
            {
                break;
            }
        }

        var suffix = i2 < raw.Length ? raw[i2..] : string.Empty;
        if (components.Count == 0)
        {
            return false;
        }

        version = new ToolVersion(components, suffix)
        {
            Raw = raw.Trim(),
        };
        return true;
    }

    /// <summary>
    /// 解析版本号，失败时抛出异常。
    /// </summary>
    /// <param name="raw">原始字符串。</param>
    /// <returns>版本对象。</returns>
    /// <exception cref="FormatException">无法解析时抛出。</exception>
    public static ToolVersion Parse(string raw)
    {
        if (TryParse(raw, out var version))
        {
            return version;
        }

        throw new FormatException($"无法解析版本号：{raw}");
    }

    /// <summary>
    /// 比较两个版本。相等返回 0，<see langword="this"/> 更大返回正数，更小返回负数。
    /// </summary>
    /// <param name="other">另一版本。</param>
    /// <returns>比较结果。</returns>
    public int CompareTo(ToolVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var max = Math.Max(_components.Length, other._components.Length);
        for (var i = 0; i < max; i++)
        {
            var a = i < _components.Length ? _components[i] : 0;
            var b = i < other._components.Length ? other._components[i] : 0;
            if (a != b)
            {
                return a.CompareTo(b);
            }
        }

        return 0;
    }

    /// <inheritdoc />
    public override string ToString() => string.IsNullOrEmpty(Raw) ? string.Join('.', _components) : Raw;
}
