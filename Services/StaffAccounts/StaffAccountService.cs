using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services.StaffAccounts;

/// <summary>违反员工账号规则；Code 默认 1，5 表示这个微信还不是会员</summary>
public sealed class StaffAccountException(string message, int code = 1) : Exception(message)
{
    public const int NotMember = 5;
    public int Code { get; } = code;
}

// 接口返回结构与小程序 pages/staffadmin 一致（属性名即 JSON 字段名），日期一律格式化成字符串
public sealed record BindingDto(int link_id, int account_id, string cell, bool is_private, bool has_wechat, string start_date, bool login_ok);
public sealed record PendingBindDto(string token, string expire_at);
public sealed record StaffDto(int id, string name, string gender, int title_level, bool valid, int? base_shop_id, string shop_name,
    string create_date, BindingDto? binding, PendingBindDto? pending_bind, bool pending_reg);
public sealed record StaffHistoryDto(int id, string cell, bool is_private, string start_date, string? end_date, string season_memo);
public sealed record StaffDetailDto(StaffDto staff, List<StaffHistoryDto> history);
public sealed record PhoneHolderDto(int staff_id, string name, bool valid, int title_level);
public sealed record PhoneHistoryDto(int staff_id, string name, string start_date, string? end_date);
public sealed record PhoneDto(int id, string cell, bool has_wechat, PhoneHolderDto? holder, List<PhoneHistoryDto> history);
public sealed record ShopDto(int id, string name);
public sealed record CodeStaffDto(int id, string name, string title_label, string shop_name);
public sealed record CodePhoneDto(int id, string tail);
public sealed record CodeDto(string token, string purpose, string status, string expire_at, CodeStaffDto? staff, CodePhoneDto? phone);
public sealed record OnboardResult(int staff_id, string? token);
public sealed record TokenResult(string token, int? account_id = null);

public sealed record OnboardInput(string? name, string? gender, int title_level, int? base_shop_id, DateTime? start_date, string? type, int? account_id);
public sealed record UpdateStaffInput(int id, string? name, string? gender, int title_level, int? base_shop_id);

/// <summary>
/// 员工账号管理：一个账号同一时间只关联一套手机号 + 微信。写操作都在事务里先查规则再写，并记 core_data_mod_log。
/// 绑定和自助登记要求这个微信已是会员：登录时 GetStaffBySocialNum 正是按 openid → 会员 → social_account_for_job.member_id 认员工。
/// </summary>
public sealed class StaffAccountService(ApplicationDBContext db, Func<DateTime>? clock = null)
{
    private const string Scene = "员工账号管理";
    private static readonly Dictionary<int, string> TitleLabels = new()
    {
        [0] = "未开通", [50] = "万龙对账", [100] = "店员", [200] = "店长", [300] = "系统管理员", [1000] = "超级管理员"
    };
    private DateTime Now => clock?.Invoke() ?? DateTime.Now;

    public static string TitleLabel(int level) => TitleLabels.TryGetValue(level, out var label) ? label : "职级 " + level;
    private static string Tail(string? cell) => string.IsNullOrEmpty(cell) ? "" : "···" + (cell.Length > 4 ? cell[^4..] : cell);
    private static string Day(DateTime d) => d.ToString("yyyy-MM-dd");
    private static string? Day(DateTime? d) => d?.ToString("yyyy-MM-dd");
    private static string Minute(DateTime d) => d.ToString("yyyy-MM-dd HH:mm");

    // ---------- 读 ----------

    private sealed class Snapshot
    {
        public List<Staff> Staff = new();
        public List<StaffSocialAccount> Links = new();
        public List<SocialAccountForJob> Accounts = new();
        public List<StaffBindCode> OpenCodes = new();
        public Dictionary<int, string> Shops = new();
        public Dictionary<string, int> MemberByOpenId = new();
    }

