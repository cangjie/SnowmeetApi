using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Controllers
{
    // 优惠券管理（后台）。鉴权 staff.title_level >= 100（店员即可）。
    //
    // 单独开一个 controller 而不是塞进 TicketController：那份文件 1200+ 行、新旧接口混杂，
    // 而且它同时服务顾客侧和店员侧；也不塞进 MemberAdminController：那份文件全文
    // MIN_LEVEL = 200，把 100 门槛的接口混进去迟早有人抄错常量。
    //
    // 与顾客侧口径的**有意差异**：这里不过滤 valid / is_active。这是一个审计视图，
    // 它的价值恰恰是能看到顾客端看不到的券（生产库 valid=0 有 88 张、is_active=0 有 271 张，
    // 后者是雪票券取卡前的正常状态）。过滤掉等于把要排查的对象藏起来。
    // 取而代之：每行下发 valid/isActive 让前端打徽标，外加一个 invalidCount 提示条。
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class TicketAdminController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;

        public TicketAdminController(ApplicationDBContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        const int MIN_LEVEL = 100; // 店员即可

        private async Task<Staff?> GetStaff(string sessionKey, string sessionType)
        {
            return await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
        }

        /// <summary>
        /// 明细视图和汇总视图共用同一份筛选条件——两个 action 第一行都调它，
        /// 条件不可能漂移，「筛选两视图共用」这个需求是靠这个方法满足的，不是靠合并接口。
        /// </summary>
        [NonAction]
        private IQueryable<Ticket> BuildFilteredQuery(DateTime? startDate, DateTime? endDate,
            int? templateId, bool? used, bool? transferred, bool includeWasted, DateTime now)
        {
            IQueryable<Ticket> q = _db.ticket.AsNoTracking();

            if (startDate != null)
            {
                DateTime s = ((DateTime)startDate).Date;
                q = q.Where(t => t.create_date >= s);
            }
            if (endDate != null)
            {
                DateTime e = ((DateTime)endDate).Date.AddDays(1);
                q = q.Where(t => t.create_date < e);
            }
            if (templateId != null)
            {
                q = q.Where(t => t.template_id == templateId);
            }
            if (used != null)
            {
                q = used == true ? q.Where(t => t.used == 1) : q.Where(t => t.used != 1);
            }
            if (!includeWasted)
            {
                // 开关打开时是整条谓词不加（而不是加反向谓词），保证"开 = 关的超集"，两个数对得上
                q = q.Where(TicketTransferRules.NotWastedFilter(now));
            }
            if (transferred != null)
            {
                IQueryable<TicketLog> transferLogs = _db.ticketLog.Where(TicketTransferRules.TransferLogFilter());
                q = transferred == true
                    ? q.Where(t => transferLogs.Any(l => l.code == t.code))
                    : q.Where(t => !transferLogs.Any(l => l.code == t.code));
            }
            return q;
        }

        // ───────────────────────── 按券明细 ─────────────────────────
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> SearchTicketsByStaff(string sessionKey,
            DateTime? startDate = null, DateTime? endDate = null, int? templateId = null,
            bool? used = null, bool? transferred = null, bool includeWasted = false,
            int pageIndex = 1, int pageSize = 20, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(Util.UrlDecode(sessionKey), sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });
            }
            (pageIndex, pageSize) = TicketAdminRules.ClampPaging(pageIndex, pageSize);
            DateTime now = DateTime.Now;

            IQueryable<Ticket> q = BuildFilteredQuery(startDate, endDate, templateId, used,
                transferred, includeWasted, now);

            int total = await q.CountAsync();
            List<Ticket> page = await q.OrderByDescending(t => t.create_date).ThenBy(t => t.code)
                .Skip((pageIndex - 1) * pageSize).Take(pageSize).ToListAsync();

            // 转赠次数：分页之后再按 code 批量捞，不在循环里逐张查（贴 TicketController.FillDisplayTime 同款范式）。
            // 顺带把最后一次转赠时间也带出来，前端不用再问一次。
            List<string> codes = page.Select(t => t.code).ToList();
            var transferRows = await _db.ticketLog
                .Where(TicketTransferRules.TransferLogFilter())
                .Where(l => codes.Contains(l.code))
                .GroupBy(l => l.code)
                .Select(g => new { code = g.Key, n = g.Count(), last = g.Max(x => x.transact_time) })
                .AsNoTracking().ToListAsync();
            Dictionary<string, int> transferCount = transferRows.ToDictionary(x => x.code, x => x.n);
            Dictionary<string, DateTime> transferLast = transferRows.ToDictionary(x => x.code, x => x.last);

            // 模板名 / 会员姓名 / 手机号，各一次批量查
            List<int> templateIds = page.Select(t => t.template_id).Distinct().ToList();
            var templates = await _db.ticketTemplate.Where(t => templateIds.Contains(t.id))
                .Select(t => new { t.id, t.name }).AsNoTracking().ToListAsync();
            List<int> memberIds = page.Where(t => t.member_id != null)
                .Select(t => (int)t.member_id).Distinct().ToList();
            var members = await _db.member.Where(m => memberIds.Contains(m.id))
                .Select(m => new { m.id, m.real_name, m.gender }).AsNoTracking().ToListAsync();
            var cells = await _db.memberSocialAccount
                .Where(m => memberIds.Contains(m.member_id) && m.valid == 1 && m.type == "cell")
                .Select(m => new { m.member_id, m.num }).AsNoTracking().ToListAsync();

            var items = page.Select(t =>
            {
                var tpl = templates.FirstOrDefault(x => x.id == t.template_id);
                var mi = t.member_id == null ? null : members.FirstOrDefault(x => x.id == t.member_id);
                var cell = t.member_id == null ? null : cells.FirstOrDefault(x => x.member_id == t.member_id);
                TicketStateView st = TicketAdminRules.DescribeState(t, now);
                TicketStateView bn = TicketTransferRules.ResolveBanner(
                    tpl != null && !string.IsNullOrWhiteSpace(tpl.name) ? tpl.name : t.name);
                int tc = transferCount.ContainsKey(t.code) ? transferCount[t.code] : 0;
                return new
                {
                    code = t.code,
                    name = (t.name ?? "").Trim(),
                    templateId = t.template_id,
                    templateName = tpl != null ? (tpl.name ?? "").Trim() : "",
                    memberId = t.member_id ?? 0,
                    memberName = mi != null ? (mi.real_name ?? "").Trim() : "",
                    memberGender = mi != null ? (mi.gender ?? "").Trim() : "",
                    memberPhone = cell != null ? (cell.num ?? "").Trim() : "",
                    createDateStr = t.create_date.ToString("yyyy-MM-dd HH:mm"),
                    expireDateStr = FormatDay(t.expire_date),
                    used = t.used,
                    usedTimeStr = t.used == 1 ? FormatMinute(t.used_time) : "",
                    transferCount = tc,
                    lastTransferTimeStr = transferLast.ContainsKey(t.code)
                        ? transferLast[t.code].ToString("yyyy-MM-dd HH:mm") : "",
                    valid = t.valid,
                    isActive = t.is_active,
                    channel = (t.channel ?? "").Trim(),
                    createMemo = (t.create_memo ?? "").Trim(),
                    stateLabel = st.Label,
                    stateCls = st.Cls,
                    bannerCls = bn.Cls,
                    bannerLabel = bn.Label
                };
            }).ToList();

            // 当前筛选下被默认范围排除掉的废券张数，给"另有 N 张废券未显示"那行用。
            // includeWasted 已经打开时就是 0（没有被藏起来的了）。
            int wastedTotal = 0;
            if (!includeWasted)
            {
                IQueryable<Ticket> all = BuildFilteredQuery(startDate, endDate, templateId, used,
                    transferred, true, now);
                wastedTotal = await all.CountAsync() - total;
            }
            int invalidCount = await q.CountAsync(t => t.valid != 1);

            return Ok(new ApiResult<object>()
            {
                code = 0,
                message = "",
                data = new { items, total, wastedTotal, invalidCount }
            });
        }

        // ───────────────────────── 按会员汇总 ─────────────────────────
        // 入参与 SearchTicketsByStaff 逐字相同（前端一套 data 直接切 URL）。
        // 拆成两个 action 而不是一个带 groupBy 参数：total 的分页单位一个是券、一个是会员，
        // 同名不同义必然把 totalPages 算错；返回形状也完全不同。
        // 自检点：同一组筛选下这里的 ticketTotal 必须等于明细接口的 total。
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> SearchTicketMembersByStaff(string sessionKey,
            DateTime? startDate = null, DateTime? endDate = null, int? templateId = null,
            bool? used = null, bool? transferred = null, bool includeWasted = false,
            int pageIndex = 1, int pageSize = 20, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(Util.UrlDecode(sessionKey), sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });
            }
            (pageIndex, pageSize) = TicketAdminRules.ClampPaging(pageIndex, pageSize);
            DateTime now = DateTime.Now;

            IQueryable<Ticket> q = BuildFilteredQuery(startDate, endDate, templateId, used,
                transferred, includeWasted, now);

            // 券数 / 已核销数在 SQL 里 GroupBy 直接算掉，不用回查。
            // ⚠️ 已核销必须用 used == 1，不能用 used_time != null——
            // TicketController.Cancel 把券退回未使用时反而写了 used_time。
            var grouped = q.GroupBy(t => t.member_id).Select(g => new
            {
                memberId = g.Key,
                lastCreate = g.Max(t => t.create_date),
                ticketCount = g.Count(),
                usedCount = g.Sum(t => t.used == 1 ? 1 : 0)
            });

            int total = await grouped.CountAsync();
            int ticketTotal = await q.CountAsync();

            var pageRows = await grouped.OrderByDescending(x => x.lastCreate).ThenBy(x => x.memberId)
                .Skip((pageIndex - 1) * pageSize).Take(pageSize).AsNoTracking().ToListAsync();

            List<int> ids = pageRows.Where(r => r.memberId != null).Select(r => (int)r.memberId).ToList();
            bool hasNullMember = pageRows.Any(r => r.memberId == null);

            // 这 ≤20 个会员的转赠次数合计：一次 join 聚合出来。
            // 不能照明细视图那样按 code 批量捞——20 个会员名下可能上千张券，Contains 参数会爆。
            var transferRows = await (
                from t in q.Where(t => (t.member_id != null && ids.Contains((int)t.member_id))
                                       || (hasNullMember && t.member_id == null))
                join l in _db.ticketLog.Where(TicketTransferRules.TransferLogFilter())
                    on t.code equals l.code
                group l by t.member_id into g
                select new { memberId = g.Key, n = g.Count() }
            ).AsNoTracking().ToListAsync();

            var members = await _db.member.Where(m => ids.Contains(m.id))
                .Select(m => new { m.id, m.real_name, m.gender }).AsNoTracking().ToListAsync();
            var cells = await _db.memberSocialAccount
                .Where(m => ids.Contains(m.member_id) && m.valid == 1 && m.type == "cell")
                .Select(m => new { m.member_id, m.num }).AsNoTracking().ToListAsync();

            var items = pageRows.Select(r =>
            {
                var mi = r.memberId == null ? null : members.FirstOrDefault(x => x.id == r.memberId);
                var cell = r.memberId == null ? null : cells.FirstOrDefault(x => x.member_id == r.memberId);
                var tf = transferRows.FirstOrDefault(x => x.memberId == r.memberId);
                return new
                {
                    memberId = r.memberId ?? 0,   // 0 = 无会员归属的历史脏数据，前端不可点
                    memberName = mi != null ? (mi.real_name ?? "").Trim() : "",
                    memberGender = mi != null ? (mi.gender ?? "").Trim() : "",
                    memberPhone = cell != null ? (cell.num ?? "").Trim() : "",
                    ticketCount = r.ticketCount,
                    usedCount = r.usedCount,
                    transferCount = tf != null ? tf.n : 0,
                    lastCreateDateStr = r.lastCreate.ToString("yyyy-MM-dd")
                };
            }).ToList();

            return Ok(new ApiResult<object>()
            {
                code = 0,
                message = "",
                data = new { items, total, ticketTotal }
            });
        }

        // ───────────────────────── 单张券详情 + 操作流水 ─────────────────────────
        // ticket_log 此前没有任何读接口，这是第一个。
        // 流水**全量**显示（转赠/核销/撤回/发放都列），不像转赠次数那样只数真转赠——
        // 店员排查一张券时，核销和撤回同样是关键线索。
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> GetTicketDetailByStaff(string code,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(Util.UrlDecode(sessionKey), sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });
            }
            code = Util.UrlDecode(code).Trim();
            Ticket t = await _db.ticket.AsNoTracking().FirstOrDefaultAsync(x => x.code == code);
            if (t == null)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "优惠券不存在", data = null });
            }
            DateTime now = DateTime.Now;

            TicketTemplate tpl = await _db.ticketTemplate.AsNoTracking()
                .FirstOrDefaultAsync(x => x.id == t.template_id);
            Member owner = t.member_id == null ? null
                : await _db.member.AsNoTracking().FirstOrDefaultAsync(m => m.id == t.member_id);
            string ownerPhone = "";
            if (t.member_id != null)
            {
                MemberSocialAccount msa = await _db.memberSocialAccount.AsNoTracking()
                    .FirstOrDefaultAsync(m => m.member_id == t.member_id && m.valid == 1 && m.type == "cell");
                ownerPhone = msa != null ? (msa.num ?? "").Trim() : "";
            }

            List<TicketLog> logs = await _db.ticketLog.Where(l => l.code == code)
                .OrderBy(l => l.transact_time).ThenBy(l => l.id).AsNoTracking().ToListAsync();

            // 流水里的 sender/accepter 是小程序 openid，一次性批量反查成姓名，别在循环里逐条查
            List<string> openIds = logs.Select(l => (l.sender_open_id ?? "").Trim())
                .Concat(logs.Select(l => (l.accepter_open_id ?? "").Trim()))
                .Where(s => s != "").Distinct().ToList();
            Dictionary<string, string> nameByOpenId = new Dictionary<string, string>();
            if (openIds.Count > 0)
            {
                var msaRows = await _db.memberSocialAccount
                    .Where(m => m.type == "wechat_mini_openid" && m.valid == 1 && openIds.Contains(m.num))
                    .Select(m => new { m.num, m.member_id }).AsNoTracking().ToListAsync();
                List<int> mids = msaRows.Select(x => x.member_id).Distinct().ToList();
                var names = await _db.member.Where(m => mids.Contains(m.id))
                    .Select(m => new { m.id, m.real_name }).AsNoTracking().ToListAsync();
                foreach (var r in msaRows)
                {
                    var n = names.FirstOrDefault(x => x.id == r.member_id);
                    string display = n != null && !string.IsNullOrWhiteSpace(n.real_name)
                        ? n.real_name.Trim() : ("会员 " + r.member_id);
                    if (!nameByOpenId.ContainsKey(r.num))
                    {
                        nameByOpenId[r.num] = display;
                    }
                }
            }

            var logItems = logs.Select(l =>
            {
                TicketStateView v = TicketAdminRules.DescribeLogEntry(l);
                return new
                {
                    timeStr = l.transact_time.ToString("yyyy-MM-dd HH:mm"),
                    typeLabel = v.Label,
                    typeCls = v.Cls,
                    fromName = ResolveName(nameByOpenId, l.sender_open_id),
                    toName = ResolveName(nameByOpenId, l.accepter_open_id),
                    memo = (l.memo ?? "").Trim()
                };
            }).ToList();

            TicketStateView st = TicketAdminRules.DescribeState(t, now);
            TicketStateView bn = TicketTransferRules.ResolveBanner(
                tpl != null && !string.IsNullOrWhiteSpace(tpl.name) ? tpl.name : t.name);
            int transferCount = logs.Count(l => TicketAdminRules.DescribeLogEntry(l).Cls == "transfer");

            return Ok(new ApiResult<object>()
            {
                code = 0,
                message = "",
                data = new
                {
                    code = t.code,
                    name = (t.name ?? "").Trim(),
                    templateId = t.template_id,
                    templateName = tpl != null ? (tpl.name ?? "").Trim() : "",
                    memberId = t.member_id ?? 0,
                    memberName = owner != null ? (owner.real_name ?? "").Trim() : "",
                    memberGender = owner != null ? (owner.gender ?? "").Trim() : "",
                    memberPhone = ownerPhone,
                    createDateStr = t.create_date.ToString("yyyy-MM-dd HH:mm"),
                    startDateStr = FormatMinute(t.start_date),
                    expireDateStr = FormatDay(t.expire_date),
                    used = t.used,
                    usedTimeStr = t.used == 1 ? FormatMinute(t.used_time) : "",
                    shared = t.shared,
                    sharedTimeStr = FormatMinute(t.shared_time),
                    valid = t.valid,
                    isActive = t.is_active,
                    channel = (t.channel ?? "").Trim(),
                    createMemo = (t.create_memo ?? "").Trim(),
                    usageMemo = (t.memo ?? "").Trim(),
                    stateLabel = st.Label,
                    stateCls = st.Cls,
                    bannerCls = bn.Cls,
                    bannerLabel = bn.Label,
                    transferCount = transferCount,
                    logs = logItems
                }
            });
        }

        [NonAction]
        private static string ResolveName(Dictionary<string, string> nameByOpenId, string openId)
        {
            string k = (openId ?? "").Trim();
            if (k == "")
            {
                return "";
            }
            return nameByOpenId.ContainsKey(k) ? nameByOpenId[k] : "未知用户";
        }

        // ───────────────────────── 某会员名下全部券（会员详情页折叠区用）─────────────────────────
        // 不分页：生产库里会员名下券数平均 2.1 张、最多 129 张，一次拉全再由前端按
        // 未使用/已核销/已过期 切换，比每切一次筛选打一次接口体验好得多。
        //
        // 三个状态是**完备互斥**的划分（每张券恰好落在一个桶里）：
        //   已核销 = used==1；已过期 = 未核销且过期；未使用 = 其余（含分享中）
        // 与 DescribeState 的区别：那个把"分享中"单列，这里并进"未使用"——
        // 站在店员视角，分享中的券依然是"还没用掉"。
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> GetMemberCouponsByStaff(int memberId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(Util.UrlDecode(sessionKey), sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });
            }
            DateTime now = DateTime.Now;

            List<Ticket> tickets = await _db.ticket.AsNoTracking()
                .Where(t => t.member_id == memberId)
                .OrderByDescending(t => t.create_date).ThenBy(t => t.code)
                .ToListAsync();

            // 转赠次数一次批量捞（口径同列表页）
            List<string> codes = tickets.Select(t => t.code).ToList();
            Dictionary<string, int> transferCount = new Dictionary<string, int>();
            if (codes.Count > 0)
            {
                var rows = await _db.ticketLog.Where(TicketTransferRules.TransferLogFilter())
                    .Where(l => codes.Contains(l.code))
                    .GroupBy(l => l.code).Select(g => new { code = g.Key, n = g.Count() })
                    .AsNoTracking().ToListAsync();
                transferCount = rows.ToDictionary(x => x.code, x => x.n);
            }

            List<int> templateIds = tickets.Select(t => t.template_id).Distinct().ToList();
            var templates = await _db.ticketTemplate.Where(x => templateIds.Contains(x.id))
                .Select(x => new { x.id, x.name }).AsNoTracking().ToListAsync();

            var items = tickets.Select(t =>
            {
                var tpl = templates.FirstOrDefault(x => x.id == t.template_id);
                bool expired = !TicketTransferRules.IsNotExpired(t, now);
                string bucket = t.used == 1 ? "used" : (expired ? "expired" : "unused");
                TicketStateView bn = TicketTransferRules.ResolveBanner(
                    tpl != null && !string.IsNullOrWhiteSpace(tpl.name) ? tpl.name : t.name);
                return new
                {
                    bannerCls = bn.Cls,
                    bannerLabel = bn.Label,
                    code = t.code,
                    name = (t.name ?? "").Trim(),
                    templateName = tpl != null ? (tpl.name ?? "").Trim() : "",
                    bucket = bucket,
                    stateLabel = bucket == "used" ? "已核销" : (bucket == "expired" ? "已过期"
                        : (t.shared == 1 ? "分享中" : "未使用")),
                    stateCls = bucket == "used" ? "used" : (bucket == "expired" ? "expired"
                        : (t.shared == 1 ? "shared" : "unused")),
                    createDateStr = t.create_date.ToString("yyyy-MM-dd"),
                    expireDateStr = FormatDay(t.expire_date),
                    usedTimeStr = t.used == 1 ? FormatMinute(t.used_time) : "",
                    transferCount = transferCount.ContainsKey(t.code) ? transferCount[t.code] : 0,
                    valid = t.valid,
                    isActive = t.is_active,
                    createMemo = (t.create_memo ?? "").Trim()
                };
            }).ToList();

            return Ok(new ApiResult<object>()
            {
                code = 0,
                message = "",
                data = new
                {
                    items,
                    total = items.Count,
                    unusedCount = items.Count(x => x.bucket == "unused"),
                    usedCount = items.Count(x => x.bucket == "used"),
                    expiredCount = items.Count(x => x.bucket == "expired")
                }
            });
        }

        // ───────────────────────── 模板下拉 ─────────────────────────
        // 不复用 MemberAdmin/GetCouponTemplates（门槛 200，店员会拿到"没有权限"），
        // 也不复用 Ticket/GetTemplateList（零鉴权 + 返回整实体带导航属性）。
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> GetTemplateOptions(string sessionKey,
            string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(Util.UrlDecode(sessionKey), sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });
            }
            var templates = await _db.ticketTemplate.Where(t => t.hide == 0)
                .OrderBy(t => t.id)
                .Select(t => new { id = t.id, name = t.name, type = t.type })
                .AsNoTracking().ToListAsync();
            return Ok(new ApiResult<object>() { code = 0, message = "", data = new { templates } });
        }

        // 时间一律服务端格式化成字符串下发：iOS 上 new Date('2026-08-16 10:30:00') 是 Invalid Date
        [NonAction]
        private static string FormatMinute(DateTime? d)
        {
            return d == null ? "" : ((DateTime)d).ToString("yyyy-MM-dd HH:mm");
        }

        [NonAction]
        private static string FormatDay(DateTime? d)
        {
            if (d == null)
            {
                return "长期有效";
            }
            DateTime v = (DateTime)d;
            // GenerateTicketByAction 对"模板没设到期日"写的是 DateTime.MaxValue
            return v.Year >= 9999 ? "长期有效" : v.ToString("yyyy-MM-dd");
        }
    }
}
