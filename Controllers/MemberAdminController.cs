using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Users;

namespace SnowmeetApi.Controllers
{
    // 会员管理（店员/店长侧）。鉴权统一 staff.title_level >= 200。
    // 储值：生产只有「服务储值」一种（= C 类），depositType 入参留 A/B/C 接口、v1 仅 C 落地。
    // 系统标签 = 参与业务（派生，不入库）；自定义标签 = member_tag 表。
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class MemberAdminController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _http;

        public MemberAdminController(ApplicationDBContext db, IConfiguration config, IHttpContextAccessor http)
        {
            _db = db;
            _config = config;
            _http = http;
        }

        // 业务标签集合（系统标签 = 会员参与过的业务类型）
        static readonly string[] BIZ_TAGS = { "租赁", "养护", "零售", "雪票", "二手回收", "水吧餐厅" };
        const int MIN_LEVEL = 200; // 店长/管理员
        const int ADMIN_LEVEL = 300; // 系统管理员（会员合并等高危操作）

        private async Task<Staff?> GetStaff(string sessionKey, string sessionType)
        {
            return await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
        }

        // ───────────────────────── 1. 列表搜索（分页） ─────────────────────────
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> SearchMembersByStaff(string sessionKey,
            string? name = null, string? cell = null, string? gender = null, string? bizTypes = null,
            string? tags = null, int pageIndex = 1, int pageSize = 20, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(sessionKey, sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });

            if (pageIndex < 1) pageIndex = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 20;

            var q = _db.member.Where(m => m.valid == 1);
            if (!string.IsNullOrWhiteSpace(name))
            {
                string nm = name.Trim();
                q = q.Where(m => m.real_name.IndexOf(nm) >= 0);
            }
            if (!string.IsNullOrWhiteSpace(gender))
            {
                string g = gender.Trim();
                q = q.Where(m => m.gender == g);
            }
            if (!string.IsNullOrWhiteSpace(cell))
            {
                // 只按主手机号（type=cell）匹配；contact 是开单/合并时的联系方式快照，不代表会员本人，不参与搜索
                string cl = cell.Trim();
                q = q.Where(m => _db.memberSocialAccount.Any(a =>
                    a.member_id == m.id && a.valid == 1 && a.type == "cell" && a.num.Contains(cl)));
            }
            // 参与业务多选：需同时参与所选全部业务（AND，与自定义标签一致）
            List<string> bizList = string.IsNullOrWhiteSpace(bizTypes)
                ? new List<string>()
                : bizTypes.Split(',').Select(t => t.Trim()).Where(t => t != "").ToList();
            foreach (string btRaw in bizList)
            {
                string bt = btRaw;
                q = q.Where(m => _db.order.Any(o => o.member_id == m.id && o.valid == 1 && o.type == bt));
            }
            List<string> tagList = string.IsNullOrWhiteSpace(tags)
                ? new List<string>()
                : tags.Split(',').Select(t => t.Trim()).Where(t => t != "").ToList();
            foreach (string tgRaw in tagList)
            {
                string tg = tgRaw;
                q = q.Where(m => _db.memberTag.Any(x => x.member_id == m.id && x.valid && x.tag == tg));
            }

            int total = await q.CountAsync();
            List<Member> pageMembers = await q.OrderByDescending(m => m.id)
                .Skip((pageIndex - 1) * pageSize).Take(pageSize).AsNoTracking().ToListAsync();

            List<int> ids = pageMembers.Select(m => m.id).ToList();
            List<int?> idsN = ids.Select(i => (int?)i).ToList();

