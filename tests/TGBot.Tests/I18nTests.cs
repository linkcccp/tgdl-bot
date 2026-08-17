// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 linkcccp

using System.IO;
using TGBot.Messaging;
using TGBot.Texts;
using TGBot.Texts.I18n;
using Xunit;

namespace TGBot.Tests;

/// <summary>
/// <see cref="I18nService"/> 单元测试（缺键回退、占位符、注册覆盖）。
/// </summary>
public class I18nServiceTests
{
    [Fact]
    public void Get_ExistingKey_ReturnsTranslatedText()
    {
        Assert.Contains("Audio", TestI18n.Instance.Get("en", UserTexts.ModeAudioButton), StringComparison.Ordinal);
        Assert.Contains("音频", TestI18n.Instance.Get("zh", UserTexts.ModeAudioButton), StringComparison.Ordinal);
    }

    [Fact]
    public void Get_MissingKey_FallsBackToEnThenKey()
    {
        // zh 缺键 → 回退 en；en 也缺 → 回退键名
        Assert.Equal("NoSuchKey", TestI18n.Instance.Get("zh", "NoSuchKey"));
    }

    [Fact]
    public void Get_MissingLanguage_FallsBackToEn()
    {
        // 未知语言码 → 默认 en
        Assert.Contains("Audio", TestI18n.Instance.Get("xx", UserTexts.ModeAudioButton), StringComparison.Ordinal);
    }

