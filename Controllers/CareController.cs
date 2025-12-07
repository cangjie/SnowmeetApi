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
            _db.care.Update(care);
            care.update_date = DateTime.Now;
            await _db.SaveChangesAsync();
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
                .Where(t => t.care_id == c.id).OrderBy(t=>t.id).ToListAsync();
            await _db.member.Entry(c.order.member).Collection(m => m.memberSocialAccounts).LoadAsync();
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
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            ApiResult<object?> r = await _staffHelper.CheckStaffLevel(100, sessionKey, sessionType);
            if (r != null)
            {
                return Ok(r);
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
                && p.valid == 1 && (p.end_date == null || ((DateTime)p.end_date).Date >= currentDate )
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
            List<Models.Product> products = await _db.product.Where(p => (p.shop.IndexOf(shop) >= 0 || shop.IndexOf(p.shop) >= 0 ) && p.category_id == 14 && p.valid == 1)
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
            List<Models.Product> products = ((ApiResult<List<Models.Product>>)((OkObjectResult)(await GetProducts(shop)).Result).Value).data;
            Models.Product product = null;
            for (int i = 0; i < products.Count; i++)
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
                && care.need_edge == 0 && care.need_wax == 1 && care.urgent == 1 && products[i].name.IndexOf("修刃打蜡") < 0 )
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
                && care.need_edge == 1 && care.need_wax == 0 && care.urgent == 0 && products[i].name.IndexOf("修刃打蜡") < 0 )
                {
                    product = products[i];
                    break;
                }
                else if (products[i].name.IndexOf("打蜡") >= 0 && products[i].name.IndexOf("次日") >= 0
                && care.need_edge == 0 && care.need_wax == 1 && care.urgent == 0 && products[i].name.IndexOf("修刃打蜡") < 0 )
                {
                    product = products[i];
                    break;
                }
            }
            return product;
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
                    create_date = DateTime.Now
                };
                await _db.careTask.AddAsync(taskSafe);
                
                if (care.need_edge == 1)
                {
                    CareTask taskEdge = new CareTask()
                    {
                        id = 0,
                        care_id = care.id,
                        task_name = "修刃",
                        memo = care.edge_degree.ToString(),
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
                        create_date = DateTime.Now
                    };
                    await _db.careTask.AddAsync(taskRepair);
                }
                if (care.need_wax == 1)
                {
                    CareTask taskWax = new CareTask()
                    {
                        id = 0,
                        care_id = care.id,
                        task_name = "打蜡",
                        memo = "",
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
                        create_date = DateTime.Now
                    };
                    await _db.careTask.AddAsync(taskUnWax);
                }
                
                CareTask taskFinish = new CareTask()
                {
                    id = 0,
                    care_id = care.id,
                    task_name = "发板",
                    create_date = DateTime.Now
                };
                await _db.careTask.AddAsync(taskFinish);
            }
            _db.order.Entry(order).State = EntityState.Detached;
            await _db.SaveChangesAsync();
        }
        [HttpGet("{taskId}")]
        public async Task<ActionResult<ApiResult<Care?>>> SetTaskStatus(int taskId, string status,
            string scene, string sessionKey, string sessionType = "wechat_mini_openid")
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
            await _db.coreDataModLog.AddAsync(log);
            _db.careTask.Entry(careTask).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            Care care = await GetCare(careTask.care_id);
            return Ok(new ApiResult<Care>()
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
    }

}