            // 只对当前页会员批量派生
            var cells = await _db.memberSocialAccount
                .Where(a => ids.Contains(a.member_id) && a.valid == 1 && a.type == "cell")
                .Select(a => new { a.member_id, a.num }).AsNoTracking().ToListAsync();
            var deposits = await _db.depositAccount
                .Where(a => ids.Contains(a.member_id) && a.valid == 1)
                .Select(a => new { a.member_id, a.income_amount, a.consume_amount }).AsNoTracking().ToListAsync();
            var pts = await _db.point
                .Where(p => ids.Contains(p.member_id) && p.valid == 1)
                .Select(p => new { p.member_id, p.points }).AsNoTracking().ToListAsync();
            var ctags = await _db.memberTag
                .Where(t => ids.Contains(t.member_id) && t.valid)
                .Select(t => new { t.member_id, t.tag }).AsNoTracking().ToListAsync();
            var bizRows = await _db.order
                .Where(o => idsN.Contains(o.member_id) && o.valid == 1 && BIZ_TAGS.Contains(o.type))
                .Select(o => new { o.member_id, o.type }).Distinct().AsNoTracking().ToListAsync();

            var items = pageMembers.Select(m => new
            {
                id = m.id,
                name = m.real_name,
                gender = m.gender,
                phone = cells.Where(c => c.member_id == m.id).Select(c => c.num).FirstOrDefault() ?? "",
                deposit = Math.Round(deposits.Where(d => d.member_id == m.id).Sum(d => d.income_amount - d.consume_amount), 2),
                points = pts.Where(p => p.member_id == m.id).Sum(p => p.points),
                sys = bizRows.Where(b => b.member_id == m.id).Select(b => b.type).ToList(),
                custom = ctags.Where(t => t.member_id == m.id).Select(t => t.tag).ToList()
            }).ToList();

