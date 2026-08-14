using TGBot.Access;
using TGBot.Texts;
using Xunit;

namespace TGBot.Tests;

/// <summary>
/// <see cref="AccessControlService"/> 单元测试。
/// </summary>
public class AccessControlTests
{
    private static readonly long[] AllowedUsers = { 100, 200 };
    private static readonly long[] TargetChats = { -100123, -100456 };

    private static AccessControlService Create()
        => new(AllowedUsers, TargetChats);

    [Fact]
    public void Evaluate_PrivateWhitelistedUser_Allowed()
    {
        Assert.True(Create().Evaluate(TriggerArea.Private, 100, 999).Allowed);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(0)]
    [InlineData(-5)]
    public void Evaluate_PrivateNonWhitelistedUser_Denied(long userId)
    {
        var decision = Create().Evaluate(TriggerArea.Private, userId, 999);
        Assert.False(decision.Allowed);
        Assert.Contains("名单", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_PrivateNullUser_Denied()
    {
        Assert.False(Create().Evaluate(TriggerArea.Private, null, 999).Allowed);
    }

    [Fact]
    public void Evaluate_GroupWhitelistedChat_Allowed()
    {
        Assert.True(Create().Evaluate(TriggerArea.GroupOrChannel, null, -100123).Allowed);
        Assert.True(Create().Evaluate(TriggerArea.GroupOrChannel, 999, -100456).Allowed);
    }

    [Theory]
    [InlineData(-100999)]
    [InlineData(12345)]
    [InlineData(0)]
    public void Evaluate_GroupNonWhitelistedChat_Denied(long chatId)
    {
        var decision = Create().Evaluate(TriggerArea.GroupOrChannel, null, chatId);
        Assert.False(decision.Allowed);
        Assert.Contains("未获得授权", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_GroupChatAllowedEvenIfUserNotWhitelisted()
    {
        var decision = Create().Evaluate(TriggerArea.GroupOrChannel, 999, -100123);
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void DenyReason_ContainsNoInternalDetails()
    {
        var d = Create().Evaluate(TriggerArea.Private, 999, -100999);
        Assert.False(d.Reason!.Contains("AllowedUserIds", StringComparison.Ordinal));
        Assert.False(d.Reason!.Contains("config", StringComparison.OrdinalIgnoreCase));
    }
}