    [Fact]
    public void Get_WithPlaceholderArgs_FormatsText()
    {
        var text = TestI18n.Instance.Get("zh", UserTexts.DownloadProgress, 42, "3.2 MiB/s");
        Assert.Contains("42%", text, StringComparison.Ordinal);
        Assert.Contains("3.2 MiB/s", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Get_ArgContainingBraces_NoFormatException_LiteralOutput()
    {
        // P2-5 验收：参数值含花括号（如 YtDlpExtraArgs 的模板/JSON 片段）不抛 FormatException，
        // 花括号按字面原样输出（/config list 展示路径依赖此行为，转义会造成双花括号回归）。
        var text = TestI18n.Instance.Get("zh", UserTexts.ConfigListLine, "YtDlpExtraArgs", "--extractor-args youtube:player_client={a,b}", TestI18n.Zh("ConfigSourceDefault"));
        Assert.Contains("--extractor-args youtube:player_client={a,b}", text, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Get_BrokenTemplate_FallsBackWithoutThrow()
    {
        // 模板自身含未配对花括号（翻译资源缺陷）→ 兜底返回原文案，绝不抛异常（防链路静默无响应）
        var i18n = new I18nService(defaultLanguage: "en", extraCatalogs: new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["en"] = new Dictionary<string, string> { ["Broken"] = "value {0} tail {" },
        });
        var text = i18n.Get("en", "Broken", "arg");
        Assert.Contains("value", text, StringComparison.Ordinal);
        Assert.Contains("tail", text, StringComparison.Ordinal);
    }

    [Fact]
    public void T_UsesDefaultLanguage()
    {
        var en = new I18nService(defaultLanguage: "en");
        Assert.Contains("Audio", en.T(UserTexts.ModeAudioButton), StringComparison.Ordinal);

        var zh = new I18nService(defaultLanguage: "zh");
        Assert.Contains("音频", zh.T(UserTexts.ModeAudioButton), StringComparison.Ordinal);
    }

    [Fact]
    public void Register_OverridesBuiltin()
    {
        // 注册在 I18nService 构造时合并生效（AppHost 单例，无运行中注册场景）。
        LanguageCatalog.Register("zh", new Dictionary<string, string> { [UserTexts.ModeAudioButton] = "覆盖文本" });
        try
        {
            var i18n = new I18nService();
            Assert.Contains("覆盖文本", i18n.Get("zh", UserTexts.ModeAudioButton), StringComparison.Ordinal);
        }
        finally
        {
            LanguageCatalog.Unregister("zh");
        }
    }

    [Fact]
    public void ExtraCatalogs_TakePrecedence()
    {
        var i18n = new I18nService(defaultLanguage: "en", extraCatalogs: new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["zz"] = new Dictionary<string, string> { [UserTexts.ModeAudioButton] = "zz-text" },
        });
        Assert.Contains("zz-text", i18n.Get("zz", UserTexts.ModeAudioButton), StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeLanguageCode_Variants()
    {
        Assert.Equal("zh", LanguageCatalog.NormalizeLanguageCode("zh-CN"));
        Assert.Equal("zh", LanguageCatalog.NormalizeLanguageCode("zh-TW"));
        Assert.Equal("zh", LanguageCatalog.NormalizeLanguageCode("zh-Hant"));
        Assert.Equal("en", LanguageCatalog.NormalizeLanguageCode("en-US"));
        Assert.Null(LanguageCatalog.NormalizeLanguageCode("ja"));
        Assert.Null(LanguageCatalog.NormalizeLanguageCode("ja-JP"));
        Assert.Null(LanguageCatalog.NormalizeLanguageCode("xx"));
        Assert.Null(LanguageCatalog.NormalizeLanguageCode(""));
        Assert.Null(LanguageCatalog.NormalizeLanguageCode(null));
    }

    [Fact]
    public void BotLanguageCodes_EnZh()
    {
        Assert.Equal("en", BotLanguage.En.Code());
        Assert.Equal("zh", BotLanguage.Zh.Code());
        Assert.True(BotLanguageExtensions.TryParseCode("zh", out var zh));
        Assert.Equal(BotLanguage.Zh, zh);
        Assert.True(BotLanguageExtensions.TryParseCode("en", out var en));
        Assert.Equal(BotLanguage.En, en);
        Assert.True(BotLanguageExtensions.TryParseCode("ZH", out var upper));
        Assert.Equal(BotLanguage.Zh, upper);
        Assert.False(BotLanguageExtensions.TryParseCode("fr", out _));
        Assert.False(BotLanguageExtensions.TryParseCode("", out _));
        Assert.False(BotLanguageExtensions.TryParseCode(null, out _));
    }
}

/// <summary>
/// 资源文件完整性测试：en/zh 键集合必须一致（防止新增键漏翻译）。
/// </summary>
public class ResourceCatalogTests
{
    [Fact]
    public void EnAndZhCatalogs_HaveIdenticalKeys()
    {
        var en = TestI18n.Instance.CatalogFor("en")!;
        var zh = TestI18n.Instance.CatalogFor("zh")!;
        Assert.Equal(en.Keys.OrderBy(k => k, StringComparer.Ordinal), zh.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void UserTexts_AllKeys_ExistInEnCatalog()
    {
        var en = TestI18n.Instance.CatalogFor("en")!;
        foreach (var prop in typeof(UserTexts).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (prop.IsLiteral && prop.FieldType == typeof(string))
            {
                var key = (string)prop.GetValue(null)!;
                Assert.True(en.ContainsKey(key), $"en.json 缺少键: {key}");
            }
        }
    }
}

/// <summary>
/// <see cref="UserLanguageStore"/> 单元测试（持久化、原子写）。
/// </summary>
public class UserLanguageStoreTests : IDisposable
{
    private readonly string _dir;

    public UserLanguageStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tgdl-uls-" + Guid.NewGuid().ToString("N")[..8]);
    }

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void SetAndGet_RoundTrips()
    {
        var store = new UserLanguageStore(_dir);
        Assert.False(store.Has(1000));
        store.Set(1000, "zh");
        Assert.True(store.Has(1000));
        Assert.Equal("zh", store.Get(1000));
    }

    [Fact]
    public void Set_InvalidLanguage_Throws()
    {
        var store = new UserLanguageStore(_dir);
        Assert.Throws<ArgumentException>(() => store.Set(1000, "not-a-lang"));
    }

    [Fact]
    public void Persisted_AcrossInstances()
    {
        var store = new UserLanguageStore(_dir);
        store.Set(1000, "zh");

        var reloaded = new UserLanguageStore(_dir);
        reloaded.Load();
        Assert.Equal("zh", reloaded.Get(1000));
    }

    [Fact]
    public void Load_MissingFile_NoThrow()
    {
        var store = new UserLanguageStore(_dir);
        store.Load();
        Assert.False(store.Has(1));
    }

    [Fact]
    public void Load_CorruptedFile_NoThrow()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "languages.json"), "not json");
        var store = new UserLanguageStore(_dir);
        store.Load();
        Assert.False(store.Has(1));
    }

    [Fact]
    public void Set_ManyUsers_ConcurrentSafe()
    {
        var store = new UserLanguageStore(_dir);
        Parallel.For(0, 50, i => store.Set(1000 + i, i % 2 == 0 ? "zh" : "en"));

        var reloaded = new UserLanguageStore(_dir);
        reloaded.Load();
        Assert.Equal("zh", reloaded.Get(1000));
        Assert.Equal("en", reloaded.Get(1001));
    }

    [Fact]
    public void Persisted_File_Has0600Mode()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Unix 权限仅 Linux/macOS 有意义
        }

        var store = new UserLanguageStore(_dir);
        store.Set(1000, "zh");

        var expected = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        Assert.Equal(expected, File.GetUnixFileMode(Path.Combine(_dir, UserLanguageStore.FileName)) & ~UnixFileMode.OtherExecute);
    }
}

