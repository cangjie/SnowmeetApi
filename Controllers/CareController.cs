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
            //List<CoreDataModLog> logs = Care.GetUpdateDifferenceLog(oriCare, care, memberId, staffId, scene);
            List<CoreDataModLog> logs = Util.GetUpdateDifferenceLog<Care>(oriCare, care, memberId, staffId, scene);
            foreach (CoreDataModLog log in logs)
            {
                await _db.coreDataModLog.AddAsync(log);
            }
            _db.care.Update(care);
            care.update_date = DateTime.Now;
            await _db.SaveChangesAsync();
            //
            /*
            //await _db.SaveChangesAsync();
            for (int i = 0; care.careImages != null && i < care.careImages.Count; i++)
            {
                CareImage ci = care.careImages[i];
                ci.image = null;
                if (ci.id == 0)
                {
                    await _db.careImage.AddAsync(ci);
                }
                else
                {
                    _db.careImage.Entry(care.careImages[i]).State = EntityState.Modified;
                }
                //_db.careImage.Entry(care.careImages[i]).State = EntityState.Modified;
            }
            
            //_db.Update(care);
            await _db.SaveChangesAsync();
            */
            return care;
        }
        [NonAction]
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
                .Include(t => t.staff).Include(t => t.terminateStaff)
                .Where(t => t.care_id == c.id).ToListAsync();
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
            List<Brand> brands = await _db.brand.Where(b => b.brand_type.Trim().Equals(type.Trim()) &&  b.brand_name.Trim().Equals(name.Trim()))
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
            List<Series> series = await _db.series.Where(s => (s.brand_name.Trim().Equals(brand) && s.type.Trim().Equals(type.Trim()) ))
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
                return Ok(new ApiResult<Care?>(){
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
        public async Task<ActionResult<ApiResult<List<Models.Product>?>>> GetProducts(string shop)
        {
            List<Models.Product> products = await _db.product
                .Where(p => (p.id == 137 || p.id == 138 || p.id == 139 || p.id == 140 || p.id == 142 || p.id == 143 || p.id == 202)
                && p.valid == 1).OrderBy(p => p.sale_price).AsNoTracking().ToListAsync();
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
                && care.need_edge == 1 && care.need_wax == 0 && care.urgent == 1)
                {
                    product = products[i];
                    break;
                }
                else if (products[i].name.IndexOf("打蜡") >= 0 && products[i].name.IndexOf("立等") >= 0
                && care.need_edge == 0 && care.need_wax == 1 && care.urgent == 1)
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
                && care.need_edge == 1 && care.need_wax == 0 && care.urgent == 0)
                {
                    product = products[i];
                    break;
                }
                else if (products[i].name.IndexOf("打蜡") >= 0 && products[i].name.IndexOf("次日") >= 0
                && care.need_edge == 0 && care.need_wax == 1 && care.urgent == 0)
                {
                    product = products[i];
                    break;
                }
            }
            return product;
        }
       

    }
    
}