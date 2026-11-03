using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers.IntroducerShare;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Share;

namespace SnowmeetApi.Tests;

// 订单上选收款人：按 教练 / KOL / 机构 筛选，也能用关键词（名称、真实姓名）或手机号搜。
public class IntroducerChoiceSearchTests
{
    private sealed class NoBinder : IAlipayRoyaltyBinder
    {
        public Task<(bool ok, string message)> BindAsync(string accountType, string account, string? realName) => Task.FromResult((true, ""));
    }

    private static JsonElement D(ApiResult<object> r) => JsonSerializer.SerializeToElement(r.data);

    // 三位介绍人：李教练（教练，13691457006）、小王（KOL，13800001111）、雪山公司（机构，无手机号）；真实姓名都是 示例+会员号
    private static async Task<ApplicationDBContext> SeedAsync()
    {
        var db = await ShareFixture.NewDbAsync();
        async Task Add(int memberId, string type, string name, string? cell)
        {
            await ShareFixture.AddIntroducerAsync(db, memberId, type);
            var row = await db.introducer.AsNoTracking().SingleAsync(i => i.member_id == memberId);
            row.name = name;
            db.introducer.Update(row);
            if (cell != null)
            {
                db.memberSocialAccount.Add(new MemberSocialAccount { member_id = memberId, type = MemberSocialAccount.TYPE_CELL, num = cell, valid = 1 });
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
        await Add(100, "教练", "李教练", "13691457006");
        await Add(101, "KOL", "小王", "13800001111");
        await Add(102, "机构", "雪山公司", null);
        return db;
    }

    private static IntroducerService Svc(ApplicationDBContext db) => new(db, new NoBinder());

    private static string[] Names(IEnumerable<IntroducerService.ChoiceView> choices) => choices.Select(c => c.name).OrderBy(n => n).ToArray();

    [Fact]
    public async Task Without_a_filter_every_valid_introducer_is_listed_with_the_last_four_digits_of_the_phone()
    {
        await using var db = await SeedAsync();

        var all = await Svc(db).ChoicesAsync(null, null);

        Assert.Equal(new[] { "小王", "李教练", "雪山公司" }.OrderBy(n => n).ToArray(), Names(all));
        Assert.Equal("7006", all.Single(c => c.name == "李教练").cellTail);
        Assert.Equal("1111", all.Single(c => c.name == "小王").cellTail);
        Assert.Equal("", all.Single(c => c.name == "雪山公司").cellTail);
    }

    [Theory]
    [InlineData("教练", "李教练")]
    [InlineData("KOL", "小王")]
    [InlineData("机构", "雪山公司")]
    public async Task The_type_filter_keeps_only_that_type(string type, string expected)
    {
        await using var db = await SeedAsync();

        Assert.Equal(new[] { expected }, Names(await Svc(db).ChoicesAsync(type, null)));
    }

    [Theory]
    [InlineData("李", "李教练")]                 // 名称
    [InlineData("示例101", "小王")]              // 真实姓名
    [InlineData("13691457006", "李教练")]        // 完整手机号
    [InlineData("1457", "李教练")]               // 手机号的一段
    [InlineData("  公司 ", "雪山公司")]          // 前后空格不影响
    public async Task The_keyword_matches_name_real_name_or_phone(string keyword, string expected)
    {
        await using var db = await SeedAsync();

        Assert.Equal(new[] { expected }, Names(await Svc(db).ChoicesAsync(null, keyword)));
    }

    [Fact]
    public async Task Type_and_keyword_combine_and_a_miss_is_an_empty_list()
    {
        await using var db = await SeedAsync();

        Assert.Equal(new[] { "李教练" }, Names(await Svc(db).ChoicesAsync("教练", "李")));
        Assert.Empty(await Svc(db).ChoicesAsync("KOL", "李"));
        Assert.Empty(await Svc(db).ChoicesAsync(null, "没有这个人"));
    }

    [Fact]
    public async Task Stopped_introducers_are_never_offered_and_an_unknown_type_is_refused()
    {
        await using var db = await SeedAsync();
        var row = await db.introducer.AsNoTracking().SingleAsync(i => i.name == "小王");
        row.valid = 0;
        db.introducer.Update(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.DoesNotContain("小王", Names(await Svc(db).ChoicesAsync(null, null)));
        Assert.Empty(await Svc(db).ChoicesAsync("KOL", null));
        var bad = await Assert.ThrowsAsync<ShareException>(() => Svc(db).ChoicesAsync("分销", null));
        Assert.Equal("介绍人类型只能是教练、KOL、机构", bad.Message);
    }

    [Fact]
    public async Task The_choices_api_takes_the_filter_and_old_clients_without_it_still_get_everyone()
    {
        await using var db = await SeedAsync();
        await ShareFixture.AddStaffSessionAsync(db, 302, 200, "manager-s");
        await ShareFixture.AddStaffSessionAsync(db, 303, 100, "clerk-s");
        var ctl = () => new IntroducerController(db, ShareFixture.Config, ShareFixture.Http);

        var old = D(await ctl().ListChoices("manager-s")).GetProperty("choices");
        Assert.Equal(3, old.GetArrayLength());

        var filtered = D(await ctl().ListChoices("manager-s", "机构", null)).GetProperty("choices");
        Assert.Equal(1, filtered.GetArrayLength());
        Assert.Equal("雪山公司", filtered[0].GetProperty("name").GetString());
        Assert.Equal("", filtered[0].GetProperty("cellTail").GetString());

        var byPhone = D(await ctl().ListChoices("manager-s", null, "7006")).GetProperty("choices");
        Assert.Equal("李教练", byPhone[0].GetProperty("name").GetString());
        Assert.Equal("7006", byPhone[0].GetProperty("cellTail").GetString());

        var badType = await ctl().ListChoices("manager-s", "分销", null);
        Assert.Equal(1, badType.code);
        Assert.Equal(3, (await ctl().ListChoices("clerk-s", "教练", null)).code);   // 店员仍然没有权限
    }
}
