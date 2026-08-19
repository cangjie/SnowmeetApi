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
    // 养护价格维护（后台）。鉴权 staff.title_level >= 300（系统管理员）。
    //
    // 维护 product.category_id = 14 这批商品——它们就是养护定价的价目表：
    // CareController.CalcCharge 按门店 + 服务组合（CarePricingRules）从里面取 sale_price。
    // 改这里 = 改线上收费，门槛比优惠券模板设置（200）还高一档。
    //
    // 不复用 Category/ModProduct（门槛 100，且收整个 Product 实体连带 images/properties，
    // 一不小心就把没打算改的列一起覆盖了）——这里只开放养护真正需要维护的几个字段。
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class CareProductAdminController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;

        public CareProductAdminController(ApplicationDBContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        const int MIN_LEVEL = 300;

        private async Task<Staff?> GetStaff(string sessionKey, string sessionType)
        {
            return await Util.GetStaffBySessionKey(_db, Util.UrlDecode(sessionKey), sessionType);
        }

        private ApiResult<object> Deny()
        {
            return new ApiResult<object>() { code = 1, message = "没有权限", data = null };
        }

        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> GetCareProductsByStaff(int? shopId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(sessionKey, sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
            {
                return Ok(Deny());
            }

            // 已停用的（valid = 0）一律不列：它们不参与计价，摆出来只会干扰
            IQueryable<Product> q = _db.product.AsNoTracking()
                .Where(p => p.category_id == CareProductRules.CareCategoryId && p.valid == 1);
            if (shopId != null && shopId > 0)
            {
                q = q.Where(p => p.shop_id == shopId);
            }
            List<Product> rows = await q.OrderBy(p => p.shop_id).ThenBy(p => p.sort)
                .ThenBy(p => p.id).ToListAsync();

            Dictionary<int, string> shopById = (await _db.shop.AsNoTracking().ToListAsync())
                .ToDictionary(x => x.id, x => (x.name ?? "").Trim());

            // 被券规则引用的商品，改价会连带改掉券后价；停用更会让规则悬空。列表上要能看见。
            List<int> ids = rows.Select(p => p.id).ToList();
            var ruleRows = await _db.productTicketTemplate.AsNoTracking()
                .Where(t => ids.Contains(t.product_id) && t.valid)
                .Select(t => new { t.product_id, t.ticket_template_id }).ToListAsync();
            Dictionary<int, int> ruleCount = ruleRows.GroupBy(t => t.product_id)
                .ToDictionary(g => g.Key, g => g.Count());

            var items = rows.Select(p =>
            {
                CareProductStateView st = CareProductRules.DescribeState(p);
                return new
                {
                    id = p.id,
                    name = p.name,
                    shopId = p.shop_id,
                    shopName = p.shop_id != null && shopById.ContainsKey((int)p.shop_id)
                        ? shopById[(int)p.shop_id] : (p.shop ?? "").Trim(),
                    salePrice = p.sale_price,
                    valid = p.valid,
                    hidden = p.hidden,
                    sort = p.sort,
                    stateLabel = st.Label,
                    stateCls = st.Cls,
                    ruleCount = ruleCount.ContainsKey(p.id) ? ruleCount[p.id] : 0,
                    // 名字对不上「双项/单项/双项加急/单项加急」时，CarePricingRules 取不到价，
                    // 服务费会静默变成 0 —— 2026-08-19 商品改名就是这么踩的，列表上必须提示
                    pricingRecognized = CareProductRules.IsPricingRecognizedName(p.name)
                };
            }).ToList();

            return Ok(new ApiResult<object>()
            {
                code = 0,
                message = "",
                data = new { items = items, total = items.Count }
            });
        }

        public class SaveCareProductRequest
        {
            public int id { get; set; }          // 0 = 新建
            public string? name { get; set; }
            public int? shopId { get; set; }
            public double? salePrice { get; set; }
            public int hidden { get; set; } = 0;
            public int sort { get; set; } = 100;
        }

        [HttpPost]
        public async Task<ActionResult<ApiResult<object>>> SaveCareProductByStaff(
            [FromBody] SaveCareProductRequest req, string sessionKey,
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

            string error = CareProductRules.Validate(req.name, req.shopId, req.salePrice);
            if (error != null)
            {
                return Ok(new ApiResult<object>() { code = 1, message = error, data = null });
            }

            Shop shop = await _db.shop.AsNoTracking().FirstOrDefaultAsync(s => s.id == req.shopId);
            if (shop == null)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "门店不存在", data = null });
            }

            bool isNew = req.id <= 0;
            Product p = isNew ? null
                : await _db.product.FirstOrDefaultAsync(x => x.id == req.id);
            if (!isNew && p == null)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "商品不存在", data = null });
            }
            if (!isNew && p.category_id != CareProductRules.CareCategoryId)
            {
                // 防止拿别的分类的商品 id 从这个入口改价
                return Ok(new ApiResult<object>() { code = 1, message = "该商品不属于养护分类", data = null });
            }
            if (isNew)
            {
                p = new Product()
                {
                    category_id = CareProductRules.CareCategoryId,
                    type = "服务",
                    valid = 1,
                    on_shelves = 0,   // 养护服务不在顾客端商城直接售卖，与现有同类商品一致
                    create_date = DateTime.Now
                };
            }

            p.name = req.name.Trim();
            p.shop_id = req.shopId;
            // shop 文本列与 shop_list 保持一致：历史上 137~140 存"万龙"、715 存"万龙服务中心"，
            // 同一个店两种写法，是旧的双向子串匹配留下的烂摊子，新数据不再制造这种分歧
            p.shop = shop.name.Trim();
            p.sale_price = (double)req.salePrice;
            p.hidden = req.hidden;
            p.sort = req.sort;
            p.update_date = DateTime.Now;

            if (isNew)
            {
                await _db.product.AddAsync(p);
            }
            else
            {
                _db.Entry(p).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();

            return Ok(new ApiResult<object>() { code = 0, message = "", data = new { id = p.id } });
        }

        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> DeleteCareProductByStaff(int id,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await GetStaff(sessionKey, sessionType);
            if (staff == null || staff.title_level < MIN_LEVEL)
            {
                return Ok(Deny());
            }
            Product p = await _db.product.FirstOrDefaultAsync(x => x.id == id);
            if (p == null || p.category_id != CareProductRules.CareCategoryId)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "商品不存在", data = null });
            }
            // 软删：历史订单还挂着这个 product_id，硬删会让它们失去定价依据
            p.valid = 0;
            p.update_date = DateTime.Now;
            _db.Entry(p).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<object>() { code = 0, message = "", data = new { id = id } });
        }
    }
}
