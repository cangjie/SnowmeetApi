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
    // 优惠券**模板**维护（后台）。鉴权 staff.title_level >= 200。
    //
    // 门槛比 TicketAdminController（100，看券）高一档是有意的：改模板等于改发放规则和定价规则——
    // biz_type 决定这类券在开单时选不选得到，available_days 决定券多久过期，
    // product_ticket_template 直接进养护服务费的算式。这不是店员日常操作。
    //
    // 全局 QueryTrackingBehavior 是 NoTracking，所以本文件里凡是要改的实体，
    // 要么查出来后显式 Entry(x).State = Modified，要么用 Add。
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class TicketTemplateAdminController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;

        public TicketTemplateAdminController(ApplicationDBContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        const int MIN_LEVEL = 200;

        private async Task<Staff?> GetStaff(string sessionKey, string sessionType)
        {
            return await Util.GetStaffBySessionKey(_db, Util.UrlDecode(sessionKey), sessionType);
        }

        private ApiResult<object> Deny()
        {
            return new ApiResult<object>() { code = 1, message = "没有权限", data = null };
        }

        // ── 列表 ────────────────────────────────────────────────────────────

        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> GetTemplateListByStaff(string sessionKey,
            string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(sessionKey, sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
            {
                return Ok(Deny());
            }

            List<TicketTemplate> templates = await _db.ticketTemplate.AsNoTracking()
                .OrderBy(t => t.hide).ThenByDescending(t => t.id).ToListAsync();
            List<int> ids = templates.Select(t => t.id).ToList();

            // 三个计数一次性按模板分组捞回来，别在循环里逐个查
            DateTime yearAgo = DateTime.Now.Date.AddYears(-1);
            var ticketStats = await _db.ticket.Where(t => ids.Contains(t.template_id))
                .GroupBy(t => t.template_id)
                .Select(g => new
                {
                    templateId = g.Key,
                    total = g.Count(),
                    recent = g.Count(x => x.create_date >= yearAgo)
                }).AsNoTracking().ToListAsync();
            var ruleStats = await _db.productTicketTemplate
                .Where(p => ids.Contains(p.ticket_template_id) && p.valid)
                .GroupBy(p => p.ticket_template_id)
                .Select(g => new { templateId = g.Key, n = g.Count() })
                .AsNoTracking().ToListAsync();

            Dictionary<int, int> totalById = ticketStats.ToDictionary(x => x.templateId, x => x.total);
            Dictionary<int, int> recentById = ticketStats.ToDictionary(x => x.templateId, x => x.recent);
            Dictionary<int, int> rulesById = ruleStats.ToDictionary(x => x.templateId, x => x.n);

            var items = templates.Select(t => new
            {
                id = t.id,
                name = t.name,
                type = t.type,
                bizType = t.biz_type,
                bizTypeText = string.IsNullOrWhiteSpace(t.biz_type) ? "未设业务类型" : t.biz_type,
                bizTypeMissing = string.IsNullOrWhiteSpace(t.biz_type),
                // WXML 不支持方法调用，展示文案一律服务端派生
                validityText = TicketTemplateRules.DescribeValidity(t),
                // 两个有效期字段同时非空 = 存量遗留的冲突态，列表上要能一眼挑出来去修
                hasConflict = t.available_days != null && t.expire_date != null,
                hide = t.hide,
                valid = t.valid,
                ticketTotal = totalById.ContainsKey(t.id) ? totalById[t.id] : 0,
                ticketRecent = recentById.ContainsKey(t.id) ? recentById[t.id] : 0,
                ruleCount = rulesById.ContainsKey(t.id) ? rulesById[t.id] : 0
            }).ToList();

            return Ok(new ApiResult<object>()
            {
                code = 0,
                message = "",
                data = new { items = items, total = items.Count }
            });
        }

        // ── 详情 ────────────────────────────────────────────────────────────

        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> GetTemplateDetailByStaff(int id,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(sessionKey, sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
            {
                return Ok(Deny());
            }

            TicketTemplate t = await _db.ticketTemplate.AsNoTracking().FirstOrDefaultAsync(x => x.id == id);
            if (t == null)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "模板不存在", data = null });
            }

            // 先取规则、再批量取商品，在内存里拼。
            // 不用 join：那是 INNER JOIN，规则指向的商品要是被删了，这条规则会从维护页上
            // 静默消失——恰恰是最需要被看见、被修掉的那一条。
            List<ProductTicketTemplate> rules = await _db.productTicketTemplate
                .Where(p => p.ticket_template_id == id && p.valid)
                .OrderBy(p => p.product_id).AsNoTracking().ToListAsync();
            List<int> productIds = rules.Select(r => r.product_id).Distinct().ToList();
            Dictionary<int, Product> productById = (await _db.product
                .Where(p => productIds.Contains(p.id)).AsNoTracking().ToListAsync())
                .ToDictionary(p => p.id, p => p);

            var ruleItems = rules.Select(r =>
            {
                Product pr = productById.ContainsKey(r.product_id) ? productById[r.product_id] : null;
                string productName = r.product_id == 0 ? "（全部商品）"
                    : (pr != null ? pr.name : "⚠ 商品已删除（id " + r.product_id + "）");
                return new
                {
                    id = r.id,
                    productId = r.product_id,
                    productName = productName,
                    productShop = pr != null ? pr.shop : "",
                    salePrice = pr != null ? pr.sale_price : 0d,
                    fixedPrice = r.fixed_price,
                    discountRate = r.discount_rate,
                    discountAmount = r.discount_amount,
                    // 三选一的当前档位，前端 radio 直接用，不在 WXML 里判空
                    mode = r.fixed_price != null ? "fixed"
                        : (r.discount_rate != null ? "rate"
                        : (r.discount_amount != null ? "amount" : "none")),
                    discountText = DescribeRule(r.fixed_price, r.discount_rate, r.discount_amount)
                };
            }).ToList();

            return Ok(new ApiResult<object>()
            {
                code = 0,
                message = "",
                data = new
                {
                    id = t.id,
                    name = t.name,
                    type = t.type,
                    memo = t.memo,
                    bizType = t.biz_type,
                    availableDays = t.available_days,
                    // 时间一律服务端格式化成字符串下发：iOS 解析 'yyyy-MM-dd HH:mm:ss' 会得到 Invalid Date
                    expireDate = t.expire_date == null ? null
                        : ((DateTime)t.expire_date).ToString("yyyy-MM-dd"),
                    validityMode = t.expire_date != null ? "date"
                        : (t.available_days != null ? "days" : "forever"),
                    validityText = TicketTemplateRules.DescribeValidity(t),
                    miniappReceptPath = t.miniapp_recept_path,
                    hide = t.hide,
                    valid = t.valid,
                    experience = t.experience,
                    needPoints = t.need_points,
                    currencyValue = t.currency_value,
                    rules = ruleItems,
                    bizTypeOptions = TicketTemplateRules.BizTypeOptions
                }
            });
        }

        [NonAction]
        private static string DescribeRule(double? fixedPrice, double? rate, double? amount)
        {
            if (fixedPrice != null)
            {
                return fixedPrice == 0 ? "免费" : ("一口价 " + fixedPrice + " 元");
            }
            if (rate != null)
            {
                return (rate * 10) + " 折";
            }
            if (amount != null)
            {
                return "立减 " + amount + " 元";
            }
            return "无优惠";
        }

        // ── 保存模板 ────────────────────────────────────────────────────────

        public class SaveTemplateRequest
        {
            public int id { get; set; }              // 0 = 新建
            public string? name { get; set; }
            public string? type { get; set; }
            public string? memo { get; set; }
            public string? bizType { get; set; }     // 空/空串 = 不参与开单，存 NULL
            /// <summary>"days" | "date" | "forever"，前端 radio 的档位，服务端据此清空另一个字段。</summary>
            public string? validityMode { get; set; }
            public int? availableDays { get; set; }
            public DateTime? expireDate { get; set; }
            public string? miniappReceptPath { get; set; }
            public int hide { get; set; } = 0;
            public int valid { get; set; } = 1;
        }

        [HttpPost]
        public async Task<ActionResult<ApiResult<object>>> SaveTemplateByStaff(
            [FromBody] SaveTemplateRequest req, string sessionKey,
            string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(sessionKey, sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
            {
                return Ok(Deny());
            }
            if (req == null)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "参数为空", data = null });
            }

            // 档位说了算：选了"按天数"就清掉截止日，反之亦然。互斥在这里就落地，
            // 不指望前端每次都记得把另一个字段置空。
            int? availableDays = req.availableDays;
            DateTime? expireDate = req.expireDate;
            switch ((req.validityMode ?? "").Trim())
            {
                case "days":
                    expireDate = null;
                    break;
                case "date":
                    availableDays = null;
                    break;
                case "forever":
                    availableDays = null;
                    expireDate = null;
                    break;
            }

            bool isNew = req.id <= 0;
            TicketTemplate t = isNew ? new TicketTemplate()
                : await _db.ticketTemplate.FirstOrDefaultAsync(x => x.id == req.id);
            if (t == null)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "模板不存在", data = null });
            }

            t.name = (req.name ?? "").Trim();
            t.type = (req.type ?? "").Trim();
            // memo / miniapp_recept_path 在 DB 里是 NOT NULL，null 会在 SaveChanges 时炸
            t.memo = (req.memo ?? "").Trim();
            t.miniapp_recept_path = (req.miniappReceptPath ?? "").Trim();
            t.biz_type = string.IsNullOrWhiteSpace(req.bizType) ? null : req.bizType.Trim();
            t.available_days = availableDays;
            t.expire_date = expireDate;
            t.hide = req.hide;
            t.valid = req.valid;

            List<string> errors = TicketTemplateRules.ValidateTemplate(t);
            if (errors.Count > 0)
            {
                return Ok(new ApiResult<object>()
                {
                    code = 1, message = string.Join("；", errors), data = null
                });
            }

            if (isNew)
            {
                await _db.ticketTemplate.AddAsync(t);
            }
            else
            {
                _db.Entry(t).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();

            return Ok(new ApiResult<object>()
            {
                code = 0, message = "", data = new { id = t.id }
            });
        }

        // ── 保存商品优惠规则 ────────────────────────────────────────────────

        public class SaveRuleRequest
        {
            public int id { get; set; }              // 0 = 新建
            public int templateId { get; set; }
            public int productId { get; set; }       // 0 = 全部商品（通配兜底）
            /// <summary>"fixed" | "rate" | "amount" | "none"</summary>
            public string? mode { get; set; }
            public double? value { get; set; }
        }

        [HttpPost]
        public async Task<ActionResult<ApiResult<object>>> SaveProductRuleByStaff(
            [FromBody] SaveRuleRequest req, string sessionKey,
            string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(sessionKey, sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
            {
                return Ok(Deny());
            }
            if (req == null || req.templateId <= 0)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "参数为空", data = null });
            }

            string mode = (req.mode ?? "").Trim();
            if (mode != "none" && req.value == null)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "请填写优惠数值", data = null });
            }
            if (mode == "rate" && (req.value <= 0 || req.value >= 1))
            {
                // 与 ResolveProductDiscount 的越界处理同口径：那边遇到越界值按不打折处理，
                // 与其让人存进去之后发现没生效，不如这里就拒掉
                return Ok(new ApiResult<object>()
                {
                    code = 1, message = "折扣比例要在 0 和 1 之间（0.8 = 8 折）；免费请用一口价 0", data = null
                });
            }
            if ((mode == "fixed" || mode == "amount") && req.value < 0)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "金额不能为负", data = null });
            }

            bool isNew = req.id <= 0;
            ProductTicketTemplate rule = isNew
                ? new ProductTicketTemplate()
                {
                    ticket_template_id = req.templateId,
                    product_id = req.productId,
                    valid = true,
                    create_date = DateTime.Now
                }
                : await _db.productTicketTemplate.FirstOrDefaultAsync(x => x.id == req.id);
            if (rule == null)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "规则不存在", data = null });
            }

            // 三个字段永远只留一个有值——ResolveProductDiscount 有固定优先级，
            // 留着旧字段会让人以为改了档位却看不到效果
            rule.product_id = req.productId;
            rule.fixed_price = mode == "fixed" ? req.value : null;
            rule.discount_rate = mode == "rate" ? req.value : null;
            rule.discount_amount = mode == "amount" ? req.value : null;
            rule.update_date = DateTime.Now;

            if (isNew)
            {
                await _db.productTicketTemplate.AddAsync(rule);
            }
            else
            {
                _db.Entry(rule).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();

            return Ok(new ApiResult<object>() { code = 0, message = "", data = new { id = rule.id } });
        }

        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> DeleteProductRuleByStaff(int id,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(sessionKey, sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
            {
                return Ok(Deny());
            }
            ProductTicketTemplate rule = await _db.productTicketTemplate
                .FirstOrDefaultAsync(x => x.id == id);
            if (rule == null)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "规则不存在", data = null });
            }
            // 软删：MatchProductRule 会跳过 valid=false 的行。硬删会让历史订单失去定价依据
            rule.valid = false;
            rule.update_date = DateTime.Now;
            _db.Entry(rule).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<object>() { code = 0, message = "", data = new { id = id } });
        }

        // ── 商品选择器 ──────────────────────────────────────────────────────

        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> GetProductOptionsByStaff(string? keyword,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(sessionKey, sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
            {
                return Ok(Deny());
            }
            // 雪票有 596 条且按场次/日期铺开，进这个选择器只会淹没真正要配的服务类商品
            IQueryable<Product> q = _db.product.AsNoTracking()
                .Where(p => p.valid == 1 && p.type != "雪票");
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                string k = Util.UrlDecode(keyword).Trim();
                q = q.Where(p => p.name.Contains(k));
            }
            var rows = await q.OrderBy(p => p.shop).ThenBy(p => p.name).Take(200)
                .Select(p => new { p.id, p.name, p.shop, p.type, p.sale_price })
                .ToListAsync();
            // label 在内存里拼：double 拼进字符串 EF 翻不成 SQL，放在 Select 里会运行时炸
            var items = rows.Select(p => new
            {
                id = p.id,
                name = p.name,
                shop = p.shop,
                type = p.type,
                salePrice = p.sale_price,
                label = p.name + "（" + p.shop + " ¥" + p.sale_price + "）"
            }).ToList();

            return Ok(new ApiResult<object>()
            {
                code = 0, message = "", data = new { items = items, total = items.Count }
            });
        }
    }
}
