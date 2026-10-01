using System;
using SnowmeetApi.Models;
using SnowmeetApi.Services.StaffAccounts;

namespace SnowmeetApi.Tests;

public class StaffAccountRulesTests
{
    [Fact]
    public void 只有在职且职级不低于300的才是系统管理员()
    {
        Assert.True(StaffAccountRules.IsAdmin(new Staff { valid = 1, title_level = 300 }));
        Assert.True(StaffAccountRules.IsAdmin(new Staff { valid = 1, title_level = 1000 }));
        Assert.False(StaffAccountRules.IsAdmin(new Staff { valid = 1, title_level = 200 }));
        Assert.False(StaffAccountRules.IsAdmin(new Staff { valid = 0, title_level = 300 }));
        Assert.False(StaffAccountRules.IsAdmin(null));
    }

    [Fact]
    public void 只能管理职级不高于自己的账号_只能设四档职级()
    {
        var admin = new Staff { title_level = 300 };
        Assert.True(StaffAccountRules.CanManage(admin, new Staff { title_level = 300 }));
        Assert.False(StaffAccountRules.CanManage(admin, new Staff { title_level = 1000 }));
        Assert.True(StaffAccountRules.IsTitleAllowed(50));
        Assert.False(StaffAccountRules.IsTitleAllowed(0));
        Assert.False(StaffAccountRules.IsTitleAllowed(1000));
    }

    [Fact]
    public void 雪季备注与CreateStaff一致_6月起算下一季()
    {
        Assert.Equal("26-27雪季", StaffAccountRules.SeasonMemo(new DateTime(2026, 9, 30)));
        Assert.Equal("25-26雪季", StaffAccountRules.SeasonMemo(new DateTime(2026, 5, 31)));
        Assert.Equal("26-27雪季", StaffAccountRules.SeasonMemo(new DateTime(2026, 6, 1)));
    }

    [Fact]
    public void 绑定码状态_已用优先于作废和过期()
    {
        var now = new DateTime(2026, 10, 1, 10, 0, 0);
        Assert.Equal("ok", StaffAccountRules.CodeStatus(new StaffBindCode { expire_date = now.AddMinutes(1) }, now));
        Assert.Equal("expired", StaffAccountRules.CodeStatus(new StaffBindCode { expire_date = now }, now));
        Assert.Equal("cancelled", StaffAccountRules.CodeStatus(new StaffBindCode { expire_date = now.AddMinutes(1), cancel_date = now }, now));
        Assert.Equal("used", StaffAccountRules.CodeStatus(new StaffBindCode { expire_date = now.AddMinutes(-1), used_date = now }, now));
    }

    [Fact]
    public void 结束关联_早于今天记那天否则记当前时间_并置valid为0()
    {
        var now = new DateTime(2026, 10, 1, 10, 0, 0);
        Assert.Equal(new DateTime(2026, 9, 28), StaffAccountRules.EndTime(new DateTime(2026, 9, 28), now));
        Assert.Equal(now, StaffAccountRules.EndTime(new DateTime(2026, 10, 1), now));
        Assert.Equal(now, StaffAccountRules.EndTime(null, now));
        var link = new StaffSocialAccount { valid = 1 };
        StaffAccountRules.EndLink(link, now, now);
        Assert.Equal(0, link.valid);
        Assert.False(StaffAccountRules.IsActiveLink(link, now));
    }

    [Fact]
    public void 手机号与性别校验()
    {
        Assert.True(StaffAccountRules.IsValidCell("13900001234"));
        Assert.True(StaffAccountRules.IsValidCell(" 13900001234 "));
        Assert.False(StaffAccountRules.IsValidCell("1390000123"));
        Assert.False(StaffAccountRules.IsValidCell("23900001234"));
        Assert.True(StaffAccountRules.IsValidGender("女"));
        Assert.False(StaffAccountRules.IsValidGender(""));
    }
}
