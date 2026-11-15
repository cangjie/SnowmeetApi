using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Storage;

namespace SnowmeetApi.Tests;

/// <summary>
/// 顾客验证手机号之后授权头像和昵称（2026-11-14 用户要求）：
///   GET  api/MemberProfile/GetMyProfile   要不要弹「完善资料」、已有的资料
///   POST api/MemberProfile/SaveMyProfile  保存昵称（可选）和头像文件（可选，存 S3），写 member 三列并刷新更新时间
/// 用 CoachFixture 的会话：session-1…7 绑了会员 1…7，session-new 没绑会员（还没验证手机号）。
/// </summary>
public sealed class MemberProfileControllerTests
{
    private static readonly byte[] Jpeg = Pad(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1 });
    private static readonly byte[] Png = Pad(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

    private static byte[] Pad(byte[] head) { var b = new byte[head.Length + 64]; head.CopyTo(b, 0); return b; }

    private static IFormFile File(byte[] bytes, string name = "avatar.jpg") =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "avatar", name);

    private static MemberProfileController Controller(CoachFixture f, IFileStorage? storage = null) =>
        new(f.NewContext(), storage ?? new MiniTicketQrTests.Storage());

    /// <summary>记下每次存文件时传的文件名、是否公开、前缀；路径带上扩展名，方便断言按文件内容认出的类型。</summary>
    private sealed class RecordingStorage : IFileStorage
    {
        public List<(string name, bool isWeb, string prefix)> Calls = new();
        public Task<string> SaveAsync(Stream content, string originalFileName, bool isWeb = true, string namePrefix = "")
        {
            Calls.Add((originalFileName, isWeb, namePrefix));
            return Task.FromResult("/upload/20261115/" + namePrefix + "x" + Path.GetExtension(originalFileName));
        }
        public Task<byte[]?> ReadAsync(string relativePath, bool isWeb = true) => Task.FromResult<byte[]?>(null);
        public string PublicUrl(string relativePath) => "https://images.test" + relativePath;
    }

    private static ApiResult<MemberProfileView> Unwrap(ActionResult<ApiResult<MemberProfileView>> r)
    {
        var ok = Assert.IsType<OkObjectResult>(r.Result);
        return Assert.IsType<ApiResult<MemberProfileView>>(ok.Value);
    }

    // 三列全是空时 EF 不会物化这个共表的可选实体（和养护首单列一样），读不到就当作「什么都没存」
    private static async Task<MemberProfile> Stored(CoachFixture f, int memberId) =>
        await f.NewContext().memberProfile.AsNoTracking().FirstOrDefaultAsync(p => p.member_id == memberId)
        ?? new MemberProfile { member_id = memberId };

    // ───────────── GetMyProfile ─────────────

    [Fact]
    public async Task GetMyProfile_AskWhenTheMemberHasNoProfileYet()
    {
        await using var f = await CoachFixture.Create();
        var r = Unwrap(await Controller(f).GetMyProfile("session-3"));
        Assert.Equal(0, r.code);
        Assert.True(r.data!.needProfile);
        Assert.Null(r.data.nickName);
        Assert.Null(r.data.avatarUrl);
    }

    [Fact]
    public async Task GetMyProfile_DoNotAskWithoutAMember()
    {
        await using var f = await CoachFixture.Create();
        // 会话还没绑会员（没验证手机号）、会话不存在、会话密钥为空：都不弹，也不报错
        foreach (string key in new[] { "session-new", "no-such-session", "" })
        {
            var r = Unwrap(await Controller(f).GetMyProfile(key));
            Assert.Equal(0, r.code);
            Assert.False(r.data!.needProfile, key);
        }
    }

    [Fact]
    public async Task GetMyProfile_DoNotAskForAnExpiredSession()
    {
        await using var f = await CoachFixture.Create();
        await using (var db = f.NewContext())
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE mini_session SET expire_date = {0} WHERE session_key = 'session-3'", DateTime.Now.AddHours(-1));
        }
        var r = Unwrap(await Controller(f).GetMyProfile("session-3"));
        Assert.False(r.data!.needProfile);
    }

    [Fact]
    public async Task GetMyProfile_ReturnsTheSavedProfileAndStopsAsking()
    {
        await using var f = await CoachFixture.Create();
        var storage = new MiniTicketQrTests.Storage();
        await Controller(f, storage).SaveMyProfile("session-3", nickName: "雪友小明", avatar: File(Jpeg));

        var r = Unwrap(await Controller(f, storage).GetMyProfile("session-3"));
        Assert.False(r.data!.needProfile);
        Assert.Equal("雪友小明", r.data.nickName);
        Assert.Equal("https://images.test/upload/1.png", r.data.avatarUrl);   // 假存储固定返回 /upload/N.png
        Assert.NotNull(r.data.updateDate);
    }

    [Fact]
    public async Task GetMyProfile_AProfileMissingTheNickNameOrTheAvatarIsAskedAgain_ButWhatIsSavedComesBack()
    {
        await using var f = await CoachFixture.Create();
        var storage = new MiniTicketQrTests.Storage();
        await Controller(f, storage).SaveMyProfile("session-3", nickName: null, avatar: File(Jpeg));            // 只存了头像
        await Controller(f, storage).SaveMyProfile("session-4", nickName: "只有昵称", avatar: null);            // 只存了昵称

        var avatarOnly = Unwrap(await Controller(f, storage).GetMyProfile("session-3")).data!;
        Assert.True(avatarOnly.needProfile, "昵称还没填，要再问");
        Assert.Null(avatarOnly.nickName);
        Assert.NotNull(avatarOnly.avatarUrl);
        Assert.NotNull(avatarOnly.updateDate);

        var nickOnly = Unwrap(await Controller(f, storage).GetMyProfile("session-4")).data!;
        Assert.True(nickOnly.needProfile, "头像还没选，要再问");
        Assert.Equal("只有昵称", nickOnly.nickName);
        Assert.Null(nickOnly.avatarUrl);

        // 补齐之后就不再问
        await Controller(f, storage).SaveMyProfile("session-3", nickName: "补上的昵称", avatar: null);
        var completed = Unwrap(await Controller(f, storage).GetMyProfile("session-3")).data!;
        Assert.False(completed.needProfile);
        Assert.Equal("补上的昵称", completed.nickName);
        Assert.NotNull(completed.avatarUrl);
    }

    // ───────────── SaveMyProfile：成功 ─────────────

    [Fact]
    public async Task Save_NickNameAndAvatar_WritesThreeColumnsAndStoresTheFileInS3()
    {
        await using var f = await CoachFixture.Create();
        var storage = new MiniTicketQrTests.Storage();
        DateTime before = DateTime.Now.AddSeconds(-2);

        var r = Unwrap(await Controller(f, storage).SaveMyProfile("session-3", nickName: "  雪友小明 ", avatar: File(Jpeg)));

        Assert.Equal(0, r.code);
        Assert.Equal("雪友小明", r.data!.nickName);
        Assert.Equal(1, storage.Saves);
        var p = await Stored(f, CoachFixture.StudentA);
        Assert.Equal("雪友小明", p.nick_name);
        Assert.StartsWith("/upload/", p.avatar);                 // 库里存站内相对路径，不是完整网址
        Assert.True(p.nick_avatar_update_date >= before && p.nick_avatar_update_date <= DateTime.Now.AddSeconds(2));
        Assert.True(storage.Files.ContainsKey(p.avatar!));        // 文件真的写进了存储
    }

    [Fact]
    public async Task Save_StoresAPublicFileNamedByWhatTheBytesAre_NotByTheUploadedName()
    {
        await using var f = await CoachFixture.Create();
        var storage = new RecordingStorage();

        // 文件头是 JPEG，文件名却叫 .png / 没有扩展名：按内容存成 .jpg，公开（isWeb）、带 avatar_ 前缀
        await Controller(f, storage).SaveMyProfile("session-3", nickName: null, avatar: File(Jpeg, "wrong.png"));
        await Controller(f, storage).SaveMyProfile("session-4", nickName: null, avatar: File(Jpeg, "tmp_file"));

        Assert.Equal(2, storage.Calls.Count);
        Assert.All(storage.Calls, c => { Assert.Equal("avatar.jpg", c.name); Assert.True(c.isWeb); Assert.Equal("avatar_", c.prefix); });
        Assert.Equal("/upload/20261115/avatar_x.jpg", (await Stored(f, CoachFixture.StudentA)).avatar);
    }

    [Fact]
    public async Task Save_NickNameOnly_LeavesTheAvatarAlone()
    {
        await using var f = await CoachFixture.Create();
        var storage = new MiniTicketQrTests.Storage();
        await Controller(f, storage).SaveMyProfile("session-3", nickName: "旧昵称", avatar: File(Png, "a.png"));
        string avatarBefore = (await Stored(f, CoachFixture.StudentA)).avatar!;
        await Task.Delay(30);

        var r = Unwrap(await Controller(f, storage).SaveMyProfile("session-3", nickName: "新昵称", avatar: null));

        Assert.Equal(0, r.code);
        var p = await Stored(f, CoachFixture.StudentA);
        Assert.Equal("新昵称", p.nick_name);
        Assert.Equal(avatarBefore, p.avatar);
        Assert.Equal(1, storage.Saves);                           // 没有再传文件
    }

    [Fact]
    public async Task Save_AvatarOnly_LeavesTheNickNameAlone()
    {
        await using var f = await CoachFixture.Create();
        var storage = new MiniTicketQrTests.Storage();
        await Controller(f, storage).SaveMyProfile("session-3", nickName: "原昵称", avatar: null);

        var r = Unwrap(await Controller(f, storage).SaveMyProfile("session-3", nickName: null, avatar: File(Png, "a.png")));

        Assert.Equal(0, r.code);
        var p = await Stored(f, CoachFixture.StudentA);
        Assert.Equal("原昵称", p.nick_name);
        Assert.EndsWith(".png", p.avatar);
    }

    [Fact]
    public async Task Save_RefreshesTheUpdateDateEveryTime()
    {
        await using var f = await CoachFixture.Create();
        await Controller(f).SaveMyProfile("session-3", nickName: "甲", avatar: null);
        DateTime first = (await Stored(f, CoachFixture.StudentA)).nick_avatar_update_date!.Value;
        await Task.Delay(1100);
        await Controller(f).SaveMyProfile("session-3", nickName: "乙", avatar: null);
        DateTime second = (await Stored(f, CoachFixture.StudentA)).nick_avatar_update_date!.Value;
        Assert.True(second > first);
    }

    [Fact]
    public async Task Save_OnlyTouchesTheSessionsOwnMember()
    {
        await using var f = await CoachFixture.Create();
        await Controller(f).SaveMyProfile("session-3", nickName: "学员甲", avatar: null);

        var other = await Stored(f, CoachFixture.StudentB);
        Assert.Null(other.nick_name);
        Assert.Null(other.avatar);
        Assert.Null(other.nick_avatar_update_date);
    }

    [Fact]
    public async Task Save_AWholeRowMemberSaveDoesNotWipeTheProfile()
    {
        // 代码里有好几处整行保存 Member（Entry(member).State = Modified）；
        // 昵称头像三列不在 Member 实体里，这样的保存碰不到它们（和养护首单列同一做法）。
        await using var f = await CoachFixture.Create();
        await Controller(f).SaveMyProfile("session-3", nickName: "雪友小明", avatar: File(Jpeg));

        await using (var db = f.NewContext())
        {
            Member m = await db.member.AsNoTracking().FirstAsync(x => x.id == CoachFixture.StudentA);
            m.real_name = "改过名字";
            db.Entry(m).State = EntityState.Modified;
            await db.SaveChangesAsync();
        }

        var p = await Stored(f, CoachFixture.StudentA);
        Assert.Equal("雪友小明", p.nick_name);
        Assert.NotNull(p.avatar);
        Assert.NotNull(p.nick_avatar_update_date);
    }

    [Fact]
    public async Task Save_CleansTheNickNameAndCutsItAtThirtyTwo()
    {
        await using var f = await CoachFixture.Create();
        await Controller(f).SaveMyProfile("session-3", nickName: "\t" + new string('甲', 40) + "\n", avatar: null);
        Assert.Equal(new string('甲', 32), (await Stored(f, CoachFixture.StudentA)).nick_name);
    }

    // ───────────── SaveMyProfile：拒绝 ─────────────

    [Fact]
    public async Task Save_WithoutAMember_AsksForPhoneVerificationAndWritesNothing()
    {
        await using var f = await CoachFixture.Create();
        var storage = new MiniTicketQrTests.Storage();
        var r = Unwrap(await Controller(f, storage).SaveMyProfile("session-new", nickName: "谁", avatar: File(Jpeg)));
        Assert.Equal(1, r.code);
        Assert.Contains("手机号", r.message);
        Assert.Equal(0, storage.Saves);
        Assert.Equal(0, await f.NewContext().memberProfile.CountAsync(p => p.nick_avatar_update_date != null));
    }

    [Fact]
    public async Task Save_WithAnInvalidSession_IsRefused()
    {
        await using var f = await CoachFixture.Create();
        var storage = new MiniTicketQrTests.Storage();
        var r = Unwrap(await Controller(f, storage).SaveMyProfile("no-such-session", nickName: "谁", avatar: File(Jpeg)));
        Assert.Equal(1, r.code);
        Assert.Equal(0, storage.Saves);
    }

    [Fact]
    public async Task Save_NeedsAtLeastOneOfNickNameOrAvatar()
    {
        await using var f = await CoachFixture.Create();
        foreach (string? nick in new string?[] { null, "", "   " })
        {
            var r = Unwrap(await Controller(f).SaveMyProfile("session-3", nickName: nick, avatar: null));
            Assert.Equal(1, r.code);
        }
        Assert.Null((await Stored(f, CoachFixture.StudentA)).nick_avatar_update_date);
    }

    [Fact]
    public async Task Save_RejectsAFileThatIsNotAnImage_EvenWithAnImageName()
    {
        await using var f = await CoachFixture.Create();
        var storage = new MiniTicketQrTests.Storage();
        byte[] html = System.Text.Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>");

        var r = Unwrap(await Controller(f, storage).SaveMyProfile("session-3", nickName: "有昵称", avatar: File(html, "avatar.png")));

        Assert.Equal(1, r.code);
        Assert.Equal(0, storage.Saves);
        var p = await Stored(f, CoachFixture.StudentA);
        Assert.Null(p.nick_name);                                  // 头像不行，整次保存都不生效，免得半截成功
        Assert.Null(p.nick_avatar_update_date);
    }

    [Fact]
    public async Task Save_RejectsAFileOverFiveMegabytes()
    {
        await using var f = await CoachFixture.Create();
        var storage = new MiniTicketQrTests.Storage();
        var big = new byte[MemberProfileRules.AvatarMaxBytes + 1];
        Jpeg.CopyTo(big, 0);

        var r = Unwrap(await Controller(f, storage).SaveMyProfile("session-3", nickName: null, avatar: File(big)));

        Assert.Equal(1, r.code);
        Assert.Equal(0, storage.Saves);
    }

    [Fact]
    public async Task Save_WhenTheStorageFails_NothingIsWritten()
    {
        await using var f = await CoachFixture.Create();
        var storage = new MiniTicketQrTests.Storage { Fail = true };

        var r = Unwrap(await Controller(f, storage).SaveMyProfile("session-3", nickName: "有昵称", avatar: File(Jpeg)));

        Assert.Equal(1, r.code);
        var p = await Stored(f, CoachFixture.StudentA);
        Assert.Null(p.nick_name);
        Assert.Null(p.avatar);
        Assert.Null(p.nick_avatar_update_date);
    }
}
