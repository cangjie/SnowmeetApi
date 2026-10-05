using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using Xunit;

namespace SnowmeetApi.Tests;

public sealed class WechatPayerSessionTests
{
    [Fact]
    public async Task UnionIdMatchedMemberCanPayUsingCurrentMiniSessionOpenId()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        await using var db = new ApplicationDBContext(
            new DbContextOptionsBuilder<ApplicationDBContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.member.Add(new Member { id = 41172, real_name = "测试会员", gender = "男" });
        db.memberSocialAccount.Add(new MemberSocialAccount
        {
            member_id = 41172, type = MemberSocialAccount.TYPE_WECHAT_UNIONID,
            num = "union-1", valid = 1
        });
        db.miniSession.Add(new MiniSession
        {
            session_key = "session-1", session_type = "wechat_mini_openid", member_id = 41172,
            wechat_openid = " mini-openid-1 ", valid = 1, expire_date = DateTime.Now.AddMinutes(30)
        });
        await db.SaveChangesAsync();

        var helper = new MemberController(db, new ConfigurationBuilder().Build());
        var (member, openId) = await helper.GetWechatPayerBySessionKey("session-1");
        Assert.Equal(41172, member?.id);
        Assert.Null(member?.wechatMiniOpenId);
        Assert.Equal("mini-openid-1", openId);

        var session = await db.miniSession.FirstAsync();
        session.expire_date = DateTime.Now.AddMinutes(-1);
        await db.SaveChangesAsync();
        var (expiredMember, expiredOpenId) = await helper.GetWechatPayerBySessionKey("session-1");
        Assert.Null(expiredMember);
        Assert.Null(expiredOpenId);
    }
}