    private async Task<Snapshot> LoadAsync()
    {
        DateTime now = Now;
        var snap = new Snapshot
        {
            Staff = await db.staff.AsNoTracking().ToListAsync(),
            Links = await db.staffSocialAccount.AsNoTracking().ToListAsync(),
            Accounts = await db.socialAccountForJob.AsNoTracking().ToListAsync(),
            OpenCodes = await db.staffBindCode.AsNoTracking().Where(c => c.used_date == null && c.cancel_date == null && c.expire_date > now).ToListAsync(),
            Shops = await db.shop.AsNoTracking().Select(s => new { s.id, s.name }).ToDictionaryAsync(s => s.id, s => s.name)
        };
        var openIds = snap.Accounts.Select(a => a.wechat_mini_openid).Where(o => !string.IsNullOrEmpty(o)).Distinct().ToList();
        var msa = await db.memberSocialAccount.AsNoTracking()
            .Where(m => m.type == MemberSocialAccount.TYPE_WECHAT_MINI_OPENID && m.valid == 1 && openIds.Contains(m.num))
            .Select(m => new { m.id, m.num, m.member_id }).ToListAsync();
        // 与 GetStaffBySocialNum 一致：同一 openid 取 id 最大的那条
        snap.MemberByOpenId = msa.GroupBy(m => m.num).ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.id).First().member_id);
        return snap;
    }

    // 登录能否认出员工身份：openid → 会员 → 该会员 id 最大的 social_account_for_job 必须就是这一行（同 GetStaffBySocialNum）
    private static bool LoginOk(Snapshot snap, SocialAccountForJob account)
    {
        if (string.IsNullOrEmpty(account.wechat_mini_openid)) return false;
        if (!snap.MemberByOpenId.TryGetValue(account.wechat_mini_openid, out int memberId)) return false;
        var latest = snap.Accounts.Where(a => a.member_id == memberId).OrderByDescending(a => a.id).FirstOrDefault();
        return latest?.id == account.id;
    }

    private StaffDto ToDto(Snapshot snap, Staff s)
    {
        DateTime now = Now;
        bool pendingReg = s.valid == 0 && snap.OpenCodes.Any(c => c.purpose == StaffBindCode.PURPOSE_SELFREG && c.staff_id == s.id);
        StaffSocialAccount? link = snap.Links.Where(l => l.staff_id == s.id && StaffAccountRules.IsActiveLink(l, now))
            .OrderByDescending(l => l.start_date).FirstOrDefault();
        if (link == null && pendingReg)
        {
            link = snap.Links.Where(l => l.staff_id == s.id && l.valid == 0 && l.end_date == null).OrderByDescending(l => l.id).FirstOrDefault();
        }
        SocialAccountForJob? account = link == null ? null : snap.Accounts.FirstOrDefault(a => a.id == link.social_account_id);
        BindingDto? binding = account == null ? null : new BindingDto(link!.id, account.id, account.cell, account.is_private == 1,
            !string.IsNullOrEmpty(account.wechat_mini_openid), Day(link.start_date), LoginOk(snap, account));
        StaffBindCode? code = snap.OpenCodes.Where(c => c.purpose == StaffBindCode.PURPOSE_PRIVATE && c.staff_id == s.id)
            .OrderByDescending(c => c.id).FirstOrDefault();
        return new StaffDto(s.id, s.name, s.gender, s.title_level, s.valid == 1, s.base_shop_id,
            s.base_shop_id != null && snap.Shops.TryGetValue(s.base_shop_id.Value, out var shop) ? shop : "",
            Day(s.create_date), binding, code == null ? null : new PendingBindDto(code.token, Minute(code.expire_date)),
            pendingReg);
    }

    public async Task<List<StaffDto>> ListStaffAsync()
    {
        var snap = await LoadAsync();
        return snap.Staff.OrderBy(s => s.id).Select(s => ToDto(snap, s)).ToList();
    }

    public async Task<StaffDetailDto> GetStaffAsync(int id)
    {
        var snap = await LoadAsync();
        Staff s = snap.Staff.FirstOrDefault(x => x.id == id) ?? throw new StaffAccountException("找不到这个账号");
        var history = snap.Links.Where(l => l.staff_id == id && (l.valid == 1 || l.end_date != null))
            .OrderByDescending(l => l.start_date).ThenByDescending(l => l.id)
            .Select(l =>
            {
                var a = snap.Accounts.FirstOrDefault(x => x.id == l.social_account_id);
                return new StaffHistoryDto(l.id, a?.cell ?? "", a?.is_private == 1, Day(l.start_date), Day(l.end_date), l.season_memo);
            }).ToList();
        return new StaffDetailDto(ToDto(snap, s), history);
    }

    public async Task<List<PhoneDto>> ListPhonesAsync()
    {
        var snap = await LoadAsync();
        DateTime now = Now;
        return snap.Accounts.Where(a => a.is_private == 0).OrderBy(a => a.id).Select(a =>
        {
            var link = snap.Links.FirstOrDefault(l => l.social_account_id == a.id && StaffAccountRules.IsActiveLink(l, now));
            var holder = link == null ? null : snap.Staff.FirstOrDefault(s => s.id == link.staff_id);
            var history = snap.Links.Where(l => l.social_account_id == a.id && (l.valid == 1 || l.end_date != null))
                .OrderByDescending(l => l.start_date).ThenByDescending(l => l.id)
                .Select(l => new PhoneHistoryDto(l.staff_id, snap.Staff.FirstOrDefault(s => s.id == l.staff_id)?.name ?? "", Day(l.start_date), Day(l.end_date)))
                .ToList();
            return new PhoneDto(a.id, a.cell, !string.IsNullOrEmpty(a.wechat_mini_openid),
                holder == null ? null : new PhoneHolderDto(holder.id, holder.name, holder.valid == 1, holder.title_level), history);
        }).ToList();
    }

    // 南山 2026-09-30 已关店，不再作为员工门店选项
    public async Task<List<ShopDto>> ListShopsAsync() =>
        await db.shop.AsNoTracking().Where(s => !s.name.Contains("南山")).OrderBy(s => s.sort).Select(s => new ShopDto(s.id, s.name)).ToListAsync();

    // ---------- 管理员写操作 ----------

    private async Task<T> InTransaction<T>(Func<Task<T>> body)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        T result = await body();
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return result;
    }

    private void Audit(string table, string field, int key, string action, int? staffId, int? memberId, string? prev, string? current) =>
        db.coreDataModLog.Add(CoreDataModLog.CreateManualLog(table, field, key, Scene + "-" + action, memberId, staffId, prev, current, null));

    private async Task<Staff> TargetAsync(int staffId)
    {
        return await db.staff.FirstOrDefaultAsync(s => s.id == staffId) ?? throw new StaffAccountException("找不到这个账号");
    }

    private async Task<StaffSocialAccount?> ActiveLinkOfStaffAsync(int staffId)
    {
        DateTime now = Now;
        return await db.staffSocialAccount.Where(l => l.staff_id == staffId && l.valid == 1 && (l.end_date == null || l.end_date >= now))
            .OrderByDescending(l => l.start_date).FirstOrDefaultAsync();
    }

    private async Task<StaffSocialAccount?> ActiveLinkOfAccountAsync(int accountId)
    {
        DateTime now = Now;
        return await db.staffSocialAccount.Where(l => l.social_account_id == accountId && l.valid == 1 && (l.end_date == null || l.end_date >= now))
            .FirstOrDefaultAsync();
    }

    private async Task<StaffBindCode?> OpenSelfRegAsync(int staffId)
    {
        DateTime now = Now;
        return await db.staffBindCode.Where(c => c.purpose == StaffBindCode.PURPOSE_SELFREG && c.staff_id == staffId
            && c.used_date == null && c.cancel_date == null && c.expire_date > now).OrderByDescending(c => c.id).FirstOrDefaultAsync();
    }

    private async Task CancelCodesAsync(string purpose, int? staffId, int? accountId)
    {
        DateTime now = Now;
        var codes = await db.staffBindCode.Where(c => c.purpose == purpose && c.used_date == null && c.cancel_date == null
            && (staffId == null || c.staff_id == staffId) && (accountId == null || c.social_account_id == accountId)).ToListAsync();
        codes.ForEach(c => c.cancel_date = now);
    }

    private StaffBindCode NewCode(string purpose, int? staffId, int? accountId, int createdBy, TimeSpan lifetime)
    {
        DateTime now = Now;
        var code = new StaffBindCode
        {
            token = StaffAccountRules.NewToken(), purpose = purpose, staff_id = staffId, social_account_id = accountId,
            created_by_staff_id = createdBy, create_date = now, expire_date = now + lifetime
        };
        db.staffBindCode.Add(code);
        return code;
    }

    private void AddLink(int staffId, int accountId, DateTime start, int valid = 1)
    {
        db.staffSocialAccount.Add(new StaffSocialAccount
        {
            staff_id = staffId, social_account_id = accountId, start_date = start, end_date = null, valid = valid,
            season_memo = StaffAccountRules.SeasonMemo(start), create_date = Now
        });
    }

    // 工作手机能分配：是工作手机、已绑定微信、没有人在用
    private async Task<SocialAccountForJob> AssignablePhoneAsync(int? accountId)
    {
        var account = await db.socialAccountForJob.FirstOrDefaultAsync(a => a.id == accountId);
        if (account == null || account.is_private != 0 || string.IsNullOrEmpty(account.wechat_mini_openid)
            || await ActiveLinkOfAccountAsync(account.id) != null)
        {
            throw new StaffAccountException("这部工作手机不能分配，请重新选择");
        }
        return account;
    }

    private static string CheckName(string? name)
    {
        string n = (name ?? "").Trim();
        if (n.Length == 0) throw new StaffAccountException("请填写姓名");
        if (n.Length > 50) throw new StaffAccountException("姓名太长");
        return n;
    }

    private async Task CheckTitleAndShopAsync(Staff op, int titleLevel, int? shopId)
    {
        if (!StaffAccountRules.IsTitleAllowed(titleLevel)) throw new StaffAccountException("请选择职级");
        if (titleLevel > op.title_level) throw new StaffAccountException("不能设置比自己高的职级");
        if (shopId != null && !await db.shop.AnyAsync(s => s.id == shopId)) throw new StaffAccountException("门店不存在");
    }

    public Task<OnboardResult> OnboardAsync(Staff op, OnboardInput input) => InTransaction(async () =>
    {
        DateTime now = Now;
        string name = CheckName(input.name);
        if (!StaffAccountRules.IsValidGender(input.gender)) throw new StaffAccountException("请选择性别");
        await CheckTitleAndShopAsync(op, input.title_level, input.base_shop_id);
        if (input.type != "job" && input.type != "private") throw new StaffAccountException("请选择分配工作手机还是用私人手机");
        DateTime start = input.start_date?.Date ?? now.Date;
        if (start > now.Date) throw new StaffAccountException("入职日期不能晚于今天");
        SocialAccountForJob? phone = input.type == "job" ? await AssignablePhoneAsync(input.account_id) : null;

        var staff = new Staff { name = name, gender = input.gender!, title_level = input.title_level, valid = 1, base_shop_id = input.base_shop_id, create_date = now, update_date = now };
        db.staff.Add(staff);
        await db.SaveChangesAsync();
        if (phone != null)
        {
            AddLink(staff.id, phone.id, start == now.Date ? now : start);
            Audit("staff", "valid", staff.id, "入职", op.id, null, null, "工作手机 " + Tail(phone.cell));
            return new OnboardResult(staff.id, null);
        }
        var code = NewCode(StaffBindCode.PURPOSE_PRIVATE, staff.id, null, op.id, StaffAccountRules.CodeLifetime);
        Audit("staff", "valid", staff.id, "入职", op.id, null, null, "私人手机待扫码绑定");
        return new OnboardResult(staff.id, code.token);
    });

    public Task<int> UpdateStaffAsync(Staff op, UpdateStaffInput input) => InTransaction(async () =>
    {
        Staff target = await TargetAsync(input.id);
        string name = CheckName(input.name);
        if (!StaffAccountRules.IsValidGender(input.gender)) throw new StaffAccountException("请选择性别");
        bool pending = target.valid == 0 && await OpenSelfRegAsync(target.id) != null;
        if (target.valid == 0 && !pending) throw new StaffAccountException("账号已停用");
        string prev = target.name + "/" + target.gender + "/" + target.title_level + "/" + target.base_shop_id;
        target.name = name;
        target.gender = input.gender!;
        if (!pending)
        {
            if (target.id == op.id && input.title_level != target.title_level) throw new StaffAccountException("不能修改自己的职级");
            if (input.title_level != target.title_level || input.base_shop_id != target.base_shop_id)
            {
                await CheckTitleAndShopAsync(op, input.title_level, input.base_shop_id);
            }
            target.title_level = input.title_level;
            target.base_shop_id = input.base_shop_id;
        }
        target.update_date = Now;
        Audit("staff", "info", target.id, "修改信息", op.id, null, prev, target.name + "/" + target.gender + "/" + target.title_level + "/" + target.base_shop_id);
        return target.id;
    });

    public Task<int> ChangePhoneAsync(Staff op, int staffId, int accountId) => InTransaction(async () =>
    {
        DateTime now = Now;
        Staff target = await TargetAsync(staffId);
        if (target.valid != 1) throw new StaffAccountException("账号已停用");
        SocialAccountForJob phone = await AssignablePhoneAsync(accountId);
        var current = await ActiveLinkOfStaffAsync(target.id);
        if (current != null) StaffAccountRules.EndLink(current, now, now);
        await CancelCodesAsync(StaffBindCode.PURPOSE_PRIVATE, target.id, null);
        AddLink(target.id, phone.id, now);
        Audit("staff", "staff_social_account", target.id, "更换手机", op.id, null, current?.social_account_id.ToString(), "工作手机 " + Tail(phone.cell));
        return target.id;
    });

    public Task<TokenResult> RebindAsync(Staff op, int staffId) => InTransaction(async () =>
    {
        Staff target = await TargetAsync(staffId);
        if (target.valid != 1) throw new StaffAccountException("账号已停用");
        await CancelCodesAsync(StaffBindCode.PURPOSE_PRIVATE, target.id, null);
        var code = NewCode(StaffBindCode.PURPOSE_PRIVATE, target.id, null, op.id, StaffAccountRules.CodeLifetime);
        return new TokenResult(code.token);
    });

    public Task<TokenResult> BindJobPhoneAsync(Staff op, int accountId) => InTransaction(async () =>
    {
        var account = await db.socialAccountForJob.FirstOrDefaultAsync(a => a.id == accountId) ?? throw new StaffAccountException("找不到这部手机");
        if (account.is_private != 0) throw new StaffAccountException("只有工作手机需要单独绑定微信");
        await CancelCodesAsync(StaffBindCode.PURPOSE_JOB_PHONE, null, account.id);
        var code = NewCode(StaffBindCode.PURPOSE_JOB_PHONE, null, account.id, op.id, StaffAccountRules.CodeLifetime);
        return new TokenResult(code.token, account.id);
    });

    public Task<TokenResult> AddJobPhoneAsync(Staff op, string? cell) => InTransaction(async () =>
    {
        string c = (cell ?? "").Trim();
        if (!StaffAccountRules.IsValidCell(c)) throw new StaffAccountException("请填写 11 位手机号");
        if (await db.socialAccountForJob.AnyAsync(a => a.is_private == 0 && a.cell == c)) throw new StaffAccountException("这部工作手机已经登记过");
        var account = new SocialAccountForJob { cell = c, wechat_mini_openid = "", member_id = 0, is_private = 0, create_date = Now };
        db.socialAccountForJob.Add(account);
        await db.SaveChangesAsync();
        var code = NewCode(StaffBindCode.PURPOSE_JOB_PHONE, null, account.id, op.id, StaffAccountRules.CodeLifetime);
        Audit("social_account_for_job", "cell", account.id, "登记工作手机", op.id, null, null, c);
        return new TokenResult(code.token, account.id);
    });

    public Task<int> OffboardAsync(Staff op, int staffId, DateTime? date) => InTransaction(async () =>
    {
        DateTime now = Now;
        Staff target = await TargetAsync(staffId);
        if (target.id == op.id) throw new StaffAccountException("不能给自己办理离职");
        if (target.valid == 0 && await OpenSelfRegAsync(target.id) != null) throw new StaffAccountException("待开通的账号请用「拒绝」");
        var current = await ActiveLinkOfStaffAsync(target.id);
        if (target.valid == 0 && current == null) throw new StaffAccountException("账号已经离职");
        if (current != null) StaffAccountRules.EndLink(current, StaffAccountRules.EndTime(date, now), now);
        await CancelCodesAsync(StaffBindCode.PURPOSE_PRIVATE, target.id, null);
        Audit("staff", "valid", target.id, target.valid == 1 ? "离职" : "结束绑定", op.id, null, target.valid.ToString(), "0");
        target.valid = 0;
        target.update_date = now;
        return target.id;
    });

    public Task<int> ApproveAsync(Staff op, int staffId, int titleLevel, int? shopId) => InTransaction(async () =>
    {
        DateTime now = Now;
        Staff target = await TargetAsync(staffId);
        var reg = await OpenSelfRegAsync(target.id);
        if (target.valid != 0 || reg == null) throw new StaffAccountException("这个账号不是待开通状态");
        await CheckTitleAndShopAsync(op, titleLevel, shopId);
        var link = await db.staffSocialAccount.Where(l => l.staff_id == target.id && l.valid == 0 && l.end_date == null)
            .OrderByDescending(l => l.id).FirstOrDefaultAsync() ?? throw new StaffAccountException("登记记录不完整，请让员工重新登记");
        var holder = await ActiveLinkOfAccountAsync(link.social_account_id);
        if (holder != null) throw new StaffAccountException("这个微信已经关联了别的账号");
        link.valid = 1;
        link.start_date = now;
        link.season_memo = StaffAccountRules.SeasonMemo(now);
        link.update_date = now;
        reg.used_date = now;
        target.valid = 1;
        target.title_level = titleLevel;
        target.base_shop_id = shopId;
        target.update_date = now;
        Audit("staff", "valid", target.id, "开通", op.id, null, "0", TitleLabel(titleLevel));
        return target.id;
    });

    public Task<int> RejectAsync(Staff op, int staffId) => InTransaction(async () =>
    {
        DateTime now = Now;
        Staff target = await TargetAsync(staffId);
        var reg = await OpenSelfRegAsync(target.id);
        if (target.valid != 0 || reg == null) throw new StaffAccountException("这个账号不是待开通状态");
        reg.cancel_date = now;
        var link = await db.staffSocialAccount.Where(l => l.staff_id == target.id && l.valid == 0 && l.end_date == null).ToListAsync();
        link.ForEach(l => StaffAccountRules.EndLink(l, now, now));
        Audit("staff", "valid", target.id, "拒绝开通", op.id, null, null, null);
        return target.id;
    });

    public Task<int> ReclaimPhoneAsync(Staff op, int accountId) => InTransaction(async () =>
    {
        DateTime now = Now;
        var link = await ActiveLinkOfAccountAsync(accountId);
        var holder = link == null ? null : await db.staff.FirstOrDefaultAsync(s => s.id == link.staff_id);
        if (link == null || holder == null || holder.valid == 1) throw new StaffAccountException("这部手机不需要收回");
        StaffAccountRules.EndLink(link, now, now);
        Audit("social_account_for_job", "staff_social_account", accountId, "收回手机", op.id, null, holder.id.ToString(), null);
        return accountId;
    });

    // ---------- 扫码的那部手机（不要求员工身份） ----------

    public async Task<CodeDto> GetCodeAsync(string? token)
    {
        var code = await db.staffBindCode.AsNoTracking().FirstOrDefaultAsync(c => c.token == token && c.purpose != StaffBindCode.PURPOSE_SELFREG)
            ?? throw new StaffAccountException("绑定码不存在");
        CodeStaffDto? staff = null;
        CodePhoneDto? phone = null;
        if (code.staff_id != null)
        {
            var s = await db.staff.AsNoTracking().FirstOrDefaultAsync(x => x.id == code.staff_id);
            if (s != null)
            {
                string shop = s.base_shop_id == null ? "" : await db.shop.Where(x => x.id == s.base_shop_id).Select(x => x.name).FirstOrDefaultAsync() ?? "";
                staff = new CodeStaffDto(s.id, s.name, TitleLabel(s.title_level), shop);
            }
        }
        if (code.social_account_id != null)
        {
            var a = await db.socialAccountForJob.AsNoTracking().FirstOrDefaultAsync(x => x.id == code.social_account_id);
            if (a != null) phone = new CodePhoneDto(a.id, Tail(a.cell));
        }
        return new CodeDto(code.token, code.purpose, StaffAccountRules.CodeStatus(code, Now), Minute(code.expire_date), staff, phone);
    }

    // 与 GetStaffBySocialNum 相同的条件：openid 对应的有效会员档案，取 id 最大的一条
    private async Task<int> MemberIdAsync(string openId)
    {
        var msa = await db.memberSocialAccount.AsNoTracking()
            .Where(m => m.num == openId && m.type == MemberSocialAccount.TYPE_WECHAT_MINI_OPENID && m.valid == 1)
            .OrderByDescending(m => m.id).FirstOrDefaultAsync();
        return msa?.member_id ?? throw new StaffAccountException("这个微信还不是会员，请先在小程序里注册会员后再扫码", StaffAccountException.NotMember);
    }

    private async Task<string> HolderNameAsync(int staffId) =>
        await db.staff.Where(s => s.id == staffId).Select(s => s.name).FirstOrDefaultAsync() ?? "";

    public Task<int?> ConfirmBindAsync(string? token, string? openId, string? cell) => InTransaction(async () =>
    {
        DateTime now = Now;
        var code = await db.staffBindCode.FirstOrDefaultAsync(c => c.token == token && c.purpose != StaffBindCode.PURPOSE_SELFREG)
            ?? throw new StaffAccountException("绑定码不存在");
        string status = StaffAccountRules.CodeStatus(code, now);
        if (status == "used") throw new StaffAccountException("这个码已经用过了");
        if (status != "ok") throw new StaffAccountException("这个码已失效，请让管理员重新生成");
        string oid = (openId ?? "").Trim();
        if (oid.Length == 0) throw new StaffAccountException("没有取到微信身份，请重新进入小程序");
        string c = (cell ?? "").Trim();
        int memberId = await MemberIdAsync(oid);
        var sameWechat = await db.socialAccountForJob.Where(a => a.wechat_mini_openid == oid).OrderByDescending(a => a.id).ToListAsync();

        if (code.purpose == StaffBindCode.PURPOSE_JOB_PHONE)
        {
            var phone = await db.socialAccountForJob.FirstOrDefaultAsync(a => a.id == code.social_account_id) ?? throw new StaffAccountException("找不到这部手机");
            if (c != phone.cell.Trim()) throw new StaffAccountException("授权的手机号 " + Tail(c) + " 和工作手机 " + Tail(phone.cell) + " 不一致");
            var other = sameWechat.FirstOrDefault(a => a.id != phone.id);
            if (other != null) throw new StaffAccountException("这个微信已登记在手机 " + Tail(other.cell) + " 上");
            phone.wechat_mini_openid = oid;
            phone.member_id = memberId;
            phone.update_date = now;
            code.used_date = now;
            Audit("social_account_for_job", "wechat_mini_openid", phone.id, "工作手机绑定微信", null, memberId, null, Tail(phone.cell));
            return (int?)null;
        }

        var staff = await db.staff.FirstOrDefaultAsync(s => s.id == code.staff_id) ?? throw new StaffAccountException("找不到这个账号");
        if (staff.valid != 1) throw new StaffAccountException("账号已停用");
        var row = sameWechat.FirstOrDefault();
        if (row != null && row.is_private == 0) throw new StaffAccountException("这个微信是工作手机 " + Tail(row.cell) + " 上的微信，不能当作私人手机绑定");
        var current = await ActiveLinkOfStaffAsync(staff.id);
        if (row != null)
        {
            var holder = await ActiveLinkOfAccountAsync(row.id);
            if (holder != null && holder.staff_id != staff.id)
            {
                var holderStaff = await db.staff.FirstOrDefaultAsync(s => s.id == holder.staff_id);
                if (holderStaff?.valid == 1) throw new StaffAccountException("这个微信已关联账号「" + holderStaff.name + "」");
                StaffAccountRules.EndLink(holder, now, now);
            }
            row.cell = c.Length > 0 ? c : row.cell;
            row.member_id = memberId;
            row.update_date = now;
        }
        else
        {
            row = new SocialAccountForJob { cell = c, wechat_mini_openid = oid, member_id = memberId, is_private = 1, create_date = now };
            db.socialAccountForJob.Add(row);
            await db.SaveChangesAsync();
        }
        if (current == null || current.social_account_id != row.id)
        {
            if (current != null) StaffAccountRules.EndLink(current, now, now);
            AddLink(staff.id, row.id, now);
        }
        code.used_date = now;
        Audit("staff", "staff_social_account", staff.id, "绑定私人手机", null, memberId, current?.social_account_id.ToString(), Tail(row.cell));
        return (int?)staff.id;
    });

    public Task<int> SelfRegisterAsync(string? openId, string? cell, string? name, string? gender) => InTransaction(async () =>
    {
        DateTime now = Now;
        string n = CheckName(name);
        if (!StaffAccountRules.IsValidGender(gender)) throw new StaffAccountException("请选择性别");
        string oid = (openId ?? "").Trim();
        if (oid.Length == 0) throw new StaffAccountException("没有取到微信身份，请重新进入小程序");
        int memberId = await MemberIdAsync(oid);
        string c = (cell ?? "").Trim();
        var row = await db.socialAccountForJob.Where(a => a.wechat_mini_openid == oid).OrderByDescending(a => a.id).FirstOrDefaultAsync();
        if (row != null)
        {
            if (row.is_private == 0) throw new StaffAccountException("这个微信是工作手机上的微信，不能用来自助登记");
            var holder = await ActiveLinkOfAccountAsync(row.id);
            if (holder != null)
            {
                var holderStaff = await db.staff.FirstOrDefaultAsync(s => s.id == holder.staff_id);
                if (holderStaff?.valid == 1) throw new StaffAccountException("这个微信已经是员工账号「" + holderStaff.name + "」");
            }
            int rowId = row.id;
            bool waiting = await db.staffBindCode.AnyAsync(x => x.purpose == StaffBindCode.PURPOSE_SELFREG && x.social_account_id == rowId
                && x.used_date == null && x.cancel_date == null && x.expire_date > now);
            if (waiting) throw new StaffAccountException("你已经提交过登记，请等待系统管理员开通");
            row.cell = c.Length > 0 ? c : row.cell;
            row.member_id = memberId;
            row.update_date = now;
        }
        else
        {
            row = new SocialAccountForJob { cell = c, wechat_mini_openid = oid, member_id = memberId, is_private = 1, create_date = now };
            db.socialAccountForJob.Add(row);
        }
        var staff = new Staff { name = n, gender = gender!, title_level = 0, valid = 0, base_shop_id = null, create_date = now, update_date = now };
        db.staff.Add(staff);
        await db.SaveChangesAsync();
        // 待开通的关联先 valid=0：登录时 GetStaff 只认 valid=1，开通前不会被当作员工
        AddLink(staff.id, row.id, now, valid: 0);
        NewCode(StaffBindCode.PURPOSE_SELFREG, staff.id, row.id, 0, StaffAccountRules.SelfRegLifetime);
        Audit("staff", "valid", staff.id, "自助登记", null, memberId, null, Tail(row.cell));
        return staff.id;
    });
}
