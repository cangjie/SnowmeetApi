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
        // / 其余 GetProduct 名称匹配 sale_price + 票券 fixed_price 覆盖；券16 减免 双项30/单项20
        [NonAction]
        public async Task<(double commonCharge, double ticketDiscount)> CalcCharge(string shop, Care care, Ticket? ticket)
        {
            if (care.use_card || care.warranty || care.entertain)
            {
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
        // 按所选券/卡推导默认服务项（换券/换卡时刻调用）：先清空服务项再套默认，
        // 与前端「更改券/卡先清空已选服务」同一口径。规则：卡名含「双项」→ 修刃+热蜡+刮蜡；
        // 卡名含「机打蜡」（如机打蜡季卡）→ 机打蜡；券12 → 机打蜡；券17/18 → 非雪季 now/later；其余不默认
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
                if (name.IndexOf("双项") >= 0)
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
        // 开单页实时计费：客户端提交 care（含项目/券码/use_card）+ 店铺，服务端算服务费与券16减免。
        // deriveServices=true（换券/换卡时传，配 cardId）：先按新选择推导服务项再计价，
        // 响应 services 返回最终服务项供前端回填（如机打蜡季卡 → 机打蜡）；平时项目开关计价不传，不动服务项
        [HttpPost]
        public async Task<ActionResult<ApiResult<object>>> CalcCareCharge([FromBody] Care care,
            string shop, int? memberId, int? cardId, bool deriveServices = false,
            string sessionKey = "", string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "没有权限", data = null });
            }
            if (care == null || string.IsNullOrWhiteSpace(shop))
            {
                return Ok(new ApiResult<object>() { code = 1, message = "参数为空", data = null });
            }
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
            }
            object services = null;
            if (deriveServices)
            {
                ApplyDefaultServices(care, card, ticket);
                services = new
                {
                    care.need_edge,
                    care.edge_degree,
                    care.need_wax,
                    care.need_unwax,
                    care.free_wax,
                    care.summer,
                    care.biz_type
                };
            }
            var (commonCharge, ticketDiscount) = await CalcCharge(shop.Trim(), care, ticket);
            return Ok(new ApiResult<object>()
            {
                code = 0,
                message = "",
                data = new { commonCharge, ticketDiscount, services }
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
            string? dealMethod = null, string? storeMemo = null)
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
                    break;
                case "强行中止":
                    careTask.end_time = DateTime.Now;
                    careTask.terminate_staff_id = staff.id;
                    break;
                default:
                    break;
            }
            careTask.memo = scene;
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
            string code, string sessionKey, string sessionType = "wechat_mini_openid")
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
            await SetTaskStatus(finishTask.id, "已完成", "验证码", sessionKey, sessionType);
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