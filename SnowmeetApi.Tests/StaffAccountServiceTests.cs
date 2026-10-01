using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Services.StaffAccounts;

namespace SnowmeetApi.Tests;

// 员工账号管理服务：内存 SQLite 跑真实 EF 模型（不连任何 SQL Server）
public sealed class StaffAccountServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ApplicationDBContext _db = null!;
    private StaffAccountService _service = null!;
    private Staff _admin = null!;

    // 演示数据：管理员 1；工作手机 11（在用，员工 2）、12（空闲）、13（未绑微信）；私人手机 21（员工 3）
    public async Task InitializeAsync()
    {
        // 工作手机刚登记时 member_id = 0，生产库没有外键；这里关掉外键检查
        _connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        await _connection.OpenAsync();
        _db = new ApplicationDBContext(new DbContextOptionsBuilder<ApplicationDBContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
        _db.shop.AddRange(new Shop { id = 4, name = "万龙店", sort = 1 }, new Shop { id = 10, name = "南山店", sort = 2 });
        _admin = new Staff { id = 1, name = "管理员", gender = "女", title_level = 300, valid = 1 };
        _db.staff.AddRange(_admin,
            new Staff { id = 2, name = "李明", gender = "男", title_level = 100, valid = 1, base_shop_id = 4 },
            new Staff { id = 3, name = "张伟", gender = "男", title_level = 100, valid = 1 },
            new Staff { id = 4, name = "老板", gender = "男", title_level = 1000, valid = 1 });
        _db.socialAccountForJob.AddRange(
            Account(11, "13900007440", 0, "o-job-11", 111),
            Account(12, "13900006240", 0, "o-job-12", 112),
            Account(13, "13900008217", 0, "", 0),
            Account(21, "13811112222", 1, "o-pri-21", 121));
        _db.staffSocialAccount.AddRange(Link(1, 2, 11), Link(2, 3, 21));
        _db.memberSocialAccount.AddRange(Msa(111, "o-job-11"), Msa(112, "o-job-12"), Msa(121, "o-pri-21"),
            Msa(500, "o-new-member"), Msa(600, "o-other-member"));
        await _db.SaveChangesAsync();
        _service = new StaffAccountService(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static SocialAccountForJob Account(int id, string cell, int isPrivate, string openid, int memberId) =>
        new() { id = id, cell = cell, is_private = isPrivate, wechat_mini_openid = openid, member_id = memberId };
    private static StaffSocialAccount Link(int id, int staffId, int accountId) =>
        new() { id = id, staff_id = staffId, social_account_id = accountId, valid = 1, start_date = DateTime.Today.AddDays(-30), season_memo = "" };
    private static MemberSocialAccount Msa(int memberId, string openid) =>
        new() { member_id = memberId, type = MemberSocialAccount.TYPE_WECHAT_MINI_OPENID, num = openid, valid = 1, memo = "" };

    private async Task<StaffDto> StaffOf(int id) => (await _service.GetStaffAsync(_admin, id)).staff;
    private async Task<PhoneDto> PhoneOf(int id) => (await _service.ListPhonesAsync()).Single(p => p.id == id);
    private async Task<int> ActiveLinks(int staffId) =>
        (await _db.staffSocialAccount.AsNoTracking().Where(l => l.staff_id == staffId).ToListAsync()).Count(l => StaffAccountRules.IsActiveLink(l, DateTime.Now));
    // 登录时 MemberLogin 就是这样认员工的
    private Task<Staff> LoginStaff(string openid) => new StaffController(_db).GetStaffBySocialNum(openid, MemberSocialAccount.TYPE_WECHAT_MINI_OPENID, DateTime.Now);

    [Fact]
    public async Task 列表_返回当前手机_微信_登录能否认出()
    {
        var list = await _service.ListStaffAsync(_admin);
        var liMing = list.Single(s => s.id == 2);
        Assert.Equal(11, liMing.binding!.account_id);
        Assert.False(liMing.binding.is_private);
        Assert.True(liMing.binding.has_wechat);
        Assert.True(liMing.binding.login_ok);
        Assert.Equal("万龙店", liMing.shop_name);
        Assert.True(liMing.manageable);
        Assert.False(list.Single(s => s.id == 4).manageable);
        Assert.Equal(new[] { "万龙店" }, (await _service.ListShopsAsync()).Select(s => s.name));
    }

    [Fact]
    public async Task 登录认不出的绑定_login_ok为false()
    {
        _db.memberSocialAccount.RemoveRange(_db.memberSocialAccount.Where(m => m.num == "o-pri-21"));
        await _db.SaveChangesAsync();
        Assert.False((await StaffOf(3)).binding!.login_ok);
        Assert.Null(await LoginStaff("o-pri-21"));
    }

    [Fact]
    public async Task 入职分配工作手机_只能选空闲且已绑微信的()
    {
        var r = await _service.OnboardAsync(_admin, new OnboardInput("新人", "女", 100, 4, null, "job", 12));
        Assert.Null(r.token);
        Assert.Equal(12, (await StaffOf(r.staff_id)).binding!.account_id);
        Assert.Equal(r.staff_id, (await PhoneOf(12)).holder!.staff_id);
        Assert.Equal(r.staff_id, (await LoginStaff("o-job-12")).id);

        await Assert.ThrowsAsync<StaffAccountException>(() => _service.OnboardAsync(_admin, new OnboardInput("甲", "男", 100, null, null, "job", 11)));
        await Assert.ThrowsAsync<StaffAccountException>(() => _service.OnboardAsync(_admin, new OnboardInput("乙", "男", 100, null, null, "job", 13)));
        await Assert.ThrowsAsync<StaffAccountException>(() => _service.OnboardAsync(_admin, new OnboardInput("丙", "男", 1000, null, null, "job", 12)));
        await Assert.ThrowsAsync<StaffAccountException>(() => _service.OnboardAsync(_admin, new OnboardInput("丁", "男", 100, null, DateTime.Today.AddDays(1), "private", null)));
    }

    [Fact]
    public async Task 入职用私人手机_扫码绑定后登录能认出_码只能用一次()
    {
        var r = await _service.OnboardAsync(_admin, new OnboardInput("新人", "男", 200, null, null, "private", null));
        var before = await StaffOf(r.staff_id);
        Assert.Null(before.binding);
        Assert.Equal(r.token, before.pending_bind!.token);
        Assert.Equal("ok", (await _service.GetCodeAsync(r.token)).status);

        await _service.ConfirmBindAsync(r.token, "o-new-member", "13700001111");
        var after = await StaffOf(r.staff_id);
        Assert.True(after.binding!.is_private);
        Assert.Equal("13700001111", after.binding.cell);
        Assert.True(after.binding.login_ok);
        Assert.Null(after.pending_bind);
        Assert.Equal(r.staff_id, (await LoginStaff("o-new-member")).id);
        Assert.Equal("used", (await _service.GetCodeAsync(r.token)).status);
        var again = await Assert.ThrowsAsync<StaffAccountException>(() => _service.ConfirmBindAsync(r.token, "o-new-member", "13700001111"));
        Assert.Contains("用过", again.Message);
    }

    [Fact]
    public async Task 不是会员的微信不能绑定()
    {
        var r = await _service.OnboardAsync(_admin, new OnboardInput("新人", "男", 100, null, null, "private", null));
        var e = await Assert.ThrowsAsync<StaffAccountException>(() => _service.ConfirmBindAsync(r.token, "o-stranger", "13700001111"));
        Assert.Equal(StaffAccountException.NotMember, e.Code);
        Assert.Null((await StaffOf(r.staff_id)).binding);
    }

    [Fact]
    public async Task 一个微信不能同时关联两个在职账号_工作手机的微信不能当私人手机()
    {
        var r = await _service.OnboardAsync(_admin, new OnboardInput("新人", "男", 100, null, null, "private", null));
        var taken = await Assert.ThrowsAsync<StaffAccountException>(() => _service.ConfirmBindAsync(r.token, "o-pri-21", "13811112222"));
        Assert.Contains("张伟", taken.Message);
        var job = await Assert.ThrowsAsync<StaffAccountException>(() => _service.ConfirmBindAsync(r.token, "o-job-12", "13900006240"));
        Assert.Contains("工作手机", job.Message);
    }

    [Fact]
    public async Task 换手机_私人换工作再换回私人_任何时候只关联一套()
    {
        await _service.ChangePhoneAsync(_admin, 3, 12);
        Assert.Equal(12, (await StaffOf(3)).binding!.account_id);
        Assert.Equal(1, await ActiveLinks(3));
        Assert.Equal(3, (await LoginStaff("o-job-12")).id);
        Assert.Null(await LoginStaff("o-pri-21"));

        var t = await _service.RebindAsync(_admin, 3);
        await _service.ConfirmBindAsync(t.token, "o-pri-21", "13811112222");
        var back = await StaffOf(3);
        Assert.Equal(21, back.binding!.account_id);
        Assert.Equal(1, await ActiveLinks(3));
        Assert.Null((await PhoneOf(12)).holder);
        Assert.Equal(3, (await _service.GetStaffAsync(_admin, 3)).history.Count);
    }

    [Fact]
    public async Task 新码生成后旧码作废()
    {
        var first = await _service.RebindAsync(_admin, 3);
        var second = await _service.RebindAsync(_admin, 3);
        Assert.Equal("cancelled", (await _service.GetCodeAsync(first.token)).status);
        Assert.Equal(second.token, (await StaffOf(3)).pending_bind!.token);
    }

    [Fact]
    public async Task 过期的码不能用()
    {
        var t = await _service.RebindAsync(_admin, 3);
        var code = await _db.staffBindCode.SingleAsync(c => c.token == t.token);
        code.expire_date = DateTime.Now.AddMinutes(-1);
        await _db.SaveChangesAsync();
        Assert.Equal("expired", (await _service.GetCodeAsync(t.token)).status);
        var e = await Assert.ThrowsAsync<StaffAccountException>(() => _service.ConfirmBindAsync(t.token, "o-new-member", "13700001111"));
        Assert.Contains("失效", e.Message);
    }

    [Fact]
    public async Task 登记工作手机_扫码手机号必须一致_同号不能重复登记()
    {
        var t = await _service.AddJobPhoneAsync(_admin, "13900005555");
        Assert.False((await PhoneOf(t.account_id!.Value)).has_wechat);
        var mismatch = await Assert.ThrowsAsync<StaffAccountException>(() => _service.ConfirmBindAsync(t.token, "o-new-member", "13700001111"));
        Assert.Contains("不一致", mismatch.Message);
        await _service.ConfirmBindAsync(t.token, "o-new-member", "13900005555");
        Assert.True((await PhoneOf(t.account_id.Value)).has_wechat);
        await Assert.ThrowsAsync<StaffAccountException>(() => _service.AddJobPhoneAsync(_admin, "13900005555"));
        await Assert.ThrowsAsync<StaffAccountException>(() => _service.AddJobPhoneAsync(_admin, "1390000"));
    }

    [Fact]
    public async Task 离职_结束关联_工作手机退回_登录认不出()
    {
        await _service.OffboardAsync(_admin, 2, null);
        var s = await StaffOf(2);
        Assert.False(s.valid);
        Assert.Null(s.binding);
        Assert.Null((await PhoneOf(11)).holder);
        Assert.Equal(0, await ActiveLinks(2));
        Assert.Null(await LoginStaff("o-job-11"));
        Assert.Single((await _service.GetStaffAsync(_admin, 2)).history);
    }

    [Fact]
    public async Task 离职日期早于今天就记那天()
    {
        var day = DateTime.Today.AddDays(-3);
        await _service.OffboardAsync(_admin, 2, day);
        var link = await _db.staffSocialAccount.AsNoTracking().SingleAsync(l => l.id == 1);
        Assert.Equal(day, link.end_date);
        Assert.Equal(0, link.valid);
    }

    [Fact]
    public async Task 不能给自己办离职_不能管理职级更高的账号()
    {
        await Assert.ThrowsAsync<StaffAccountException>(() => _service.OffboardAsync(_admin, 1, null));
        await Assert.ThrowsAsync<StaffAccountException>(() => _service.OffboardAsync(_admin, 4, null));
        await Assert.ThrowsAsync<StaffAccountException>(() => _service.UpdateStaffAsync(_admin, new UpdateStaffInput(4, "老板", "男", 300, null)));
        await Assert.ThrowsAsync<StaffAccountException>(() => _service.UpdateStaffAsync(_admin, new UpdateStaffInput(1, "管理员", "女", 200, null)));
    }

    [Fact]
    public async Task 修改信息()
    {
        await _service.UpdateStaffAsync(_admin, new UpdateStaffInput(2, "李明明", "男", 200, null));
        var s = await StaffOf(2);
        Assert.Equal("李明明", s.name);
        Assert.Equal(200, s.title_level);
        Assert.Null(s.base_shop_id);
        Assert.Equal(1, await _db.coreDataModLog.CountAsync(l => l.key_value == 2 && l.staff_id == 1));
    }

    [Fact]
    public async Task 收回离职员工占用的手机()
    {
        var li = await _db.staff.SingleAsync(s => s.id == 2);
        li.valid = 0;
        await _db.SaveChangesAsync();
        Assert.False((await PhoneOf(11)).holder!.valid);
        await _service.ReclaimPhoneAsync(_admin, 11);
        Assert.Null((await PhoneOf(11)).holder);
        await Assert.ThrowsAsync<StaffAccountException>(() => _service.ReclaimPhoneAsync(_admin, 11));
    }

    [Fact]
    public async Task 自助登记_待开通时登录认不出_开通后认得出()
    {
        int id = await _service.SelfRegisterAsync("o-new-member", "13700002222", "新人", "女");
        var pending = await StaffOf(id);
        Assert.True(pending.pending_reg);
        Assert.False(pending.valid);
        Assert.Equal("13700002222", pending.binding!.cell);
        Assert.Null(await LoginStaff("o-new-member"));
        var dup = await Assert.ThrowsAsync<StaffAccountException>(() => _service.SelfRegisterAsync("o-new-member", "13700002222", "新人", "女"));
        Assert.Contains("等待", dup.Message);
        await Assert.ThrowsAsync<StaffAccountException>(() => _service.OffboardAsync(_admin, id, null));

        await _service.ApproveAsync(_admin, id, 100, 4);
        var active = await StaffOf(id);
        Assert.True(active.valid);
        Assert.False(active.pending_reg);
        Assert.Equal(100, active.title_level);
        Assert.Equal(id, (await LoginStaff("o-new-member")).id);
    }

    [Fact]
    public async Task 自助登记被拒绝_账号停用且没有手机()
    {
        int id = await _service.SelfRegisterAsync("o-new-member", "13700002222", "新人", "女");
        await _service.RejectAsync(_admin, id);
        var s = await StaffOf(id);
        Assert.False(s.pending_reg);
        Assert.False(s.valid);
        Assert.Null(s.binding);
        await Assert.ThrowsAsync<StaffAccountException>(() => _service.ApproveAsync(_admin, id, 100, null));
    }

    [Fact]
    public async Task 自助登记_非会员和已在职的微信都拒绝()
    {
        var e = await Assert.ThrowsAsync<StaffAccountException>(() => _service.SelfRegisterAsync("o-stranger", "13700002222", "新人", "女"));
        Assert.Equal(StaffAccountException.NotMember, e.Code);
        var taken = await Assert.ThrowsAsync<StaffAccountException>(() => _service.SelfRegisterAsync("o-pri-21", "13811112222", "张伟", "男"));
        Assert.Contains("张伟", taken.Message);
    }

    [Fact]
    public async Task 接口_会话失效返回2_非管理员返回3_管理员正常()
    {
        _db.miniSession.AddRange(
            new MiniSession { session_key = "admin-key", session_type = "wechat_mini_openid", valid = 1, expire_date = DateTime.Now.AddHours(1), wechat_openid = "o-admin" },
            new MiniSession { session_key = "staff-key", session_type = "wechat_mini_openid", valid = 1, expire_date = DateTime.Now.AddHours(1), wechat_openid = "o-job-11" });
        _db.socialAccountForJob.Add(Account(31, "13900000001", 1, "o-admin", 0));
        _db.staffSocialAccount.Add(Link(31, 1, 31));
        await _db.SaveChangesAsync();
        var controller = new StaffAdminController(_db);

        Assert.Equal(2, (await controller.ListStaff("no-such-key")).code);
        Assert.Equal(3, (await controller.ListStaff("staff-key")).code);
        var ok = await controller.ListStaff("admin-key");
        Assert.Equal(0, ok.code);
        var bad = await controller.Offboard("admin-key", new StaffAdminController.OffboardInput(1, null));
        Assert.Equal(1, bad.code);
        Assert.Contains("自己", bad.message);
    }
}
