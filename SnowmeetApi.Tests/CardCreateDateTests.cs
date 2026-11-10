using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// 生产库 card.create_date 是 NOT NULL 且没有默认值（2025-07-09 迁移加的），而 Card 模型曾经没有映射这一列：
/// EF 生成的 INSERT 里不带它，所有往 card 表插券号的入口（会员详情页发券 GrantCoupon、教练批量发券等）在生产上必报
/// “Cannot insert the value NULL into column 'create_date'” → 500。测试库是 SQLite + EnsureCreated（表结构由模型生成），
/// 所以以前拦不住，这里直接断言模型映射了这一列、并且发券后 card 行带着 create_date。
/// </summary>
public class CardCreateDateTests
{
    [Fact]
    public async Task CardModel_MapsCreateDateAsRequiredColumn()
    {
        await using var f = await CoachFixture.Create();
        var prop = f.Db.Model.FindEntityType(typeof(Card))!.FindProperty("create_date");
        Assert.NotNull(prop);   // 没映射 → INSERT 不带这一列 → 生产库 NOT NULL 报错
        Assert.False(prop!.IsNullable);
        Assert.Equal(typeof(DateTime), prop.ClrType);
    }

    [Fact]
    public void NewCard_DefaultsCreateDateToNow()
    {
        var before = DateTime.Now.AddSeconds(-5);
        var card = new Card { card_no = "x" };
        var prop = typeof(Card).GetProperty("create_date");
        Assert.NotNull(prop);
        var value = (DateTime)prop!.GetValue(card)!;
        Assert.InRange(value, before, DateTime.Now.AddSeconds(5));   // 不能是 0001-01-01：datetime 列放不下，会直接溢出
    }

    [Fact]
    public async Task GrantCoupon_FromMemberDetailPage_WritesCardRowWithCreateDate()
    {
        await using var f = await CoachFixture.Create();
        var ctl = new MemberAdminController(f.NewContext(), f.Config, new HttpContextAccessor());
        var before = DateTime.Now.AddSeconds(-5);

        var result = await ctl.GrantCoupon(new MemberAdminController.CouponRequest { memberId = CoachFixture.StudentA, templateId = 16, count = 2 }, "session-7");

        var api = (ApiResult<object>)((OkObjectResult)result.Result!).Value!;
        Assert.Equal(0, api.code);
        Assert.Equal(2, await f.Db.ticket.AsNoTracking().CountAsync(t => t.member_id == CoachFixture.StudentA && t.template_id == 16 && t.channel == "系统后台"));
        var created = await f.Db.card.AsNoTracking().Where(c => c.is_ticket == 1)
            .Select(c => EF.Property<DateTime>(c, "create_date")).ToListAsync();
        Assert.Equal(2, created.Count);
        Assert.All(created, d => Assert.InRange(d, before, DateTime.Now.AddSeconds(5)));
    }
}
