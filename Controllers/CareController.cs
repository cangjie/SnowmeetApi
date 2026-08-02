using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aop.Api.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
namespace SnowmeetApi.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class CareController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _http;
        public CareController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = context;
            _config = config;
            _http = httpContextAccessor;
        }
        [NonAction]
        public async Task<Care> UpdateCare(Care care, int? memberId, int? staffId, string scene)
        {
            Care oriCare = await _db.care.Where(c => c.id == care.id)
                .Include(c => c.careImages).AsNoTracking().FirstOrDefaultAsync();
            List<CoreDataModLog> logs = Util.GetUpdateDifferenceLog<Care>(oriCare, care, memberId, staffId, scene);
            foreach (CoreDataModLog log in logs)
            {
                await _db.coreDataModLog.AddAsync(log);
            }
            for (int i = 0; oriCare.careImages != null && i < oriCare.careImages.Count; i++)
            {
                CareImage oriImage = oriCare.careImages[i];
                if (care.careImages.Where(c => c.id == oriImage.id).ToList().Count <= 0)
                {
                    _db.careImage.Entry(oriImage).State = EntityState.Deleted;
                }
            }
            try
            {
                //care.order = null;
                care.tasks = null;
                care.update_date = DateTime.Now;
                _db.care.Update(care);
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                care.update_date = DateTime.Now;
                _db.care.Entry(care).State = EntityState.Modified;
                await _db.SaveChangesAsync();
            }
            care.tasks = await _db.careTask.Where(c => c.care_id == care.id && c.valid == 1).OrderBy(t => t.id)
                .Include(t => t.staff).AsNoTracking().ToListAsync();
            return care;
        }
        [HttpGet]
        public async Task<Care> GetCare(int id)
        {
            Care c = await _db.care.FindAsync(id);
            if (c == null)
            {
                return null;
            }
            OrderController _orderHelper = new OrderController(_db, _config, _http);
            if (c.order_id != null)
            {
                c.order = await _orderHelper.GetOrder((int)c.order_id);
            }
            else
            {
                c.order = null;
            }
            c.tasks = await _db.careTask
                .Include(t => t.staff)
                .Include(t => t.terminateStaff)
                .Where(t => t.care_id == c.id).OrderBy(t => t.id).ToListAsync();
            if (c.order.member != null)
            {
                await _db.member.Entry(c.order.member).Collection(m => m.memberSocialAccounts).LoadAsync();
            }
            return c;
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<List<Brand>?>>> UpdateBrandByStaff(string type, string brandName,
            string chineseName, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<Brand?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            await UpdateBrand(type, brandName + "/" + chineseName, staff.id);
            return await GetBrands(type.Trim());
        }
        [NonAction]
        public async Task<Brand> UpdateBrand(string type, string dispayedName, int? staffId)
        {
            if (dispayedName.IndexOf("/") <= 0 || dispayedName.StartsWith("/") || dispayedName.EndsWith("/")
                || dispayedName.IndexOf("未知") >= 0)
            {
                return null;
            }
            string[] dispayedNameArr = dispayedName.Split('/');
            string name = dispayedNameArr[0];
            string chineseName = dispayedNameArr[1];
            List<Brand> brands = await _db.brand.Where(b => b.brand_type.Trim().Equals(type.Trim()) && b.brand_name.Trim().Equals(name.Trim()))
                .AsNoTracking().ToListAsync();
            if (brands.Count > 0)
            {
                return brands[0];
            }
            Brand brand = new Brand()
            {
                brand_type = type,
                brand_name = name,
                chinese_name = chineseName.Trim(),
                staff_id = staffId
            };
            await _db.brand.AddAsync(brand);
            await _db.SaveChangesAsync();
            return brand;
        }
        [NonAction]
        public async Task<Series> UpdateSeries(Brand brand, string seriesName, int? staffId)
        {
            seriesName = seriesName.Trim();
            if (seriesName.Equals("") || seriesName.IndexOf("未知") >= 0)
            {
                return null;
            }
            List<Series> sl = await _db.series.Where(s => s.type.Trim().Equals(brand.brand_type.Trim())
                && s.brand_name.Trim().Equals(brand.brand_name.Trim()) && s.serial_name.Trim().Equals(seriesName))
                .AsNoTracking().ToListAsync();
            if (sl.Count > 0)
            {
                return sl[0];
            }
            Series s = new Series()
            {
                brand_name = brand.brand_name.Trim(),
                type = brand.brand_type.Trim(),
                serial_name = seriesName,
                staff_id = staffId
            };
            await _db.series.AddAsync(s);
            await _db.SaveChangesAsync();
            return s;
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<List<Brand>>>> GetBrands(string type)
        {
            List<Brand> brands = await _db.brand.Where(b => b.brand_type.Trim().Equals(type.Trim()))
                .OrderBy(b => b.brand_name).AsNoTracking().ToListAsync();
            return Ok(new ApiResult<List<Brand>>()
            {
                code = 0,
                message = "",
                data = brands
            });
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<List<Series>>>> GetSeries(string brand, string type)
        {
            brand = Util.UrlDecode(brand).Trim();
            List<Series> series = await _db.series.Where(s => (s.brand_name.Trim().Equals(brand) && s.type.Trim().Equals(type.Trim())))
                .AsNoTracking().ToListAsync();
            return Ok(new ApiResult<List<Series>>()
            {
                code = 0,
                message = "",
                data = series
            });
        }
        [HttpGet("{careId}")]
        public async Task<ActionResult<ApiResult<Care?>>> GetCareByStaff(int careId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff == null)
            {
                return Ok(new ApiResult<Care?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            Care care = await GetCare(careId);
            if (care == null)
            {
                return Ok(new ApiResult<Care?>()
                {
                    code = 1,
                    message = "未找到",
                    data = null
                });
            }
            else
            {
                return Ok(new ApiResult<Care?>()
                {
                    code = 0,
                    message = "",
                    data = care
                });
            }
        }
        [HttpPost]
        public async Task<ActionResult<ApiResult<Care>>> UpdateCareByStaff([FromBody] Care care, [FromQuery] string scene,
            [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<Care?>
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            scene = Util.UrlDecode(scene);
            care = await UpdateCare(care, null, staff.id, scene);
            Brand brand = await UpdateBrand(care.equipment, care.brand, staff.id);
            if (brand != null && care.series != null)
            {
                await UpdateSeries(brand, care.series, staff.id);
            }
            return Ok(new ApiResult<Care>()
            {
                code = 0,
                message = "",
                data = care
            });
        }
        [HttpGet]
        public ActionResult<ApiResult<List<string>>> GetOthersService(string type)
        {
            type = Util.UrlDecode(type).Trim();
            switch (type)
            {
                case "双板":
                    return Ok(new ApiResult<List<string>>()
                    {
                        code = 0,
                        message = "",
                        data = Enum.GetNames(typeof(Care.SkiService)).ToList()
                    });
                case "单板":
                    return Ok(new ApiResult<List<string>>()
                    {
                        code = 0,
                        message = "",
                        data = Enum.GetNames(typeof(Care.BoardService)).ToList()
                    });
                default:
                    return Ok(new ApiResult<List<string>>()
                    {
                        code = 1,
                        message = "未知的器材类型",
                        data = new List<string>()
                    });
            }
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<List<Models.Product>?>>> GetCareProducts(int shopId)
        {
            DateTime currentDate = DateTime.Now.Date;
            List<Models.Product> products = await _db.product
                .Include(p => p.productTicketTemplate)
                .Where(p => (p.category_id == 14 || p.category_id == 15) && ((int)p.shop_id) == shopId
                && p.valid == 1 && (p.end_date == null || ((DateTime)p.end_date).Date >= currentDate)
                )
                .AsNoTracking().ToListAsync();
            return Ok(new ApiResult<List<Models.Product>?>()
            {
                code = 0,
                message = "",
                data = products
            });
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<List<Models.Product>?>>> GetProducts(string shop)
        {
            /*
            List<Models.Product> products = await _db.product
                .Where(p => (p.id == 137 || p.id == 138 || p.id == 139 || p.id == 140 || p.id == 142 || p.id == 143 || p.id == 202)
                && p.valid == 1).OrderBy(p => p.sale_price).AsNoTracking().ToListAsync();
            */
            List<Models.Product> products = await _db.product.Where(p => (p.shop.IndexOf(shop) >= 0 || shop.IndexOf(p.shop) >= 0) && p.category_id == 14 && p.valid == 1)
                .OrderBy(p => p.sale_price).AsNoTracking().ToListAsync();
            return Ok(new ApiResult<List<Models.Product>?>()
            {
                code = 0,
                message = "",
                data = products
            });
        }
        [NonAction]
        public async Task<Models.Product?> GetProduct(string shop, Care care)
        {
            if (care.summer != null)
            {
                return new Models.Product()
                {
                    id = 0,
                    sale_price = 330,
                    valid = 1
                };
            }
            List<Models.Product> products = ((ApiResult<List<Models.Product>>)((OkObjectResult)(await GetProducts(shop)).Result).Value).data;
            Models.Product product = null;
            for (int i = 0; i < products.Count; i++)
            {
                if (products[i].name == "非雪季养护" && care.biz_type == "非雪季养护")
                {
                    product = products[i];
                    break;
                }
                if (shop.IndexOf("万龙") >= 0)
                {
                    if (products[i].name.IndexOf("修刃打蜡") >= 0 && products[i].name.IndexOf("立等") >= 0
                    && care.need_edge == 1 && care.need_wax == 1 && care.urgent == 1)
                    {
                        product = products[i];
                        break;
                    }
                    else if (products[i].name.IndexOf("修刃") >= 0 && products[i].name.IndexOf("立等") >= 0
                    && care.need_edge == 1 && care.need_wax == 0 && care.urgent == 1 && products[i].name.IndexOf("修刃打蜡") < 0)
                    {
                        product = products[i];
                        break;
                    }
                    else if (products[i].name.IndexOf("打蜡") >= 0 && products[i].name.IndexOf("立等") >= 0
                    && care.need_edge == 0 && care.need_wax == 1 && care.urgent == 1 && products[i].name.IndexOf("修刃打蜡") < 0)
                    {
                        product = products[i];
                        break;
                    }
                    else if (products[i].name.IndexOf("修刃打蜡") >= 0 && products[i].name.IndexOf("次日") >= 0
                    && care.need_edge == 1 && care.need_wax == 1 && care.urgent == 0)
                    {
                        product = products[i];
                        break;
                    }
                    else if (products[i].name.IndexOf("修刃") >= 0 && products[i].name.IndexOf("次日") >= 0
                    && care.need_edge == 1 && care.need_wax == 0 && care.urgent == 0 && products[i].name.IndexOf("修刃打蜡") < 0)
                    {
                        product = products[i];
                        break;
                    }
                    else if (products[i].name.IndexOf("打蜡") >= 0 && products[i].name.IndexOf("次日") >= 0
                    && care.need_edge == 0 && care.need_wax == 1 && care.urgent == 0 && products[i].name.IndexOf("修刃打蜡") < 0)
                    {
                        product = products[i];
                        break;
                    }
                }
                else
                {
                    if (products[i].name.IndexOf("修刃打蜡") >= 0
                    && care.need_edge == 1 && care.need_wax == 1)
                    {
                        product = products[i];
                        break;
                    }
                    else if (products[i].name.IndexOf("修刃") >= 0
                    && care.need_edge == 1 && care.need_wax == 0 && products[i].name.IndexOf("修刃打蜡") < 0)
                    {
                        product = products[i];
                        break;
                    }
                    else if (products[i].name.IndexOf("打蜡") >= 0
                    && care.need_edge == 0 && care.need_wax == 1 && products[i].name.IndexOf("修刃打蜡") < 0)
                    {
                        product = products[i];
                        break;
                    }
                }
            }
            return product;
        }
        // 养护服务费定价（真理之源，CalcCareCharge 开单实时计费与 PlaceCareOrder 下单落库共用）：
        // 选会员卡按 0（2026-07-09 用户拍板，核销链路另做）/ 质保招待 0 / summer 330（GetProduct 内置）
        // / 其余 GetProduct 名称匹配 sale_price + 票券 fixed_price 覆盖；券16 减免 双项30/单项20。
        // 例外：机打蜡季卡（卡名含「机打蜡」）升级热蜡/增加修刃的加价规则与免费打蜡券（券12模板）一致
        [NonAction]
        public async Task<(double commonCharge, double ticketDiscount)> CalcCharge(string shop, Care care, Ticket? ticket, PunchCard? card = null)
        {
            if (care.use_card || care.warranty || care.entertain)
            {
                // 机打蜡季卡：默认机打蜡免费（无匹配产品 → 0）；升级热蜡/加修刃按当前服务匹配产品，
                // 再用券12模板的 fixed_price 覆盖——与使用免费打蜡券完全同一套加价规则
                if (care.use_card && !care.warranty && !care.entertain
                    && card != null && (card.card_name ?? "").IndexOf("机打蜡") >= 0)
                {
                    Models.Product upProduct = await GetProduct(shop, care);
                    if (upProduct == null)
                    {
                        return (0, 0);
                    }
                    double upCharge = upProduct.sale_price;
                    TicketTemplate tpl12 = await _db.ticketTemplate.Where(t => t.id == 12)
                        .Include(t => t.productTicketTemplates).ThenInclude(p => p.product)
                        .AsNoTracking().FirstOrDefaultAsync();
                    if (tpl12 != null && tpl12.productTicketTemplates != null)
                    {
                        ProductTicketTemplate productTicketTemplate = tpl12.productTicketTemplates
                            .Where(p => p.product_id == upProduct.id || p.product_id == 0).FirstOrDefault();
                        if (productTicketTemplate != null && productTicketTemplate.fixed_price != null)
                        {
                            upCharge = (double)productTicketTemplate.fixed_price;
                        }
                    }
                    return (upCharge, 0);
                }
                return (0, 0);
            }
            Models.Product product = await GetProduct(shop, care);
            double commonCharge = product == null ? 0 : product.sale_price;
            double ticketDiscount = 0;
            if (ticket != null)
            {
                if (product != null && ticket.template != null && ticket.template.productTicketTemplates != null)
                {
                    ProductTicketTemplate productTicketTemplate = ticket.template.productTicketTemplates
                        .Where(p => p.product_id == product.id || p.product_id == 0).FirstOrDefault();
                    if (productTicketTemplate != null && productTicketTemplate.fixed_price != null)
                    {
                        commonCharge = (double)productTicketTemplate.fixed_price;
                    }
                }
                if (ticket.template_id == 16)
                {
                    if (care.need_edge == 1 && care.need_wax == 1)
                    {
                        ticketDiscount = 30;
                    }
                    else if (care.need_edge == 1 || care.need_wax == 1)
                    {
                        ticketDiscount = 20;
                    }
                }
            }
            return (commonCharge, ticketDiscount);
        }
        // 季卡是否「还没开卡」：三项装备信息都为空 = 尚未绑定到具体装备，第一次用它养护时才绑。
        // 判定口径必须与前端 care_recept_form 的「即将开卡」提示一致，否则会出现
        // 「界面提示要开卡、生效时却没写」或反过来的情况。
        // serial 不参与判定（CLAUDE.md：serial 暂不参与限制，很多装备本来就没序列号）。
        [NonAction]
        public static bool IsSeasonCardUnbound(PunchCard card)
        {
            if (card == null || card.total != null)
            {
                return false;   // 只有季卡（total=NULL 不限次数）才有开卡这回事
            }
            return string.IsNullOrWhiteSpace(card.equip_type)
                && string.IsNullOrWhiteSpace(card.equip_brand)
                && string.IsNullOrWhiteSpace(card.equip_scale);
        }

        // 本次养护的装备信息是否够写进卡：类型/品牌/长度三项齐全才算。
        // 缺一项就不开卡——写进去一张残缺的绑定，这张季卡以后就再也匹配不上任何装备了
        [NonAction]
        public static bool HasFullEquipInfo(Care care)
        {
            return care != null
                && !string.IsNullOrWhiteSpace(care.equipment)
                && !string.IsNullOrWhiteSpace(care.brand)
                && !string.IsNullOrWhiteSpace(care.scale);
        }

        // 按所选券/卡推导默认服务项（换券/换卡时刻调用）：先清空服务项再套默认，
        // 与前端「更改券/卡先清空已选服务」同一口径。规则：双项卡 → 修刃+热蜡+刮蜡；
        // 卡名含「机打蜡」（如机打蜡季卡）→ 机打蜡；券12 → 机打蜡；券17/18 → 非雪季 now/later；其余不默认。
        // 「是不是双项卡」优先看 punch_card.care_project_count（商品维护页定义、发卡时复制过来），
        // 该字段为空的历史卡才回退到「卡名含双项」的老口径。
        [NonAction]
        public void ApplyDefaultServices(Care care, PunchCard card, Ticket ticket)
        {
            care.need_edge = 0;
            care.edge_degree = null;
            care.need_wax = 0;
            care.need_unwax = 0;
            care.free_wax = 0;
            care.summer = null;
            if (care.biz_type == "非雪季养护")
            {
                care.biz_type = null;
            }
            if (card != null)
            {
                string name = (card.card_name ?? "").Trim();
                bool isDoubleProject = card.care_project_count != null
                    ? card.care_project_count == 2
                    : name.IndexOf("双项") >= 0;   // 历史卡没有该字段，回退老口径
                if (isDoubleProject)
                {
                    care.need_edge = 1;
                    care.edge_degree = "89";
                    care.need_wax = 1;
                    care.need_unwax = 1;
                }
                else if (name.IndexOf("机打蜡") >= 0)
                {
                    care.free_wax = 1;
                }
            }
            else if (ticket != null)
            {
                switch (ticket.template_id)
                {
                    case 12:
                        care.free_wax = 1;
                        break;
                    case 17:
                        care.summer = "now";
                        care.biz_type = "非雪季养护";
                        break;
                    case 18:
                        care.need_edge = 1;
                        care.edge_degree = "89";
                        care.need_wax = 1;
                        care.need_unwax = 1;
                        care.summer = "later";
                        care.biz_type = "非雪季养护";
                        break;
                }
            }
        }
        // 开单页实时计费请求：每次界面操作都提交当前界面的全量状态——
        // 店铺 / 会员 / care（装备信息、服务项、券码、use_card、card_id/card_name、附加费、减免）。
        // 卡选择跟着单件装备（care）走，不在订单级，所以卡信息只在 care 内、不设平级字段。
        // changedField = 本次界面操作改动的字段名（need_wax/free_wax/need_edge/summer 等），
        // 服务联动规则按它判定（联动是事件语义：开热蜡带上刮蜡 ≠ 开着热蜡手动关刮蜡）
        public class CalcCareChargeRequest
        {
            public string shop { get; set; }
            public int? memberId { get; set; }
            public bool deriveServices { get; set; } = false;
            public string? changedField { get; set; } = null;
            public Care care { get; set; }
        }
        // 服务项联动规则（真理之源，前端不再本地联动；未来新增联动只改这里）：
        // 开/关热蜡 → 刮蜡跟随；开热蜡与机打蜡互斥；开机打蜡清热蜡/刮蜡；
        // 开修刃默认角度 89；非雪季 later→修刃+热蜡+刮蜡 / now→清三项，非雪季下取消立等
        [NonAction]
        public void ApplyServiceLinkage(Care care, string changedField)
        {
            switch (changedField)
            {
                case "need_wax":
                    care.need_unwax = care.need_wax;
                    if (care.need_wax == 1)
                    {
                        care.free_wax = 0;
                    }
                    break;
                case "free_wax":
                    if (care.free_wax == 1)
                    {
                        care.need_wax = 0;
                        care.need_unwax = 0;
                    }
                    break;
                case "need_edge":
                    if (care.need_edge == 1 && string.IsNullOrWhiteSpace(care.edge_degree))
                    {
                        care.edge_degree = "89";
                    }
                    break;
                case "summer":
                    if (care.summer == "later")
                    {
                        care.need_edge = 1;
                        care.need_wax = 1;
                        care.need_unwax = 1;
                    }
                    else if (care.summer == "now")
                    {
                        care.need_edge = 0;
                        care.need_wax = 0;
                        care.need_unwax = 0;
                    }
                    if (care.summer != null)
                    {
                        care.urgent = 0;
                        if (string.IsNullOrWhiteSpace(care.edge_degree))
                        {
                            care.edge_degree = "89";
                        }
                    }
                    break;
            }
        }
        // 开单页实时计费：POST 全量状态（见 CalcCareChargeRequest），服务端算服务费与券16减免。
        // deriveServices=true（换券/换卡时传）：先按新选择推导服务项再计价，
        // 响应 services 返回最终服务项供前端回填（如机打蜡季卡 → 机打蜡）；平时项目开关计价不传，不动服务项
        [HttpPost]
        public async Task<ActionResult<ApiResult<object>>> CalcCareCharge([FromBody] CalcCareChargeRequest req,
            string sessionKey = "", string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });
            }
            Care care = req == null ? null : req.care;
            string shop = req == null ? null : req.shop;
            if (care == null || string.IsNullOrWhiteSpace(shop))
            {
                return Ok(new ApiResult<object>() { code = 1, message = "参数为空", data = null });
            }
            int? memberId = req.memberId;
            // 卡选择跟着 care 走：卡 id 取 care.card_id（DB 实体列，随草稿持久化）
            int? cardId = care.card_id;
            Ticket ticket = null;
            if (care.ticket_code != null && care.ticket_code.Trim() != "")
            {
                ticket = await _db.ticket.Where(t => t.code == care.ticket_code && t.valid == 1 && t.used == 0)
                    .Include(t => t.template).ThenInclude(p => p.productTicketTemplates).ThenInclude(p => p.product)
                    .AsNoTracking().FirstOrDefaultAsync();
            }
            PunchCard card = null;
            if (cardId != null)
            {
                card = await _db.punchCard.Where(c => c.id == cardId).AsNoTracking().FirstOrDefaultAsync();
                if (card != null && memberId != null && card.member_id != memberId)
                {
                    card = null;   // 卡不属于该会员，忽略
                }
                if (card != null && card.is_refund)
                {
                    card = null;   // 已退款的卡钱已退回顾客，不再享受卡权益（不参与定价）
                }
            }
            if (req.deriveServices)
            {
                // 换券/换卡：按新选择推导默认服务项
                ApplyDefaultServices(care, card, ticket);
            }
            else if (!string.IsNullOrWhiteSpace(req.changedField))
            {
                // 普通界面操作：按本次改动字段应用服务联动
                ApplyServiceLinkage(care, req.changedField.Trim());
            }
            var (commonCharge, ticketDiscount) = await CalcCharge(shop.Trim(), care, ticket, card);
            care.common_charge = commonCharge;
            if (ticketDiscount > 0 && care.discount < ticketDiscount)
            {
                // 券16 减免取「保底」不取「覆盖」（与 PlaceCareOrder 同口径）：选券自动带出券面减免，
                // 店员之后手动加大的减免（如老板临时再让利）保留，不被重算碾回券面值；低于券面才补齐
                care.discount = ticketDiscount;
            }
            // 返回整个 care：联动/推导后的服务项 + 计费结果都在其中，前端以此为真理之源回填
            return Ok(new ApiResult<object>()
            {
                code = 0,
                message = "",
                data = new { commonCharge, ticketDiscount, care }
            });
        }
        [HttpGet]
        public async Task EffectCareOrder(int orderId)
        {
            Models.Order order = await _db.order.Where(o => o.id == orderId)
                .Include(o => o.cares).AsNoTracking().FirstOrDefaultAsync();
            if (order == null)
            {
                return;
            }
            List<Care>? todayCareList = null;
            for (int i = 0; order.cares != null && i < order.cares.Count; i++)
            {
                Care care = order.cares[i];
                string? taskFlowCode = null;
                if (todayCareList == null)
                {
                    todayCareList = await _db.care.Include(c => c.order)
                        .Where(c => c.id < care.id && c.valid == 1 && c.order.valid == 1
                        && c.task_flow_code != null && c.order.biz_date.Date == DateTime.Now.Date
                        && c.order.shop == order.shop).AsNoTracking().ToListAsync();
                }
                string[] orderCodeArr = order.code.Split('_');
                taskFlowCode = orderCodeArr[0] + '-' + orderCodeArr[2] + '-' + (todayCareList.Count + i + 1).ToString().PadLeft(3, '0');
                _db.care.Update(care);
                care.task_flow_code = taskFlowCode;
                CareTask taskSafe = new CareTask()
                {
                    id = 0,
                    care_id = care.id,
                    task_name = "安全检查",
                    sort = 10,
                    create_date = DateTime.Now
                };
                await _db.careTask.AddAsync(taskSafe);
                if (care.biz_type == "非雪季养护")
                {
                    TicketController _tHelper = new TicketController(_db, _config);
                    if (care.summer != null)
                    {
                        Ticket ticketSummer = await _tHelper.GenerateTicketByAction(17, (int)care.order.member_id, 1, care.order.id, "非雪季养护", "");
                        Ticket ticketSummerDouble = await _tHelper.GenerateTicketByAction(18, (int)care.order.member_id, 1, care.order.id, "非雪季养护", "");
                        if (care.summer == "now")
                        {
                            ticketSummer.used = 1;
                            ticketSummer.used_time = DateTime.Now;
                            ticketSummerDouble.used = 0;
                            ticketSummerDouble.used_time = null;
                            CareTask taskEdge = new CareTask()
                            {
                                id = 0,
                                care_id = care.id,
                                task_name = "修刃",
                                memo = care.edge_degree == null ? "89" : care.edge_degree.ToString(),
                                sort = 20,
                                create_date = DateTime.Now
                            };
                            await _db.careTask.AddAsync(taskEdge);
                            CareTask taskWax = new CareTask()
                            {
                                id = 0,
                                care_id = care.id,
                                task_name = "热蜡",
                                memo = "",
                                sort = 40,
                                create_date = DateTime.Now
                            };
                            await _db.careTask.AddAsync(taskWax);
                            CareTask taskSummer = new CareTask()
                            {
                                id = 0,
                                care_id = care.id,
                                task_name = "寄存或快递",
                                memo = "",
                                sort = 50,
                                create_date = DateTime.Now
                            };
                            await _db.careTask.AddAsync(taskSummer);
                            CareTask taskUnWax = new CareTask()
                            {
                                id = 0,
                                care_id = care.id,
                                task_name = "刮蜡",
                                memo = "",
                                sort = 60,
                                create_date = DateTime.Now
                            };
                            await _db.careTask.AddAsync(taskUnWax);
                            CareTask taskFinish = new CareTask()
                            {
                                id = 0,
                                care_id = care.id,
                                task_name = "发板",
                                sort = 60,
                                create_date = DateTime.Now
                            };
                            await _db.careTask.AddAsync(taskFinish);

                        }
                        else if (care.summer == "later")
                        {
                            ticketSummer.used = 0;
                            ticketSummer.used_time = null;
                            ticketSummerDouble.used = 1;
                            ticketSummerDouble.used_time = DateTime.Now;

                            CareTask taskEdge = new CareTask()
                            {
                                id = 0,
                                care_id = care.id,
                                task_name = "修刃",
                                memo = care.edge_degree == null ? "89" : care.edge_degree.ToString(),
                                sort = 20,
                                create_date = DateTime.Now
                            };
                            await _db.careTask.AddAsync(taskEdge);
                            CareTask taskWax = new CareTask()
                            {
                                id = 0,
                                care_id = care.id,
                                task_name = "热蜡",
                                memo = "",
                                sort = 30,
                                create_date = DateTime.Now
                            };
                            await _db.careTask.AddAsync(taskWax);
                            CareTask taskUnWax = new CareTask()
                            {
                                id = 0,
                                care_id = care.id,
                                task_name = "刮蜡",
                                memo = "",
                                sort = 40,
                                create_date = DateTime.Now
                            };
                            await _db.careTask.AddAsync(taskUnWax);
                            CareTask taskFinish = new CareTask()
                            {
                                id = 0,
                                care_id = care.id,
                                task_name = "发板",
                                sort = 50,
                                create_date = DateTime.Now
                            };
                            await _db.careTask.AddAsync(taskFinish);
                        }
                        ticketSummer.biz_type = "养护";
                        ticketSummer.biz_id = care.id;
                        ticketSummer.member_id = care.order.member_id;
                        ticketSummer.valid = 1;
                        ticketSummerDouble.biz_type = "养护";
                        ticketSummerDouble.biz_id = care.id;
                        ticketSummerDouble.member_id = care.order.member_id;
                        ticketSummerDouble.valid = 1;
                        _db.ticket.Entry(ticketSummer).State = EntityState.Modified;
                        _db.ticket.Entry(ticketSummerDouble).State = EntityState.Modified;
                    }
                    else
                    {
                        Ticket ticket = await _db.ticket.Where(t => t.code == care.ticket_code).AsNoTracking().FirstOrDefaultAsync();
                        if (ticket.template_id == 17)
                        {
                            CareTask taskEdge = new CareTask()
                            {
                                id = 0,
                                care_id = care.id,
                                task_name = "修刃",
                                memo = care.edge_degree == null ? "89" : care.edge_degree.ToString(),
                                sort = 20,
                                create_date = DateTime.Now
                            };
                            await _db.careTask.AddAsync(taskEdge);
                            CareTask taskWax = new CareTask()
                            {
                                id = 0,
                                care_id = care.id,
                                task_name = "热蜡",
                                memo = "",
                                sort = 40,
                                create_date = DateTime.Now
                            };
                            await _db.careTask.AddAsync(taskWax);
                            CareTask taskSummer = new CareTask()
                            {
                                id = 0,
                                care_id = care.id,
                                task_name = "寄存或快递",
                                memo = "",
                                sort = 50,
                                create_date = DateTime.Now
                            };
                            await _db.careTask.AddAsync(taskSummer);
                            CareTask taskUnWax = new CareTask()
                            {
                                id = 0,
                                care_id = care.id,
                                task_name = "刮蜡",
                                memo = "",
                                sort = 60,
                                create_date = DateTime.Now
                            };
                            await _db.careTask.AddAsync(taskUnWax);
                            CareTask taskFinish = new CareTask()
                            {
                                id = 0,
                                care_id = care.id,
                                task_name = "发板",
                                sort = 60,
                                create_date = DateTime.Now
                            };
                            await _db.careTask.AddAsync(taskFinish);
                            ticket.used = 1;
                            ticket.used_time = DateTime.Now;
                            _db.ticket.Entry(ticket).State = EntityState.Modified;
                            await _db.SaveChangesAsync();
                            _db.ticket.Entry(ticket).State = EntityState.Detached;
                            await _db.SaveChangesAsync();
                        }
                    }

                }
                else
                {
                    if (care.need_edge == 1)
                    {
                        CareTask taskEdge = new CareTask()
                        {
                            id = 0,
                            care_id = care.id,
                            task_name = "修刃",
                            memo = care.edge_degree == null ? "89" : care.edge_degree.ToString(),
                            sort = 20,
                            create_date = DateTime.Now
                        };
                        await _db.careTask.AddAsync(taskEdge);
                    }
                    if (care.need_repair == 1)
                    {
                        CareTask taskRepair = new CareTask()
                        {
                            id = 0,
                            care_id = care.id,
                            task_name = "维修",
                            memo = care.repair_memo,
                            sort = 30,
                            create_date = DateTime.Now
                        };
                        await _db.careTask.AddAsync(taskRepair);
                    }
                    if (care.free_wax == 1)
                    {
                        CareTask taskWax = new CareTask()
                        {
                            id = 0,
                            care_id = care.id,
                            task_name = "机打蜡",
                            memo = "",
                            sort = 40,
                            create_date = DateTime.Now
                        };
                        await _db.careTask.AddAsync(taskWax);
                    }
                    if (care.need_wax == 1)
                    {
                        CareTask taskWax = new CareTask()
                        {
                            id = 0,
                            care_id = care.id,
                            task_name = "热蜡",
                            memo = "",
                            sort = 40,
                            create_date = DateTime.Now
                        };
                        await _db.careTask.AddAsync(taskWax);
                    }
                    if (care.need_unwax == 1)
                    {
                        CareTask taskUnWax = new CareTask()
                        {
                            id = 0,
                            care_id = care.id,
                            task_name = "刮蜡",
                            memo = "",
                            sort = 50,
                            create_date = DateTime.Now
                        };
                        await _db.careTask.AddAsync(taskUnWax);
                    }
                    CareTask taskFinish = new CareTask()
                    {
                        id = 0,
                        care_id = care.id,
                        task_name = "发板",
                        sort = 60,
                        create_date = DateTime.Now
                    };
                    await _db.careTask.AddAsync(taskFinish);
                }
                if (care.ticket_code != null)
                {
                    Ticket ticket = await _db.ticket.Where(t => t.code == care.ticket_code).AsNoTracking().FirstOrDefaultAsync();
                    if (ticket != null)
                    {
                        ticket.used = 1;
                        ticket.used_time = DateTime.Now;
                        ticket.biz_type = "养护";
                        ticket.biz_id = care.id;
                        _db.ticket.Entry(ticket).State = EntityState.Modified;
                    }
                }
                // 次卡核销：本单该 care 用了会员卡 → 抵 1 次 + 写 punch_card_used。
                // 季卡（total==null）不扣次数、仍记录使用（供「上次使用时间」）。幂等：已有本 care 记录则跳过，
                // 防支付回调 / 重复调用 EffectCareOrder 重复扣。镜像 RentController.UseRentalPunchCard。
                if (care.use_card && care.card_id != null)
                {
                    bool alreadyUsed = await _db.punchCardUsed.AnyAsync(u => u.card_id == care.card_id
                        && u.order_id == order.id && u.biz_id == care.id && u.biz_type == "养护" && u.valid);
                    if (!alreadyUsed)
                    {
                        PunchCard punchCard = await _db.punchCard.Where(c => c.id == care.card_id).FirstOrDefaultAsync();
                        // 不再比对 punchCard.member_id == order.member_id：卡归属在 PlaceCareOrder 定价时
                        // 已按下单会员校验（不符会清 care.card_id），而 order.member_id 会在支付成功时被
                        // DealSuccessPaidOrder 改写为付款方（同人跨通道两个会员号即触发，实录 71884：
                        // 15506 下单选卡 → 支付宝付款方 41137 → 归属改写 → 旧守卫静默跳过核销）。
                        // 定价已享卡权益的单，生效时必须落 punch_card_used，否则计次卡白嫖、季卡丢使用记录。
                        // is_refund 例外：卡已退款（钱退回顾客）不得核销——PlaceCareOrder 定价时已清掉这类
                        // 卡引用，这里是防御（万一是先下单、后退卡的时序）
                        if (punchCard != null && !punchCard.is_refund)
                        {
                            PunchCardUsed cardUsed = new PunchCardUsed()
                            {
                                card_id = punchCard.id,
                                order_id = order.id,
                                biz_type = "养护",
                                biz_id = care.id,
                                payment_id = null,
                                punch_count = 1,
                                valid = true,
                                create_date = DateTime.Now
                            };
                            await _db.punchCardUsed.AddAsync(cardUsed);
                            if (punchCard.total != null)   // 非季卡才扣次数
                            {
                                punchCard.punches = (punchCard.punches ?? 0) + 1;
                                punchCard.update_date = DateTime.Now;
                                _db.punchCard.Entry(punchCard).State = EntityState.Modified;   // 全局 NoTracking，必须显式
                            }
                            // 季卡「开卡」：季卡是绑定装备的（equip_type/brand/scale 三项全非空即已限定装备），
                            // 但发卡/售卡时并不知道顾客拿哪块板来，三项是空的。第一次真正用它养护时，
                            // 就把这次养护的装备写进卡——此后这张季卡只能给这块板用。
                            // 只在三项**都为空**时写（已开卡的不覆盖），且本次装备信息要够全，
                            // 免得写进去一张残缺的绑定把卡废掉。
                            if (IsSeasonCardUnbound(punchCard) && HasFullEquipInfo(care))
                            {
                                punchCard.equip_type = (care.equipment ?? "").Trim();
                                punchCard.equip_brand = (care.brand ?? "").Trim();
                                punchCard.equip_scale = (care.scale ?? "").Trim();
                                punchCard.equip_serial = (care.serials ?? "").Trim();
                                punchCard.update_date = DateTime.Now;
                                _db.punchCard.Entry(punchCard).State = EntityState.Modified;
                                await _db.coreDataModLog.AddAsync(CoreDataModLog.CreateManualLog(
                                    "punch_card", "equip", punchCard.id, "季卡开卡绑定装备", null, order.staff_id,
                                    null, punchCard.equip_type + " " + punchCard.equip_brand + " " + punchCard.equip_scale,
                                    "首次使用该季卡养护，按本次装备开卡"));
                            }
                            await _db.coreDataModLog.AddAsync(CoreDataModLog.CreateManualLog("care", "次卡消费",
                                care.id, "养护次卡消费", null, order.staff_id, "0", "1", "养护核销扣次卡"));
                        }
                    }
                }
            }
            _db.order.Entry(order).State = EntityState.Detached;
            await _db.SaveChangesAsync();
        }
        [HttpGet("{careId}")]
        public async Task<ActionResult<ApiResult<Models.Order>?>> SetPickImageId(int careId, int imageId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<Models.Order?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            Care care = await _db.care.Where(c => c.id == careId).FirstOrDefaultAsync();
            care.pick_image_id = imageId;
            _db.care.Entry(care).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            OrderController _orderHelper = new OrderController(_db, _config, _http);
            Models.Order order = await _orderHelper.GetOrder((int)care.order_id);
            return Ok(new ApiResult<Models.Order?>()
            {
                code = 0,
                message = "",
                data = order

            });
        }
        [HttpGet("{taskId}")]
        public async Task<ActionResult<ApiResult<Care?>>> SetTaskStatus(int taskId, string status,
            string scene, string sessionKey, string sessionType = "wechat_mini_openid",
            string? dealMethod = null, string? storeMemo = null, string? taskMemo = null,
            bool isCancel = false, string? cancelReason = null)
        {
            scene = Util.UrlDecode(scene);
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff.title_level < 100)
            {
                return Ok(new ApiResult<Care?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            CareTask careTask = await _db.careTask.Where(t => t.id == taskId && t.valid == 1).AsNoTracking().FirstOrDefaultAsync();

            CoreDataModLog log = Util.CreateCoreDataModLog("care_task", "status", taskId, careTask.status, status, null, staff.id, scene);
            careTask.status = status;
            switch (status)
            {
                case "已开始":
                    careTask.start_time = DateTime.Now;
                    careTask.staff_id = staff.id;
                    break;
                case "已完成":
                    careTask.end_time = DateTime.Now;
                    careTask.staff_id = staff.id;
                    if (careTask.task_name == "发板")
                    {
                        if (isCancel)
                        {
                            // 取消：装备未真正完成养护，跳过"养护完成赠送"券，改记 Care.is_cancel/cancel_reason
                            Care careCancel = await _db.care.Where(c => c.id == careTask.care_id).AsNoTracking().FirstOrDefaultAsync();
                            if (careCancel != null)
                            {
                                careCancel.is_cancel = true;
                                careCancel.cancel_reason = cancelReason != null ? Util.UrlDecode(cancelReason) : null;
                                careCancel.update_date = DateTime.Now;
                                _db.care.Entry(careCancel).State = EntityState.Modified;
                                CoreDataModLog cancelLog = CoreDataModLog.CreateManualLog("care", "is_cancel", careCancel.id,
                                    scene, null, staff.id, "0", "1",
                                    "取消发板" + (string.IsNullOrEmpty(careCancel.cancel_reason) ? "" : "，原因：" + careCancel.cancel_reason));
                                await _db.coreDataModLog.AddAsync(cancelLog);
                            }
                        }
                        else
                        {
                            try
                            {
                                TicketController _tHelper = new TicketController(_db, _config);
                                Care careFinish = await _db.care.Where(c => c.id == careTask.care_id).AsNoTracking().FirstOrDefaultAsync();
                                Models.Order order = await _db.order.Where(o => o.id == careFinish.order_id).AsNoTracking().FirstOrDefaultAsync();
                                await _tHelper.CreateTicket(16, order.member_id, staff.id, "养护完成赠送", "养护", careFinish.id, true, DateTime.Now, null);
                            }
                            catch
                            {

                            }
                        }
                    }
                    break;
                case "强行中止":
                    careTask.end_time = DateTime.Now;
                    careTask.terminate_staff_id = staff.id;
                    break;
                default:
                    break;
            }
            // taskMemo 显式传入（哪怕空串）时代表调用方要写真实备注，覆盖默认的 "memo=场景字符串" 行为；
            // 不传（null）时保持旧语义不变——memo 记录本次状态变更的场景，供审计追溯
            careTask.memo = taskMemo != null ? Util.UrlDecode(taskMemo) : scene;
            careTask.update_date = DateTime.Now;
            careTask.deal_method = dealMethod != null? Util.UrlDecode(dealMethod):null;
            careTask.store_memo = storeMemo != null ? Util.UrlDecode(storeMemo): null;
            await _db.coreDataModLog.AddAsync(log);
            _db.careTask.Entry(careTask).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            Care care = await GetCare(careTask.care_id);
            //非雪季养护打蜡完成，下一步应该是快递和寄存步骤的开始
            if (care.biz_type == "非雪季养护" && careTask.task_name == "热蜡" && careTask.status == "已完成")
            {
                CareTask nextTask = care.tasks.OrderBy(t => t.sort).Where(t => t.sort > careTask.sort).FirstOrDefault();
                if (nextTask != null && (nextTask.task_name.IndexOf("存") >= 0 || nextTask.task_name.IndexOf("快递") >= 0))
                {
                    nextTask.status = "已开始";
                    nextTask.start_time = DateTime.Now;
                    nextTask.staff_id = staff.id;

                    _db.careTask.Entry(nextTask).State = EntityState.Modified;
                    await _db.SaveChangesAsync();
                }
            }
            return Ok(new ApiResult<Care>()
            {
                code = 0,
                message = "",
                data = care
            });
        }
        [HttpGet("{careId}")]
        public async Task<ActionResult<ApiResult<Care?>>> CreateVerifyCode(int careId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff.title_level < 100)
            {
                return Ok(new ApiResult<Care?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            string code = new Random().Next(0, 9999).ToString().PadLeft(4, '0');
            Care care = await _db.care.Where(c => c.id == careId).FirstOrDefaultAsync();
            Models.Order order = await _db.order.Where(o => o.id == care.order_id).FirstOrDefaultAsync();
            care.veri_code = code;
            care.veri_code_time = DateTime.Now;
            care.update_date = DateTime.Now;
            _db.care.Add(care).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            string content = order.code + "|品牌：" + care.equipment + care.brand + "长度：" + care.scale + "|" + code;
            try
            {
                string notUrl = "https://wxoa.snowmeet.top/api/TemlateMessage/SendTemplateMessage?memberId=" + order.member_id.ToString()
                    + "&templateId=" + Util.UrlEncode("-FxfVcWYFq079YIWfaT6khxQn6__b-CD9Xty_M_iP1U") + "&first=" + Util.UrlEncode("") + "&keywords=" + Util.UrlEncode(content)
                    + "&remark=" + Util.UrlDecode("") + "&url=" + Util.UrlEncode("") + "&sessionKey=" + Util.UrlEncode("abcd123!@#");
                Util.GetWebContent(notUrl);
            }
            catch
            {

            }
            return Ok(new ApiResult<Care?>()
            {
                code = 0,
                message = "",
                data = care
            });
        }
        [HttpGet("{careId}")]
        public async Task<ActionResult<ApiResult<Care?>>> VeriCareFinishCode(int careId,
            string code, string sessionKey, string sessionType = "wechat_mini_openid",
            bool isCancel = false, string? cancelReason = null)
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff.title_level < 100)
            {
                return Ok(new ApiResult<Care?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            Care care = await _db.care.Where(c => c.id == careId).Include(c => c.tasks).AsNoTracking().FirstOrDefaultAsync();
            if (care.veri_code != code || care.veri_code_time == null || ((DateTime)care.veri_code_time).AddMinutes(60) < DateTime.Now)
            {
                return Ok(new ApiResult<Care?>()
                {
                    code = 1,
                    message = "验证码错误或已过期",
                    data = null
                });
            }
            CareTask finishTask = care.tasks.Where(t => t.task_name == "发板" && t.valid == 1).FirstOrDefault();
            await SetTaskStatus(finishTask.id, "已完成", "验证码", sessionKey, sessionType,
                null, null, null, isCancel, cancelReason);
            care = await _db.care.Where(c => c.id == careId).Include(c => c.tasks).AsNoTracking().FirstOrDefaultAsync();
            return Ok(new ApiResult<Care?>()
            {
                code = 0,
                message = "",
                data = care
            });
        }
        /*
        [NonAction]
        public async Task<Care> GetCare(int careId)
        {
            Care care = await _db.care.Where(c => c.id == careId)
                .Include(c => c.tasks).ThenInclude(t => t.staff)
                .AsNoTracking().FirstOrDefaultAsync();
            return care;
        }
        */
        [HttpGet]
        public async Task<ActionResult<List<CareReport>>> GetReport(DateTime startDate, DateTime endDate,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<List<CareReport>>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            List<Care> cares = await _db.care
                .Include(c => c.order).ThenInclude(o => o.member).ThenInclude(m => m.memberSocialAccounts.Where(m => m.valid == 1))
                .Include(c => c.order).ThenInclude(o => o.payments).ThenInclude(p => p.refunds)
                .Include(c => c.order).ThenInclude(o => o.staff)
                .Include(c => c.tasks.Where(t => t.valid == 1)).ThenInclude(t => t.staff)
                .Where(c => c.order.biz_date.Date >= startDate.Date && c.order.biz_date.Date <= endDate.Date
                && c.valid == 1 && c.order.valid == 1 && c.order.is_test == 0)
                .OrderByDescending(c => c.order.id).AsSplitQuery().AsNoTracking().ToListAsync();
            List<CareReport> reports = new List<CareReport>();
            for (int i = 0; i < cares.Count; i++)
            {
                Care care = cares[i];
                CareTask taskEdge = care.tasks.Where(t => t.task_name == "修刃" && t.valid == 1).FirstOrDefault();
                CareTask taskWax = care.tasks.Where(t => (t.task_name == "打蜡" || t.task_name == "热蜡") && t.valid == 1).FirstOrDefault();
                CareTask taskUnWax = care.tasks.Where(t => t.task_name == "刮蜡" && t.valid == 1).FirstOrDefault();
                CareTask taskRepair = care.tasks.Where(t => t.task_name == "维修" && t.valid == 1).FirstOrDefault();
                CareTask taskSafe = care.tasks.Where(t => t.task_name == "安全检查" && t.valid == 1).FirstOrDefault();
                CareTask taskGiveOut = care.tasks.Where(t => t.task_name == "发板" && t.valid == 1).FirstOrDefault();
                Staff staffEdge = taskEdge != null ? taskEdge.staff : null;
                Staff staffWax = taskWax != null ? taskWax.staff : null;
                Staff staffUnWax = taskUnWax != null ? taskUnWax.staff : null;
                Staff staffRepair = taskRepair != null ? taskRepair.staff : null;
                Staff staffSafe = taskSafe != null ? taskSafe.staff : null;
                Staff staffGiveOut = taskGiveOut != null ? taskGiveOut.staff : null;

                CareReport report = new CareReport()
                {
                    id = care.id,
                    order_id = care.order.code,
                    order = care.order,
                    shop = care.order.shop,
                    total_paid = care.order.paidAmount,
                    task_flow_num = care.task_flow_code,
                    equip_type = care.equipment,
                    equip_brand = care.brand,
                    equip_scale = care.scale,
                    degree = care.edge_degree != null ? care.edge_degree.ToString() : "",
                    edge = staffEdge != null ? staffEdge.name : "",
                    wax = staffWax != null ? staffWax.name : "",
                    unwax = staffUnWax != null ? staffUnWax.name : "",
                    more = care.repair_memo == null ? "" : care.repair_memo,
                    memo = care.memo,
                    jishi = staffRepair != null ? staffRepair.name : "",
                    additional_fee = care.repair_charge,
                    staff = care.order.staff.name,
                    logs = care.tasks
                };
                reports.Add(report);
            }
            return Ok(reports);
            /*
            return Ok(new ApiResult<List<CareReport>>()
            {
                code = 0,
                message = "",
                data = reports
            });
            /*
            List<CareReport> reports = await _db.careReport.Where(c => c.create_date >= startDate && c.create_date <= endDate).ToListAsync();
            return Ok(new ApiResult<List<CareReport>>()
            {
                code = 0,
                message = "",
                data = reports
            });
            */
        }
        // 会员在本系统养护过的装备（按装备类型），开单时快速带出品牌/长度。
        // 只取 valid=1（已下单生效）的 care；按 brand+scale 去重，每件装备取最近一次养护时间，按时间倒序
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> GetMemberCaredEquipments([FromQuery] int memberId,
            [FromQuery] string equipment, [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });
            }
            equipment = Util.UrlDecode(equipment).Trim();
            List<Care> cares = await _db.care
                .Where(c => c.valid == 1 && c.equipment != null && c.equipment.Trim() == equipment
                    && c.order.member_id == memberId
                    && c.brand != null && c.brand.Trim() != "")
                .OrderByDescending(c => c.create_date)
                .AsNoTracking().ToListAsync();
            List<object> list = new List<object>();
            HashSet<string> seen = new HashSet<string>();
            foreach (Care c in cares)
            {
                string key = (c.brand ?? "").Trim() + "|" + (c.scale ?? "").Trim();
                if (seen.Contains(key))
                {
                    continue;
                }
                seen.Add(key);
                list.Add(new
                {
                    brand = c.brand,
                    scale = c.scale,
                    boot_length = c.boot_length,
                    serials = c.serials,
                    year = c.year,
                    with_pole = c.with_pole,
                    last_care_date = c.create_date
                });
            }
            return Ok(new ApiResult<object>() { code = 0, message = "", data = list });
        }

        public class PagedCareItemResult
        {
            public List<Care> items { get; set; } = new();
            public int total { get; set; } = 0;
        }

        public class CareProgressItem
        {
            public int id { get; set; }
            public int order_id { get; set; }
            public string order_code { get; set; } = "";
            public string shop { get; set; } = "";
            public string? biz_type { get; set; }
            public string equipment { get; set; } = "";
            public string? brand { get; set; }
            public string? scale { get; set; }
            public DateTime create_date { get; set; }
            public string? customerName { get; set; }
            public string? customerCell { get; set; }
            public int completedCount { get; set; }
            public int pendingCount { get; set; }
            public List<string> completedTasks { get; set; } = new();
            public List<string> pendingTasks { get; set; } = new();
        }

        public class PagedCareProgressItemResult
        {
            public List<CareProgressItem> items { get; set; } = new();
            public int total { get; set; } = 0;
        }

        // 养护已生效订单里所有"未发板"的装备（顾客已送来养护、还没取走）。一件装备一条，供店员
        // 催取用。"已生效"= task_flow_code!=null（EffectCareOrder 跑过）；"未发板"= 该 care 任务链里
        // task_name=="发板" 那条的 status 不是 已完成/强行中止（is_cancel=true 时发板任务本身已是
        // "已完成"，天然被这条判定排除，无需额外处理）。分页与 Order/GetOrdersByStaffPaged 同款：
        // 先按条件查出全量再内存 Skip/Take（这个"店里正压着的未取件装备"工作集本身有界，不算大列表）。
        [HttpGet]
        public async Task<ActionResult<ApiResult<PagedCareItemResult>>> GetUnpickedCareItemsByStaff(
            string? shop, string? equipment, string? brand, string? cell,
            string sessionKey, string sessionType = "wechat_mini_openid",
            bool? isTest = null, bool? isSummerCare = null, string sortOrder = "asc",
            int pageIndex = 1, int pageSize = 10)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            shop = shop == null ? null : Util.UrlDecode(shop);
            equipment = equipment == null ? null : Util.UrlDecode(equipment);
            brand = brand == null ? null : Util.UrlDecode(brand);
            cell = cell == null ? null : Util.UrlDecode(cell);

            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null)
            {
                return Ok(new ApiResult<PagedCareItemResult>() { code = 1, message = "不是管理员", data = null });
            }

            List<Care> candidates = await _db.care
                .Where(c => c.valid == 1 && c.task_flow_code != null
                    && c.order.valid == 1 && c.order.type == "养护"
                    && (shop == null || c.order.shop.Trim().Equals(shop.Trim()))
                    && (equipment == null || c.equipment.Trim().Equals(equipment.Trim()))
                    && (brand == null || (c.brand != null && c.brand.Contains(brand)))
                    && (cell == null ||
                        (c.order.contact_num != null && c.order.contact_num.Contains(cell)) ||
                        (c.order.member != null && c.order.member.memberSocialAccounts.Any(msa =>
                            msa.type.Trim().Equals("cell") && msa.num.Contains(cell))))
                    && (isTest == null || c.order.is_test == ((bool)isTest ? 1 : 0))
                    && (isSummerCare == null || (c.biz_type == "非雪季养护") == isSummerCare)
                )
                .Include(c => c.tasks.Where(t => t.valid == 1))
                .Include(c => c.careImages).ThenInclude(i => i.image)
                .Include(c => c.order).ThenInclude(o => o.member).ThenInclude(m => m.memberSocialAccounts)
                .AsSplitQuery().AsNoTracking()
                .ToListAsync();

            IEnumerable<Care> filtered = candidates.Where(c =>
            {
                List<CareTask> validTasks = c.tasks?.Where(t => t.valid == 1).OrderBy(t => t.sort).ThenBy(t => t.create_date).ToList() ?? new();
                CareTask finishTask = validTasks.Where(t => t.task_name == "发板").FirstOrDefault();
                if (finishTask == null || finishTask.status == "已完成" || finishTask.status == "强行中止")
                {
                    return false;
                }

                bool hasPendingNonGiveOut = validTasks.Any(t => t.task_name != "发板" && t.status != "已完成" && t.status != "强行中止");
                return !hasPendingNonGiveOut;
            });
            List<Care> unpicked = (sortOrder != null && sortOrder.Trim().ToLower() == "desc"
                    ? filtered.OrderByDescending(c => c.create_date)
                    : filtered.OrderBy(c => c.create_date))
                .ToList();

            int total = unpicked.Count;
            List<Care> paged = unpicked.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList();

            return Ok(new ApiResult<PagedCareItemResult>()
            {
                code = 0,
                message = "",
                data = new PagedCareItemResult { items = paged, total = total }
            });
        }

        [HttpGet]
        public async Task<ActionResult<ApiResult<PagedCareProgressItemResult>>> GetIncompleteCareItemsByStaff(
            string? shop, string? equipment, string? brand, string? cell,
            string sessionKey, string sessionType = "wechat_mini_openid",
            bool? isTest = null, bool? isSummerCare = null, string sortOrder = "asc",
            int pageIndex = 1, int pageSize = 10)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            shop = shop == null ? null : Util.UrlDecode(shop);
            equipment = equipment == null ? null : Util.UrlDecode(equipment);
            brand = brand == null ? null : Util.UrlDecode(brand);
            cell = cell == null ? null : Util.UrlDecode(cell);

            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null)
            {
                return Ok(new ApiResult<PagedCareProgressItemResult>() { code = 1, message = "不是管理员", data = null });
            }

            List<Care> candidates = await _db.care
                .Where(c => c.valid == 1 && c.task_flow_code != null && c.is_cancel != true
                    && c.order.valid == 1 && c.order.type == "养护"
                    && (shop == null || c.order.shop.Trim().Equals(shop.Trim()))
                    && (equipment == null || c.equipment != null && c.equipment.Trim().Equals(equipment.Trim()))
                    && (brand == null || (c.brand != null && c.brand.Contains(brand)))
                    && (cell == null ||
                        (c.order.contact_num != null && c.order.contact_num.Contains(cell)) ||
                        (c.order.member != null && c.order.member.memberSocialAccounts.Any(msa =>
                            msa.type.Trim().Equals("cell") && msa.num.Contains(cell))))
                    && (isTest == null || c.order.is_test == ((bool)isTest ? 1 : 0))
                    && (isSummerCare == null || (c.biz_type == "非雪季养护") == isSummerCare)
                )
                .Include(c => c.tasks.Where(t => t.valid == 1))
                .Include(c => c.order).ThenInclude(o => o.member).ThenInclude(m => m.memberSocialAccounts)
                .AsSplitQuery().AsNoTracking()
                .ToListAsync();

            List<CareProgressItem> progressItems = new();
            foreach (Care care in candidates)
            {
                List<CareTask> validTasks = care.tasks?.Where(t => t.valid == 1).OrderBy(t => t.sort).ThenBy(t => t.create_date).ToList() ?? new();
                List<string> completedTasks = validTasks.Where(t => t.status == "已完成")
                    .Select(t => t.task_name?.Trim() ?? "")
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList();
                List<string> pendingTasks = validTasks.Where(t => t.status != "已完成" && t.status != "强行中止")
                    .Select(t => t.task_name?.Trim() ?? "")
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList();
                if (pendingTasks.Count == 0)
                {
                    continue;
                }

                progressItems.Add(new CareProgressItem
                {
                    id = care.id,
                    order_id = care.order_id ?? 0,
                    order_code = care.order?.code ?? "",
                    shop = care.order?.shop ?? "",
                    biz_type = care.biz_type,
                    equipment = care.equipment ?? "",
                    brand = care.brand,
                    scale = care.scale,
                    create_date = care.create_date,
                    customerName = care.order?.customerCalledName ?? care.order?.member?.real_name,
                    customerCell = care.order?.contact_num ?? (care.order?.member?.memberSocialAccounts?.FirstOrDefault(msa => msa.type.Trim().Equals("cell"))?.num),
                    completedCount = completedTasks.Count,
                    pendingCount = pendingTasks.Count,
                    completedTasks = completedTasks,
                    pendingTasks = pendingTasks
                });
            }

            IEnumerable<CareProgressItem> filtered = (sortOrder != null && sortOrder.Trim().ToLower() == "desc"
                    ? progressItems.OrderByDescending(c => c.create_date)
                    : progressItems.OrderBy(c => c.create_date));
            List<CareProgressItem> ordered = filtered.ToList();
            int total = ordered.Count;
            List<CareProgressItem> paged = ordered.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList();

            return Ok(new ApiResult<PagedCareProgressItemResult>()
            {
                code = 0,
                message = "",
                data = new PagedCareProgressItemResult { items = paged, total = total }
            });
        }

        // 会员 + 该装备类型的最近一次安全检查数值，供新单默认预填（身高/体重/脱落值/角度）。
        // 只取 valid=1 且已填过身高的记录（身高是安检必填项，用它当"这条记录确实做过安检"的锚点），
        // 不按 brand/scale 去重——身高体重是顾客本人身体数据、脱落值/角度也常年沿用，取最近一次即可。
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> GetMemberLatestSafeCheck([FromQuery] int memberId,
            [FromQuery] string equipment, [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });
            }
            equipment = Util.UrlDecode(equipment).Trim();
            Care care = await _db.care
                .Where(c => c.valid == 1 && c.equipment != null && c.equipment.Trim() == equipment
                    && c.order.member_id == memberId
                    && c.height != null && c.height.Trim() != "")
                .OrderByDescending(c => c.create_date)
                .AsNoTracking().FirstOrDefaultAsync();
            if (care == null)
            {
                return Ok(new ApiResult<object>() { code = 0, message = "", data = null });
            }
            return Ok(new ApiResult<object>()
            {
                code = 0,
                message = "",
                data = new
                {
                    height = care.height,
                    weight = care.weight,
                    gap = care.gap,
                    front_din = care.front_din,
                    rear_din = care.rear_din,
                    left_angle = care.left_angle,
                    right_angle = care.right_angle,
                    last_care_date = care.create_date
                }
            });
        }

        [HttpPost]
        public async Task<ActionResult<ApiResult<Models.Order?>>> SaveCareRecept([FromBody] Models.Order order,
            [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<Models.Order?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            order.needRender = false;
            // 防级联清理（同 SaveRentRecept）：JSON 往返回来的 member/staff 子图会让 _db.Update(order)
            // 在 TrackGraph 阶段抛 Value cannot be null (key)。本接口只管 order 标量 + cares 子图，
            // member_id / staff_id 标量列不受影响。care 的 tasks 只由 EffectCareOrder / SetTaskStatus 维护，
            // careImages.image 置空只留 image_id 标量，避免把 UploadFile 行标脏。
            order.member = null;
            order.staff = null;
            order.rentals = null;
            for (int i = 0; order.cares != null && i < order.cares.Count; i++)
            {
                Care care = order.cares[i];
                care.order = null;
                care.tasks = null;
                care.pickImage = null;
                for (int j = 0; care.careImages != null && j < care.careImages.Count; j++)
                {
                    care.careImages[j].care = null;
                    care.careImages[j].image = null;
                }
            }
            if (order.id == 0)
            {
                if (_http.HttpContext.Request.Host.Value != null
                    && _http.HttpContext.Request.Host.Value.Equals("mini.snowmeet.top"))
                {
                    order.is_test = 0;
                }
                else
                {
                    order.is_test = 1;
                }
                order.type = "养护";
                order.staff_id = staff.id;
                order.create_date = DateTime.Now;
                order.valid = 0;
                order.recepting = 1;
                for (int i = 0; order.cares != null && i < order.cares.Count; i++)
                {
                    Care care = order.cares[i];
                    care.valid = 0;
                    care.create_date = DateTime.Now;
                }
                await _db.order.AddAsync(order);
                await _db.SaveChangesAsync();
            }
            else
            {
                for (int i = 0; order.cares != null && i < order.cares.Count; i++)
                {
                    Care care = order.cares[i];
                    if (care.id == 0)
                    {
                        care.order_id = order.id;
                        care.valid = 0;
                        care.create_date = DateTime.Now;
                    }
                    else
                    {
                        care.update_date = DateTime.Now;
                    }
                }
                try
                {
                    _db.Update(order);
                    await _db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
                // 购物车里被删掉的 care 物理删除（连带 care_image 行）。
                // EffectCareOrder 加载 order.cares 不过滤 valid，软删会让已删除的板照样生成任务。
                List<Care> oriCares = await _db.care.Include(c => c.careImages)
                    .Where(c => c.order_id == order.id).AsNoTracking().ToListAsync();
                // 删除一律用 Entry().State = Deleted 而不是 Remove()：Remove 会沿导航图遍历，
                // careImage.care 经 Include fixup 指向 AsNoTracking 加载的 Care 实例，附加时与上面
                // _db.Update(order) 已跟踪的同 id posted Care 撞键 → InvalidOperationException（曾报 care 25631）
                for (int i = 0; i < oriCares.Count; i++)
                {
                    Care ori = oriCares[i];
                    if (order.cares == null || order.cares.Where(c => c.id == ori.id).ToList().Count == 0)
                    {
                        for (int j = 0; ori.careImages != null && j < ori.careImages.Count; j++)
                        {
                            _db.Entry(ori.careImages[j]).State = EntityState.Deleted;
                        }
                        _db.Entry(ori).State = EntityState.Deleted;
                    }
                    else
                    {
                        // 已有 care：payload 里不再包含的照片行显式删除（graph Update 不会删缺失子行）
                        Care posted = order.cares.Where(c => c.id == ori.id).First();
                        for (int j = 0; ori.careImages != null && j < ori.careImages.Count; j++)
                        {
                            CareImage oriImage = ori.careImages[j];
                            if (posted.careImages == null
                                || posted.careImages.Where(ci => ci.id == oriImage.id).ToList().Count == 0)
                            {
                                _db.Entry(oriImage).State = EntityState.Deleted;
                            }
                        }
                    }
                }
                try
                {
                    await _db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
            return Ok(new ApiResult<Models.Order?>()
            {
                code = 0,
                message = "",
                data = order
            });
        }
    }

}