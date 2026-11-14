using SnowmeetApi.Helpers;

namespace SnowmeetApi.Tests;

/// <summary>
/// 顾客昵称、头像（member.nick_name / avatar / nick_avatar_update_date）的纯规则：
/// 要不要弹「完善资料」、昵称清洗、头像文件类型识别。控制器和数据库不在这里测。
/// </summary>
public sealed class MemberProfileRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 15, 10, 0, 0);

    // ───────────── 要不要弹 ─────────────

    [Fact]
    public void NeedPrompt_IsTrueUntilTheProfileHasBeenSavedOnce()
    {
        Assert.True(MemberProfileRules.NeedPrompt(null, Now));
        Assert.False(MemberProfileRules.NeedPrompt(Now.AddMinutes(-1), Now));
    }

    [Fact]
    public void NeedPrompt_DoesNotAskAgainForOldProfilesYet()
    {
        // 2026-11-14 用户：更新时间将来超过一定时间要重新获取，「但目前不必做」。
        // 将来要做，只改 NeedPrompt 这一处；在那之前，再老的资料也不重新问。
        Assert.False(MemberProfileRules.NeedPrompt(Now.AddYears(-3), Now));
    }

    // ───────────── 昵称 ─────────────

    [Theory]
    [InlineData("小明", "小明")]
    [InlineData("  小明  ", "小明")]
    [InlineData("\t小明\n", "小明")]
    [InlineData("a\u0000b\u0007c", "abc")]          // 控制字符直接去掉
    [InlineData("小 明", "小 明")]                    // 中间的空格保留
    [InlineData("🙂雪友🙂", "🙂雪友🙂")]               // 表情不被拆坏
    public void CleanNickName_TrimsAndStripsControlCharacters(string raw, string expected)
    {
        Assert.Equal(expected, MemberProfileRules.CleanNickName(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u0000\u0007")]
    public void CleanNickName_ReturnsNullWhenNothingIsLeft(string? raw)
    {
        Assert.Null(MemberProfileRules.CleanNickName(raw));
    }

    [Fact]
    public void CleanNickName_CutsAtThirtyTwoCharacters()
    {
        string cut = MemberProfileRules.CleanNickName(new string('甲', 40))!;
        Assert.Equal(MemberProfileRules.NickNameMaxLength, new System.Globalization.StringInfo(cut).LengthInTextElements);
        Assert.Equal(32, MemberProfileRules.NickNameMaxLength);
    }

    [Fact]
    public void CleanNickName_NeverCutsAnEmojiInHalf()
    {
        // 32 个字符的位置正好落在一个代理对（表情）中间时，整个表情都不要，不留半个
        string raw = new string('甲', 31) + "🙂🙂";
        string cut = MemberProfileRules.CleanNickName(raw)!;
        Assert.DoesNotContain('�', cut);
        Assert.False(char.IsHighSurrogate(cut[^1]), "不能以半个表情结尾");
        Assert.True(cut.StartsWith(new string('甲', 31)));
    }

    // ───────────── 头像文件类型（按文件头认，不信文件名） ─────────────

    [Fact]
    public void DetectImageExtension_RecognisesTheFourSupportedFormats()
    {
        Assert.Equal(".jpg", MemberProfileRules.DetectImageExtension(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1 }));
        Assert.Equal(".png", MemberProfileRules.DetectImageExtension(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 }));
        Assert.Equal(".gif", MemberProfileRules.DetectImageExtension("GIF89a\0\0\0\0\0\0"u8.ToArray()));
        Assert.Equal(".gif", MemberProfileRules.DetectImageExtension("GIF87a\0\0\0\0\0\0"u8.ToArray()));
        Assert.Equal(".webp", MemberProfileRules.DetectImageExtension("RIFF\x10\0\0\0WEBPVP8 "u8.ToArray()));
    }

    [Fact]
    public void DetectImageExtension_RejectsEverythingElse()
    {
        Assert.Null(MemberProfileRules.DetectImageExtension(Array.Empty<byte>()));
        Assert.Null(MemberProfileRules.DetectImageExtension(new byte[] { 0xFF, 0xD8 }));                    // 太短
        Assert.Null(MemberProfileRules.DetectImageExtension("<html><script>alert(1)</script>"u8.ToArray())); // 网页
        Assert.Null(MemberProfileRules.DetectImageExtension("MZ\x90\0\x03\0\0\0\x04\0\0\0\xFF\xFF"u8.ToArray())); // 可执行文件
        Assert.Null(MemberProfileRules.DetectImageExtension("RIFF\x10\0\0\0WAVEfmt "u8.ToArray()));            // RIFF 但不是 WEBP
        Assert.Null(MemberProfileRules.DetectImageExtension("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray())); // SVG 能带脚本，不收
    }

    [Fact]
    public void AvatarMaxBytes_IsFiveMegabytes()
    {
        Assert.Equal(5L * 1024 * 1024, MemberProfileRules.AvatarMaxBytes);
    }
}