/// <summary>
/// <see cref="UserLanguageResolver"/> 解析链单元测试。
/// </summary>
public class UserLanguageResolverTests : IDisposable
{
    private readonly string _dir;

    public UserLanguageResolverTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tgdl-ulr-" + Guid.NewGuid().ToString("N")[..8]);
    }

    public void Dispose() => Directory.Delete(_dir, true);

    private static InboundMessage Msg(string? languageCode, bool isPrivate, long? senderId) => new()
    {
        ChatId = isPrivate ? senderId ?? 1 : -100123,
        IsPrivate = isPrivate,
        SenderUserId = senderId,
        LanguageCode = languageCode,
    };

    [Fact]
    public void ExplicitChoice_Wins()
    {
        var store = new UserLanguageStore(_dir);
        store.Set(1000, "zh");
        var resolver = new UserLanguageResolver(store, m => m.Language);

        var msg = Msg(null, true, 1000);
        msg = msg.WithLanguage("en");
        Assert.Equal("zh", resolver.Resolve(msg));
    }

    [Fact]
    public void LanguageCode_UsedWhenNoExplicitChoice()
    {
        var store = new UserLanguageStore(_dir);
        var resolver = new UserLanguageResolver(store, m => m.LanguageCode);
        Assert.Equal("zh", resolver.Resolve(Msg("zh-CN", true, 1000)));
        Assert.Equal("en", resolver.Resolve(Msg("en-US", true, 1000)));
        // 非 zh/en 前缀不映射 → 走全局/fallback
        Assert.Equal("en", resolver.Resolve(Msg("ja", true, 1000)));
    }

    [Fact]
    public void GlobalDefault_AppliedWhenNoSignals()
    {
        var store = new UserLanguageStore(_dir);
        var resolver = new UserLanguageResolver(store, m => m.LanguageCode, globalDefault: "zh");
        Assert.Equal("zh", resolver.Resolve(Msg(null, true, 1000)));
        Assert.Equal("zh", resolver.Resolve(Msg(null, false, null)));
    }

    [Fact]
    public void FallbackEn_WhenNothingElse()
    {
        var store = new UserLanguageStore(_dir);
        var resolver = new UserLanguageResolver(store, m => m.Language);
        Assert.Equal("en", resolver.Resolve(Msg(null, true, 1000)));
        Assert.Equal("en", resolver.Resolve(Msg(null, false, null)));
        Assert.Equal("en", resolver.Resolve(Msg("", true, 1000)));
    }

    [Fact]
    public void Groups_SkipStore()
    {
        var store = new UserLanguageStore(_dir);
        store.Set(999, "zh");
        var resolver = new UserLanguageResolver(store, m => m.Language);

        // 群组消息即使 sender 有显式选择也不读 store（按 language_code / 全局 / fallback）
        var msg = Msg(null, false, 999);
        Assert.Equal("en", resolver.Resolve(msg));
    }
}