            return Ok(new ApiResult<object>() { code = 0, message = "", data = new { items, total } });
        }

        // ───────────────────────── 2. 会员详情 ─────────────────────────
        [HttpGet("{memberId}")]
        public async Task<ActionResult<ApiResult<object>>> GetMemberDetailByStaff(int memberId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(sessionKey, sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });

            Member m = await _db.member.Where(x => x.id == memberId).AsNoTracking().FirstOrDefaultAsync();
            if (m == null)
                return Ok(new ApiResult<object>() { code = 1, message = "会员不存在", data = null });

            var msaAll = await _db.memberSocialAccount
                .Where(a => a.member_id == memberId && a.valid == 1)
                .Select(a => new { a.type, a.num }).AsNoTracking().ToListAsync();
            string cell = msaAll.Where(a => a.type == "cell").Select(a => a.num).FirstOrDefault() ?? "";

            var depAccts = await _db.depositAccount
                .Where(a => a.member_id == memberId && a.valid == 1)
                .Select(a => new { a.type, a.income_amount, a.consume_amount }).AsNoTracking().ToListAsync();
            double depositTotal = Math.Round(depAccts.Sum(a => a.income_amount - a.consume_amount), 2);
            // 按 type 分组（预留 A/B/C；目前仅「服务储值」= C）
            var depositByType = depAccts.GroupBy(a => a.type)
                .Select(g => new { type = g.Key, available = Math.Round(g.Sum(a => a.income_amount - a.consume_amount), 2) }).ToList();

            int points = await _db.point.Where(p => p.member_id == memberId && p.valid == 1).SumAsync(p => (int?)p.points) ?? 0;

            List<string> sysTags = await _db.order
                .Where(o => o.member_id == memberId && o.valid == 1 && BIZ_TAGS.Contains(o.type))
                .Select(o => o.type).Distinct().AsNoTracking().ToListAsync();
            List<string> customTags = await _db.memberTag
                .Where(t => t.member_id == memberId && t.valid).Select(t => t.tag).AsNoTracking().ToListAsync();

            // 绑定账户（只读展示）；contactCells = 合并会员时保留的联系手机号
            var accounts = new
            {
                wechatOpenId = msaAll.Where(a => a.type == "wechat_mini_openid").Select(a => a.num).FirstOrDefault(),
                wechatUnionId = msaAll.Where(a => a.type == "wechat_unionid").Select(a => a.num).FirstOrDefault(),
                alipayPayerId = msaAll.Where(a => a.type == "alipay_payerid").Select(a => a.num).FirstOrDefault(),
                cell = cell,
                contactCells = msaAll.Where(a => a.type == "contact").Select(a => a.num).ToList()
            };

            // 最近订单（轻量直查，避免 GetCommonOrders 重 include）
            var recentOrders = await _db.order
                .Where(o => o.member_id == memberId && o.valid == 1)
                .OrderByDescending(o => o.id).Take(30)
                .Select(o => new { id = o.id, code = o.code, type = o.type, bizDate = o.biz_date })
                .AsNoTracking().ToListAsync();

            // 名下次卡
            var punchCards = await _db.punchCard
                .Where(c => c.member_id == memberId)
                .Select(c => new { c.id, c.biz_type, c.card_name, c.total, c.punches }).AsNoTracking().ToListAsync();

            var data = new
            {
                id = m.id,
                name = m.real_name,
                gender = m.gender,
                phone = cell,
                source = m.source,
                following_wechat = m.following_wechat,
                depositTotal = depositTotal,
                depositByType = depositByType,
                points = points,
                sys = sysTags,
                custom = customTags,
                accounts = accounts,
                recentOrders = recentOrders,
                punchCards = punchCards
            };
            return Ok(new ApiResult<object>() { code = 0, message = "", data = data });
        }

        // ───────────────────────── 2b. 修改会员资料（姓名/性别/手机号，全程留 core_data_mod_log）─────────────────────────
        public class UpdateProfileRequest
        {
            public int memberId { get; set; }
            public string? realName { get; set; }
            public string? gender { get; set; }
            public string? cell { get; set; }
        }

        [HttpPost]
        public async Task<ActionResult<ApiResult<object>>> UpdateMemberProfile([FromBody] UpdateProfileRequest req,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(sessionKey, sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });
            if (req == null)
                return Ok(new ApiResult<object>() { code = 1, message = "参数为空", data = null });

            MemberController memberHelper = new MemberController(_db, _config);
            Member ori = await memberHelper.GetWholeMemberById(req.memberId);
            if (ori == null)
                return Ok(new ApiResult<object>() { code = 1, message = "会员不存在", data = null });

            // 1) 手机号改绑（先做；被他人占用则整单不改）—— 复用 Unbind/Bind，两者各写 core_data_mod_log
            string newCell = (req.cell ?? "").Trim();
            string curCell = (ori.cell ?? "").Trim();
            if (newCell != "" && newCell != curCell)
            {
                bool takenByOther = await _db.memberSocialAccount.AnyAsync(m =>
                    m.valid == 1 && m.type == "cell" && m.num == newCell && m.member_id != req.memberId);
                if (takenByOther)
                    return Ok(new ApiResult<object>() { code = 1, message = "手机号已被他人绑定", data = null });
                if (curCell != "")
                    await memberHelper.UnbindMemberMainCellNum(req.memberId, curCell, "会员详情修改手机号", staff);
                MemberSocialAccount msa = await memberHelper.BindMemberMainCellNum(req.memberId, newCell, "会员详情修改手机号", staff);
                if (msa == null)
                    return Ok(new ApiResult<object>() { code = 1, message = "手机号绑定失败", data = null });
            }

            // 2) 姓名/性别（复用 UpdateMemberInfo 内置的 real_name/gender 差异日志）
            Member m = new Member()
            {
                id = req.memberId,
                real_name = req.realName != null ? req.realName.Trim() : (ori.real_name ?? ""),
                gender = req.gender != null ? req.gender.Trim() : (ori.gender ?? ""),
                currentContactNum = null,
                memberSocialAccounts = new List<MemberSocialAccount>()
            };
            await memberHelper.UpdateMemberInfo(m, staff, "会员详情修改资料");

            return Ok(new ApiResult<object>() { code = 0, message = "", data = new { id = req.memberId } });
        }

        // ───────────────────────── 3. 标签维护 ─────────────────────────
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> AddMemberTag(int memberId, string tag,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(sessionKey, sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });
            tag = (tag ?? "").Trim();
            if (tag == "")
                return Ok(new ApiResult<object>() { code = 1, message = "标签为空", data = null });
            bool exists = await _db.memberTag.AnyAsync(t => t.member_id == memberId && t.valid && t.tag == tag);
            if (!exists)
            {
                MemberTag mt = new MemberTag()
                {
                    id = 0,
                    member_id = memberId,
                    tag = tag,
                    staff_id = staff.id,
                    valid = true,
                    create_date = DateTime.Now
                };
                await _db.memberTag.AddAsync(mt);
                await _db.SaveChangesAsync();
            }
            List<string> customTags = await _db.memberTag
                .Where(t => t.member_id == memberId && t.valid).Select(t => t.tag).AsNoTracking().ToListAsync();
            return Ok(new ApiResult<object>() { code = 0, message = "", data = new { custom = customTags } });
        }

        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> RemoveMemberTag(int memberId, string tag,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(sessionKey, sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });
            tag = (tag ?? "").Trim();
            List<MemberTag> rows = await _db.memberTag
                .Where(t => t.member_id == memberId && t.valid && t.tag == tag).ToListAsync();
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].valid = false;
                _db.memberTag.Entry(rows[i]).State = EntityState.Modified;
            }
            if (rows.Count > 0) await _db.SaveChangesAsync();
            List<string> customTags = await _db.memberTag
                .Where(t => t.member_id == memberId && t.valid).Select(t => t.tag).AsNoTracking().ToListAsync();
            return Ok(new ApiResult<object>() { code = 0, message = "", data = new { custom = customTags } });
        }

        // 标签库（可后台维护的预设标签字典）
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> GetTagLibrary(string sessionKey,
            string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(sessionKey, sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });
            var tags = await _db.memberTagPreset
                .Where(t => t.valid)
                .OrderBy(t => t.sort).ThenBy(t => t.id)
                .Select(t => new { tag = t.tag, group = t.group_name }).AsNoTracking().ToListAsync();
            return Ok(new ApiResult<object>() { code = 0, message = "", data = new { tags } });
        }

        // ───────────────────────── 3b. 标签库维护（合并/删除/新增，只管 member_tag_preset）─────────────────────────
        // 库标签 + 每个被多少会员使用（用量>0 不允许删除，只能先合并）
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> GetTagLibraryWithStats(string sessionKey,
            string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(sessionKey, sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });
            var presets = await _db.memberTagPreset.Where(t => t.valid)
                .OrderBy(t => t.sort).ThenBy(t => t.id)
                .Select(t => new { t.tag, t.group_name }).AsNoTracking().ToListAsync();
            // 每个 tag 被多少会员使用（member_tag valid，去重 member_id）
            var usage = await _db.memberTag.Where(t => t.valid)
                .GroupBy(t => t.tag)
                .Select(g => new { tag = g.Key, cnt = g.Select(x => x.member_id).Distinct().Count() })
                .AsNoTracking().ToListAsync();
            var tags = presets.Select(p => new
            {
                tag = p.tag,
                group = p.group_name,
                count = usage.Where(u => u.tag == p.tag).Select(u => u.cnt).FirstOrDefault()
            }).ToList();
            return Ok(new ApiResult<object>() { code = 0, message = "", data = new { tags } });
        }

        // 合并：把标签 from 迁到 to —— 会员身上的 from 改成 to（已有 to 则去重），from 从标签库移除
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> MergeTagPreset(string from, string to,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(sessionKey, sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });
            from = (from ?? "").Trim();
            to = (to ?? "").Trim();
            if (from == "" || to == "" || from == to)
                return Ok(new ApiResult<object>() { code = 1, message = "合并参数无效", data = null });

            // 已经拥有 to 的会员集合（valid）
            var haveTo = await _db.memberTag.Where(t => t.valid && t.tag == to)
                .Select(t => t.member_id).Distinct().ToListAsync();
            // 会员身上所有 valid 的 from 标签行
            List<MemberTag> fromRows = await _db.memberTag.Where(t => t.valid && t.tag == from).ToListAsync();
            var seen = new HashSet<int>(haveTo);
            for (int i = 0; i < fromRows.Count; i++)
            {
                MemberTag r = fromRows[i];
                if (seen.Contains(r.member_id))
                {
                    r.valid = false;           // 该会员已有 to，去重掉 from
                }
                else
                {
                    r.tag = to;                // 迁移到 to
                    seen.Add(r.member_id);
                }
                _db.memberTag.Entry(r).State = EntityState.Modified;
            }
            // from 从标签库移除；确保 to 在库里（不在则加）
            List<MemberTagPreset> fromPresets = await _db.memberTagPreset.Where(p => p.valid && p.tag == from).ToListAsync();
            for (int i = 0; i < fromPresets.Count; i++)
            {
                fromPresets[i].valid = false;
                _db.memberTagPreset.Entry(fromPresets[i]).State = EntityState.Modified;
            }
            bool toExists = await _db.memberTagPreset.AnyAsync(p => p.valid && p.tag == to);
            if (!toExists)
            {
                await _db.memberTagPreset.AddAsync(new MemberTagPreset() { id = 0, tag = to, sort = 0, valid = true, create_date = DateTime.Now });
            }
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<object>() { code = 0, message = "", data = new { moved = fromRows.Count } });
        }

        // 删除库标签：仅当没有会员在用（member_tag valid 用量=0）才允许
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> DeleteTagPreset(string tag,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(sessionKey, sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });
            tag = (tag ?? "").Trim();
            if (tag == "")
                return Ok(new ApiResult<object>() { code = 1, message = "标签为空", data = null });
            int used = await _db.memberTag.Where(t => t.valid && t.tag == tag).Select(t => t.member_id).Distinct().CountAsync();
            if (used > 0)
                return Ok(new ApiResult<object>() { code = 1, message = "已被 " + used + " 个会员使用，不能删除（请先合并）", data = null });
            List<MemberTagPreset> rows = await _db.memberTagPreset.Where(p => p.valid && p.tag == tag).ToListAsync();
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].valid = false;
                _db.memberTagPreset.Entry(rows[i]).State = EntityState.Modified;
            }
            if (rows.Count > 0) await _db.SaveChangesAsync();
            return Ok(new ApiResult<object>() { code = 0, message = "", data = new { deleted = rows.Count } });
        }

        // 新增库标签
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> AddTagPreset(string tag, string sessionKey,
            string? groupName = null, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(sessionKey, sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });
            tag = (tag ?? "").Trim();
            if (tag == "")
                return Ok(new ApiResult<object>() { code = 1, message = "标签为空", data = null });
            bool exists = await _db.memberTagPreset.AnyAsync(p => p.valid && p.tag == tag);
            if (!exists)
            {
                await _db.memberTagPreset.AddAsync(new MemberTagPreset()
                {
                    id = 0, tag = tag, group_name = string.IsNullOrWhiteSpace(groupName) ? null : groupName.Trim(),
                    sort = 999, valid = true, create_date = DateTime.Now
                });
                await _db.SaveChangesAsync();
            }
            return Ok(new ApiResult<object>() { code = 0, message = "", data = new { tag } });
        }

        // ───────────────────────── 4. 手机号注册会员 ─────────────────────────
        public class RegisterRequest
        {
            public string cell { get; set; }
            public string? realName { get; set; }
            public string? gender { get; set; }
        }

        [HttpPost]
        public async Task<ActionResult<ApiResult<object>>> RegisterMemberByPhone([FromBody] RegisterRequest req,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(sessionKey, sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });
            string cell = (req?.cell ?? "").Trim();
            if (cell.Length < 6)
                return Ok(new ApiResult<object>() { code = 1, message = "手机号无效", data = null });

            MemberController memberHelper = new MemberController(_db, _config);
            Member existing = await memberHelper.GetWholeMemberByNum(cell, "cell");
            if (existing != null)
            {
                return Ok(new ApiResult<object>()
                {
                    code = 0,
                    message = "",
                    data = new { exists = true, id = existing.id, name = existing.real_name, gender = existing.gender, phone = cell }
                });
            }

            Member member = new Member()
            {
                id = 0,
                real_name = (req.realName ?? "").Trim(),
                gender = (req.gender ?? "").Trim(),
                source = "店员注册",
                valid = 1,
                following_wechat = 0
            };
            await _db.member.AddAsync(member);
            await _db.SaveChangesAsync();

            MemberSocialAccount msa = await memberHelper.BindMemberMainCellNum(member.id, cell, "店员注册", staff);
            if (msa == null)
                return Ok(new ApiResult<object>() { code = 1, message = "手机号已被其它会员占用", data = null });

            return Ok(new ApiResult<object>()
            {
                code = 0,
                message = "",
                data = new { exists = false, id = member.id, name = member.real_name, gender = member.gender, phone = cell }
            });
        }

        // ───────────────────────── 5. 充值储值（depositType 留 A/B/C 接口，v1 仅 C=服务储值） ─────────────────────────
        public class ChargeRequest
        {
            public int memberId { get; set; }
            public string depositType { get; set; } = "C";
            public double amount { get; set; }
        }

        [HttpPost]
        public async Task<ActionResult<ApiResult<object>>> ChargeMemberDeposit([FromBody] ChargeRequest req,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(sessionKey, sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });
            if (req == null || req.amount <= 0)
                return Ok(new ApiResult<object>() { code = 1, message = "金额无效", data = null });
            bool memberExists = await _db.member.AnyAsync(m => m.id == req.memberId && m.valid == 1);
            if (!memberExists)
                return Ok(new ApiResult<object>() { code = 1, message = "会员不存在", data = null });

            // v1：A/B/C 三类暂只有 C（= 服务储值）。depositType 已入参，未来扩展 type/sub_type 映射。
            DepositController depositHelper = new DepositController(_db, _config);
            await depositHelper.DepositCharge(req.memberId, 0, Math.Round(req.amount, 2),
                DateTime.Now.AddYears(50), sessionKey, sessionType,
                "服务储值", "", "", "会员管理充值", "店员充值");

            double depositTotal = await _db.depositAccount
                .Where(a => a.member_id == req.memberId && a.valid == 1)
                .SumAsync(a => (double?)(a.income_amount - a.consume_amount)) ?? 0;
            return Ok(new ApiResult<object>()
            {
                code = 0, message = "", data = new { depositTotal = Math.Round(depositTotal, 2) }
            });
        }

        // ───────────────────────── 6. 发放次卡（预置卡种） ─────────────────────────
        public class PunchCardRequest
        {
            public int memberId { get; set; }
            public string bizType { get; set; }   // 租赁 / 养护
            public string cardName { get; set; }
            public int total { get; set; }
        }

        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> GetPunchCardPresets(string sessionKey,
            string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(sessionKey, sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });
            var presets = await _db.punchCard
                .Select(c => new { c.biz_type, c.card_name, c.total }).Distinct()
                .OrderBy(c => c.biz_type).ThenBy(c => c.card_name).AsNoTracking().ToListAsync();
            return Ok(new ApiResult<object>() { code = 0, message = "", data = new { presets } });
        }

        [HttpPost]
        public async Task<ActionResult<ApiResult<object>>> GrantPunchCard([FromBody] PunchCardRequest req,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(sessionKey, sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });
            if (req == null || req.total <= 0 || string.IsNullOrWhiteSpace(req.cardName) || string.IsNullOrWhiteSpace(req.bizType))
                return Ok(new ApiResult<object>() { code = 1, message = "参数无效", data = null });
            bool memberExists = await _db.member.AnyAsync(m => m.id == req.memberId && m.valid == 1);
            if (!memberExists)
                return Ok(new ApiResult<object>() { code = 1, message = "会员不存在", data = null });

            PunchCard card = new PunchCard()
            {
                id = 0,
                biz_type = req.bizType.Trim(),
                card_name = req.cardName.Trim(),
                member_id = req.memberId,
                total = req.total,
                punches = 0,
                create_date = DateTime.Now
            };
            await _db.punchCard.AddAsync(card);
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<object>() { code = 0, message = "", data = new { id = card.id } });
        }

        // ───────────────────────── 7. 发券（券模板，直绑该会员） ─────────────────────────
        public class CouponRequest
        {
            public int memberId { get; set; }
            public int templateId { get; set; }
            public int count { get; set; } = 1;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> GetCouponTemplates(string sessionKey,
            string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(sessionKey, sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });
            // TicketTemplate 模型只映射了 id/type/name/memo/hide/expire_date 子集（biz_type/currency_value 在 DB 有列、模型未映射，v1 不取）
            var templates = await _db.ticketTemplate
                .Where(t => t.hide == 0)
                .Select(t => new { id = t.id, name = t.name, type = t.type, memo = t.memo })
                .AsNoTracking().ToListAsync();
            return Ok(new ApiResult<object>() { code = 0, message = "", data = new { templates } });
        }

        [HttpPost]
        public async Task<ActionResult<ApiResult<object>>> GrantCoupon([FromBody] CouponRequest req,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(sessionKey, sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });
            if (req == null || req.count <= 0 || req.count > 50)
                return Ok(new ApiResult<object>() { code = 1, message = "数量无效", data = null });
            Member member = await _db.member.Where(m => m.id == req.memberId && m.valid == 1)
                .Include(m => m.memberSocialAccounts.Where(a => a.valid == 1)).AsNoTracking().FirstOrDefaultAsync();
            if (member == null)
                return Ok(new ApiResult<object>() { code = 1, message = "会员不存在", data = null });
            TicketTemplate tpl = await _db.ticketTemplate.FindAsync(req.templateId);
            if (tpl == null)
                return Ok(new ApiResult<object>() { code = 1, message = "券模板不存在", data = null });

            string openId = member.wechatMiniOpenId ?? "";
            DateTime now = DateTime.Now;
            DateTime expire = (tpl.expire_date != null && tpl.expire_date != DateTime.MaxValue)
                ? (DateTime)tpl.expire_date : now.AddDays(30);

            int granted = 0;
            for (int i = 0; i < req.count; i++)
            {
                string code = Util.GetRandomCode(9);
                int retry = 0;
                while (await _db.card.AnyAsync(c => c.card_no == code) && retry < 1000)
                {
                    code = Util.GetRandomCode(9);
                    retry++;
                }
                if (retry >= 1000) continue;
                Card card = new Card() { card_no = code, is_ticket = 1, type = "" };
                await _db.card.AddAsync(card);
                await _db.SaveChangesAsync();

                Ticket ticket = new Ticket()
                {
                    code = code,
                    template_id = tpl.id,
                    name = (tpl.name ?? "").Trim(),
                    memo = (tpl.memo ?? "").Trim(),
                    member_id = req.memberId,
                    open_id = openId,
                    start_date = now,
                    expire_date = expire,
                    used = 0,
                    is_active = 1,
                    valid = 1,
                    shared = 0,
                    printed = 0,
                    channel = "店员发放",
                    staff_id = staff.id,
                    miniapp_recept_path = (tpl.miniapp_recept_path ?? "").Trim(),
                    create_date = now
                };
                await _db.ticket.AddAsync(ticket);
                await _db.SaveChangesAsync();
                granted++;
            }
            return Ok(new ApiResult<object>() { code = 0, message = "", data = new { granted } });
        }

        // ───────────────────────── 8. 会员合并（当前会员 → 目标会员） ─────────────────────────
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> MergeMemberByStaff(int sourceMemberId, int targetMemberId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(sessionKey, sessionType);
            if (staff == null || staff.title_level < ADMIN_LEVEL)
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });
            if (sourceMemberId == targetMemberId)
                return Ok(new ApiResult<object>() { code = 1, message = "不能合并到自己", data = null });
            Member source = await _db.member.AsNoTracking().FirstOrDefaultAsync(m => m.id == sourceMemberId);
            Member target = await _db.member.AsNoTracking().FirstOrDefaultAsync(m => m.id == targetMemberId);
            if (source == null || target == null)
                return Ok(new ApiResult<object>() { code = 1, message = "会员不存在", data = null });
            if (source.is_merge == 1)
                return Ok(new ApiResult<object>() { code = 1, message = "该会员已被合并过", data = null });
            if (target.is_merge == 1)
                return Ok(new ApiResult<object>() { code = 1, message = "目标会员已被合并，不能作为合并目标", data = null });

            MemberController memberHelper = new MemberController(_db, _config);
            await memberHelper.MergeMember(sourceMemberId, targetMemberId);
            return Ok(new ApiResult<object>() { code = 0, message = "", data = new { sourceMemberId, targetMemberId } });
        }
    }
}
