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
using SnowmeetApi.Models.Rent;
using SnowmeetApi.Models.Users;
using System.Collections;
using SnowmeetApi.Controllers.User;
using SnowmeetApi.Controllers.Order;
using Mono.TextTemplating;
using TencentCloud.Ocr.V20181119.Models;
using NPOI.XSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.SS.Formula.Functions;
using SQLitePCL;
using Microsoft.CodeAnalysis;
using NPOI.POIFS.Properties;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
namespace SnowmeetApi.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class RentController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private IConfiguration _config;
        public string _appId = "";
        public bool isStaff = false;
        private IConfiguration _oriConfig;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly DateTime startDate = DateTime.Parse("2020-10-20");
        private MemberController _memberHelper;
        //private OrderOnlinesController _orderHelper;
        public class Balance
        {
            public int id { get; set; }
            public string shop { get; set; }
            public string name { get; set; } = "";
            public string cell { get; set; } = "";
            public DateTime? settleDate { get; set; }
            public double deposit { get; set; } = 0;
            public double refund { get; set; } = 0;
            public double earn { get; set; } = 0;
            public string staff { get; set; } = "";
            public double reparation { get; set; } = 0;
            public double rental { get; set; } = 0;
            public string payMethod { get; set; } = "";
        }
        public RentController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = context;
            _oriConfig = config;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
            _httpContextAccessor = httpContextAccessor;
            _memberHelper = new MemberController(context, config);
        }
        [HttpGet]
        public ActionResult<ApiResult<ArrayList>> GetRentType()
        {
            ArrayList arr = new ArrayList();
            foreach (object s in Enum.GetNames(typeof(RentPrice.RentType)))
            {
                arr.Add(s.ToString());
            }
            return Ok(new ApiResult<ArrayList>()
            {
                code = 0,
                message = "",
                data = arr
            });
        }
        [HttpGet]
        public ActionResult<ApiResult<ArrayList>> GetDayType()
        {
            ArrayList arr = new ArrayList();
            foreach (object s in Enum.GetNames(typeof(RentPrice.DayType)))
            {
                arr.Add(s.ToString());
            }
            return Ok(new ApiResult<ArrayList>()
            {
                code = 0,
                message = "",
                data = arr
            });
        }
        [HttpGet]
        public ActionResult<ApiResult<ArrayList>> GetSceneType()
        {
            ArrayList arr = new ArrayList();
            foreach (object s in Enum.GetNames(typeof(RentPrice.Scene)))
            {
                arr.Add(s.ToString());
            }
            return Ok(new ApiResult<ArrayList>()
            {
                code = 0,
                message = "",
                data = arr
            });
        }
        [HttpPost("{shopId}")]
        public async Task<ActionResult<ApiResult<List<RentPrice>?>>> UpdateRentPrice([FromRoute] int shopId,
            [FromBody] List<RentPrice> priceList, [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff == null || staff.title_level < 200)
            {
                return Ok(new ApiResult<List<RentPrice>?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            for (int i = 0; i < priceList.Count; i++)
            {
                RentPrice rentPrice = priceList[i];
                rentPrice.valid = 1;
                rentPrice.staff_id = staff.id;
                if (rentPrice.id == 0)
                {
                    await _db.rentPrice.AddAsync(rentPrice);
                }
                else
                {
                    RentPrice oriPrice = await _db.rentPrice.Where(p => p.id == rentPrice.id).AsNoTracking().FirstOrDefaultAsync();
                    List<CoreDataModLog> logs = Util.GetUpdateDifferenceLog<RentPrice>(oriPrice, rentPrice, null, staff.id, "修改租赁商品价格");
                    for (int j = 0; j < logs.Count; j++)
                    {
                        await _db.coreDataModLog.AddAsync(logs[j]);
                    }
                    _db.rentPrice.Entry(rentPrice).State = EntityState.Modified;
                }
            }
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<List<RentPrice>?>()
            {
                code = 0,
                message = "",
                data = priceList
            });

        }
        [HttpGet("{priceId}")]
        public async Task<ActionResult<ApiResult<RentPrice>>> GetRentPriceById(int priceId)
        {
            RentPrice rentPrice = await _db.rentPrice.Where(p => p.id == priceId).AsNoTracking().FirstOrDefaultAsync();
            return Ok(new ApiResult<RentPrice>()
            {
                code = 0,
                message = "",
                data = rentPrice
            });
        }
        [HttpGet("{shopId}")]
        public async Task<ActionResult<ApiResult<List<RentPrice>>>> GetRentPriceList(int shopId, string type, int id, string scene)
        {
            scene = Util.UrlDecode(scene);
            type = Util.UrlDecode(type);
            List<RentPrice> rentPrice = await _db.rentPrice
                .Where(p => p.valid == 1 && p.shop_id == shopId && type.Trim().Equals(p.type)
                && ((type.Trim().Equals("分类") && p.category_id == id)
                || (type.Trim().Equals("套餐") && p.package_id == id))
                && p.scene.Trim().Equals(scene)).AsNoTracking().ToListAsync();
            return Ok(new ApiResult<List<RentPrice>>()
            {
                code = 0,
                message = "",
                data = rentPrice
            });
        }
        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResult<RentCategory?>>> ModCategory(int id, string code, string name,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            name = Util.UrlDecode(name);
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);

            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff == null || staff.title_level < 200)
            {
                return Ok(new ApiResult<List<RentPrice>?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            RentCategory rentCate = await _db.rentCategory.Where(r => r.code.Trim().Equals(code.Trim())).AsNoTracking().FirstOrDefaultAsync();


            if (rentCate != null && rentCate.id != id)
            {
                return Ok(new ApiResult<RentCategory?>()
                {
                    code = 1,
                    message = "编号重复",
                    data = null
                });
            }
            rentCate = await _db.rentCategory.Where(r => r.id == id).AsNoTracking().FirstOrDefaultAsync();
            RentCategory rentCateOri = await _db.rentCategory.Where(r => r.id == id).AsNoTracking().FirstOrDefaultAsync();
            if (rentCate == null)
            {
                return NotFound();
            }
            if (rentCate.code.Trim().Length == code.Trim().Length && !rentCate.code.Trim().Equals(code.Trim()))
            {
                rentCate = await MoveCategory(rentCate.id, code.Trim());
                if (rentCate == null)
                {
                    return NotFound();
                }
            }
            if (!rentCate.name.Trim().Equals(name.Trim()))
            {
                rentCate.name = name;
                rentCate.update_date = DateTime.Now;
                List<CoreDataModLog> logs = Util.GetUpdateDifferenceLog<RentCategory>(rentCateOri, rentCate, null, staff.id, "修改租赁商品分类名称或编码");
                for (int j = 0; j < logs.Count; j++)
                {
                    await _db.coreDataModLog.AddAsync(logs[j]);
                }
                _db.rentCategory.Entry(rentCate).State = EntityState.Modified;
                await _db.SaveChangesAsync();
            }
            return Ok(new ApiResult<RentCategory?>()
            {
                code = 0,
                message = "",
                data = rentCate
            });
        }
        [NonAction]
        public async Task<RentCategory> MoveCategory(int id, string code)
        {
            RentCategory rentCategory = await _db.rentCategory.FindAsync(id);
            if (rentCategory == null)
            {
                return null;
            }
            if (rentCategory.code.Length != code.Length
                || !rentCategory.code.Substring(0, code.Length - 2).Equals(code.Substring(0, code.Length - 2)))
            {
                return null;
            }
            var nodeList = await _db.rentCategory
                .Where(r => r.code.Trim().StartsWith(rentCategory.code.Trim()))
                .ToListAsync();
            for (int i = 0; nodeList != null && i < nodeList.Count; i++)
            {
                RentCategory rc = nodeList[i];
                string currentCode = rc.code;
                currentCode = code + currentCode.Substring(code.Length, currentCode.Length - code.Length);
                rc.code = currentCode;
                rc.update_date = DateTime.Now;
                _db.rentCategory.Entry(rc).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();
            return (RentCategory)((OkObjectResult)(await GetCategory(code)).Result).Value;
        }
        [HttpGet("{code}")]
        public async Task<ActionResult<ApiResult<RentCategory?>>> AddCategoryManual(string code, string name, string sessionKey, string sessionType)
        {
            name = Util.UrlDecode(name);
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            code = code.Trim();
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey);
            if (staff.title_level < 200)
            {
                return Ok(new ApiResult<RentCategory?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }

            RentCategory rc = await _db.rentCategory.Where(r => r.code.Trim().Equals(code.Trim())).FirstOrDefaultAsync();
            if (rc != null)
            {
                return NoContent();
            }
            if (code.Length > 2)
            {
                RentCategory rcFather = await _db.rentCategory
                     .Where(r => r.code.Trim().Equals(code.Substring(0, code.Length - 2))).FirstOrDefaultAsync();
                if (rcFather == null)
                {
                    return NotFound();
                }
            }
            RentCategory rcNew = new RentCategory()
            {
                name = name,
                code = code,
                valid = 1,
                staff_id = staff.id
            };
            await _db.rentCategory.AddAsync(rcNew);
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<RentCategory?>()
            {
                code = 0,
                message = "",
                data = rcNew
            });
        }
        [HttpGet]
        public async Task<ActionResult<RentCategory>> AddCategory(string code, string name, string sessionKey, string sessionType)
        {
            name = Util.UrlDecode(name);
            code = code == null ? "" : code.Trim();
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member.is_admin != 1)
            {
                return BadRequest();
            }
            List<RentCategory> rcL = await _db.rentCategory
                .Where(c => (c.code.Trim().Length == code.Length + 2 && c.code.Trim().StartsWith(code)))
                .OrderByDescending(c => c.code).ToListAsync();
            string newCode = code;
            if (rcL == null || rcL.Count == 0)
            {
                newCode = newCode + "01";
            }
            else
            {
                RentCategory lastRc = rcL[0];
                int maxV = int.Parse(lastRc.code.Substring(lastRc.code.Length - 2, 2));
                newCode = newCode + (maxV + 1).ToString().PadLeft(2, '0');
            }
            RentCategory rcNew = new RentCategory()
            {
                name = name,
                code = newCode
            };
            await _db.rentCategory.AddAsync(rcNew);
            await _db.SaveChangesAsync();
            return Ok(rcNew);
        }
        //准备废弃3
        [HttpGet]
        public async Task<ActionResult<ApiResult<List<RentCategory>>>> GetAllCategories()
        {
            var topL = await _db.rentCategory.Where(r => (r.code.Trim().Length == 2))
                .OrderBy(r => r.code).AsNoTracking().ToListAsync();
            if (topL == null || topL.Count == 0)
            {
                return BadRequest();
            }
            List<RentCategory> rl = new List<RentCategory>();
            for (int i = 0; i < topL.Count; i++)
            {
                string code = topL[i].code;
                RentCategory rc = await GetSimpleCategory(code);
                rl.Add(rc);
            }
            return Ok(new ApiResult<List<RentCategory>>()
            {
                code = 0,
                message = "",
                data = rl
            });
        }
        [HttpGet]
        public async Task<ActionResult<ICollection<RentCategory>>> GetTopRentCategories()
        {
            var topL = await _db.rentCategory.Where(r => (r.code.Trim().Length == 2))
                .OrderBy(r => r.code).ToListAsync();

            return Ok(new ApiResult<List<RentCategory>>()
            {
                code = 0,
                message = "",
                data = topL
            });
        }
        [HttpGet("{fatherId}")]
        public async Task<ActionResult<ICollection<RentCategory>>> GetSubRentCategories(int fatherId)
        {
            RentCategory father = await _db.rentCategory.Where(c => c.id == fatherId).AsNoTracking().FirstOrDefaultAsync();

            List<RentCategory> topL = await _db.rentCategory.Where(r => (r.code.Trim().Length == 4 && r.code.StartsWith(father.code.Trim())))
                //.Include(c => c.associateCategories.Where(a => a.valid))//.ThenInclude(a => a.category).AsTracking()
                .AsNoTracking()
                .OrderBy(r => r.code).ToListAsync();


            for (int i = 0; i < topL.Count; i++)
            {
                topL[i].associateCategories = await _db.rentCategoryAssociate.Where(a => a.valid && a.category_id == topL[i].id)
                    .Include(a => a.category).AsNoTracking().ToListAsync();
                /*
                for(int j = 0; j < topL[i].associateCategories.Count; j++)
                {
                    topL[i].associateCategories[j].category = await _db.rentCategory
                        .Where(c => c.id == topL[i].associateCategories[j].associate_id)
                        .AsNoTracking().FirstOrDefaultAsync();
                }
                */
            }

            return Ok(new ApiResult<List<RentCategory>>()
            {
                code = 0,
                message = "",
                data = topL
            });
        }
        [HttpGet("{id}")]
        public async Task<ActionResult<RentCategory>> GetCategoryById(int id)
        {
            RentCategory category = await _db.rentCategory.FindAsync(id);
            return await GetCategory(category.code.Trim());
        }
        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResult<RentCategory>>> GetRentCategory(int id)
        {
            RentCategory category = await _db.rentCategory.Where(c => c.id == id).AsNoTracking().FirstOrDefaultAsync();
            category.father = await _db.rentCategory.Where(c => (category.code.StartsWith(c.code))).AsNoTracking().FirstOrDefaultAsync();
            category.associateCategories = await _db.rentCategoryAssociate
                .Where(a => a.valid && a.category_id == id).Include(a => a.category).AsNoTracking().ToListAsync();

            return Ok(new ApiResult<RentCategory>()
            {
                code = 0,
                message = "",
                data = category
            });
        }
        [HttpGet("{code}")]
        public async Task<ActionResult<ApiResult<RentCategory?>>> GetRentCategoryByCode(string code)
        {
            RentCategory category = await GetSimpleCategory(code);
            return Ok(new ApiResult<RentCategory>()
            {
                code = 0,
                message = "",
                data = category
            });
        }
        [NonAction]
        public async Task<RentCategory?> GetSimpleCategory(string code = "")
        {
            code = code.Trim();
            RentCategory rc = await _db.rentCategory
                //.Include(r => r.associateCategories.Where(a => a.valid))//.ThenInclude(c => c.category)
                .AsNoTracking()
                .Where(r => r.code.Trim().Equals(code.Trim())).FirstOrDefaultAsync();
            /*
            for(int i = 0; i < rc.associateCategories.Count; i++)
            {
                rc.associateCategories[i].category = await _db.rentCategory
                    .Where(r => r.id == rc.associateCategories[i].associate_id)
                    .AsNoTracking().FirstOrDefaultAsync();
            }
            */
            if (rc == null)
            {
                return null;
            }
            var rcL = await _db.rentCategory.AsNoTracking().Where(r => r.code.Trim().Length == code.Length + 2
                && r.code.StartsWith(code))
                //.Include(r => r.associateCategories.Where(a => a.valid))//.ThenInclude(c => c.category)
                .AsNoTracking()
                .OrderBy(r => r.code).ToListAsync();
            if (rcL != null && rcL.Count > 0)
            {
                List<RentCategory> children = new List<RentCategory>();
                for (int i = 0; i < rcL.Count; i++)
                {
                    string childCode = rcL[i].code;
                    RentCategory child = await GetSimpleCategory(childCode);
                    child.infoFields = rc.infoFields;
                    child.associateCategories = await _db.rentCategoryAssociate
                        .Where(a => a.category_id == child.id && a.valid)
                        .Include(a => a.category).AsNoTracking().ToListAsync();
                    if (child != null)
                    {
                        children.Add(child);
                    }

                }
                rc.children = children;
            }
            return rc;
        }
        [HttpGet("{code}")]
        public async Task<ActionResult<RentCategory>> GetCategory(string code = "")
        {
            code = code.Trim();
            RentCategory rc = await _db.rentCategory
                .Include(r => r.priceList).Include(r => r.infoFields).Include(r => r.productList)
                //.Include(r => r.associateCategories.Where(r => r.valid))
                .Where(r => r.code.Trim().Equals(code.Trim())).FirstAsync();
            /*
            for(int i = 0; i < rc.associateCategories.Count; i++)
            {
                rc.associateCategories[i].category = await _db.rentCategory
                    .Where(r => r.id == rc.associateCategories[i].associate_id)
                    .AsNoTracking().FirstOrDefaultAsync();
            }
            */
            if (rc == null)
            {
                return NotFound();
            }
            if ((rc.infoFields == null || rc.infoFields.Count == 0) && code.Length > 2)
            {
                RentCategory rcInfo = await _db.rentCategory
                .Include(r => r.priceList)
                .Include(r => r.infoFields)
                .Include(r => r.productList)
                .Where(r => r.code.Trim().Equals(code.Trim().Substring(0, 2))).FirstAsync();
                rc.infoFields = rcInfo.infoFields;
            }
            var rcL = await _db.rentCategory.Include(r => r.priceList).Where(r => r.code.Trim().Length == code.Length + 2
                && r.code.StartsWith(code)).OrderBy(r => r.code).ToListAsync();
            if (rcL != null && rcL.Count > 0)
            {
                List<RentCategory> children = new List<RentCategory>();
                for (int i = 0; i < rcL.Count; i++)
                {
                    RentCategory child = (RentCategory)((OkObjectResult)(await GetCategory(rcL[i].code)).Result).Value;
                    child.infoFields = rc.infoFields;
                    if (child != null)
                    {
                        children.Add(child);
                    }

                }
                rc.children = children;
            }
            if (rc.children != null)
            {
                rc.priceList = null;
            }
            var pList = (from product in rc.productList
                         where product.valid == 1
                         select product).ToList();
            rc.productList = pList;
            rc.associateCategories = await _db.rentCategoryAssociate.Where(c => c.category_id == rc.id && c.valid)
                .Include(a => a.category).AsNoTracking().ToListAsync();
            return Ok(rc);
        }
        [HttpGet("{code}")]
        public async Task<ActionResult> DeleteCategory(string code, string sessionKey, string sessionType)
        {
            RentCategory rc = (RentCategory)((OkObjectResult)(await GetCategory(code)).Result).Value;
            if (rc.children != null)
            {
                return BadRequest();
            }
            _db.rentCategory.Remove(rc);
            await _db.SaveChangesAsync();
            return Ok();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentCategory>> SetShopCategoryRentPrice(int id, string shop, string dayType, string scene, string price, string sessionKey, string sessionType)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member.is_admin != 1)
            {
                return BadRequest();
            }
            shop = Util.UrlDecode(shop);
            dayType = Util.UrlDecode(dayType);
            scene = Util.UrlDecode(scene);

            RentCategory cate = await _db.rentCategory.Include(r => r.priceList)
                .Where(r => r.id == id).FirstOrDefaultAsync();
            if (cate == null || (cate.children != null && cate.children.Count > 0))
            {
                return NotFound();
            }
            var priceL = await _db.rentPrice.Where(p => (p.type.Trim().Equals("分类")
                && p.category_id == cate.id && p.day_type.Trim().Equals(dayType)
                && p.scene.Trim().Equals(scene) && p.shop.Trim().Equals(shop))).ToListAsync();
            double? numPrice = price.Equals("-") ? null : double.Parse(price);
            if (priceL == null || priceL.Count == 0)
            {
                RentPrice rp = new RentPrice()
                {
                    shop = shop,
                    type = "分类",
                    category_id = cate.id,
                    day_type = dayType,
                    scene = scene,
                    price = numPrice,
                    update_date = DateTime.Now
                };
                await _db.rentPrice.AddAsync(rp);
            }
            else
            {
                RentPrice rp = priceL[0];
                rp.price = numPrice;
                rp.update_date = DateTime.Now;
                _db.rentPrice.Entry(rp).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();
            RentCategory rc = (RentCategory)((OkObjectResult)(await GetCategory(cate.code.Trim())).Result).Value;
            return Ok(rc);
        }
        [HttpGet("{code}")]
        public async Task<ActionResult<ApiResult<RentCategory?>>> UpdateCategory(string code, string name, double guaranty, string scene, string sessionKey, string sessionType)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            name = Util.UrlDecode(name);
            scene = Util.UrlDecode(scene);
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey);
            if (staff.title_level < 200)
            {
                return Ok(new ApiResult<RentCategory?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            RentCategory cate = await _db.rentCategory
            .Where(r => r.code.Trim().Equals(code.Trim())).FirstOrDefaultAsync();
            if (cate == null)
            {
                return NotFound();
            }
            cate.update_date = DateTime.Now;
            if (!cate.name.Trim().Equals(name.Trim()))
            {

                CoreDataModLog logName = new CoreDataModLog()
                {
                    id = 0,
                    table_name = "rent_category",
                    field_name = "name",
                    key_value = cate.id,
                    prev_value = cate.name,
                    current_value = name,
                    is_manual = 1,
                    staff_id = staff.id,
                    manual_memo = "修改分类名称",
                    scene = scene
                };
                cate.name = name.Trim();
                await _db.coreDataModLog.AddAsync(logName);
            }
            //cate.name = name.Trim();
            if (cate.deposit != guaranty)
            {
                CoreDataModLog logDeposit = new CoreDataModLog()
                {
                    id = 0,
                    table_name = "rent_category",
                    field_name = "deposit",
                    key_value = cate.id,
                    prev_value = cate.deposit.ToString(),
                    current_value = guaranty.ToString(),
                    is_manual = 1,
                    staff_id = staff.id,
                    manual_memo = "修改分类名称",
                    scene = scene
                };
                cate.deposit = guaranty;
                await _db.coreDataModLog.AddAsync(logDeposit);
            }
            _db.Entry(cate).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<RentCategory?>()
            {
                code = 0,
                message = "",
                data = cate
            });
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<RentPackage?>>> AddRentPackage(string name, string description, string sessionKey, string sessionType)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff == null || staff.title_level < 200)
            {
                return Ok(new ApiResult<RentPackage?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            RentPackage rp = new RentPackage()
            {
                name = Util.UrlDecode(name),
                description = Util.UrlDecode(description),
                valid = 1,
                staff_id = staff.id,
                item_count = 2,
                update_date = DateTime.Now
            };
            await _db.rentPackage.AddAsync(rp);
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<RentPackage?>()
            {
                code = 0,
                message = "",
                data = rp
            });
        }
        [HttpGet("{packageId}")]
        public async Task<ActionResult<ActionResult<RentPackage?>>> RentPackageCategoryAdd(int packageId, int categoryId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff == null || staff.title_level < 200)
            {
                return Ok(new ApiResult<RentPackage?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }

            RentCategory rentCategory = await _db.rentCategory.FindAsync(categoryId);
            if (rentCategory == null)
            {
                return NotFound();
            }
            RentPackageCategory rpc = await _db.rentPackageCategory
                .Where(r => r.package_id == packageId && r.category_id == rentCategory.id)
                .AsNoTracking().FirstOrDefaultAsync();
            if (rpc == null)
            {
                rpc = new RentPackageCategory()
                {
                    package_id = packageId,
                    category_id = rentCategory.id,
                    update_date = DateTime.Now
                };
                CoreDataModLog log = new CoreDataModLog()
                {
                    id = 0,
                    table_name = "rent_package_category",
                    field_name = "category_id",
                    key_value = packageId,
                    prev_value = "",
                    current_value = categoryId.ToString(),
                    is_manual = 1,
                    staff_id = staff.id,
                    manual_memo = "添加套餐分类",
                    scene = "后台",
                    create_date = DateTime.Now
                };

                await _db.coreDataModLog.AddAsync(log);
                await _db.rentPackageCategory.AddAsync(rpc);
                await _db.SaveChangesAsync();
            }
            RentPackage pr = await _db.rentPackage
                .Include(r => r.rentPackageCategoryList).ThenInclude(r => r.rentCategory)
                .Where(r => r.id == packageId).FirstAsync();
            return Ok(new ApiResult<RentPackage?>()
            {
                code = 0,
                message = "",
                data = pr
            });
        }
        [HttpGet("{packageId}")]
        public async Task<ActionResult<RentPackage>> RentPackageCategoryDel(int packageId, int categoryId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff == null || staff.title_level < 200)
            {
                return Ok(new ApiResult<RentPackage?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            RentCategory category = await _db.rentCategory.FindAsync(categoryId);
            if (category == null)
            {
                return NotFound();
            }
            CoreDataModLog log = new CoreDataModLog()
            {
                id = 0,
                table_name = "rent_package_category",
                field_name = "category_id",
                key_value = packageId,
                prev_value = categoryId.ToString(),
                current_value = "",
                is_manual = 1,
                staff_id = staff.id,
                manual_memo = "删除套餐分类",
                scene = "后台",
                create_date = DateTime.Now
            };
            RentPackageCategory rpc = await _db.rentPackageCategory.FindAsync(packageId, category.id);
            _db.rentPackageCategory.Remove(rpc);
            await _db.coreDataModLog.AddAsync(log);
            await _db.SaveChangesAsync();

            RentPackage pr = await _db.rentPackage
                .Include(r => r.rentPackageCategoryList).ThenInclude(r => r.rentCategory)
                .Where(r => r.id == packageId).FirstAsync();
            return Ok(new ApiResult<RentPackage?>()
            {
                code = 0,
                message = "",
                data = pr
            });
        }
        [HttpGet("{packageId}")]
        public async Task<ActionResult<ApiResult<RentPackage?>>> UpdateRentPackageBaseInfo(int packageId, string name, string description, double deposit,
            string sessionKey, string sessionType, string? shop = null)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            name = Util.UrlDecode(name);
            description = Util.UrlDecode(description);
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff == null || staff.title_level < 200)
            {
                return new ApiResult<RentPackage?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                };
            }
            RentPackage p = await _db.rentPackage.Where(p => p.id == packageId).AsNoTracking().FirstOrDefaultAsync();
            RentPackage oriP = await _db.rentPackage.Where(p => p.id == packageId).AsNoTracking().FirstOrDefaultAsync();

            if (p == null)
            {
                return NotFound();
            }
            p.name = Util.UrlDecode(name);
            p.description = Util.UrlDecode(description);
            p.deposit = deposit;
            p.shop = shop;
            p.update_date = DateTime.Now;
            List<CoreDataModLog> logs = Util.GetUpdateDifferenceLog<RentPackage>(oriP, p, null, staff.id, "修改套餐信息");
            for (int i = 0; i < logs.Count; i++)
            {
                await _db.coreDataModLog.AddAsync(logs[i]);
            }
            _db.rentPackage.Entry(p).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<RentPackage?>()
            {
                code = 0,
                message = "",
                data = p
            });
            //return await GetRentPackage(packageId);
        }
        [HttpGet("{packageId}")]
        public async Task<ActionResult<RentPackage>> SetPackageRentPrice(int packageId, string shop, string dayType, string scene, string price, string sessionKey, string sessionType)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member.is_admin != 1)
            {
                return BadRequest();
            }
            shop = Util.UrlDecode(shop);
            dayType = Util.UrlDecode(dayType);
            scene = Util.UrlDecode(scene);
            RentPackage rentPackage = await _db.rentPackage.FindAsync(packageId);
            if (rentPackage == null)
            {
                return NotFound();
            }
            var priceL = await _db.rentPrice.Where(p => (p.type.Trim().Equals("套餐")
                && p.package_id == packageId && p.day_type.Trim().Equals(dayType)
                && p.scene.Trim().Equals(scene) && p.shop.Trim().Equals(shop))).ToListAsync();
            if (priceL == null || priceL.Count == 0)
            {
                RentPrice rp = new RentPrice()
                {
                    shop = shop,
                    type = "套餐",
                    package_id = packageId,
                    day_type = dayType,
                    scene = scene,
                    price = price.Trim().Equals("-") ? null : double.Parse(price),
                    update_date = DateTime.Now
                };
                await _db.rentPrice.AddAsync(rp);
            }
            else
            {
                RentPrice rp = priceL[0];
                rp.price = price.Trim().Equals("-") ? null : double.Parse(price);
                rp.update_date = DateTime.Now;
                _db.rentPrice.Entry(rp).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();
            ApiResult<RentPackage> packageResult = (ApiResult<RentPackage>)(((OkObjectResult)((await GetRentPackage(packageId)).Result)).Value);
            return Ok(packageResult.data);
        }
        [HttpGet("{categoryId}")]
        public async Task<ActionResult<RentCategoryInfoField>> CategoryInfoFieldAdd(int categoryId, string fieldName, int sort, string sessionKey, string sessionType)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member.is_admin != 1)
            {
                return BadRequest();
            }
            fieldName = Util.UrlDecode(fieldName);
            RentCategoryInfoField field = await _db.rentCategoryInfoField
                .Where(f => f.category_id == categoryId && f.field_name.Trim().Equals(fieldName.Trim()))
                .FirstOrDefaultAsync();
            if (field == null)
            {

                field = new RentCategoryInfoField()
                {
                    id = 0,
                    category_id = categoryId,
                    field_name = fieldName,
                    is_delete = 0,
                    sort = sort,
                    update_date = DateTime.Now
                };
                await _db.rentCategoryInfoField.AddAsync(field);
            }
            else
            {
                field.sort = sort;
                field.is_delete = 0;
                _db.rentCategoryInfoField.Entry(field).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();
            return Ok(field);
        }
        [HttpGet("{fieldId}")]
        public async Task<ActionResult<RentCategoryInfoField>> CategoryInfoFieldMod(int fieldId, string fieldName, int sort, bool delete, string sessionKey, string sessionType)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member.is_admin != 1)
            {
                return BadRequest();
            }
            RentCategoryInfoField field = await _db.rentCategoryInfoField.FindAsync(fieldId);
            if (field == null)
            {
                return NotFound();
            }
            field.field_name = Util.UrlDecode(fieldName);
            field.sort = sort;
            field.is_delete = delete ? 1 : 0;
            _db.rentCategoryInfoField.Entry(field).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(field);
        }
        [HttpGet("{categoryId}")]
        public async Task<ActionResult<ApiResult<RentProduct?>>> AddRentProduct(int categoryId, string? shop, string name, string sessionKey, string sessionType)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 200)
            {
                return Ok(new ApiResult<RentProduct?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            RentProduct p = new RentProduct()
            {
                id = 0,
                category_id = categoryId,
                shop = shop,
                name = name.Trim(),
                staff_id = staff.id,
                valid = 1
            };
            await _db.rentProduct.AddAsync(p);
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<RentProduct?>()
            {
                code = 0,
                message = "",
                data = p
            });
        }
        [HttpPost]
        public async Task<ActionResult<ApiResult<RentProduct?>>> ModRentProduct(RentProduct rentProduct,
            [FromQuery] string sessionKey, [FromQuery] string sessionType)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 200)
            {
                return Ok(new ApiResult<RentProduct?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            RentProduct ori = await _db.rentProduct.AsNoTracking().Where(p => p.id == rentProduct.id).FirstOrDefaultAsync();
            if (ori == null)
            {
                return NotFound();
            }
            List<CoreDataModLog> logs = Util.GetUpdateDifferenceLog<RentProduct>(ori, rentProduct, null, staff.id, "修改租赁商品基础信息");
            for (int i = 0; i < logs.Count; i++)
            {
                await _db.coreDataModLog.AddAsync(logs[i]);
            }
            _db.rentProduct.Entry(rentProduct).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<RentProduct?>()
            {
                code = 0,
                message = "",
                data = rentProduct
            });
        }
        [HttpGet("{productId}")]
        public async Task<ActionResult<ApiResult<RentProduct?>>> GetRentProduct(int productId)
        {
            var productList = await _db.rentProduct.Where(p => p.id == productId)
                .Include(p => p.images)
                .Include(p => p.detailInfo)
                .AsNoTracking().ToListAsync();
            if (productList == null || productList.Count == 0)
            {
                return NotFound();
            }
            RentProduct product = productList[0];
            RentCategory category = (RentCategory)((OkObjectResult)((await GetCategoryById(product.category_id)).Result)).Value;
            for (int i = 0; i < category.infoFields.Count; i++)
            {
                bool exists = false;
                for (int j = 0; j < product.detailInfo.Count; j++)
                {
                    if (product.detailInfo[j].field_id == category.infoFields[i].id)
                    {
                        exists = true;
                        product.detailInfo[j].fieldName = category.infoFields[i].field_name.Trim();
                        product.detailInfo[j].field = category.infoFields[i];
                    }
                }
                if (!exists)
                {
                    RentProductDetailInfo info = new RentProductDetailInfo()
                    {
                        product_id = product.id,
                        field_id = category.infoFields[i].id,
                        info = "",
                        update_date = DateTime.Now,
                        fieldName = category.infoFields[i].field_name.Trim(),
                        field = category.infoFields[i]

                    };
                    product.detailInfo.Add(info);
                }
            }
            return Ok(new ApiResult<RentProduct?>()
            {
                code = 0,
                message = "",
                data = product
            });
        }
        [HttpPost("{productId}")]
        public async Task<ActionResult<ApiResult<RentProduct?>>> UpdateRentProductDetailInfo(int productId,
            [FromQuery] string sessionKey, [FromQuery] string sessionType, List<RentProductDetailInfo> details)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member.is_admin != 1)
            {
                return BadRequest();
            }
            for (int i = 0; i < details.Count; i++)
            {
                RentProductDetailInfo info = details[i];

                RentProductDetailInfo oriInfo = await _db.rentProductDetailInfo.FindAsync(productId, info.field_id);
                if (oriInfo != null)
                {
                    oriInfo.info = info.info.Trim();
                    _db.rentProductDetailInfo.Entry(oriInfo).State = EntityState.Modified;
                    await _db.SaveChangesAsync();

                }
                else
                {
                    info.update_date = DateTime.Now;
                    await _db.rentProductDetailInfo.AddAsync(info);
                    await _db.SaveChangesAsync();
                }
            }
            return await GetRentProduct(productId);
        }
        [HttpPost("{productId}")]
        public async Task<ActionResult<ApiResult<RentProduct?>>> SetRentProductImage(int productId, [FromQuery] string sessionKey,
            [FromQuery] string sessionType, string[] images)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 200)
            {
                return Ok(new ApiResult<RentProduct?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            var imageList = await _db.rentProductImage.Where(i => i.product_id == productId).ToListAsync();
            for (int i = 0; i < imageList.Count; i++)
            {
                _db.rentProductImage.Remove(imageList[i]);
            }
            await _db.SaveChangesAsync();
            for (int i = 0; i < images.Length; i++)
            {
                RentProductImage img = new RentProductImage()
                {
                    id = 0,
                    product_id = productId,
                    image_url = images[i].Trim(),
                    sort = i,
                    update_date = DateTime.Now
                };
                await _db.rentProductImage.AddAsync(img);
            }
            await _db.SaveChangesAsync();
            return await GetRentProduct(productId);
        }
        [HttpGet]
        public async Task UpdateFinishDateByRefund()
        {
            List<RentOrder> rentList = await _db.RentOrder
                .Include(r => r.order)
                    .ThenInclude(o => o.paymentList.Where(p => p.status.Trim().Equals("支付成功")))
                        .ThenInclude(p => p.refunds.Where(r => (!r.refund_id.Trim().Equals("") || r.state == 1)))
                .Where(r => r.finish_date == null && r.closed == 0 && r.order_id != 0)
                .ToListAsync();
            for (int i = 0; i < rentList.Count; i++)
            {
                RentOrder order = rentList[i];
                if (order.refunds.Count > 0)
                {
                    var dateList = (from refund in order.refunds
                                    orderby refund.create_date descending
                                    select refund.create_date).ToList();
                    order.finish_date = (DateTime)dateList[0];
                    _db.RentOrder.Entry(order).State = EntityState.Modified;
                }
            }
            await _db.SaveChangesAsync();
        }
        [HttpGet]
        public async Task UpdateFinishDateByReturn()
        {
            List<RentOrder> rentList = await _db.RentOrder
                .Include(r => r.details)
                .Include(r => r.order)
                    .ThenInclude(o => o.paymentList.Where(p => p.status.Trim().Equals("支付成功")))
                        .ThenInclude(p => p.refunds.Where(r => (!r.refund_id.Trim().Equals("") || r.state == 1)))
                .Where(r => r.finish_date == null && r.closed == 0
                ).OrderByDescending(r => r.id).ToListAsync();
            for (int i = 0; i < rentList.Count; i++)
            {
                RentOrder order = rentList[i];
                if (order.order_id != 0 && order.refunds.Count > 0)
                {
                    continue;
                }
                if (order.details.Count > 0)
                {
                    var l = (from detail in order.details
                             where (detail.deposit_type.Trim().Equals("立即租赁")
                             && !detail.status.Equals("已归还")
                             && !detail.status.Equals("未领取"))
                             select detail.id)
                        .ToList();
                    if (l.Count > 0)
                    {
                        continue;
                    }
                    var lDate = (from detail in order.details
                                 where detail.status.Equals("已归还")
                                 orderby detail.real_end_date descending
                                 select detail.real_end_date)
                        .ToList();
                    if (lDate.Count > 0)
                    {
                        order.finish_date = (DateTime)lDate[0];
                        _db.RentOrder.Entry(order).State = EntityState.Modified;
                    }
                    else
                    {
                        l = (from detail in order.details
                             where (detail.deposit_type.Trim().Equals("立即租赁") && !detail.status.Equals("未领取"))
                             select detail.id).ToList();
                        if (l.Count == 0 && order.create_date < DateTime.Now.AddHours(-12))
                        {
                            order.closed = 1;
                            _db.RentOrder.Entry(order).State = EntityState.Modified;
                        }
                    }
                }
                else
                {
                    order.closed = 1;
                    _db.RentOrder.Entry(order).State = EntityState.Modified;

                }
            }
            await _db.SaveChangesAsync();
        }
        [NonAction]
        public async Task StartRent(int rentId)
        {
            RentOrder order = await _db.RentOrder.FindAsync(rentId);
            List<RentOrderDetail> rentItemList = await _db.RentOrderDetail
                .Where(i => i.rent_list_id == rentId && i.valid == 1).ToListAsync();
            for (int i = 0; rentItemList != null && i < rentItemList.Count; i++)
            {
                RentOrderDetail detail = rentItemList[i];
                RentOrderDetailLog log;
                switch (detail.deposit_type.Trim())
                {
                    case "立即租赁":
                        detail.start_date = DateTime.Now;
                        detail.rent_status = RentOrderDetail.RentStatus.已发放.ToString();
                        detail.update_date = DateTime.Now;
                        detail.pick_date = DateTime.Now;
                        _db.RentOrderDetail.Entry(detail).State = EntityState.Modified;
                        log = new RentOrderDetailLog()
                        {
                            id = 0,
                            detail_id = detail.id,
                            status = RentOrderDetailLog.Status.已发放.ToString(),
                            staff_open_id = order.staff_open_id,
                            create_date = DateTime.Now
                        };
                        await _db.rentOrderDetailLog.AddAsync(log);
                        log = new RentOrderDetailLog()
                        {
                            id = 0,
                            detail_id = detail.id,
                            status = RentOrderDetailLog.Status.开始计费.ToString(),
                            staff_open_id = order.staff_open_id,
                            create_date = DateTime.Now
                        };
                        await _db.rentOrderDetailLog.AddAsync(log);
                        break;
                    case "延时租赁":
                        detail.update_date = DateTime.Now;
                        detail.rent_status = RentOrderDetail.RentStatus.已发放.ToString();
                        detail.pick_date = DateTime.Now;
                        detail.update_date = DateTime.Now;
                        _db.RentOrderDetail.Entry(detail).State = EntityState.Modified;
                        log = new RentOrderDetailLog()
                        {
                            id = 0,
                            detail_id = detail.id,
                            status = RentOrderDetailLog.Status.已发放.ToString(),
                            staff_open_id = order.staff_open_id,
                            create_date = DateTime.Now
                        };
                        await _db.rentOrderDetailLog.AddAsync(log);
                        break;
                    case "预约租赁":
                        detail.rent_status = RentOrderDetail.RentStatus.未领取.ToString();
                        detail.update_date = DateTime.Now;
                        _db.RentOrderDetail.Entry(detail).State = EntityState.Modified;
                        break;
                    case "先租后取":
                        detail.start_date = DateTime.Now;
                        detail.update_date = DateTime.Now;
                        detail.rent_status = RentOrderDetail.RentStatus.未领取.ToString();
                        _db.RentOrderDetail.Entry(detail).State = EntityState.Modified;
                        log = new RentOrderDetailLog()
                        {
                            id = 0,
                            detail_id = detail.id,
                            status = RentOrderDetailLog.Status.开始计费.ToString(),
                            staff_open_id = order.staff_open_id,
                            create_date = DateTime.Now
                        };
                        await _db.rentOrderDetailLog.AddAsync(log);
                        break;
                    case "预付押金":
                        detail.update_date = DateTime.Now;
                        detail.rent_status = RentOrderDetail.RentStatus.未领取.ToString();
                        _db.RentOrderDetail.Entry(detail).State = EntityState.Modified;
                        break;
                    default:
                        break;
                }
            }
            await _db.SaveChangesAsync();
        }
        [NonAction]
        public async Task StartRent_bak(int rentId)
        {
            var rentItemList = await _db.RentOrderDetail.Where(i => i.rent_list_id == rentId).ToListAsync();
            for (int i = 0; rentItemList != null && i < rentItemList.Count; i++)
            {
                RentOrderDetail detail = rentItemList[i];
                if (detail.deposit_type.Trim().Equals("立即租赁"))
                {
                    DateTime nowTime = DateTime.Now;
                    if (detail.start_date == null)
                    {
                        detail.start_date = DateTime.Now;
                    }
                    else
                    {
                        DateTime startDate = (DateTime)detail.start_date;
                        startDate = startDate.AddHours(nowTime.Hour).AddMinutes(nowTime.Minute)
                            .AddSeconds(nowTime.Second).AddMilliseconds(nowTime.Millisecond);
                        detail.start_date = startDate;
                    }
                    _db.RentOrderDetail.Entry(detail).State = EntityState.Modified;
                }
            }
            await _db.SaveChangesAsync();
        }
        [HttpGet("{code}")]
        public async Task<ActionResult<SnowmeetApi.Models.Rent.RentItem>> GetRentItem(string code, string shop)
        {
            var rentItemList = await _db.RentItem.Where(r => r.code.Trim().Equals(code.Trim())).ToListAsync();
            if (rentItemList != null && rentItemList.Count > 0)
            {
                SnowmeetApi.Models.Rent.RentItem item = rentItemList[0];
                if (item.rental == 0)
                {
                    item.rental = item.GetRental(shop);
                }
                //item.rental_reserve = item.rental_member;
                return Ok(item);
            }
            else
            {
                return NotFound();
            }
        }
        [NonAction]
        public async Task<List<RentOrder>> GetAllFinishedRentOrder()
        {
            DateTime startDate = DateTime.Parse("2024-10-1");
            DateTime endDate = DateTime.Parse("2025-5-1");
            List<RentOrder> rList = await _db.RentOrder
                .Where(r => (r.finish_date >= startDate.Date && r.finish_date <= endDate.Date && r.closed == 0))
                .Include(r => r.receptMsa).ThenInclude(m => m.member)
                .Include(r => r.order).ThenInclude(o => o.paymentList.Where(p => p.status.Equals("支付成功")))
                    .ThenInclude(p => p.msa).ThenInclude(m => m.member)
                .Include(r => r.order).ThenInclude(o => o.paymentList.Where(p => p.status.Equals("支付成功")))
                    .ThenInclude(p => p.refunds.Where(r => r.state == 1 || !r.refund_id.Trim().Equals("")))
                .Include(r => r.additionalPayments.Where(a => a.is_paid == 1))
                    .ThenInclude(a => a.order).ThenInclude(o => o.paymentList.Where(p => p.status.Equals("支付成功")))
                        .ThenInclude(p => p.msa).ThenInclude(m => m.member)
                .Include(r => r.additionalPayments.Where(a => a.is_paid == 1))
                    .ThenInclude(a => a.order).ThenInclude(o => o.paymentList.Where(p => p.status.Equals("支付成功")))
                        .ThenInclude(p => p.refunds.Where(r => r.state == 1 || !r.refund_id.Trim().Equals("")))
                .Include(r => r.details.Where(d => d.valid == 1).OrderByDescending(d => d.id)).ThenInclude(d => d.log)
                    .ThenInclude(d => d.msa).ThenInclude(m => m.member)
                .OrderByDescending(o => o.id).AsNoTracking().ToListAsync();
            return rList;
        }
        [NonAction]
        public void ExportExcelCreateHead(XSSFWorkbook workbook, ISheet sheet, string[] commonHead, string[] paymentHead,
            string[] refundHead, int maxPaymentCount, int maxRefundCount)
        {
            List<string> headList = new List<string>();
            for (int i = 0; i < commonHead.Length; i++)
            {
                headList.Add(commonHead[i]);

            }
            for (int i = 0; i < maxPaymentCount; i++)
            {
                for (int j = 0; j < paymentHead.Length; j++)
                {
                    headList.Add(paymentHead[j] + (i + 1).ToString());
                }
            }
            for (int i = 0; i < maxRefundCount; i++)
            {
                for (int j = 0; j < refundHead.Length; j++)
                {
                    headList.Add(refundHead[j] + (i + 1).ToString());
                }
            }
            IFont headFont = workbook.CreateFont();
            headFont.Color = NPOI.HSSF.Util.HSSFColor.White.Index;
            headFont.IsBold = true;
            ICellStyle headStyle = workbook.CreateCellStyle();
            headStyle.Alignment = HorizontalAlignment.Center;
            headStyle.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Black.Index;
            headStyle.FillPattern = FillPattern.SolidForeground;
            headStyle.SetFont(headFont);
            headStyle.VerticalAlignment = VerticalAlignment.Center;
            IRow headRow = sheet.CreateRow(0);
            headRow.Height = 500;
            for (int i = 0; i < headList.Count; i++)
            {
                ICell headCell = headRow.CreateCell(i);
                headCell.SetCellValue(headList[i].Trim());
                headCell.SetCellType(CellType.String);
                headCell.CellStyle = headStyle;
                if (i < commonHead.Length)
                {
                    switch (i)
                    {
                        case 0:

                        case 2:

                            sheet.SetColumnWidth(i, 1000);
                            break;
                        case 3:
                        case 22:
                        case 25:
                        case 26:
                        case 9:
                        case 10:
                        case 14:
                        case 15:
                        case 16:
                        case 1:
                            sheet.SetColumnWidth(i, 3500);
                            break;

                        case 5:
                        case 7:
                        case 27:
                        case 30:
                        case 32:
                        case 34:
                            sheet.SetColumnWidth(i, 3000);
                            break;
                        case 6:
                        case 8:
                        case 28:
                        case 31:
                        case 33:
                        case 35:
                            sheet.SetColumnWidth(i, 2500);
                            break;
                        case 4:
                        case 18:
                        case 29:
                        case 36:

                            sheet.SetColumnWidth(i, 3500);
                            break;
                        case 17:
                            sheet.SetColumnWidth(i, 10000);
                            break;
                        default:

                            break;
                    }
                }
                else if (i < commonHead.Length + maxPaymentCount * paymentHead.Length)
                {
                    switch ((i - commonHead.Length) % paymentHead.Length)
                    {
                        case 0:
                            sheet.SetColumnWidth(i, 3500);
                            break;
                        case 2:
                        case 3:
                            sheet.SetColumnWidth(i, 7200);
                            break;
                        case 4:
                            sheet.SetColumnWidth(i, 2900);
                            break;
                        case 5:
                            sheet.SetColumnWidth(i, 3000);
                            break;
                        case 6:
                            sheet.SetColumnWidth(i, 2500);
                            break;
                        case 7:
                            sheet.SetColumnWidth(i, 3500);
                            break;
                        default:
                            break;
                    }
                }
                else
                {
                    switch ((i - commonHead.Length - maxPaymentCount * paymentHead.Length) % refundHead.Length)
                    {
                        case 0:
                            sheet.SetColumnWidth(i, 7200);
                            break;
                        case 1:
                            sheet.SetColumnWidth(i, 11000);
                            break;
                        case 2:
                            sheet.SetColumnWidth(i, 2900);
                            break;
                        case 3:
                            sheet.SetColumnWidth(i, 3000);
                            break;
                        case 4:
                            sheet.SetColumnWidth(i, 2500);
                            break;
                        case 5:
                            sheet.SetColumnWidth(i, 3500);
                            break;
                        default:
                            break;
                    }

                }
            }
        }
        [NonAction]
        public async Task ExportExcelInsertData(XSSFWorkbook workbook, ISheet sheet, List<RentOrder> l,
            string[] commonHead, string[] paymentHead, string[] refundHead, int maxPaymentCount, int maxRefundCount)
        {
            string nullStr = "【-】";
            int index = 0;
            int subIndex = 0;
            IFont fontProblem = workbook.CreateFont();
            fontProblem.Color = NPOI.HSSF.Util.HSSFColor.Red.Index;
            IFont fontHidden = workbook.CreateFont();
            fontHidden.Color = NPOI.HSSF.Util.HSSFColor.Grey50Percent.Index;
            IFont fontUseDeposit = workbook.CreateFont();
            fontUseDeposit.Color = NPOI.HSSF.Util.HSSFColor.Blue.Index;
            IFont fontUseCard = workbook.CreateFont();
            fontUseDeposit.Color = NPOI.HSSF.Util.HSSFColor.Green.Index;
            IDataFormat format = workbook.CreateDataFormat();
            for (int i = 0; i < l.Count; i++)
            {
                ICellStyle styleText = workbook.CreateCellStyle();
                styleText.Alignment = HorizontalAlignment.Center;
                styleText.DataFormat = format.GetFormat("General");
                ICellStyle styleMoney = workbook.CreateCellStyle();
                styleMoney.DataFormat = format.GetFormat("¥#,##0.00");
                ICellStyle styleMoneyProblem = workbook.CreateCellStyle();
                styleMoneyProblem.DataFormat = format.GetFormat("¥#,##0.00");
                styleMoneyProblem.SetFont(fontProblem);
                ICellStyle styleNum = workbook.CreateCellStyle();
                styleNum.DataFormat = format.GetFormat("0");
                ICellStyle styleDate = workbook.CreateCellStyle();
                styleDate.DataFormat = format.GetFormat("yyyy-MM-dd");
                ICellStyle styleTime = workbook.CreateCellStyle();
                styleTime.DataFormat = format.GetFormat("HH:mm:ss");
                RentOrder o = l[i];
                string type = "正常";
                if (o.hide == 1)
                {
                    styleText.SetFont(fontHidden);
                    styleDate.SetFont(fontHidden);
                    styleMoney.SetFont(fontHidden);
                    styleTime.SetFont(fontHidden);
                    styleNum.SetFont(fontHidden);
                    type = "隐匿";
                }
                for (int j = 0; j < o.payments.Count; j++)
                {
                    if (o.payments[j].pay_method.Trim().Equals("储值支付"))
                    {
                        styleText.SetFont(fontUseDeposit);
                        styleDate.SetFont(fontUseDeposit);
                        styleMoney.SetFont(fontUseDeposit);
                        styleTime.SetFont(fontUseDeposit);
                        styleNum.SetFont(fontUseDeposit);
                        type = "储值";
                        break;
                    }
                }
                if (o.totalCharge - o.totalCharge < 0)
                {
                    styleText.SetFont(fontProblem);
                    styleDate.SetFont(fontProblem);
                    styleMoney.SetFont(fontProblem);
                    styleTime.SetFont(fontProblem);
                    styleNum.SetFont(fontProblem);
                }
                if (o.pay_option.Equals("次卡支付"))
                {
                    type = "次卡";
                    styleText.SetFont(fontUseCard);
                    styleDate.SetFont(fontUseCard);
                    styleMoney.SetFont(fontUseCard);
                    styleTime.SetFont(fontUseCard);
                    styleNum.SetFont(fontUseCard);
                }
                if (o.pay_option.Equals("招待"))
                {
                    type = "招待";
                    styleDate.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Yellow.Index;
                    styleDate.FillPattern = FillPattern.SolidForeground;
                    styleTime.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Yellow.Index;
                    styleTime.FillPattern = FillPattern.SolidForeground;
                    styleNum.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Yellow.Index;
                    styleNum.FillPattern = FillPattern.SolidForeground;
                    styleMoney.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Yellow.Index;
                    styleMoney.FillPattern = FillPattern.SolidForeground;
                    styleText.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Yellow.Index;
                    styleText.FillPattern = FillPattern.SolidForeground;
                }
                index++;
                for (int j = 0; j < o.details.Count; j++)
                {
                    RentOrderDetail detail = o.details[j];
                    if (detail.returnStaffName.Trim().Equals(""))
                    {
                        await _db.RentOrderDetail.Entry(detail).Reference(d => d.returnMsa).LoadAsync();
                        if (detail.returnMsa != null)
                        {
                            await _db.memberSocialAccount.Entry(detail.returnMsa).Reference(m => m.member).LoadAsync();
                        }
                    }
                    subIndex++;
                    IRow dr = sheet.CreateRow(subIndex);
                    dr.Height = 500;
                    for (int k = 0; k < commonHead.Length; k++)
                    {
                        ICell cell = dr.CreateCell(k);
                        bool needMerge = false;
                        switch (k)
                        {
                            case 0:
                                if (j == 0)
                                {
                                    cell.SetCellValue(index);
                                    cell.CellStyle = styleNum;
                                }
                                needMerge = true;
                                break;
                            case 1:
                                cell.SetCellValue(subIndex);
                                cell.CellStyle = styleNum;
                                break;
                            case 2:
                                if (j == 0)
                                {
                                    cell.SetCellValue(type);
                                    cell.CellStyle = styleText;
                                }
                                needMerge = true;
                                break;
                            case 3:
                                if (j == 0)
                                {
                                    cell.SetCellValue(o.id);
                                    cell.CellStyle = styleNum;
                                }

                                needMerge = true;
                                break;
                            case 4:
                                if (j == 0)
                                {
                                    cell.SetCellValue(o.shop.Trim());
                                    cell.CellStyle = styleText;
                                }

                                needMerge = true;

                                break;
                            case 5:
                                if (j == 0)
                                {
                                    cell.SetCellValue(o.create_date);
                                    cell.CellStyle = styleDate;
                                }

                                needMerge = true;

                                break;
                            case 6:
                                if (j == 0)
                                {
                                    cell.SetCellValue(o.create_date);
                                    cell.CellStyle = styleTime;
                                }

                                needMerge = true;

                                break;
                            case 7:
                                if (j == 0)
                                {
                                    cell.SetCellValue((DateTime)o.finish_date);
                                    cell.CellStyle = styleDate;
                                }

                                needMerge = true;

                                break;
                            case 8:
                                if (j == 0)
                                {
                                    cell.SetCellValue((DateTime)o.finish_date);
                                    cell.CellStyle = styleTime;
                                }

                                needMerge = true;

                                break;
                            case 9:

                                cell.SetCellValue(j == 0 ? o.totalDeposit : 0);
                                cell.CellStyle = styleMoney;
                                needMerge = true;

                                break;
                            case 10:
                                double totalRental = o.totalRental;
                                cell.SetCellValue(j == 0 ? totalRental : 0);
                                if (totalRental < 0)
                                {
                                    cell.CellStyle = styleMoneyProblem;
                                }
                                else
                                {
                                    cell.CellStyle = styleMoney;
                                }
                                needMerge = true;
                                break;
                            case 11:
                                cell.SetCellValue(j == 0 ? o.totalReparation : 0);
                                cell.CellStyle = styleMoney;
                                needMerge = true;

                                break;
                            case 12:
                                cell.SetCellValue(j == 0 ? o.totalOvertimeCharge : 0);
                                cell.CellStyle = styleMoney;
                                needMerge = true;

                                break;
                            case 13:
                                cell.SetCellValue(j == 0 ? o.totalDiscount : 0);
                                cell.CellStyle = styleMoney;
                                needMerge = true;

                                break;
                            case 14:
                                cell.SetCellValue(j == 0 ? o.totalCharge : 0);
                                cell.CellStyle = styleMoney;
                                needMerge = true;

                                break;
                            case 15:
                                cell.SetCellValue(j == 0 ? o.totalRefund : 0);
                                cell.CellStyle = styleMoney;
                                needMerge = true;
                                break;
                            case 16:
                                double totalEarn = o.totalEarn;
                                cell.SetCellValue(j == 0 ? totalEarn : 0);
                                if (totalEarn != o.totalRental + o.totalOvertimeCharge + o.totalReparation)
                                {
                                    cell.CellStyle = styleMoneyProblem;
                                }
                                else
                                {
                                    cell.CellStyle = styleMoney;
                                }
                                needMerge = true;
                                break;
                            case 17:
                                string memo = o.memo + (o.order != null && o.order.memo != null ? o.order.memo.Trim() : "");
                                cell.SetCellValue(memo);
                                cell.CellStyle = styleText;
                                needMerge = true;
                                break;
                            case 18:
                                if (j == 0)
                                {
                                    cell.SetCellValue((o.receptMsa == null) ? "" : o.receptMsa.member.real_name);
                                    cell.CellStyle = styleText;
                                }

                                needMerge = true;
                                break;
                            case 19:
                                cell.SetCellValue(detail.rent_item_code);
                                cell.CellStyle = styleText;
                                break;
                            case 20:
                                cell.SetCellValue(detail.rent_item_class);
                                cell.CellStyle = styleText;
                                break;
                            case 21:
                                cell.SetCellValue(detail.rent_item_name);
                                cell.CellStyle = styleText;
                                break;
                            case 22:
                                cell.SetCellValue(detail.deposit);
                                cell.CellStyle = styleMoney;
                                break;
                            case 23:
                                cell.SetCellValue(j == 0 ? detail.unit_rental : 0);
                                cell.CellStyle = styleMoney;
                                break;
                            case 24:
                                double summary = 0;
                                for (int m = 0; m < o.rentalDetails.Count; m++)
                                {
                                    if (o.rentalDetails[m].item.id == detail.id)
                                    {
                                        summary += o.rentalDetails[m].rental;
                                    }
                                }
                                cell.SetCellValue(j == 0 ? summary : 0);
                                cell.CellStyle = styleMoney;
                                break;
                            case 25:
                                cell.SetCellValue(j == 0 ? detail.reparation : 0);
                                cell.CellStyle = styleMoney;
                                break;
                            case 26:
                                cell.SetCellValue(j == 0 ? detail.overtime_charge : 0);
                                cell.CellStyle = styleMoney;
                                break;
                            case 27:
                                if (detail.pick_date == null)
                                {
                                    cell.SetCellValue(nullStr);
                                    cell.CellStyle = styleText;
                                }
                                else
                                {
                                    cell.SetCellValue((DateTime)detail.pick_date);
                                    cell.CellStyle = styleDate;
                                }

                                break;
                            case 28:
                                if (detail.pick_date == null)
                                {
                                    cell.SetCellValue(nullStr);
                                    cell.CellStyle = styleText;
                                }
                                else
                                {
                                    cell.SetCellValue((DateTime)detail.pick_date);
                                    cell.CellStyle = styleTime;
                                }
                                break;
                            case 29:
                                cell.SetCellValue(detail.pickStaffName);
                                cell.CellStyle = styleText;
                                break;
                            case 30:
                                if (detail.start_date == null)
                                {
                                    cell.SetCellValue(nullStr);
                                    cell.CellStyle = styleText;
                                }
                                else
                                {
                                    cell.SetCellValue((DateTime)detail.start_date);
                                    cell.CellStyle = styleDate;
                                }
                                break;
                            case 31:
                                if (detail.start_date == null)
                                {
                                    cell.SetCellValue(nullStr);
                                    cell.CellStyle = styleText;
                                }
                                else
                                {
                                    cell.SetCellValue((DateTime)detail.start_date);
                                    cell.CellStyle = styleTime;
                                }
                                break;
                            case 32:
                                if (detail.real_end_date == null)
                                {
                                    cell.SetCellValue(nullStr);
                                    cell.CellStyle = styleText;
                                }
                                else
                                {
                                    cell.SetCellValue((DateTime)detail.real_end_date);
                                    cell.CellStyle = styleDate;
                                }
                                break;
                            case 33:
                                if (detail.real_end_date == null)
                                {
                                    cell.SetCellValue(nullStr);
                                    cell.CellStyle = styleText;
                                }
                                else
                                {
                                    cell.SetCellValue((DateTime)detail.real_end_date);
                                    cell.CellStyle = styleTime;
                                }
                                break;
                            case 34:
                                if (detail.return_date == null)
                                {
                                    cell.SetCellValue(nullStr);
                                    cell.CellStyle = styleText;
                                }
                                else
                                {
                                    cell.SetCellValue((DateTime)detail.return_date);
                                    cell.CellStyle = styleDate;
                                }
                                break;
                            case 35:
                                if (detail.return_date == null)
                                {
                                    cell.SetCellValue(nullStr);
                                    cell.CellStyle = styleText;
                                }
                                else
                                {
                                    cell.SetCellValue((DateTime)detail.return_date);
                                    cell.CellStyle = styleTime;
                                }
                                break;
                            case 36:
                                cell.SetCellValue(detail.returnStaffName);
                                cell.CellStyle = styleText;
                                break;
                            default:
                                break;
                        }
                        if (needMerge)
                        {
                            if (o.details.Count > 1 && j == o.details.Count - 1)
                            {
                                sheet.AddMergedRegion(new NPOI.SS.Util.CellRangeAddress(subIndex - o.details.Count + 1, subIndex, k, k));
                            }
                        }
                    }
                    for (int k = 0; k < maxPaymentCount; k++)
                    {
                        if (k < o.payments.Count)
                        {
                            OrderPayment payment = o.payments[k];
                            for (int m = 0; m < paymentHead.Length; m++)
                            {
                                int colIndex = commonHead.Length + k * paymentHead.Length + m;
                                ICell cell = dr.CreateCell(colIndex);
                                switch ((colIndex - commonHead.Length) % paymentHead.Length)
                                {
                                    case 0:
                                        if (j == 0)
                                        {
                                            cell.SetCellValue(payment.shop.Trim());
                                            cell.CellStyle = styleText;
                                        }

                                        break;
                                    case 1:
                                        if (j == 0)
                                        {
                                            cell.SetCellValue(payment.pay_method.Trim());
                                            cell.CellStyle = styleText;
                                        }

                                        break;
                                    case 2:
                                        if (j == 0)
                                        {
                                            cell.SetCellValue(payment.wepay_trans_id != null ? payment.wepay_trans_id.Trim() : "");
                                            cell.CellStyle = styleText;
                                        }

                                        break;
                                    case 3:
                                        if (j == 0)
                                        {
                                            cell.SetCellValue(payment.out_trade_no != null ? payment.out_trade_no.Trim() : "");
                                            cell.CellStyle = styleText;
                                        }

                                        break;
                                    case 4:
                                        cell.SetCellValue(j == 0 ? payment.amount : 0);
                                        cell.CellStyle = styleMoney;
                                        break;
                                    case 5:
                                        if (j == 0)
                                        {
                                            cell.SetCellValue(payment.create_date);
                                            cell.CellStyle = styleDate;
                                        }

                                        break;
                                    case 6:
                                        if (j == 0)
                                        {
                                            cell.SetCellValue(payment.create_date);
                                            cell.CellStyle = styleTime;
                                        }

                                        break;
                                    case 7:
                                        if (j == 0)
                                        {
                                            string staffName = (payment.msa != null && payment.msa.member != null) ? payment.msa.member.real_name : "";
                                            cell.SetCellValue(staffName);
                                            cell.CellStyle = styleText;
                                        }

                                        break;
                                    default:
                                        break;
                                }
                                if (o.details.Count > 1 && j == o.details.Count - 1)
                                {
                                    sheet.AddMergedRegion(new NPOI.SS.Util.CellRangeAddress(subIndex - o.details.Count + 1, subIndex, colIndex, colIndex));
                                }
                            }
                        }
                        else
                        {
                            for (int m = 0; m < paymentHead.Length; m++)
                            {
                                int colIndex = commonHead.Length + k * paymentHead.Length + m;
                                ICell cell = dr.CreateCell(colIndex);
                                cell.SetCellValue(nullStr);
                                cell.CellStyle = styleText;
                                if (o.details.Count > 1 && j == o.details.Count - 1)
                                {
                                    sheet.AddMergedRegion(new NPOI.SS.Util.CellRangeAddress(subIndex - o.details.Count + 1, subIndex, colIndex, colIndex));
                                }
                            }
                        }
                    }
                    for (int k = 0; k < maxRefundCount; k++)
                    {
                        if (k < o.refunds.Count)
                        {
                            OrderPaymentRefund refund = o.refunds[k];
                            for (int m = 0; m < refundHead.Length; m++)
                            {
                                int colIndex = commonHead.Length + maxPaymentCount * paymentHead.Length + k * refundHead.Length + m;
                                ICell cell = dr.CreateCell(colIndex);
                                switch ((colIndex - commonHead.Length - maxPaymentCount * paymentHead.Length) % refundHead.Length)
                                {
                                    case 0:
                                        if (j == 0)
                                        {
                                            cell.SetCellValue(refund.refund_id != null ? refund.refund_id.Trim() : "");
                                            cell.CellStyle = styleText;
                                        }

                                        break;
                                    case 1:
                                        if (j == 0)
                                        {
                                            cell.SetCellValue(refund.out_refund_no != null ? refund.out_refund_no.Trim() : "");
                                            cell.CellStyle = styleText;
                                        }

                                        break;
                                    case 2:

                                        cell.SetCellValue(j == 0 ? refund.amount : 0);
                                        cell.CellStyle = styleMoney;
                                        break;
                                    case 3:
                                        if (j == 0)
                                        {
                                            cell.SetCellValue(refund.create_date);
                                            cell.CellStyle = styleDate;
                                        }

                                        break;
                                    case 4:
                                        if (j == 0)
                                        {
                                            cell.SetCellValue(refund.create_date);
                                            cell.CellStyle = styleTime;
                                        }

                                        break;
                                    case 5:
                                        if (j == 0)
                                        {
                                            string staffName = "";
                                            cell.SetCellValue(staffName);
                                            cell.CellStyle = styleText;
                                        }
                                        break;
                                    default:
                                        break;
                                }
                                if (o.details.Count > 1 && j == o.details.Count - 1)
                                {
                                    sheet.AddMergedRegion(new NPOI.SS.Util.CellRangeAddress(subIndex - o.details.Count + 1, subIndex, colIndex, colIndex));
                                }
                            }
                        }
                        else
                        {
                            for (int m = 0; m < refundHead.Length; m++)
                            {
                                int colIndex = commonHead.Length + maxPaymentCount * paymentHead.Length + k * refundHead.Length + m;
                                ICell cell = dr.CreateCell(colIndex);
                                cell.SetCellValue(nullStr);
                                cell.CellStyle = styleText;
                                if (o.details.Count > 1 && j == o.details.Count - 1)
                                {
                                    sheet.AddMergedRegion(new NPOI.SS.Util.CellRangeAddress(subIndex - o.details.Count + 1, subIndex, colIndex, colIndex));
                                }
                            }
                        }
                    }
                }
            }
        }
        [HttpGet]
        public async Task ExportExcel()
        {
            List<RentOrder> rList = await GetAllFinishedRentOrder();
            int maxPaymentCount = 0;
            int maxRefundCount = 0;
            for (int i = 0; i < rList.Count; i++)
            {
                RentOrder order = rList[i];
                maxPaymentCount = Math.Max(maxPaymentCount, order.payments.Count);
                maxRefundCount = Math.Max(maxRefundCount, order.refunds.Count);
            }
            //类型：正常，招待，储值，隐匿
            string[] commonHead = new string[] { "序号", "子序号", "类型", "订单号", "门店", "业务日期", "业务时间", "结算日期", "结算时间", "总计押金", "总计租金", "总计赔偿", "总计超时",
                "总计减免", "总计实收", "总计退款", "结余", "备注","接待", "物品编号", "物品分类", "物品名称", "押金", "租金单价", "租金小计" ,  "赔偿", "超时", "发放日期", "发放时间", "发放人",
                "起租日期", "起租时间", "退租日期", "退租时间", "归还日期", "归还时间", "接收人" };
            string[] paymentHead = new string[] { "收款门店", "支付方式", "微信支付单号", "商户订单号", "金额", "收款日期", "收款时间", "收款人" };
            string[] refundHead = new string[] { "退款单号", "商户退款单号", "退款金额", "退款日期", "退款时间", "退款人" };
            XSSFWorkbook workbook = new XSSFWorkbook();
            ISheet sheet = workbook.CreateSheet("24-25租赁");
            ExportExcelCreateHead(workbook, sheet, commonHead, paymentHead, refundHead, maxPaymentCount, maxRefundCount);
            await ExportExcelInsertData(workbook, sheet, rList, commonHead, paymentHead, refundHead, maxPaymentCount, maxRefundCount);
            string filePath = $"{Environment.CurrentDirectory}" + "/rent.xlsx";
            using (var file = System.IO.File.Create(filePath))
            {
                workbook.Write(file);
            }
        }
        [HttpGet]
        public async Task<ActionResult<List<Balance>>> GetBalance(string shop, DateTime startDate, DateTime endDate, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            shop = Util.UrlDecode(shop).Replace("'", "").Trim();
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (!shop.Trim().Equals("万龙") && user.member.is_admin != 1 && user.member.is_manager != 1)
            {
                return NoContent();
            }
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            var idList = await _db.idList.FromSqlRaw(" select distinct rent_list_id as id from rent_list_detail  "
                + " left join rent_list on rent_list.[id] = rent_list_id "
                + " where finish_date >= '" + startDate.ToShortDateString() + "' "
                + " and finish_date <= '" + endDate.AddDays(1).ToShortDateString() + "' and shop like '" + shop + "%'  "
                + " and finish_date is not null and closed = 0 "
                )
                .AsNoTracking().ToListAsync();
            List<Balance> bList = new List<Balance>();
            for (int i = 0; i < idList.Count; i++)
            {
                RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(idList[i].id, sessionKey, false)).Result).Value;
                if (order.totalDepositPaidAmount > 0)
                {
                    continue;
                }
                if (!order.status.Trim().Equals("已完成"))
                {
                    continue;
                }
                if (order.order == null)
                {
                    continue;
                }
                double totalPayment = 0;
                double totalRefund = 0;
                for (int j = 0; order.order != null && j < order.order.paymentList.Count; j++)
                {
                    OrderPayment payment = order.order.payments[j];
                    if (payment.status.Equals("支付成功") && !payment.pay_method.Equals("储值支付"))
                    {
                        totalPayment += order.order.payments[j].amount;
                    }
                }
                for (int j = 0; order.refunds != null && j < order.refunds.Count; j++)
                {
                    if (!order.refunds[j].refund_id.Trim().Equals("") || order.refunds[j].state == 1)
                    {
                        totalRefund += order.refunds[j].amount;
                    }
                }
                double totalReparation = 0;
                double totalRental = 0;
                for (int j = 0; j < order.details.Count; j++)
                {
                    totalReparation += order.details[j].reparation;
                    RentOrderDetail detail = order.details[j];
                    double subRental = Math.Round(detail.suggestRental, 2) - Math.Round(detail.rental_ticket_discount, 2)
                        - Math.Round(detail.rental_discount, 2) + Math.Round(detail.overtime_charge, 2);
                    totalRental += subRental;
                }

                Balance b = new Balance()
                {
                    id = order.id,
                    shop = order.shop,
                    name = order.real_name.Trim(),
                    cell = order.cell_number.Trim(),
                    settleDate = order.finish_date,
                    deposit = totalPayment,
                    refund = totalRefund,
                    earn = totalPayment - totalRefund,
                    reparation = totalReparation,
                    staff = order.staff_name,
                    payMethod = order.order.pay_method.Trim(),
                    rental = totalRental
                };
                try
                {
                    if (b.settleDate >= startDate && ((DateTime)b.settleDate).Date <= endDate.Date)
                    {
                        bList.Add(b);
                    }
                }
                catch
                {

                }

            }
            return Ok(bList.OrderByDescending(b => b.id).ToList());
        }
        [HttpGet("{cell}")]
        public async Task<ActionResult<RentOrder[]>> GetRentOrderListByCell(string cell, string sessionKey, string status = "", string shop = "")
        {
            cell = cell.Trim();
            if (cell.Length < 4)
            {
                return BadRequest();
            }
            if (cell.Length > 4)
            {
                RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(int.Parse(cell), sessionKey, false)).Result).Value;
                return Ok(new RentOrder[] { rentOrder });
            }
            shop = Util.UrlDecode(shop).Trim();
            status = Util.UrlDecode(status).Trim();
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            int orderId = 0;
            if (cell.Trim().Length == 4)
            {
                try
                {
                    orderId = int.Parse(cell);
                }
                catch
                {

                }
            }
            var orderListTemp = await _db.RentOrder
                .Where(o => ((o.cell_number.EndsWith(cell) || o.id == orderId) && (shop.Equals("") || o.shop.Trim().Equals(shop)))
                && o.create_date.Date > DateTime.Parse("2024-10-15"))
                .OrderByDescending(o => o.id).ToListAsync();
            if (orderListTemp == null || orderListTemp.Count <= 0)
            {
                return NotFound();
            }
            List<RentOrder> orderList = new List<RentOrder>();
            for (int i = 0; i < orderListTemp.Count; i++)
            {
                RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(orderListTemp[i].id, sessionKey, false)).Result).Value;
                if (status.Equals("") || order.status.Trim().Equals(status))
                {
                    orderList.Add(order);
                }
            }
            return Ok(orderList.ToArray<RentOrder>());
        }
        [NonAction]
        public async Task RestoreStaffInfo(RentOrder order)
        {
            var receptList = await _db.recept.Where(r => r.submit_return_id == order.id)
                .AsNoTracking().ToListAsync();
            if (receptList != null && receptList.Count > 0)
            {
                order.staff_open_id = receptList[0].update_staff.Trim();
                Member? staffUser = await _memberHelper.GetWholeMemberByNum(order.staff_open_id, "wechat_mini_openid");
                order.staff_name = staffUser == null ? "" : staffUser.real_name;
                _db.RentOrder.Entry(order);
                await _db.SaveChangesAsync();
            }
        }
        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrder>> GetRentOrder(int id, string sessionKey, bool needAuth = true)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            List<RentOrder> rentOrderList = await _db.RentOrder
                .Include(r => r.details.OrderBy(d => d.package_code).OrderBy(d => d.id))
                    .ThenInclude(d => d.log)
                .Include(r => r.order)
                    .ThenInclude(o => o.paymentList.Where(p => p.status.Trim().Equals("支付成功")).OrderByDescending(p => p.id))
                        .ThenInclude(p => p.refunds.Where(r => (r.state == 1 || !r.refund_id.Trim().Equals(""))).OrderByDescending(r => r.id))
                .Include(r => r.additionalPayments)
                    .ThenInclude(a => a.order)
                        .ThenInclude(o => o.paymentList.Where(p => p.status.Equals("支付成功")).OrderByDescending(p => p.id))
                            .ThenInclude(p => p.refunds.Where(r => r.state == 1).OrderByDescending(r => r.id))
                .Include(r => r.rewards.OrderByDescending(r => r.id))
                    .ThenInclude(r => r.rentRewardRefunds.OrderByDescending(r => r.id))
                        .ThenInclude(r => r.refund)
                .Where(r => r.id == id).ToListAsync();
            if (rentOrderList.Count == 0)
            {
                rentOrderList = await _db.RentOrder
                .Include(r => r.details)
                    .ThenInclude(d => d.log)
                .Where(r => r.id == id).ToListAsync();
            }
            if (rentOrderList.Count == 0)
            {
                return NotFound();
            }
            RentOrder rentOrder = rentOrderList[0];
            if (needAuth)
            {
                if (rentOrder == null)
                {
                    return NotFound();
                }
                if (rentOrder.staff_open_id.Trim().Equals("") || rentOrder.staff_name == null || rentOrder.staff_name.Trim().Equals(""))
                {
                    try
                    {
                        await RestoreStaffInfo(rentOrder);
                    }
                    catch
                    {

                    }
                }
            }
            if (rentOrder.order != null)
            {
                List<MemberSocialAccount> msaList = await _db.memberSocialAccount
                    .Where(m => (m.num.Trim().Equals(rentOrder.staff_open_id) && m.type.Trim().Equals("wechat_mini_openid")))
                    .Include(m => m.member).ToListAsync();
                if (msaList != null && msaList.Count > 0)
                {
                    //rentOrder.order.msa = msaList[0];
                }
                for (int i = 0; i < rentOrder.refunds.Count; i++)
                {
                    OrderPaymentRefund r = rentOrder.refunds[i];
                    msaList = await _db.memberSocialAccount
                        .Where(m => (m.num.Trim().Equals(r.oper) && m.type.Trim().Equals("wechat_mini_openid")))
                        .Include(m => m.member).ToListAsync();
                    if (msaList != null && msaList.Count > 0)
                    {
                        //r.msa = msaList[0];
                    }
                }
            }
            bool allReturned = true;
            DateTime returnTime = rentOrder.create_date;
            for (int i = 0; i < rentOrder.details.Count; i++)
            {
                DateTime endDate = DateTime.Now;
                RentOrderDetail detail = rentOrder.details[i];
                if (detail.real_end_date != null)
                {
                    endDate = (DateTime)detail.real_end_date;
                }
                DateTime endTime = DateTime.Now;
                if (detail.real_end_date != null)
                {
                    endTime = (DateTime)detail.real_end_date;
                }

                if (rentOrder.start_date.Hour >= 16 && rentOrder.start_date.Date == endTime.Date)
                {
                    detail.overTime = false;
                }
                else if (endTime.Hour >= 18)
                {
                    detail.overTime = true;
                }
                else
                {
                    detail.overTime = false;

                }
                if (!detail.rentStatus.Trim().Equals("已归还"))
                {
                    allReturned = false;
                }
                else
                {
                    if (detail.real_end_date != null)
                    {
                        returnTime = detail.real_end_date > returnTime ? (DateTime)detail.real_end_date : returnTime;
                    }
                }
                switch (rentOrder.shop.Trim())
                {
                    case "南山":
                        TimeSpan ts = endDate - rentOrder.start_date;
                        detail._suggestRental = detail.unit_rental * (ts.Days + 1);
                        detail._timeLength = (ts.Days + 1).ToString() + "天";
                        break;
                    default:

                        if (rentOrder.start_date.Date == endDate.Date && rentOrder.start_date.Hour >= 16)
                        {
                            detail._suggestRental = detail.unit_rental;
                            detail._timeLength = "夜场";
                        }
                        else
                        {
                            if (detail.start_date == null)
                            {
                                detail._timeLength = "--";
                            }
                            else
                            {
                                TimeSpan ts1 = endDate.Date - ((DateTime)detail.start_date).Date;
                                int days = ts1.Days;
                                days++;
                                detail._suggestRental = detail.unit_rental * days;
                                detail._timeLength = days.ToString() + "天";
                            }

                        }
                        break;
                }
            }

            if (allReturned && rentOrder.order != null && !rentOrder.order.pay_method.Trim().Equals("微信支付")
                && rentOrder.end_date == null)
            {
                rentOrder.end_date = returnTime;
                _db.RentOrder.Entry(rentOrder).State = EntityState.Modified;
                await _db.SaveChangesAsync();
            }
            if (rentOrder.staff_name.Trim().Equals(""))
            {
                rentOrder.staff_name = rentOrder.order == null ? "" : rentOrder.order.staffName.Trim();
            }

            if (rentOrder.staff_name.Trim().Equals(""))
            {
                if (rentOrder.recept != null && rentOrder.recept.Count > 0)
                {

                    rentOrder.staff_name = rentOrder.recept[0].update_staff_name.Trim().Equals("") ?
                        rentOrder.recept[0].recept_staff_name : rentOrder.recept[0].update_staff_name.Trim();
                    if (rentOrder.staff_name.Trim().Equals(""))
                    {
                        try
                        {
                            string staffOpenId = rentOrder.recept[0].update_staff.Trim().Equals("") ?
                                rentOrder.recept[0].recept_staff.Trim() : rentOrder.recept[0].update_staff.Trim();
                            //MiniAppUser? staffUser = await _db.MiniAppUsers.FindAsync(staffOpenId.Trim());
                            Member? staffUser = await _memberHelper.GetWholeMemberByNum(staffOpenId.Trim(), "wechat_mini_openid");
                            if (staffUser != null)
                            {
                                rentOrder.staff_name = staffUser.real_name.Trim();
                            }
                        }
                        catch
                        {

                        }
                    }
                }

            }


            if (rentOrder.pay_option.Trim().Equals("招待"))
            {
                rentOrder.backColor = "yellow";
            }
            if (rentOrder.order != null)
            {
                if (rentOrder.order.pay_state == 0)
                {
                    rentOrder.backColor = "red";
                    if (rentOrder.status.Trim().Equals("已关闭"))
                    {
                        rentOrder.backColor = "";
                    }
                }

                if (!rentOrder.order.pay_method.Trim().Equals("微信支付") && rentOrder.status.Equals("全部归还"))
                {
                    rentOrder.textColor = "red";
                }
            }
            else
            {
                if (rentOrder.pay_option.Trim().Equals("招待"))
                {
                    rentOrder.backColor = "yellow";
                }
                else
                {
                    rentOrder.backColor = "pink";
                }

            }

            if (rentOrder.status.Equals("已退款") || rentOrder.status.Equals("已完成"))
            {
                rentOrder.textColor = "red";
            }
            if (rentOrder.status.Trim().Equals("已关闭"))
            {
                rentOrder.textColor = "#C0C0C0";
            }
            for (int i = 0; rentOrder.additionalPayments != null
                && i < rentOrder.additionalPayments.Count; i++)
            {
                RentAdditionalPayment p = rentOrder.additionalPayments[i];
                List<MemberSocialAccount> msaL = await _db.memberSocialAccount
                    .Where(m => m.type.Trim().Equals("wechat_mini_openid") && m.num.Trim().Equals(p.staff_open_id.Trim()))
                    .Include(m => m.member).AsNoTracking().ToListAsync();
                if (msaL != null && msaL.Count > 0)
                {
                    p.staffMember = msaL[0].member;
                }
            }
            var ret = Ok(rentOrder);
            return ret;
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrderDetailLog>> SetDetailLog(int id, string status,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            status = Util.UrlDecode(status);
            RentOrderDetail detail = await _db.RentOrderDetail.FindAsync(id);
            switch (status)
            {
                case "已发放":
                    if (detail.pick_date == null)
                    {
                        detail.pick_date = DateTime.Now;
                    }
                    break;
                case "已归还":
                    if (detail.return_date == null)
                    {
                        detail.return_date = DateTime.Now;
                    }
                    break;
                default:
                    break;
            }

            RentOrderDetailLog log = new RentOrderDetailLog()
            {
                id = 0,
                detail_id = id,
                status = status,
                staff_open_id = user.miniAppOpenId,
                prev_value = detail.rent_status == null ? "" : detail.rent_status.Trim(),
                create_date = DateTime.Now
            };
            detail.rent_status = status.Trim();
            detail.update_date = DateTime.Now;
            _db.RentOrderDetail.Entry(detail).State = EntityState.Modified;
            await _db.rentOrderDetailLog.AddAsync(log);
            await _db.SaveChangesAsync();
            return Ok(log);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrderDetail>> SetUnReturn(int id, string sessionKey)
        {
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentOrderDetail detail = await _db.RentOrderDetail.FindAsync(id);
            detail.real_end_date = null;
            _db.Entry(detail).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            await SetDetailLog(id, "已发放", sessionKey);
            detail.log = await _db.rentOrderDetailLog.Where(l => l.detail_id == detail.id)
                .OrderByDescending(l => l.id).AsNoTracking().ToListAsync();
            return Ok(detail);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrderDetail>> SetReturn(int id, float rental,
            double reparation, DateTime returnDate, string memo, string sessionKey, double overTimeCharge = 0)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            memo = Util.UrlDecode(memo);
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentOrderDetail detail = await _db.RentOrderDetail.FindAsync(id);
            detail.real_end_date = returnDate;
            detail.real_rental = rental;
            detail.reparation = reparation;
            detail.memo = memo.Trim();
            detail.overtime_charge = overTimeCharge;
            detail.return_staff = user.miniAppOpenId.Trim();
            _db.Entry(detail).State = EntityState.Modified;
            await _db.SaveChangesAsync();


            await SetDetailLog(id, "已归还", sessionKey);
            detail.log = await _db.rentOrderDetailLog.Where(l => l.detail_id == detail.id)
                .OrderByDescending(l => l.id).ToListAsync();


            bool allReturned = true;

            double rentalTotal = 0;

            RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder((int)detail.rent_list_id, sessionKey, false)).Result).Value;

            for (int i = 0; i < rentOrder.details.Count; i++)
            {
                RentOrderDetail item = rentOrder.details[i];
                rentalTotal = rentalTotal + item.real_rental + item.overtime_charge + item.reparation;
                if (detail.status.Trim().Equals("未归还"))
                {
                    allReturned = false;
                    //break;
                }
            }
            if (allReturned && Math.Round(rentalTotal, 2) >= Math.Round(rentOrder.deposit_final, 2))
            {
                rentOrder.end_date = DateTime.Now;
                _db.Entry(rentOrder);
                await _db.SaveChangesAsync();
            }
            return Ok(detail);
        }
        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrderDetail>> SetPick(int id,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentOrderDetail detail = await _db.RentOrderDetail.FindAsync(id);

            RentOrderDetailLog log = new RentOrderDetailLog()
            {
                id = 0,
                detail_id = detail.id,
                status = RentOrderDetail.RentStatus.已发放.ToString(),
                prev_value = detail.rent_status.Trim(),
                staff_open_id = user.miniAppOpenId.Trim(),
                create_date = DateTime.Now
            };
            await _db.rentOrderDetailLog.AddAsync(log);
            detail.rent_status = RentOrderDetail.RentStatus.已发放.ToString();
            detail.update_date = DateTime.Now;
            _db.RentOrderDetail.Entry(detail).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(detail);
        }
        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrder>> SetPaidManual(int id, string payMethod, string sessionKey)
        {
            RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(id, sessionKey, false)).Result).Value;
            sessionKey = Util.UrlDecode(sessionKey);
            payMethod = Util.UrlDecode(payMethod);
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            if (rentOrder == null || rentOrder.order == null)
            {
                return NotFound();
            }
            if (rentOrder.order.payments == null || rentOrder.order.paymentList.Count == 0)
            {
                OrderPayment payment = new OrderPayment()
                {
                    id = 0,
                    pay_method = payMethod,
                    amount = rentOrder.order.final_price,
                    staff_open_id = user.miniAppOpenId,
                    order_id = rentOrder.order.id
                };
                await _db.OrderPayment.AddAsync(payment);
            }
            OrderOnline order = await _db.OrderOnlines.FindAsync(rentOrder.order_id);
            order.pay_method = payMethod;
            _db.Entry(order).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return await SetPaid(id, sessionKey);

        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrder>> SetPaid(int id, string sessionKey)
        {
            RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(id, sessionKey, false)).Result).Value;
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }

            if (rentOrder == null || rentOrder.order == null
                || rentOrder.order.payments == null || rentOrder.order.paymentList.Count <= 0)
            {
                return NotFound();
            }
            OrderPayment payment = rentOrder.order.payments[0];
            OrderOnline order = rentOrder.order;
            payment.status = "支付成功";
            order.pay_state = 1;
            order.pay_time = DateTime.Now;
            _db.Entry(payment).State = EntityState.Modified;
            _db.Entry(order).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(rentOrder);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrder>> Bind(int id, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _db);
            RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(id, sessionKey, false)).Result).Value;
            if (rentOrder == null)
            {
                return NotFound();
            }
            if (rentOrder.open_id.Trim().Equals(""))
            {
                rentOrder.open_id = user.miniAppOpenId;
                _db.Entry(rentOrder).State = EntityState.Modified;
            }
            if (rentOrder.order != null && rentOrder.open_id.Trim().Equals(""))
            {
                OrderOnline order = rentOrder.order;
                order.open_id = user.miniAppOpenId.Trim();
                _db.Entry(order).State = EntityState.Modified;
                if (order.payments != null && order.paymentList.Count > 0)
                {
                    OrderPayment pay = order.payments[0];
                    if (pay.open_id.Trim().Equals(""))
                    {
                        pay.open_id = user.miniAppOpenId.Trim();
                        _db.Entry(pay).State = EntityState.Modified;
                    }

                }
            }
            await _db.SaveChangesAsync();
            return Ok(rentOrder);
        }


        [HttpGet]
        public async Task<ActionResult<RentOrderCollection>> GetUnSettledOrderBefore(DateTime date, string sessionKey, string shop = "")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            shop = Util.UrlDecode(shop);
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }

            var rentOrderList = await _db.RentOrder.FromSqlRaw(" select  * from rent_list  "
                + " where create_date < '" + date.ToShortDateString() + "' and create_date > '" + startDate.ToShortDateString() + "' "
                + " and exists ( select 'a' from rent_list_detail  where rent_list_detail.rent_list_id = rent_list.id and "
                + " (real_end_date is null or real_end_date >= '" + date.ToShortDateString() + "' )) "
                + (shop.Trim().Equals("") ? " " : " and shop = '" + shop.Replace("'", "").Trim()
                + "' and closed = 0  and (finish_date >  '" + date.ToShortDateString() + "' or finish_date is null) "
                //+ " and [id] = 6290 "
                ))
                .ToListAsync();

            RentOrder[] orderArr = new RentOrder[rentOrderList.Count];
            double totalDeposit = 0;
            double totalRental = 0;
            List<RentOrder> list = new List<RentOrder>();

            for (int i = 0; i < orderArr.Length; i++)
            {
                RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderList[i].id, sessionKey, false)).Result).Value;
                if (order.status.Equals("已付押金") || order.status.Equals("已退款"))
                {
                    list.Add(order);
                    //list.Append(order);
                }
                else
                {
                    Console.WriteLine(order.id.ToString() + " " + order.status);
                    continue;
                }

                totalDeposit += order.GetChargedDeposit(date);
                double subTotalRental = 0;
                for (int j = 0; j < order.rentalDetails.Count; j++)
                {
                    SnowmeetApi.Models.Rent.RentalDetail detail = order.rentalDetails[j];
                    if (detail.date.Date < date.Date)
                    {
                        subTotalRental = subTotalRental + detail.rental;
                    }

                }
                totalRental = totalRental + subTotalRental;
            }

            RentOrderCollection sum = new RentOrderCollection();
            sum.date = date.Date;
            sum.type = "当日前未完结";
            sum.count = list.Count;
            sum.unRefundDeposit = totalDeposit;
            sum.unSettledRental = totalRental;
            sum.orders = list.ToArray();
            return Ok(sum);
        }

        [HttpGet]
        public async Task<ActionResult<RentOrderCollection>> GetCurrentSameDaySettled(DateTime date, string sessionKey, string shop = "")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            shop = Util.UrlDecode(shop);
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            var rentOrderList = await _db.RentOrder
                .Where(r => (r.create_date.Date == date.Date && r.create_date >= startDate
                    && ((DateTime)r.end_date).Date == date.Date
                    && r.order_id != 0 && (shop.Trim().Equals("") || shop.Trim().Equals(r.shop.Trim()))))
                .Join(_db.OrderOnlines, r => r.order_id, o => o.id,
                    (r, o) => new { r.id, r.start_date, r.end_date, o.pay_state, o.final_price, r.deposit_final, r.refund })
                .Where(o => o.pay_state == 1)
                .ToListAsync();

            double totalDeposit = 0;
            double totalRental = 0;
            List<RentOrder> orderArr = new List<RentOrder>();
            //RentOrder[] orderArr = new RentOrder[rentOrderList.Count];
            for (int i = 0; i < rentOrderList.Count; i++)
            {
                RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderList[i].id, sessionKey, false)).Result).Value;
                //orderArr[i] = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderList[i].id, sessionKey)).Result).Value;
                if (!order.status.Trim().Equals("已退款")
                    && !order.status.Trim().Equals("全部归还")
                    && !order.status.Trim().Equals("已完成"))
                {
                    continue;

                }
                orderArr.Add(order);
                totalDeposit = order.GetChargedDeposit(date.AddDays(1)) + totalDeposit;
                double subTotalRental = 0;
                for (int j = 0; j < order.rentalDetails.Count; j++)
                {
                    SnowmeetApi.Models.Rent.RentalDetail detail = order.rentalDetails[j];
                    subTotalRental = subTotalRental + detail.rental;
                }
                totalRental = totalRental + subTotalRental;
            }
            RentOrderCollection sum = new RentOrderCollection();
            sum.date = date.Date;
            sum.type = "日租日结";
            sum.totalDeposit = totalDeposit;
            sum.totalRental = totalRental;
            sum.orders = orderArr.ToArray<RentOrder>();
            sum.count = sum.orders.Length;
            return Ok(sum);
        }

        [HttpGet]
        public async Task<ActionResult<RentOrderCollection>> GetCurrentDayPlaced(DateTime date, string sessionKey, string shop = "")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            shop = Util.UrlDecode(shop).Trim();
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            var rentOrderList = await _db.RentOrder
                .Where(r => (r.create_date.Date == date.Date && r.create_date >= startDate
                && (shop.Equals("") || shop.Equals(r.shop.Trim()))))
                .Join(_db.OrderOnlines, r => r.order_id, o => o.id,
                    (r, o) => new { r.id, r.start_date, r.end_date, o.pay_state, o.final_price, r.deposit_final, r.refund })
                .Where(o => o.pay_state == 1)
                .ToListAsync();

            double totalDeposit = 0;
            double totalRental = 0;
            RentOrder[] orderArr = new RentOrder[rentOrderList.Count];
            for (int i = 0; i < orderArr.Length; i++)
            {
                orderArr[i] = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderList[i].id, sessionKey, false)).Result).Value;
                totalDeposit = orderArr[i].GetChargedDeposit(date.AddDays(1)) + totalDeposit;
                double subTotalRental = 0;
                for (int j = 0; j < orderArr[i].rentalDetails.Count; j++)
                {
                    SnowmeetApi.Models.Rent.RentalDetail detail = orderArr[i].rentalDetails[j];
                    if (detail.date.Date <= date.Date)
                    {
                        subTotalRental = subTotalRental + detail.rental;
                    }
                }
                totalRental = totalRental + subTotalRental;
            }
            RentOrderCollection sum = new RentOrderCollection();
            sum.date = date.Date;
            sum.type = "当日新订单";
            sum.totalDeposit = totalDeposit;
            sum.totalRental = totalRental;
            sum.orders = orderArr;
            return Ok(sum);
        }

        [HttpGet]
        public async Task<ActionResult<RentOrderCollection>> GetCurrentDaySettledPlacedBefore(DateTime date, string sessionKey, string shop = "")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            shop = Util.UrlDecode(shop).Trim();
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            var rentOrderList = await _db.RentOrder
                .Where(r => ((shop.Equals("") || shop.Equals(r.shop.Trim()))
                    && r.finish_date != null && ((DateTime)r.finish_date).Date == date.Date)
                    && r.create_date.Date < date.Date)
                .Join(_db.OrderOnlines, r => r.order_id, o => o.id,
                    (r, o) => new { r.id, r.start_date, r.finish_date, o.pay_state, o.final_price, r.deposit_final, r.refund })
                .Where(o => o.pay_state == 1)
                .ToListAsync();

            double totalDeposit = 0;
            double totalRental = 0;
            List<RentOrder> orderArr = new List<RentOrder>();

            for (int i = 0; i < rentOrderList.Count; i++)
            {
                RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderList[i].id, sessionKey, false)).Result).Value;
                if (!order.status.Trim().Equals("已退款")
                    && !order.status.Trim().Equals("全部归还")
                    && !order.status.Trim().Equals("已完成"))
                {
                    continue;

                }
                orderArr.Add(order);
                totalDeposit = order.totalCharge + totalDeposit;
                double subTotalRental = 0;
                for (int j = 0; j < order.rentalDetails.Count; j++)
                {
                    SnowmeetApi.Models.Rent.RentalDetail detail = order.rentalDetails[j];
                    if (detail.date.Date <= date.Date)
                    {
                        subTotalRental = subTotalRental + detail.rental;
                    }
                }
                totalRental = totalRental + subTotalRental;
            }
            RentOrderCollection sum = new RentOrderCollection();
            sum.date = date.Date;
            sum.type = "当日新订单";
            sum.totalDeposit = totalDeposit;
            sum.totalRental = totalRental;
            sum.orders = orderArr.ToArray<RentOrder>();
            return Ok(sum);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrderDetail>> ModItemInfo(int id, float rental,
            double reparation, string memo, double overTimeCharge, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            memo = Util.UrlDecode(memo);
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentOrderDetail detail = await _db.RentOrderDetail.FindAsync(id);
            //detail.real_end_date = returnDate;
            detail.real_rental = rental;
            detail.reparation = reparation;
            detail.memo = memo.Trim();
            detail.overtime_charge = overTimeCharge;
            _db.Entry(detail).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(detail);
        }
        [HttpGet]
        public async Task<ActionResult<List<string>>> GetClassList()
        {
            List<string> list = new List<string>();
            list.Add("双板");
            list.Add("双板鞋");
            list.Add("雪杖");
            list.Add("单板");
            list.Add("单板鞋");
            list.Add("头盔");
            list.Add("雪镜");
            list.Add("雪服");
            list.Add("雪裤");
            list.Add("连体雪服");
            list.Add("手套");
            list.Add("护具");
            list.Add("电加热马甲");
            list.Add("运动相机");
            list.Add("无人机");
            list.Add("对讲机");
            var oriList = await _db.RentItem.Select(r => r.@class)
                .AsNoTracking().Distinct().ToListAsync();
            foreach (var ori in oriList)
            {
                bool exists = false;
                foreach (var l in list)
                {
                    if (ori.ToString().Equals(l.ToString()))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists && !ori.ToString().Equals("其他")
                    && ori.ToString().IndexOf("电子") < 0
                    && ori.ToString().IndexOf("雪服上衣") < 0)
                {
                    list.Add(ori.ToString());
                }
            }
            list.Add("其他");
            return Ok(list);
        }
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SnowmeetApi.Models.Rent.RentalDetail>>> GetRentDetailReport(DateTime start, DateTime end, string sessionKey)
        {
            ArrayList details = new ArrayList();
            RentOrderCollection beforeOrders = (RentOrderCollection)((OkObjectResult)(await GetUnSettledOrderBefore(start, sessionKey)).Result).Value;
            for (int i = 0; i < beforeOrders.orders.Length; i++)
            {
                RentOrder order = beforeOrders.orders[i];
                for (int j = 0; j < order.rentalDetails.Count; j++)
                {
                    if (order.rentalDetails[j].date >= start.Date && order.rentalDetails[j].date <= end.Date)
                    {
                        SnowmeetApi.Models.Rent.RentalDetail dtl = order.rentalDetails[j];
                        dtl._name = order.real_name;
                        dtl._cell = order.cell_number;
                        dtl._shop = order.shop.Trim();
                        dtl._staff = order.staff_name.Trim();
                        details.Add(dtl);
                    }
                }
            }
            var rentOrderIdList = await _db.RentOrder
                .Where(r => (r.create_date.Date >= start.Date && r.create_date.Date <= end.Date))
                .Join(_db.OrderOnlines, r => r.order_id, o => o.id,
                    (r, o) => new { r.id, r.start_date, r.end_date, o.pay_state, o.final_price, r.deposit_final, r.refund, r.staff_name })
                .Where(o => o.pay_state == 1).ToListAsync();
            for (int i = 0; i < rentOrderIdList.Count; i++)
            {
                RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderIdList[i].id, sessionKey, false)).Result).Value;
                for (int j = 0; j < order.rentalDetails.Count; j++)
                {
                    DateTime rentDate = order.rentalDetails[j].date;
                    if (rentDate.Date >= start && rentDate.Date <= end)
                    {
                        SnowmeetApi.Models.Rent.RentalDetail dtl = order.rentalDetails[j];
                        dtl._name = order.real_name;
                        dtl._cell = order.cell_number;
                        dtl._shop = order.shop.Trim();
                        dtl._staff = order.staff_name.Trim();
                        details.Add(dtl);
                    }
                }
            }
            SnowmeetApi.Models.Rent.RentalDetail[] detailArr = new SnowmeetApi.Models.Rent.RentalDetail[details.Count];
            for (int i = 0; i < detailArr.Length; i++)
            {
                var dtl = details[i];
                detailArr[i] = (SnowmeetApi.Models.Rent.RentalDetail)dtl;
            }
            return Ok(detailArr);
        }
        [HttpGet("{orderId}")]
        public async Task<ActionResult<RentOrder>> SetMemo(int orderId, string memo, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            memo = Util.UrlDecode(memo).Trim();
            RentOrder? order = await _db.RentOrder.FindAsync(orderId);
            if (order == null)
            {
                return BadRequest();
            }
            string prevMemo = order.memo.Trim();
            order.memo = memo;
            _db.RentOrder.Entry(order).State = EntityState.Modified;
            RentOrderLog log = new RentOrderLog()
            {
                id = 0,
                rent_list_id = order.id,
                memo = "修改备注",
                field_name = "memo",
                prev_value = prevMemo.Trim(),
                oper_member_id = user.member.id
            };
            await _db.rentOrderLog.AddAsync(log);
            await _db.SaveChangesAsync();
            return Ok(order);
        }
        [HttpPost]
        public async Task<ActionResult<RentOrderDetail>> AppendDetail(string sessionKey, RentOrderDetail detail)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            switch (detail.deposit_type.Trim())
            {
                case "立即租赁":
                    detail.start_date = DateTime.Now;
                    detail.pick_date = DateTime.Now;
                    detail.rent_status = RentOrderDetail.RentStatus.已发放.ToString();
                    break;
                case "延时租赁":
                    detail.pick_date = DateTime.Now;
                    detail.start_date = ((DateTime)detail.pick_date).Date.AddDays(1);
                    detail.rent_status = RentOrderDetail.RentStatus.已发放.ToString();
                    break;
                case "先租后取":
                    detail.pick_date = null;
                    detail.start_date = DateTime.Now;
                    detail.rent_status = RentOrderDetail.RentStatus.未领取.ToString();
                    break;
                case "预约租赁":
                    detail.pick_date = null;
                    detail.start_date = null;
                    detail.rent_status = RentOrderDetail.RentStatus.未领取.ToString();
                    break;
                default:
                    break;
            }
            detail.memo = DateTime.Now.ToString() + " " + user.miniAppUser.real_name + " 追加";
            detail.rent_staff = user.miniAppOpenId.Trim();
            await _db.RentOrderDetail.AddAsync(detail);
            await _db.SaveChangesAsync();
            return Ok(detail);
        }
        [HttpPost]
        public async Task<ActionResult<SnowmeetApi.Models.Rent.RentalDetail>> UpdateDetail([FromQuery] string sessionKey, [FromBody] RentOrderDetail detail)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentOrderDetailLog log = new RentOrderDetailLog()
            {
                id = 0,
                detail_id = detail.id,
                status = "修改",
                staff_open_id = user.miniAppOpenId.Trim(),
                create_date = DateTime.Now
            };
            RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder((int)detail.rent_list_id, sessionKey, false)).Result).Value;
            for (int i = 0; i < order.details.Count; i++)
            {
                if (order.details[i].id == detail.id)
                {
                    if (order.details[i].start_date != null && order.details[i].start_date != detail.start_date)
                    {
                        log.status = "修改起租时间";
                        log.prev_value = order.details[i].start_date.ToString();
                    }
                    if (order.details[i].real_end_date != null && order.details[i].real_end_date != detail.real_end_date)
                    {
                        log.status = "修改退租时间";
                        log.prev_value = order.details[i].start_date.ToString();
                    }
                    if (order.details[i].pick_date != null && order.details[i].pick_date != detail.pick_date)
                    {
                        log.status = "修改发放时间";
                        log.prev_value = order.details[i].pick_date.ToString();
                    }
                    if (order.details[i].return_date != null && order.details[i].return_date != detail.return_date)
                    {
                        log.status = "修改归还时间";
                        log.prev_value = order.details[i].return_date.ToString();
                    }
                }
                _db.RentOrderDetail.Entry(order.details[i]).State = EntityState.Detached;
            }
            _db.RentOrder.Entry(order).State = EntityState.Detached;
            await _db.SaveChangesAsync();
            detail.rental_count = order.rentalDetails.Count;
            detail.update_date = DateTime.Now;
            _db.RentOrderDetail.Entry(detail).State = EntityState.Modified;
            _db.RentOrder.Entry(order).State = EntityState.Modified;

            await _db.rentOrderDetailLog.AddAsync(log);
            await _db.SaveChangesAsync();
            return Ok(detail);
        }
        [HttpGet("{detailId}")]
        public async Task<ActionResult<RentOrderDetail>> ReserveMore(int detailId, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentOrderDetail item = await _db.RentOrderDetail.FindAsync(detailId);
            item.id = 0;
            item.real_end_date = null;
            item.start_date = DateTime.Now.Date.AddDays(1);
            item.reparation = 0;
            item.overtime_charge = 0;
            item.deposit_type = "预约租赁";
            await _db.AddAsync(item);
            await _db.SaveChangesAsync();
            return Ok(item);
        }
        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrder>> SetClose(int id, string sessionKey)
        {
            UnicUser user = await Util.GetUser(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            var result = (await GetRentOrder(id, sessionKey, false)).Result;
            if (!result.GetType().Name.Trim().Equals("OkObjectResult"))
            {
                return NotFound();
            }
            RentOrder order = (RentOrder)((OkObjectResult)result).Value;
            if (order.status.Trim().Equals("未支付"))
            {
                order.closed = 1;
                _db.Entry(order).State = EntityState.Modified;
                await _db.SaveChangesAsync();
            }
            return Ok(order);
        }
        [NonAction]
        public async Task<UnicUser> GetUser(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _db);
            return user;
        }
        [HttpGet]
        public async Task<ActionResult<List<RentOrder>>> GetUnReturnedItems(string sessionKey, string shop)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            shop = Util.UrlDecode(shop);
            UnicUser user = await Util.GetUser(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            List<RentOrder> list = await GetUnReturnedItems(shop);
            return Ok(list);
        }
        [NonAction]
        public async Task<List<RentOrder>> GetUnReturnedItems(string shop = "")
        {
            var rentItemList = await _db.RentOrderDetail
                .FromSqlRaw(" select * from rent_list_detail  "
                + "  where  datepart(hh,rent_list_detail.start_date) <> 0 and  "
                + " datepart(mi,rent_list_detail.start_date) <> 0 "
                + " and datepart(s,rent_list_detail.start_date) <> 0 "
                + " and real_end_date is null order by [id] desc ")
                .AsNoTracking().ToListAsync();
            List<RentOrder> ret = new List<RentOrder>();
            for (int i = 0; i < rentItemList.Count; i++)
            {
                RentOrderDetail item = rentItemList[i];
                if (!item.status.Trim().Equals("已发放"))
                {
                    continue;
                }
                var rL = await _db.RentOrder.Where(r => r.id == item.rent_list_id)
                    .AsNoTracking().ToListAsync();
                if (rL == null || rL.Count == 0)
                {
                    continue;
                }
                RentOrder rentOrder = rL[0];
                if (rentOrder == null)
                {
                    continue;
                }
                if (rentOrder.order_id > 0)
                {
                    rentOrder.order = await _db.OrderOnlines.FindAsync(rentOrder.order_id);
                    rentOrder.order.paymentList = await _db.OrderPayment
                        .Where(p => p.order_id == rentOrder.order_id).ToListAsync();
                    rentOrder.order.refunds = await _db.orderPaymentRefund
                        .Where(r => r.order_id == rentOrder.order_id).ToListAsync();

                }
                rentOrder.details = (new RentOrderDetail[] { item }).ToList();
                if (!rentOrder.status.Equals("已关闭")
                    && !rentOrder.status.Equals("未支付")
                    && !rentOrder.status.Equals("已退款")
                    && !rentOrder.status.Equals("全部归还"))
                {
                    if (shop.Trim().Equals("") || rentOrder.shop.Trim().Equals(shop))
                    {

                        ret.Add(rentOrder);
                    }
                }
            }
            return ret;
        }
        [HttpGet]
        public async Task<ActionResult<RentOrderList>> GetRentOrderList(DateTime startDate, DateTime endDate, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await Util.GetUser(sessionKey, _db);
            if (user.member.is_admin != 1 && user.member.is_manager != 1)
            {
                return NoContent();
            }
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentOrderList list = new RentOrderList();
            list.items = new List<RentOrderList.ListItem>();
            var rentList = await _db.RentOrder.FromSqlRaw(" select * from rent_list where ( pay_option = '招待' "
                + " or exists ( select 'a' from order_online where rent_list.order_id = order_online.id and pay_state = 1 ) ) "
                + " and create_date >= '" + startDate.ToShortDateString() + "' and create_date < '" + endDate.AddDays(1).ToShortDateString() + "' "
                ).OrderBy(r => r.shop).OrderByDescending(r => r.create_date.Date)
                .AsNoTracking().ToListAsync();
            for (int i = 0; i < rentList.Count; i++)
            {
                RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(rentList[i].id, sessionKey, false)).Result).Value;
                if (!rentOrder.pay_option.Trim().Equals("招待")
                    && (rentOrder.order_id == 0 || rentOrder.order == null || rentOrder.order.pay_state != 1))
                {
                    continue;
                }
                if (!rentOrder.status.Equals("全部归还") && !rentOrder.status.Equals("已退款"))
                {
                    continue;
                }
                RentOrderList.ListItem item = new RentOrderList.ListItem();
                item.cell = rentOrder.cell_number.Trim();
                item.name = rentOrder.real_name.Trim();
                item.dayOfWeek = Util.GetDayOfWeek(rentOrder.create_date);
                item.staffOpenId = rentOrder.staff_open_id;
                item.staffName = rentOrder.staff_name;
                item.status = rentOrder.status;
                item.shop = rentOrder.shop.Trim();
                item.orderDate = rentOrder.create_date;
                item.payDate = (rentOrder.order != null && rentOrder.order.pay_state == 1) ?
                    rentOrder.order.pay_time : null;
                item.id = rentOrder.id;
                item.memo = rentOrder.memo;
                item.entertain = rentOrder.pay_option.IndexOf("招待") >= 0 ? "是" : "否";
                for (int j = 0; rentOrder.order_id > 0
                    && rentOrder.order != null && j < rentOrder.order.paymentList.Count; j++)
                {
                    if (rentOrder.order.payments[j].status.Trim().Equals("支付成功")
                        && rentOrder.order.payments[j].out_trade_no != null)
                    {
                        item.out_trade_no = rentOrder.order.payments[j].out_trade_no.Trim();
                        break;
                    }
                }
                for (int j = 0; rentOrder.order_id != 0 && rentOrder.order != null
                    && j < rentOrder.order.paymentList.Count; j++)
                {
                    if (rentOrder.order.payments[j].status.Trim().Equals("支付成功"))
                    {
                        RentOrderList.RentDeposit deposit = new RentOrderList.RentDeposit();
                        deposit.id = rentOrder.order.payments[j].id;
                        deposit.payDate = (DateTime)rentOrder.order.pay_time;
                        deposit.payMethod = rentOrder.order.payments[j].pay_method.Trim();
                        deposit.amount = rentOrder.order.payments[j].amount;
                        item.deposits = new RentOrderList.RentDeposit[] { deposit };
                    }
                }
                List<RentOrderList.RentRefund> refundList = new List<RentOrderList.RentRefund>();
                for (int j = 0; rentOrder.order_id != 0 && rentOrder.order != null
                    && rentOrder.order.refunds != null && j < rentOrder.order.refunds.Count; j++)
                {
                    RentOrderList.RentRefund r = new RentOrderList.RentRefund();
                    r.id = rentOrder.order.refunds[j].id;
                    r.refundDate = rentOrder.order.refunds[j].create_date;
                    r.depositId = rentOrder.order.refunds[j].payment_id;
                    r.amount = rentOrder.order.refunds[j].amount;
                    r.refund_id = rentOrder.order.refunds[j].refund_id.Trim();
                    string operOpenId = rentOrder.order.refunds[j].oper;
                    Member? refundUser = await _memberHelper.GetWholeMemberByNum(operOpenId, "wechat_mini_openid");
                    if (refundUser != null)
                    {
                        r.staffName = refundUser.real_name.Trim();
                    }
                    else
                    {
                        r.staffName = "";
                    }
                    refundList.Add(r);
                }
                item.refunds = refundList.ToArray<RentOrderList.RentRefund>();
                List<RentOrderList.Rental> rentalList = new List<RentOrderList.Rental>();
                for (int j = 0; rentOrder.order_id != 0 && rentOrder.order != null
                    && j < rentOrder.rentalDetails.Count; j++)
                {
                    if (rentOrder.rentalDetails[j] == null)
                    {
                        continue;
                    }
                    SnowmeetApi.Models.Rent.RentalDetail rentalDtl = rentOrder.rentalDetails[j];
                    bool exists = false;
                    for (int k = 0; k < rentalList.Count; k++)
                    {
                        if (rentalList[k].rentalDate.Date == rentalDtl.date.Date)
                        {
                            rentalList[k].rental += rentalDtl.rental;
                            exists = true;
                            break;
                        }
                    }
                    if (!exists)
                    {
                        RentOrderList.Rental r = new RentOrderList.Rental();
                        r.rental = rentOrder.rentalDetails[j].rental;
                        r.rentalDate = rentOrder.rentalDetails[j].date;
                        rentalList.Add(r);
                    }

                }
                item.rental = rentalList.ToArray();
                list.items.Add(item);
            }
            list.startDate = startDate;
            list.endDate = endDate;
            int dayIndex = 1;
            for (int i = list.items.Count - 1; i >= 0; i--)
            {
                list.items[i].indexOfDay = dayIndex;
                if (i > 0)
                {
                    if (list.items[i].orderDate.Date < list.items[i - 1].orderDate.Date || !list.items[i - 1].shop.Trim().Equals(list.items[i].shop.Trim()))
                    {
                        dayIndex = 1;
                    }
                    else
                    {
                        dayIndex++;
                    }
                }
                list.maxDepositsLength = Math.Max(list.maxDepositsLength,
                    (list.items[i].deposits != null) ? list.items[i].deposits.Length : 0);
                list.maxRefundLength = Math.Max(list.maxRefundLength,
                    (list.items[i].refunds != null) ? list.items[i].refunds.Length : 0);
                list.maxRentalLength = Math.Max(list.maxRentalLength,
                    (list.items[i].rental != null) ? list.items[i].rental.Length : 0);
            }
            return Ok(list);
        }
        [HttpGet("{addPayId}")]
        public async Task<ActionResult<RentAdditionalPayment>> ConfirmAdditionPayment(int addPayId, string payMethod,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            if (payMethod.Trim().Equals("微信支付"))
            {
                return BadRequest();
            }
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await Util.GetUser(sessionKey, _db);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            if (!user.isStaff)
            {
                return BadRequest();
            }
            payMethod = Util.UrlDecode(payMethod);
            RentAdditionalPayment addPay = await _db.rentAdditionalPayment.FindAsync(addPayId);
            RentOrder rentOrder = await _db.RentOrder.FindAsync(addPay.rent_list_id);
            if (addPay == null || rentOrder == null)
            {
                return NotFound();
            }
            double amount = addPay.amount;
            OrderOnline order = new OrderOnline()
            {
                id = 0,
                type = "押金",
                shop = rentOrder.shop.Trim(),
                open_id = user.wlMiniOpenId.Trim(),
                name = rentOrder.real_name.Trim(),
                cell_number = rentOrder.cell_number.Trim(),
                pay_method = payMethod.Trim(),
                pay_memo = "追加押金",
                pay_state = 0,
                order_price = addPay.amount,
                order_real_pay_price = amount,
                ticket_amount = 0,
                other_discount = 0,
                final_price = amount,
                ticket_code = rentOrder.ticket_code.Trim(),
                staff_open_id = addPay.staff_open_id,
                score_rate = 0,
                generate_score = 0

            };
            await _db.OrderOnlines.AddAsync(order);
            await _db.SaveChangesAsync();
            OrderPayment payment = new OrderPayment()
            {
                id = 0,
                order_id = order.id,
                amount = addPay.amount,
                pay_method = payMethod.Trim(),
                staff_open_id = addPay.staff_open_id.Trim(),
                status = "支付成功"
            };
            //order.paymentList.Add(payment);
            await _db.OrderPayment.AddAsync(payment);
            await _db.SaveChangesAsync();
            addPay.order_id = order.id;
            addPay.is_paid = 1;
            addPay.pay_method = payMethod.Trim();
            _db.rentAdditionalPayment.Entry(addPay).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(addPay);
        }
        [HttpGet("{rentListId}")]
        public async Task<ActionResult<RentAdditionalPayment>> CreateAdditionalPayment(int rentListId, double amount, string reason,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await Util.GetUser(sessionKey, _db);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            if (!user.isStaff)
            {
                return BadRequest();
            }
            RentAdditionalPayment addPay = new RentAdditionalPayment()
            {
                rent_list_id = rentListId,
                amount = amount,
                reason = Util.UrlDecode(reason),
                staff_open_id = user.member.wechatMiniOpenId.Trim(),
                pay_method = "微信支付",
                create_date = DateTime.Now
            };
            await _db.rentAdditionalPayment.AddAsync(addPay);
            await _db.SaveChangesAsync();
            return Ok(addPay);
        }
        [HttpGet("{rentAddPayId}")]
        public async Task<ActionResult<OrderOnline>> PlaceAdditionalOrder(int rentAddPayId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            List<RentAdditionalPayment> rentAddPayList = await _db.rentAdditionalPayment
                .Where(r => r.id == rentAddPayId).Include(r => r.rentOrder).AsNoTracking().ToListAsync();
            if (rentAddPayList == null || rentAddPayList.Count == 0)
            {
                return BadRequest();
            }
            RentAdditionalPayment payment = rentAddPayList[0];
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            string payMethod = payment.pay_method;
            UnicUser user = await Util.GetUser(sessionKey, _db);
            if (user == null)
            {
                return NoContent();
            }
            RentOrder rentOrder = payment.rentOrder;
            if (rentOrder == null)
            {
                return NotFound();
            }
            string shop = rentOrder.shop.Trim();
            if (shop.StartsWith("万龙"))
            {
                shop = "万龙体验中心";
            }
            OrderOnline order = new OrderOnline()
            {
                id = 0,
                type = "押金",
                shop = rentOrder.shop.Trim(),
                open_id = user.wlMiniOpenId.Trim(),
                name = rentOrder.real_name.Trim(),
                cell_number = rentOrder.cell_number.Trim(),
                pay_method = payMethod.Trim(),
                pay_memo = "追加押金",
                pay_state = 0,
                order_price = payment.amount,
                order_real_pay_price = payment.amount,
                ticket_amount = 0,
                other_discount = 0,
                final_price = payment.amount,
                ticket_code = rentOrder.ticket_code.Trim(),
                staff_open_id = payment.staff_open_id,
                score_rate = 0,
                generate_score = 0

            };
            await _db.OrderOnlines.AddAsync(order);
            await _db.SaveChangesAsync();
            payment.order_id = order.id;
            payment.order = order;
            payment.update_date = DateTime.Now;
            _db.rentAdditionalPayment.Entry(payment).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            if (order.id == 0)
            {
                return BadRequest();
            }
            OrderPaymentController _orderHelper = new OrderPaymentController(_db, _oriConfig, _httpContextAccessor);
            OrderPayment paymentReal = (OrderPayment)((OkObjectResult)(await _orderHelper.CreatePayment(order.id, payMethod, order.final_price)).Result).Value;
            paymentReal.staff_open_id = order.staff_open_id;
            order.paymentList = (new OrderPayment[] { paymentReal }).ToList();
            return Ok(order);
        }
        [HttpGet("addPayId")]
        public async Task<ActionResult<RentAdditionalPayment>> GetAddPayment(int addPayId, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await Util.GetUser(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            List<RentAdditionalPayment> addPayList = await _db.rentAdditionalPayment.Where(r => r.id == addPayId)
                .AsNoTracking().ToListAsync();
            if (addPayList == null || addPayList.Count == 0)
            {
                return NotFound();
            }
            return Ok(addPayList[0]);
        }
        [NonAction]
        public async Task AdditionalOrderPaid(int orderId)
        {
            List<RentAdditionalPayment> addPayList = await _db.rentAdditionalPayment
                .Where(r => r.order_id == orderId).ToListAsync();
            for (int i = 0; i < addPayList.Count; i++)
            {
                RentAdditionalPayment addPay = addPayList[i];
                addPay.is_paid = 1;
                _db.rentAdditionalPayment.Entry(addPay).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();
        }
        [HttpGet("{orderId}")]
        public async Task<ActionResult<RentOrder>> SetFinish(int orderId, DateTime? finishDate,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await Util.GetUser(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            if (finishDate == null)
            {
                if (user.member.is_manager == 0
                && user.member.is_admin == 0
                && user.member.is_staff == 0
                )
                {
                    return BadRequest();
                }
            }
            RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(orderId, sessionKey)).Result).Value;
            if (rentOrder == null)
            {
                return NotFound();
            }
            if (rentOrder.finish_date == null && !rentOrder.status.Trim().Equals("已退款") && !rentOrder.status.Trim().Equals("全部归还"))
            {
                return NoContent();
            }
            RentOrderLog log = new RentOrderLog()
            {
                id = 0,
                rent_list_id = rentOrder.id,
                memo = finishDate == null ? "订单重开" : "订单完成",
                field_name = "finish_date",
                prev_value = rentOrder.finish_date == null ? null : rentOrder.finish_date.ToString(),
                oper_member_id = user.member.id
            };
            await _db.rentOrderLog.AddAsync(log);
            rentOrder.finish_date = finishDate;
            if (rentOrder.hide == 0 && finishDate != null && rentOrder.shop.Trim().Equals("万龙体验中心"))
            {
                List<RentReward> rentRewards = await _db.rentReward
                    .Where(r => r.need_correct == 1 && r.correct_rent_list_id == null)
                    .OrderBy(r => r.id).ToListAsync();
                if (rentRewards.Count > 0)
                {
                    RentReward r = rentRewards[0];
                    r.correct_rent_list_id = rentOrder.id;
                    r.update_date = DateTime.Now;
                    _db.rentReward.Entry(r).State = EntityState.Modified;
                    rentOrder.hide = 1;

                }
            }
            rentOrder.update_date = DateTime.Now;
            _db.RentOrder.Entry(rentOrder).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(rentOrder);
        }
        [HttpGet("{orderId}")]
        public async Task<ActionResult<List<RentOrderLog>>> GetRentOrderLogs(int orderId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await Util.GetUser(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            return await _db.rentOrderLog.Where(l => l.rent_list_id == orderId)
                .Include(l => l.member).OrderByDescending(l => l.id).ToListAsync();
        }
        [HttpPost("{rentOrderId}")]
        public async Task<ActionResult<RentOrder>> Refund([FromRoute] int rentOrderId,
            [FromBody] List<OrderPaymentRefund> refundList, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await Util.GetUser(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentOrder rentOrder = await _db.RentOrder.FindAsync(rentOrderId);
            if (rentOrder == null || rentOrder.order_id == null || rentOrder.order_id <= 0)
            {
                return NotFound();
            }
            OrderPaymentController _refunder = new OrderPaymentController(_db, _oriConfig, _httpContextAccessor);
            for (int i = 0; i < refundList.Count; i++)
            {
                OrderPaymentRefund refund = refundList[i];
                try
                {
                    await _refunder.Refund(refund.payment_id, refund.amount, "租赁退押金", sessionKey, sessionType);
                }
                catch
                {

                }
            }
            RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderId, sessionKey, false)).Result).Value;
            return Ok(order);
        }
        [HttpPost]
        public async Task<ActionResult<RentReward>> RewardRefund([FromBody] RentReward reward,
            [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await Util.GetUser(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            reward.oper_member_id = user.member.id;
            await _db.rentReward.AddAsync(reward);
            await _db.SaveChangesAsync();
            OrderPaymentController _refunder = new OrderPaymentController(_db, _oriConfig, _httpContextAccessor);
            bool allSuccess = true;
            for (int i = 0; i < reward.rentRewardRefunds.Count; i++)
            {
                RentRewardRefund r = reward.rentRewardRefunds[i];
                try
                {
                    OrderPaymentRefund refund = (OrderPaymentRefund)((OkObjectResult)(await _refunder.Refund(r.payment_id, r.amount, "销售抵扣租赁", sessionKey, sessionType)).Result).Value;
                    r.refund_id = refund.id;
                    r.update_date = DateTime.Now;
                    _db.rentRewardRefund.Entry(r).State = EntityState.Modified;
                    if (refund.refund_id.Trim().Equals(""))
                    {
                        allSuccess = false;
                    }
                }
                catch
                {
                    allSuccess = false;
                }
            }
            if (allSuccess)
            {
                reward.refund_finish = 1;
                reward.update_date = DateTime.Now;
                RentOrder rentOrder = await _db.RentOrder.FindAsync(reward.rent_list_id);
                if (rentOrder.finish_date != null)
                {
                    DateTime fDate = (DateTime)rentOrder.finish_date;
                    if (fDate.Year == reward.create_date.Year
                        && fDate.Month == reward.create_date.Month)
                    {
                        reward.need_correct = 0;
                    }
                }
                else
                {
                    reward.need_correct = 0;
                }
                _db.rentReward.Entry(reward).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();

            for (int i = 0; i < reward.rentRewardRefunds.Count; i++)
            {
                RentRewardRefund rewardRefund = reward.rentRewardRefunds[i];
                await _db.rentRewardRefund.Entry(rewardRefund).Reference(r => r.payment).LoadAsync();
                await _db.rentRewardRefund.Entry(rewardRefund).Reference(r => r.refund).LoadAsync();
            }

            return Ok(reward);
        }
        [HttpGet("{addPayId}")]
        public async Task<ActionResult<RentAdditionalPayment>> GetAdditionalPayment(int addPayId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await Util.GetUser(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentAdditionalPayment payment = await _db.rentAdditionalPayment.FindAsync(addPayId);
            if (payment == null)
            {
                return NotFound();
            }
            return Ok(payment);
        }
        [NonAction]
        public async Task<RentProduct> GetProduct(int productId)
        {
            RentProduct product = await _db.rentProduct
                .Include(p => p.category).ThenInclude(c => c.priceList)
                .Where(p => p.id == productId).AsNoTracking().FirstAsync();
            return product;
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<List<RentCategory>>>> GetAllCategoryList()
        {
            List<RentCategory> l = await _db.rentCategory
                .Where(c => c.valid == 1 && c.code.Length == 2)
                .AsNoTracking().OrderBy(c => c.code).ToListAsync();
            for (int i = 0; i < l.Count; i++)
            {
                RentCategory c = l[i];
                c.children = await _db.rentCategory
                    .Where(c2 => c2.valid == 1 && c2.code.StartsWith(c.code) && c2.code.Length == 4)
                    .AsNoTracking().OrderBy(c2 => c2.code).ToListAsync();
                if (c.children.Count == 0)
                {
                    c.children = null;
                }
            }
            return Ok(new ApiResult<List<RentCategory>>()
            {
                code = 0,
                message = "",
                data = l
            });
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<List<RentPackage>>>> GetRentPackageList(string? shop = null)
        {
            List<RentPackage> list = await _db.rentPackage.Include(c => c.rentPackageCategoryList)
                .ThenInclude(rpc => rpc.rentCategory)
                .Where(r => r.valid == 1 && (r.shop == null || shop == null || r.shop == shop))
                .OrderByDescending(r => r.id).ToListAsync();
            return Ok(new ApiResult<List<RentPackage>>()
            {
                code = 0,
                message = "",
                data = list
            });
        }
        [HttpGet("{packageId}")]
        public async Task<ActionResult<ApiResult<RentPackage>>> GetRentPackage(int packageId)
        {
            RentPackage rp = await _db.rentPackage
                .Include(r => r.rentPackageCategoryList.Where(rc => rc.valid))
                    .ThenInclude(r => r.rentCategory)
                .Include(r => r.rentPackagePriceList)
                .Where(r => r.id == packageId).FirstAsync();
            return Ok(new ApiResult<RentPackage>()
            {
                code = 0,
                message = "",
                data = rp
            });
        }
        [HttpGet("{barCode}")]
        public async Task<ActionResult<ApiResult<RentProduct?>>> GetRentProductByBarcode(string barCode)
        {
            List<RentProduct> productList = await _db.rentProduct
                .Where(p => p.barcode.Trim().Equals(barCode.Trim()))
                .OrderByDescending(p => p.id).AsNoTracking().ToListAsync();
            if (productList == null || productList.Count <= 0)
            {
                return Ok(new ApiResult<object?>()
                {
                    code = 1,
                    message = "找不到该商品",
                    data = null
                });
            }
            else
            {
                RentProduct p = await GetProduct(productList[0].id);
                return Ok(new ApiResult<RentProduct>()
                {
                    code = 0,
                    message = "",
                    data = p
                });
            }
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<List<RentProduct>>>> GetRentProductFuzzy(string key, int? categoryId)
        {
            key = Util.UrlDecode(key);
            List<RentProduct> products = await _db.rentProduct
                .Where(p => p.valid == 1 && (p.barcode.Contains(key) || p.name.ToLower().Contains(key.ToLower())))
                .Include(p => p.category).AsNoTracking().ToListAsync();
            if (categoryId != null)
            {
                RentCategory category = await _db.rentCategory.Where(c => c.id == categoryId).AsNoTracking().FirstOrDefaultAsync();
                List<RentCategory> categories = await _db.rentCategory.Where(c => c.code.StartsWith(category.code)).AsNoTracking().ToListAsync();
                List<RentProduct> results = new List<RentProduct>();
                for (int i = 0; i < products.Count; i++)
                {

                    RentProduct product = products[i];
                    if (product.category_id == categoryId)
                    {
                        results.Add(product);
                    }
                    else if (categories.Where(c => c.id == product.category_id).ToList().Count > 0)
                    {
                        results.Add(product);
                    }
                }
                return Ok(new ApiResult<List<RentProduct>>()
                {
                    code = 0,
                    message = "",
                    data = results
                });
            }
            return Ok(new ApiResult<List<RentProduct>>()
            {
                code = 0,
                message = "",
                data = products
            });
        }
        [HttpGet("{categoryId}")]
        public async Task<ActionResult<ApiResult<List<RentProduct>?>>> GetRentProductByCategory(int categoryId)
        {
            RentCategory category = await _db.rentCategory.Where(c => c.id == categoryId).AsNoTracking().FirstOrDefaultAsync();
            if (category == null)
            {
                return Ok(new ApiResult<List<RentProduct>?>()
                {
                    code = 1,
                    message = "分类不存在",
                    data = null
                });
            }
            List<RentProduct> products = await _db.rentProduct
                .Include(p => p.category)
                .Where(p => p.valid == 1 && (p.category.code.StartsWith(category.code) || p.category_id == categoryId))
                //.Include(p => p.category)
                .AsNoTracking().ToListAsync();
            return Ok(new ApiResult<List<RentProduct>>()
            {
                code = 0,
                message = "",
                data = products
            });
        }
        [HttpGet("{categoryId}")]
        public async Task<ActionResult<ApiResult<RentPackage?>>> DeleteRentPackage(int categoryId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 200)
            {
                return Ok(new ApiResult<RentPackage?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            RentPackage package = await _db.rentPackage
                .Where(r => r.id == categoryId).AsNoTracking().FirstOrDefaultAsync();
            RentPackage ori = await _db.rentPackage
                .Where(r => r.id == categoryId).AsNoTracking().FirstOrDefaultAsync();
            if (package == null)
            {
                return Ok(new ApiResult<RentPackage?>()
                {
                    code = 1,
                    message = "找不到该套餐",
                    data = null
                });
            }
            package.valid = 0;
            package.update_date = DateTime.Now;
            List<CoreDataModLog> logs = Util.GetUpdateDifferenceLog<RentPackage>(ori, package, null, staff.id, "删除套餐");
            for (int i = 0; i < logs.Count; i++)
            {
                await _db.coreDataModLog.AddAsync(logs[i]);
            }
            _db.rentPackage.Entry(package).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<RentPackage>()
            {
                code = 0,
                message = "",
                data = package
            });
        }
        [HttpPost]
        public async Task<ActionResult<ApiResult<Models.Order?>>> SaveRentRecept([FromBody] Models.Order order,
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
            // 找回中断单时前端会原样带回 GetReceptingOrder 包含的 member / memberSocialAccounts 导航对象。
            // 下面 else 分支的 _db.Update(order) 会把整图标脏级联更新（含 member 子图），SaveChanges 抛错被
            // try/catch 静默吞掉 → 租金修改、新增套餐全都不落库。SaveRentRecept 只管 order 标量 + rentals 子图，
            // member_id / staff_id 是独立标量列，置空导航对象不影响归属。与下面 details / category 同属防级联清理。
            order.member = null;
            order.staff = null;
            for (int i = 0; i < order.rentals.Count; i++)
            {
                order.rentals[i].details = null;
                for (int j = 0; j < order.rentals[i].rentItems.Count; j++)
                {
                    order.rentals[i].rentItems[j].category = null;
                }
            }
            if (order.create_date == null)
            {
                order.create_date = DateTime.Now;
                for (int i = 0; i < order.rentals.Count; i++)
                {
                    if (order.rentals[i].create_date == null)
                    {
                        order.rentals[i].create_date = DateTime.Now;
                    }
                    for (int j = 0; j < order.rentals[i].rentItems.Count; j++)
                    {
                        if (order.rentals[i].rentItems[j].create_date == null)
                        {
                            order.rentals[i].rentItems[j].create_date = DateTime.Now;
                        }
                    }
                }
            }
            if (order.id == 0)
            {
                if (_httpContextAccessor.HttpContext.Request.Host.Value != null
                && _httpContextAccessor.HttpContext.Request.Host.Value.Equals("mini.snowmeet.top"))
                {
                    order.is_test = 0;
                }
                else
                {
                    order.is_test = 1;
                }
                order.staff_id = staff.id;
                order.create_date = DateTime.Now;
                order.valid = 0;
                order.recepting = 1;
                for (int i = 0; i < order.rentals.Count; i++)
                {
                    Rental rental = order.rentals[i];
                    for (int j = 0; j < rental.rentItems.Count; j++)
                    {
                        Models.RentItem item = rental.rentItems[j];
                        item.logs = null;
                    }
                    rental.details = null;
                }
                await _db.order.AddAsync(order);
                await _db.SaveChangesAsync();
            }
            else
            {
                for (int i = 0; i < order.rentals.Count; i++)
                {
                    int rentalId = order.rentals[i].id;
                    if (rentalId > 0)
                    {
                        List<RentalPricePreset> presets = await _db.rentalPricePreset
                            .Where(r => r.rental_id == rentalId).AsNoTracking().ToListAsync();
                        for (int j = 0; j < presets.Count; j++)
                        {
                            _db.rentalPricePreset.Remove(presets[j]);
                        }
                    }
                }
                List<Rental> newRentals = order.rentals.Where(r => r.id == 0).ToList();
                for (int i = 0; i < newRentals.Count; i++)
                {
                    Rental newRental = newRentals[i];
                    newRental.order_id = order.id;
                    newRental.create_date = DateTime.Now;
                    for (int j = 0; newRental.pricePresets != null && j < newRental.pricePresets.Count; j++)
                    {
                        RentalPricePreset preset = newRental.pricePresets[j];
                        preset.rental_id = newRental.id;
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

                List<Rental> rentals = order.rentals;
                List<Models.Rental> oriRentals = await _db.rental.Include(r => r.rentItems)
                .Where(r => r.order_id == order.id).AsNoTracking().ToListAsync();
                for (int i = 0; i < oriRentals.Count; i++)
                {
                    Rental ori = oriRentals[i];
                    if (rentals.Where(r => r.id == ori.id).ToList().Count == 0)
                    {
                        for (int j = 0; j < ori.rentItems.Count; j++)
                        {
                            _db.rentItem.Remove(ori.rentItems[j]);
                        }
                        List<RentalPricePreset> presets = await _db.rentalPricePreset
                        .Where(r => r.rental_id == ori.id).AsNoTracking().ToListAsync();
                        for (int j = 0; j < presets.Count; j++)
                        {
                            _db.rentalPricePreset.Remove(presets[j]);
                        }
                        try
                        {
                            _db.rental.Remove(ori);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
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
            for (int i = 0; i < order.rentals.Count; i++)
            {
                Rental rental = order.rentals[i];
                for (int j = 0; j < rental.rentItems.Count; j++)
                {
                    Models.RentItem item = rental.rentItems[j];
                    item.category = await _db.rentCategory.Where(c => c.id == item.category_id).AsNoTracking().FirstOrDefaultAsync();
                }
            }
            // 已废弃：原先雪板类租赁默认追加对讲机的逻辑，暂时停用
            // if (order.needIntercom != false)
            // {
            //     order = await AddInterCom(order);
            // }
            if (order.type == "租赁")
            {
                for (int i = 0; i < order.rentals.Count; i++)
                {
                    Rental rental = order.rentals[i];
                    if (rental.category_id != null)
                    {
                        rental = await BuildAssociates(rental);
                    }
                    if (order.shop == "崇礼旗舰店")
                    {
                        if (rental.package_id == 29 || rental.package_id == 30)
                        {
                            order.needRender = await SetRentItemDefaults(rental);
                        }
                    }
                }
            }
            return Ok(new ApiResult<Models.Order?>()
            {
                code = 0,
                message = "",
                data = order
            });
        }
        [NonAction]
        public async Task<bool> SetRentItemDefaults(Rental rental)
        {
            bool changed = false;
            for (int j = 0; j < rental.rentItems.Count; j++)
            {
                Models.RentItem item = rental.rentItems[j];
                if (item.category == null)
                {
                    item.category = await _db.rentCategory.Where(c => c.id == item.category_id).AsNoTracking().FirstOrDefaultAsync();
                }

                bool unNeedFillInfo = false;
                switch (item.category_id)
                {
                    case 27:
                    case 40:
                    case 42:
                    case 46:
                    case 47:
                    case 48:
                    case 76:
                    case 77:
                    case 78:
                    case 79:
                    case 80:
                    case 81:
                    case 82:
                    case 85:
                        unNeedFillInfo = true;
                        break;
                    default:
                        break;
                }
                if (item.name == null && unNeedFillInfo)
                {
                    item.name = item.category.name.Trim();
                    item.noCode = true;
                    changed = true;
                }
            }
            return changed;
        }
        [NonAction]
        public async Task<Rental> BuildAssociates(Rental rental)
        {
            Models.RentItem item = rental.rentItems.Where(r => !r.is_associate).FirstOrDefault();
            List<RentCategoryAssociate> assoCateList = await _db.rentCategoryAssociate.Include(a => a.category)
                .Where(a => a.valid && a.category_id == item.category_id).AsNoTracking().ToListAsync();
            List<Models.RentItem> assoItems = rental.rentItems.Where(r => r.is_associate && r.valid == 1).ToList();
            for (int i = 0; i < assoItems.Count; i++)
            {
                if (!assoCateList.Any(a => a.associate_id == assoItems[i].category_id))
                {
                    assoItems[i].valid = 0;
                    assoItems[i].update_date = DateTime.Now;
                    _db.rentItem.Entry(assoItems[i]).State = EntityState.Modified;
                    await _db.SaveChangesAsync();
                }
            }
            for (int i = 0; i < assoCateList.Count; i++)
            {
                if (!rental.rentItems.Any(r => r.is_associate && r.category_id == assoCateList[i].associate_id))
                {
                    Models.RentItem assoItem = new Models.RentItem()
                    {
                        id = 0,
                        rental_id = rental.id,
                        category_id = assoCateList[i].associate_id,
                        class_name = assoCateList[i].category.name,
                        valid = 1,
                        is_associate = true,
                        noCode = true,
                        atOnce = true
                    };
                    await _db.rentItem.AddAsync(assoItem);
                    await _db.SaveChangesAsync();
                    assoItem.category = await _db.rentCategory.Where(c => c.id == assoItem.category_id)
                        .AsNoTracking().FirstOrDefaultAsync();
                }
            }
            return rental;
        }
        [NonAction]
        public async Task<Models.Order> AddInterCom(Models.Order order)
        {
            bool exists = true;
            if (order.shop.IndexOf("万龙") < 0 && order.shop.IndexOf("旗舰") < 0)
            {
                return order;
            }
            List<Rental> rentals = await _db.rental.Where(r => r.order_id == order.id && r.category_id == 94)
                .AsNoTracking().ToListAsync();
            if (rentals == null || rentals.Count == 0)
            {
                for (int i = 0; exists && i < order.rentals.Count; i++)
                {
                    Rental rentalExists = order.rentals[i];
                    if (rentalExists.package_id != null)
                    {
                        RentPackage package = await _db.rentPackage.Where(p => p.id == rentalExists.package_id)
                            .AsNoTracking().FirstOrDefaultAsync();
                        if (package != null
                            && (package.name.IndexOf("板") >= 0 || package.name.IndexOf("On套餐") >= 0
                            || package.name.IndexOf("FIS") >= 0))
                        {
                            exists = false;
                        }

                    }
                    else
                    {
                        for (int j = 0; exists && rentalExists.rentItems != null && j < rentalExists.rentItems.Count; j++)
                        {
                            Models.RentItem rentItemExists = rentalExists.rentItems[j];
                            if (rentItemExists.category_id == 2
                            || rentItemExists.category_id == 3
                            || rentItemExists.category_id == 4
                            || rentItemExists.category_id == 6
                            || rentItemExists.category_id == 7
                            || rentItemExists.category_id == 23
                            || rentItemExists.category_id == 24
                            || rentItemExists.category_id == 40
                            || rentItemExists.category_id == 42
                            || rentItemExists.category_id == 69)
                            {
                                exists = false;
                            }
                        }
                    }

                }

            }

            if (exists)
            {
                return order;
            }
            string name = "小米对讲机";
            int categoryId = 94;
            RentCategory category = await _db.rentCategory.Where(c => c.id == categoryId)
                .AsNoTracking().FirstOrDefaultAsync();
            Rental rental = new Rental()
            {
                order_id = order.id,
                category_id = categoryId,
                name = name,
                start_date = DateTime.Now.Date,
                valid = 1,
                memo = "租赁装备赠送",
                guaranty = 0,
                noGuaranty = true
            };
            Models.RentItem item = new Models.RentItem()
            {
                rental_id = rental.id,
                class_name = name,
                category_id = categoryId,
                valid = 1,
                noCode = true,
                atOnce = true
            };
            rental.rentItems.Add(item);
            RentalPricePreset preset = new RentalPricePreset()
            {
                rental_id = rental.id,
                rent_type = "日场",
                price = 0,
                manual = true
            };
            rental.pricePresets.Add(preset);
            await _db.rental.AddAsync(rental);
            Rental rental2 = new Rental()
            {
                order_id = order.id,
                category_id = categoryId,
                name = name,
                start_date = DateTime.Now.Date,
                valid = 1,
                memo = "租赁装备赠送",
                guaranty = 0,
                noGuaranty = true,
            };
            Models.RentItem item2 = new Models.RentItem()
            {
                rental_id = rental2.id,
                class_name = name,
                category_id = categoryId,
                valid = 1,
                noCode = true,
                atOnce = true
            };
            rental2.rentItems.Add(item2);
            RentalPricePreset preset2 = new RentalPricePreset()
            {
                rental_id = rental.id,
                rent_type = "日场",
                price = 0,
                manual = true
            };
            rental2.pricePresets.Add(preset2);
            await _db.rental.AddAsync(rental2);
            await _db.SaveChangesAsync();
            rental.rentItems[0].category = category;
            rental2.rentItems[0].category = category;
            order.needRender = true;
            return order;
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<List<Models.Order>?>>> GetReceptingOrders(string? shop,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<List<Models.Order>?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            shop = Util.UrlDecode(shop);
            List<Models.Order> orders = await _db.order
                .Include(o => o.staff)
                .Include(o => o.member).ThenInclude(m => m.memberSocialAccounts)
                .Where(o => (o.shop.Trim().Equals(shop) || shop == "" || shop == null) && o.valid == 0 && o.recepting == 1 && o.create_date.Date == DateTime.Now.Date)
                .OrderByDescending(o => o.id).AsNoTracking().ToListAsync();
            return Ok(new ApiResult<List<Models.Order>?>()
            {
                code = 0,
                message = "",
                data = orders
            });
        }
        [HttpGet("{orderId}")]
        public async Task<ActionResult<ApiResult<Models.Order?>>> GetReceptingOrder(int orderId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<List<Models.Order>?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            Models.Order order = await _db.order
                .Include(o => o.rentals).ThenInclude(r => r.rentItems)
                .Include(o => o.rentals).ThenInclude(r => r.pricePresets)
                .Include(o => o.member).ThenInclude(m => m.memberSocialAccounts)
                .Where(o => o.id == orderId).FirstOrDefaultAsync();
            // 找回中断单的购物车按「添加时间正序」（先添加在上），id 自增=创建先后。
            // 原 OrderByDescending 会让后加的排最上，与开单页期望相反。
            order.rentals = order.rentals.OrderBy(r => r.id).ToList();
            return Ok(new ApiResult<Models.Order?>()
            {
                code = 0,
                message = "",
                data = order
            });

        }
        [NonAction]
        public async Task<Models.RentalDetail?> SetRentalDetail(int rentalId, DateTime date, int? staffId)
        {
            List<Models.RentalDetail> detailList = await _db.rentalDetail
                .Where(r => r.rental_id == rentalId && r.rental_date.Date == date.Date)
                .AsNoTracking().ToListAsync();
            if (detailList.Count > 0)
            {
                CoreDataModLog log = CoreDataModLog.CreateManualLog("Rental", "", rentalId, "租赁开单",
                        null, null, null, null, "该笔租赁已经开始计费");
                await _db.coreDataModLog.AddAsync(log);
                await _db.SaveChangesAsync();
                return null;
            }
            string rentType = "";
            string dayType = "";
            string scene = "";
            double discount = 0;
            double? price = null;
            Models.Rental rental = await _db.rental.Where(r => r.id == rentalId).AsNoTracking().FirstOrDefaultAsync();
            RentalPricePreset? preset = await _db.rentalPricePreset
                .Where(p => p.rental_id == rentalId && p.rent_date.Date == date.Date)
                .AsNoTracking().FirstOrDefaultAsync();
            if (preset != null)
            {
                rentType = preset.rent_type.Trim();
                dayType = preset.day_type.Trim();
                scene = preset.scene.Trim();
                discount = preset.discount;
                price = preset.price;
            }
            else
            {
                Models.RentalDetail prevRentalDetail = await _db.rentalDetail
                    .Where(r => r.rental_id == rentalId && r.rental_date.Date < date.Date)
                    .OrderByDescending(r => r.rental_date).AsNoTracking().FirstOrDefaultAsync();
                if (prevRentalDetail == null)
                {
                    rentType = "日场";
                }
                else
                {
                    rentType = "多日";
                }
                if (date.DayOfWeek == DayOfWeek.Sunday || date.DayOfWeek == DayOfWeek.Saturday)
                {
                    dayType = "周末";
                }
                else
                {
                    dayType = "平日";
                }

                if (rental == null)
                {
                    CoreDataModLog log = CoreDataModLog.CreateManualLog("Rental", "", rentalId, "租赁开单",
                        null, null, null, null, "未找到租赁订单");
                    await _db.coreDataModLog.AddAsync(log);
                    await _db.SaveChangesAsync();
                    return null;
                }
                scene = "门市";
            }
            List<RentPrice> priceList = new List<RentPrice>();
            if (rental.package_id != null)
            {
                priceList = await _db.rentPrice.Where(r => r.category_id == rental.category_id).AsNoTracking().ToListAsync();
            }
            else
            {
                priceList = await _db.rentPrice.Where(r => r.package_id == rental.package_id).AsNoTracking().ToListAsync();
            }
            RentPrice rentPrice = priceList.Where(p => p.day_type == dayType && p.rent_type == rentType && p.scene == scene).FirstOrDefault();
            if (rentPrice == null && price == null)
            {
                CoreDataModLog log = CoreDataModLog.CreateManualLog("Rental", "", rentalId, "租赁开单",
                   null, null, null, null, "租赁价格未确认");
                await _db.coreDataModLog.AddAsync(log);
                await _db.SaveChangesAsync();
                return null;
            }
            if (price == null)
            {
                price = rentPrice.price;
            }
            Models.RentalDetail detail = new Models.RentalDetail()
            {
                id = 0,
                rental_id = rentalId,
                rent_price_id = rentPrice == null ? null : rentPrice.id,
                charge_type = "租金",
                rental_date = date,
                rent_item_id = null,
                amount = (double)price,
                memo = "",
                staff_id = staffId,
                valid = 1,
                create_date = DateTime.Now
            };
            await _db.rentalDetail.AddAsync(detail);
            await _db.SaveChangesAsync();
            if (discount > 0)
            {
                Discount discountObj = new Discount()
                {
                    id = 0,
                    amount = discount,
                    order_id = rental.order_id,
                    biz_type = "租赁",
                    biz_id = rental.id,
                    sub_biz_type = "日租金",
                    sub_biz_id = detail.id,
                    staff_id = rental.staff_id,
                    member_id = null,
                    valid = 1,
                    create_date = DateTime.Now

                };
                CoreDataModLog logD = CoreDataModLog.CreateManualLog("RentDetail", "", detail.id, "租赁开单",
                    null, null, null, null, "设置减免");
                await _db.coreDataModLog.AddAsync(logD);
                await _db.discount.AddAsync(discountObj);
                await _db.SaveChangesAsync();
            }

            return detail;
        }
        [HttpGet]
        public async Task<Rental?> EffectRental(int rentalId, int? staffId)
        {
            Rental rental = await _db.rental.Where(r => r.id == rentalId)
                .AsNoTracking().FirstOrDefaultAsync();
            rental.valid = 1;
            rental.update_date = DateTime.Now;
            if (rental.appending != null)
            {
                rental.appending = false;
                rental.append_commit_time = DateTime.Now;
                rental.update_date = DateTime.Now;
            }
            _db.rental.Entry(rental).State = EntityState.Modified;
            List<Models.RentItem> items = await _db.rentItem.Where(i => i.rental_id == rentalId)
                .AsNoTracking()
                .ToListAsync();
            for (int i = 0; i < items.Count; i++)
            {
                Models.RentItem item = items[i];
                item.valid = 1;
                item.update_date = DateTime.Now;
                _db.rentItem.Entry(item).State = EntityState.Modified;
                if (item.atOnce)
                {
                    RentItemLog log = new RentItemLog()
                    {
                        id = 0,
                        rent_item_id = item.id,
                        status = Models.RentItem.RentItemStatus.已发放.ToString(),
                        staff_id = staffId,
                        create_date = DateTime.Now
                    };
                    CoreDataModLog logI = CoreDataModLog.CreateManualLog("RentItem", "", item.id, "租赁开单",
                        null, null, null, null, "立即发放");
                    await _db.coreDataModLog.AddAsync(logI);
                    await _db.rentItemLog.AddAsync(log);
                    // 立即发放：同步把库存租赁物标记为「租赁中」（与手动发放 SetRentItemStatus 口径一致，
                    // 否则立即租赁生效后 rentItem 已发放但 rent_product.status 仍是「正常」，库存占用统计出错）
                    if (item.rent_product_id != null)
                    {
                        RentProduct rp = await _db.rentProduct.Where(p => p.id == item.rent_product_id).FirstOrDefaultAsync();
                        if (rp != null)
                        {
                            rp.status = "租赁中";
                            rp.update_date = DateTime.Now;
                            _db.rentProduct.Entry(rp).State = EntityState.Modified;
                        }
                    }
                }
            }
            CoreDataModLog logR = CoreDataModLog.CreateManualLog("Rental", "", rentalId, "租赁开单",
                null, null, null, null, "开始设置租金");
            await _db.coreDataModLog.AddAsync(logR);
            await _db.SaveChangesAsync();
            if (rental.start_date == null)
            {
                await SetRentalDetail(rentalId, DateTime.Now.Date, staffId);
            }
            else
            {
                await SetRentalDetail(rentalId, ((DateTime)rental.start_date).Date, staffId);
            }
            return await GetRental(rentalId);
        }
        [NonAction]
        public async Task<Models.Order> EffectAppendingRentals(int orderId, int paymentId)
        {
            OrderController _orderH = new OrderController(_db, _oriConfig, _httpContextAccessor);
            Models.Order order = await _orderH.GetOrder(orderId);
            if (order.availablePayments.Where(a => a.id == paymentId).ToList().Count <= 0)
            {
                return null;
            }
            for (int i = 0; order.appendingRentals != null && i < order.appendingRentals.Count; i++)
            {
                Rental appendingRental = order.appendingRentals[i];
                List<Guaranty> guaranties = appendingRental.guaranties;
                for (int j = 0; guaranties != null && j < guaranties.Count; j++)
                {
                    Guaranty g = guaranties[j];
                    if (g.payStatus != "支付完成")
                    {
                        GuarantyPayment gp = new GuarantyPayment()
                        {
                            guaranty_id = g.id,
                            payment_id = paymentId
                        };
                        await _db.guarantyPayment.AddAsync(gp);
                    }
                }
            }
            await _db.SaveChangesAsync();
            for (int i = 0; order.appendingRentals != null && i < order.appendingRentals.Count; i++)
            {
                Rental appendingRental = order.appendingRentals[i];
                _db.rental.Entry(appendingRental).State = EntityState.Detached;
                await _db.SaveChangesAsync();
                appendingRental = await EffectRental(order.appendingRentals[i].id, order.appendingRentals[i].staff_id);
            }
            return order;
        }
        [HttpGet]
        public async Task<Models.Order> EffectRentOrder(int orderId, int paymentId)
        {
            try
            {
                List<Rental> appendingRentals = await _db.rental
                    .Where(r => r.order_id == orderId && r.valid == 1 && r.appending != null && r.append_commit_time == null)
                    .AsNoTracking().ToListAsync();
                if (appendingRentals != null && appendingRentals.Count > 0)
                {
                    return await EffectAppendingRentals(orderId, paymentId);
                }
            }
            catch
            {
                return null;
            }
            OrderController _orderHelper = new OrderController(_db, _oriConfig, _httpContextAccessor);
            Models.Order order = (await _orderHelper.GetCommonOrders(orderId, null, null, null, null, null, null)).FirstOrDefault();
            OrderPayment payment = order.availablePayments.Where(p => p.id == paymentId).FirstOrDefault();
            List<Guaranty> guaranties = new List<Guaranty>();
            double guarantyAmount = 0;
            if (order == null || payment == null || payment.status != OrderPayment.PaymentStatus.支付成功.ToString())
            {
                CoreDataModLog logOrder = CoreDataModLog.CreateManualLog("Order", "", order.id, "租赁开单",
                    null, null, null, null, "未确认支付");
                await _db.coreDataModLog.AddAsync(logOrder);
                await _db.SaveChangesAsync();
                return null;
            }
            for (int i = 0; order.rentals != null && i < order.rentals.Count; i++)
            {
                CoreDataModLog rentalLog = CoreDataModLog.CreateManualLog("Rental", "", order.rentals[i].id, "租赁下单支付回调", null, null, null, null, "开始检查租赁子订单");
                await _db.coreDataModLog.AddAsync(rentalLog);
                await _db.SaveChangesAsync();
                Rental rental = await GetRental(order.rentals[i].id);
                bool existsUnpaidGuaranty = false;
                for (int j = 0; rental.guaranties != null && j < rental.guaranties.Count; j++)
                {
                    Guaranty guaranty = rental.guaranties[j];
                    if (guaranty.payStatus != "支付成功")
                    {
                        existsUnpaidGuaranty = true;
                        continue;
                    }
                }
                if (!existsUnpaidGuaranty && rental.guaranties.Count > 0)
                {
                    CoreDataModLog logG = CoreDataModLog.CreateManualLog("Rental", "", rental.id, "租赁开单",
                    null, null, null, null, "押金重复支付");
                    await _db.coreDataModLog.AddAsync(logG);
                    await _db.SaveChangesAsync();
                    return null;
                }
                for (int j = 0; rental.guaranties != null && j < rental.guaranties.Count; j++)
                {
                    Guaranty guaranty = rental.guaranties[j];
                    if (guaranty.guarantyPayments == null || guaranty.guarantyPayments.Count == 0)
                    {
                        guaranties.Add(guaranty);
                        guarantyAmount += (double)guaranty.amount;
                    }
                }
            }
            if (Math.Round(guarantyAmount, 2) > Math.Round(payment.amount, 2))
            {
                CoreDataModLog logGP = CoreDataModLog.CreateManualLog("Order", "", order.id, "租赁开单",
                    null, null, null, null, "未足额支付押金");
                await _db.coreDataModLog.AddAsync(logGP);
                await _db.SaveChangesAsync();
                return null;
            }
            for (int i = 0; i < guaranties.Count; i++)
            {
                //Guaranty guaranty = guaranties[i];
                GuarantyPayment gp = new GuarantyPayment()
                {
                    guaranty_id = guaranties[i].id,
                    payment_id = paymentId,
                    create_date = DateTime.Now
                };

                await _db.guarantyPayment.AddAsync(gp);
            }
            CoreDataModLog log = CoreDataModLog.CreateManualLog("Order", "", order.id, "租赁开单",
                null, null, null, null, "确认押金支付");
            await _db.coreDataModLog.AddAsync(log);
            await _db.SaveChangesAsync();
            for (int i = 0; i < order.rentals.Count; i++)
            {
                CoreDataModLog logR = CoreDataModLog.CreateManualLog("Rental", "", order.rentals[i].id, "租赁开单",
                null, null, null, null, "开始生效租赁子订单");
                await _db.coreDataModLog.AddAsync(logR);
                await _db.SaveChangesAsync();
                order.rentals[i] = await EffectRental(order.rentals[i].id, payment.staff_id);
            }
            return order;
        }
        [NonAction]
        public async Task<Models.Rental> GetRental(int rentalId)
        {
            Models.Rental rental = await _db.rental.Where(r => r.id == rentalId)
                .Include(r => r.staff)
                .Include(r => r.rentItems).ThenInclude(i => i.logs).ThenInclude(l => l.staff)
                .Include(r => r.rentItems).ThenInclude(i => i.category)
                .Include(r => r.details).ThenInclude(d => d.rentPrice)
                .Include(r => r.details).ThenInclude(d => d.discounts)
                .Include(r => r.discounts)
                .Include(r => r.pricePresets)
                .Include(r => r.order)
                .Include(r => r.guaranties).ThenInclude(g => g.guarantyPayments).ThenInclude(p => p.payment)
                .AsNoTracking().FirstOrDefaultAsync();
            rental.rentItems = rental.rentItems.OrderBy(i => i.next_id).ThenByDescending(i => i.id).ToList();
            for (int i = 0; i < rental.rentItems.Count; i++)
            {
                Models.RentItem rentItem = rental.rentItems[i];
                rentItem.repairationCharges = await _db.rentalDetail
                    .Where(r => r.rent_item_id == rentItem.id).AsNoTracking().ToListAsync();
                //await _db.rentItem.Entry(rentItem).Collection(i => i.repairationCharges).LoadAsync();
            }
            return rental;
        }


        [HttpGet("{rentItemId}")]
        public async Task<ActionResult<ApiResult<List<Models.RentItem>?>>> GetRentItemChanges(int rentItemId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<List<Models.RentItem>?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            Models.RentItem item = await _db.rentItem.Where(r => r.id == rentItemId)
                .AsNoTracking().FirstOrDefaultAsync();
            if (item == null)
            {
                return Ok(new ApiResult<List<Models.RentItem>?>()
                {
                    code = 1,
                    message = "无此租赁物",
                    data = null
                });
            }
            List<Models.RentItem> logList = await GetRentItemChangesLog(item);
            return Ok(new ApiResult<List<Models.RentItem>?>()
            {
                code = 0,
                message = "",
                data = logList
            });
        }


        [NonAction]
        public async Task<List<Models.RentItem>?> GetRentItemChangesLog(Models.RentItem item)
        {
            if (item.prev_id == null)
            {
                return null;
            }
            if (item.changesLog == null)
            {
                item.changesLog = new List<Models.RentItem>();
            }
            Models.RentItem prevItem = await _db.rentItem.Where(r => r.id == item.prev_id)
                .Include(i => i.logs).AsNoTracking().FirstOrDefaultAsync();

            if (prevItem.prev_id == null)
            {
                return new List<Models.RentItem>() { prevItem };

            }
            else
            {
                item.changesLog.Add(prevItem);
                List<Models.RentItem> logList = await GetRentItemChangesLog(prevItem);
                for (int i = 0; logList != null && i < logList.Count; i++)
                {
                    item.changesLog.Add(logList[i]);
                }
                return item.changesLog; ;
            }

        }
        [HttpGet("{rentalId}")]
        public async Task<ActionResult<ApiResult<Models.Rental?>>> GetRentalByStaff(int rentalId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<Models.Rental?>()
                {
                    code = 1,
                    message = "",
                    data = null
                });
            }
            Rental rental = await GetRental(rentalId);
            return Ok(new ApiResult<Models.Rental?>()
            {
                code = 0,
                message = "",
                data = rental
            });
        }
        [HttpGet("{rentItemId}")]
        public async Task<ActionResult<ApiResult<Models.Rental?>>> SetRentItemRepairAmount(int rentItemId,
            double amount, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<Models.Rental?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            Models.RentItem? rentItem = await _db.rentItem.Where(r => r.id == rentItemId)
                .AsNoTracking().FirstOrDefaultAsync();
            if (rentItem == null)
            {
                return Ok(new ApiResult<Models.Rental?>()
                {
                    code = 1,
                    message = "无此租赁物",
                    data = null
                });
            }
            List<Models.RentalDetail> details = await _db.rentalDetail
                .Where(d => d.rent_item_id == rentItemId && d.charge_type == "赔偿金"
                    && d.valid == 1 && d.rental_id == rentItem.rental_id)
                .AsNoTracking().ToListAsync();
            for (int i = 0; i < details.Count; i++)
            {
                details[i].valid = 0;
                details[i].update_date = DateTime.Now;
                _db.rentalDetail.Entry(details[i]).State = EntityState.Modified;
            }
            if (amount > 0)
            {
                Models.RentalDetail detail = new Models.RentalDetail()
                {
                    id = 0,
                    rental_id = (int)rentItem.rental_id,
                    rent_item_id = rentItemId,
                    charge_type = "赔偿金",
                    rental_date = DateTime.Now,
                    amount = amount,
                    memo = "手动设置赔偿金",
                    staff_id = staff.id,
                    valid = 1,
                    create_date = DateTime.Now
                };
                await _db.rentalDetail.AddAsync(detail);
            }
            await _db.SaveChangesAsync();
            Rental rental = await GetRental((int)rentItem.rental_id);
            return Ok(new ApiResult<Models.Rental>()
            {
                code = 0,
                message = "",
                data = rental
            });
        }
        [HttpGet("{rentalId}")]
        public async Task<ActionResult<ApiResult<Models.Rental?>>> ReturnAllRentItems(int rentalId, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<Models.Rental?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            Rental rental = await GetRental(rentalId);
            ActionResult<ApiResult<Models.Rental?>> newRental = null;
            for (int i = 0; i < rental.rentItems.Count; i++)
            {
                Models.RentItem rentItem = rental.rentItems[i];
                if (rentItem.status == "已发放" && rentItem.noNeed != true)
                {
                    newRental = (ActionResult<ApiResult<Models.Rental?>>)(await SetRentItemStatus(rentItem.id, "已归还", sessionKey, sessionType));
                }
            }
            return newRental;
        }
        [HttpGet("{orderId}")]
        public async Task<ActionResult<ApiResult<Models.Order?>>> RetualAllRentItemInOrderByCategory(int orderId, 
            int categoryId, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<Models.Rental?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            OrderController _orderHelper = new OrderController(_db, _config, _httpContextAccessor);
            Models.Order order = await _orderHelper.GetOrder(orderId);
            for(int i = 0; order.rentals != null && i < order.rentals.Count; i++)
            {
                for(int j = 0; order.rentals[i].rentItems != null && j < order.rentals[i].rentItems.Count; j++)
                {
                    Models.RentItem item = order.rentals[i].rentItems[j];
                    if (item.status == "已发放" && item.noNeed != true)
                    {
                         RentCategory fatherCategory = await _db.rentCategory
                            .Where(c => c.id == categoryId && c.valid == 1)
                            .AsNoTracking().FirstOrDefaultAsync();
                        if (await _db.rentCategory.AnyAsync(c => c.id == item.category_id && c.valid == 1 && c.code.IndexOf(fatherCategory.code) == 0 ))
                        {
                            await SetRentItemStatus(item.id, "已归还", sessionKey, sessionType);
                        }
                    }
                    
                }
            }
            order = await _orderHelper.GetOrder(orderId);
            for(int i = 0; order.rentals != null && i < order.rentals.Count; i++)
            {
                order.rentals[i] = await GetRental(order.rentals[i].id);
            }
            return Ok(new ApiResult<Models.Order?>()
            {
                code = 0,
                message = "",
                data = order
            });
        }
        [HttpGet("{rentItemId}")]
        public async Task<ActionResult<ApiResult<Models.Rental?>>> SetRentItemStatus(int rentItemId,
            string status, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<Models.Rental?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            if (status != "未发放" && status != "已暂存" && status != "已发放" && status != "已归还")
            {
                return Ok(new ApiResult<Models.Rental?>()
                {
                    code = 1,
                    message = "无此状态",
                    data = null
                });
            }
            Models.RentItem? rentItem = await _db.rentItem.Where(r => r.id == rentItemId)
                .AsNoTracking().FirstOrDefaultAsync();
            if (rentItem == null)
            {
                return Ok(new ApiResult<Models.Rental?>()
                {
                    code = 1,
                    message = "无此租赁物",
                    data = null
                });
            }
            RentItemLog log = new RentItemLog()
            {
                id = 0,
                rent_item_id = rentItemId,
                status = Util.UrlDecode(status),
                staff_id = staff.id,
                valid = 1,
                create_date = DateTime.Now
            };
            await _db.rentItemLog.AddAsync(log);
            await _db.SaveChangesAsync();

            RentProduct? rentProduct = null;
            if (rentItem.rent_product_id != null)
            {
                rentProduct = await _db.rentProduct.Where(p => p.id == rentItem.rent_product_id).FirstOrDefaultAsync();
            }
            if (rentProduct != null)
            {
                if (log.status == "已发放")
                {
                    rentProduct.status = "租赁中";
                    rentProduct.update_date = DateTime.Now;
                    _db.rentProduct.Entry(rentProduct).State = EntityState.Modified;
                    await _db.SaveChangesAsync();
                }
                else if (log.status == "已归还")
                {
                    rentProduct.status = "正常";
                    rentProduct.update_date = DateTime.Now;
                    _db.rentProduct.Entry(rentProduct).State = EntityState.Modified;
                    await _db.SaveChangesAsync();
                }
            }

            Rental rental = await GetRental((int)rentItem.rental_id);
            bool allReturned = true;
            for (int i = 0; rental.rentItems != null && i < rental.rentItems.Count; i++)
            {
                if (rental.rentItems[i].status != "已归还" && rental.rentItems[i].status != "未发放"
                && rental.rentItems[i].status != "已更换" && rental.rentItems[i].noNeed == false)
                {
                    allReturned = false;
                    break;
                }
            }
            if (allReturned)
            {
                rental.settled = 1;
                rental.update_date = DateTime.Now;
                CoreDataModLog dataLog = CoreDataModLog.CreateManualLog("Rental", "settled", rental.id,
                    "归还租赁物", null, staff.id, "0", "1", "全部归还，结算租金");
                await _db.coreDataModLog.AddAsync(dataLog);
                for (int k = 0; rental.guaranties != null && k < rental.guaranties.Count; k++)
                {
                    Guaranty guaranty = rental.guaranties[k];
                    guaranty.relieve = 1;
                    guaranty.update_date = DateTime.Now;
                    _db.guaranty.Entry(guaranty).State = EntityState.Modified;
                }
                _db.rental.Entry(rental).State = EntityState.Modified;
                await _db.SaveChangesAsync();
            }
            if (log.status == "已发放")
            {
                rental.settled = 0;
                rental.update_date = DateTime.Now;
                for (int k = 0; rental.guaranties != null && k < rental.guaranties.Count; k++)
                {
                    Guaranty guaranty = rental.guaranties[k];
                    guaranty.relieve = 0;
                    guaranty.update_date = DateTime.Now;
                    _db.guaranty.Entry(guaranty).State = EntityState.Modified;
                }
                _db.rental.Entry(rental).State = EntityState.Modified;
                await _db.SaveChangesAsync();
            }
            return Ok(new ApiResult<Models.Rental>()
            {
                code = 0,
                message = "",
                data = rental
            });
        }
        [NonAction]
        public async Task<Models.Rental> UpdateRental(Models.Rental rental, string scene, int? staffId, int? memberId = null)
        {
            Rental oriRental = await GetRental(rental.id);
            if (rental._filledOverTimeCharge != null && oriRental.totalOvertimeAmount != rental._filledOverTimeCharge)
            {
                DateTime? rentDate = null;
                List<Models.RentalDetail> details = oriRental.availabelRentDetails.Where(d => d.charge_type == "超时费").OrderByDescending(d => d.rental_date).ToList();
                for (int i = 0; details != null && i < details.Count; i++)
                {
                    if (rentDate == null)
                    {
                        rentDate = details[i].rental_date;
                    }
                    details[i].valid = 0;
                    details[i].update_date = DateTime.Now;
                    _db.rentalDetail.Entry(details[i]).State = EntityState.Modified;
                }
                if (rentDate == null)
                {
                    if (rental.realEndDate != null)
                    {
                        rentDate = rental.realEndDate;
                    }
                    else if (rental.realStartDate != null)
                    {
                        rentDate = rental.realStartDate;
                    }
                    else
                    {
                        rentDate = DateTime.Now;
                    }
                }
                Models.RentalDetail detail = new Models.RentalDetail()
                {
                    id = 0,
                    rental_id = rental.id,
                    rental_date = (DateTime)rentDate,
                    rent_item_id = null,
                    charge_type = "超时费",
                    rent_price_id = null,
                    amount = (double)rental._filledOverTimeCharge,
                    staff_id = staffId,
                    valid = 1,
                    create_date = DateTime.Now

                };
                await _db.rentalDetail.AddAsync(detail);
            }
            List<CoreDataModLog> logs = Util.GetUpdateDifferenceLog<Models.Rental>(oriRental, rental, memberId, staffId, scene);
            for (int i = 0; i < logs.Count; i++)
            {
                await _db.coreDataModLog.AddAsync(logs[i]);
            }
            _db.rental.Entry(rental).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return rental;
        }
        [HttpPost]
        public async Task<ActionResult<ApiResult<Models.RentItem>>> UpdateRentItemByStaff([FromBody] Models.RentItem rentItem, [FromQuery] string scene,
            [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<Models.Rental?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            scene = Util.UrlDecode(scene);
            Models.RentItem item = await UpdateRentItem(rentItem, scene, staff.id, null);
            return Ok(new ApiResult<Models.RentItem>()
            {
                code = 0,
                message = "",
                data = item
            });
        }
        [NonAction]
        public async Task<Models.RentItem> UpdateRentItem(Models.RentItem rentItem, string scene, int? staffId, int? memberId)
        {
            Models.RentItem oriItem = await _db.rentItem.Where(r => r.id == rentItem.id).AsNoTracking().FirstOrDefaultAsync();
            List<CoreDataModLog> logs = Util.GetUpdateDifferenceLog<Models.RentItem>(oriItem, rentItem, null, staffId, scene);
            for (int i = 0; i < logs.Count; i++)
            {
                await _db.coreDataModLog.AddAsync(logs[i]);
            }
            _db.rentItem.Entry(rentItem).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            rentItem.category = await _db.rentCategory.Where(c => c.id == rentItem.category_id).AsNoTracking().FirstOrDefaultAsync();
            rentItem.logs = await _db.rentItemLog.Where(l => l.rent_item_id == rentItem.id).Include(l => l.staff).AsNoTracking().ToListAsync();
            return rentItem;
        }
        [HttpPost]
        public async Task<ActionResult<ApiResult<Models.Rental?>>> UpdateRentalByStaff([FromBody] Models.Rental rental, [FromQuery] string scene,
            [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<Models.Rental?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            scene = Util.UrlDecode(scene);
            await UpdateRental(rental, scene, staff.id, null);
            return Ok(new ApiResult<Models.Rental>()
            {
                code = 0,
                message = "",
                data = await GetRental(rental.id)
            });
        }
        [HttpPost]
        public async Task<ActionResult<ApiResult<List<Models.RentalDetail>?>>> UpdateRentalDetails([FromBody] List<Models.RentalDetail> details,
            [FromQuery] string scene, [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            scene = Util.UrlDecode(scene);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<List<Models.RentalDetail>>()
                {
                    code = 1,
                    message = "",
                    data = null
                });
            }
            List<Models.RentalDetail> newDetails = new List<Models.RentalDetail>();
            for (int i = 0; i < details.Count; i++)
            {
                Models.RentalDetail detail = details[i];
                if (detail._filledDiscountAmount != detail.discountTotalAmount)
                {
                    OrderController _orderHelper = new OrderController(_db, _oriConfig, _httpContextAccessor);
                    Models.Rental rental = await _db.rental.Where(r => r.id == detail.rental_id).AsNoTracking().FirstOrDefaultAsync();
                    if (rental == null)
                    {
                        continue;
                    }
                    await _orderHelper.UpdateSingleDiscount((int)rental.order_id, "租赁", rental.id, "日租金", detail.id,
                        (double)detail._filledDiscountAmount, staff.id, scene);
                }
                Models.RentalDetail oriDetail = await _db.rentalDetail.Where(r => r.id == details[i].id)
                    .AsNoTracking().FirstOrDefaultAsync();
                if (oriDetail == null)
                {
                    continue;
                }
                List<CoreDataModLog> logs = Util.GetUpdateDifferenceLog<Models.RentalDetail>(oriDetail, detail, null, staff.id, scene);
                _db.rentalDetail.Entry(detail).State = EntityState.Modified;
                for (int j = 0; j < logs.Count; j++)
                {
                    await _db.coreDataModLog.AddAsync(logs[j]);
                }
                await _db.SaveChangesAsync();
                newDetails.Add(detail);
            }
            return Ok(new ApiResult<List<Models.RentalDetail>>()
            {
                code = 0,
                message = "",
                data = newDetails
            });
        }

        // 按天一次性修改：当天租金 + 当天减免 + 当天超时费（按天 upsert）；
        // waived=true 时免除当天全部费用（valid 置 0、金额保留），取消勾选即恢复
        [HttpPost("{rentalId}")]
        public async Task<ActionResult<ApiResult<Models.Rental?>>> UpdateRentalDayChargesByStaff([FromRoute] int rentalId,
            [FromQuery] int rentDetailId, [FromQuery] double rent, [FromQuery] double overtime, [FromQuery] double discount,
            [FromQuery] string scene, [FromQuery] string sessionKey, [FromQuery] bool waived = false,
            [FromQuery] string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            scene = Util.UrlDecode(scene);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<Models.Rental?>() { code = 1, message = "没有权限", data = null });
            }
            Models.Rental rental = await _db.rental.Where(r => r.id == rentalId).AsNoTracking().FirstOrDefaultAsync();
            if (rental == null || rental.order_id == null)
            {
                return Ok(new ApiResult<Models.Rental?>() { code = 1, message = "无此租赁", data = null });
            }
            // 当天租金明细：用 id 定位（每一行的真理之源），同时也确定"当天"
            Models.RentalDetail rentDetail = await _db.rentalDetail
                .Where(d => d.id == rentDetailId && d.rental_id == rentalId).FirstOrDefaultAsync();
            if (rentDetail == null)
            {
                return Ok(new ApiResult<Models.Rental?>() { code = 1, message = "无此租金明细", data = null });
            }
            DateTime theDay = rentDetail.rental_date.Date;
            OrderController _orderHelper = new OrderController(_db, _oriConfig, _httpContextAccessor);

            if (waived)
            {
                // ===== 路径 A：免除当天全部费用（valid→0，金额保留）=====
                // 1) 当天租金明细
                if (rentDetail.valid == 1)
                {
                    await _db.coreDataModLog.AddAsync(CoreDataModLog.CreateManualLog("rental_detail", "valid",
                        rentDetail.id, scene, null, staff.id, rentDetail.valid.ToString(), "0", "免除当日租金"));
                    rentDetail.valid = 0;
                    rentDetail.update_date = DateTime.Now;
                    _db.Entry(rentDetail).State = EntityState.Modified;
                    await _db.SaveChangesAsync();
                }
                // 2) 当天超时费（按天定位）
                Models.RentalDetail otDetailW = await _db.rentalDetail
                    .Where(d => d.rental_id == rentalId && d.rental_date.Date == theDay
                        && d.charge_type == "超时费" && d.valid == 1)
                    .OrderByDescending(d => d.id).FirstOrDefaultAsync();
                if (otDetailW != null)
                {
                    await _db.coreDataModLog.AddAsync(CoreDataModLog.CreateManualLog("rental_detail", "valid",
                        otDetailW.id, scene, null, staff.id, otDetailW.valid.ToString(), "0", "免除当日超时费"));
                    otDetailW.valid = 0;
                    otDetailW.update_date = DateTime.Now;
                    _db.Entry(otDetailW).State = EntityState.Modified;
                    await _db.SaveChangesAsync();
                }
                // 3) 当天减免：仅在有 valid=1 减免时才作废（避免 UpdateSingleDiscount(amount=0,无行) NRE）
                var existDiscountW = await _db.discount.Where(d => d.order_id == rental.order_id
                    && d.biz_type == "租赁" && d.biz_id == rental.id && d.sub_biz_type == "日租金"
                    && d.sub_biz_id == rentDetail.id && d.ticket_code == null && d.valid == 1)
                    .AsNoTracking().FirstOrDefaultAsync();
                if (existDiscountW != null)
                {
                    await _orderHelper.UpdateSingleDiscount((int)rental.order_id, "租赁", rental.id, "日租金",
                        rentDetail.id, 0, staff.id, scene);
                }
                Rental waivedRental = await GetRental(rentalId);
                return Ok(new ApiResult<Models.Rental?>() { code = 0, message = "", data = waivedRental });
            }

            // ===== 路径 B：正常更新 / 从免除恢复 =====
            // 1) 当天租金额 + 复活（"金额是否变" 与 "是否需复活" 相互独立）
            bool rentDirty = false;
            if (rentDetail.amount != rent)
            {
                await _db.coreDataModLog.AddAsync(CoreDataModLog.CreateManualLog("rental_detail", "amount",
                    rentDetail.id, scene, null, staff.id, rentDetail.amount.ToString(), rent.ToString(), "修改租金"));
                rentDetail.amount = rent;
                rentDirty = true;
            }
            if (rentDetail.valid != 1)
            {
                await _db.coreDataModLog.AddAsync(CoreDataModLog.CreateManualLog("rental_detail", "valid",
                    rentDetail.id, scene, null, staff.id, rentDetail.valid.ToString(), "1", "恢复当日租金"));
                rentDetail.valid = 1;
                rentDirty = true;
            }
            if (rentDirty)
            {
                rentDetail.update_date = DateTime.Now;
                _db.Entry(rentDetail).State = EntityState.Modified;
                await _db.SaveChangesAsync();
            }
            // 2) 当天减免（与现有不同才写；UpdateSingleDiscount 自带"非0复活/0作废"）
            var existDiscount = await _db.discount.Where(d => d.order_id == rental.order_id
                && d.biz_type == "租赁" && d.biz_id == rental.id && d.sub_biz_type == "日租金"
                && d.sub_biz_id == rentDetail.id && d.ticket_code == null && d.valid == 1)
                .AsNoTracking().FirstOrDefaultAsync();
            double curDiscount = existDiscount == null ? 0 : existDiscount.amount;
            if (curDiscount != discount)
            {
                await _orderHelper.UpdateSingleDiscount((int)rental.order_id, "租赁", rental.id, "日租金",
                    rentDetail.id, discount, staff.id, scene);
            }
            // 3) 当天超时费 upsert（按天：rental_id + 当天 + 超时费；不限 valid 以支持恢复）
            Models.RentalDetail otDetail = await _db.rentalDetail
                .Where(d => d.rental_id == rentalId && d.rental_date.Date == theDay && d.charge_type == "超时费")
                .OrderByDescending(d => d.id).FirstOrDefaultAsync();
            if (otDetail != null)
            {
                if (overtime <= 0)
                {
                    if (otDetail.valid == 1)
                    {
                        await _db.coreDataModLog.AddAsync(CoreDataModLog.CreateManualLog("rental_detail", "valid",
                            otDetail.id, scene, null, staff.id, otDetail.valid.ToString(), "0", "清空超时费"));
                        otDetail.valid = 0;
                        otDetail.update_date = DateTime.Now;
                        _db.Entry(otDetail).State = EntityState.Modified;
                        await _db.SaveChangesAsync();
                    }
                }
                else
                {
                    bool otDirty = false;
                    if (otDetail.amount != overtime)
                    {
                        await _db.coreDataModLog.AddAsync(CoreDataModLog.CreateManualLog("rental_detail", "amount",
                            otDetail.id, scene, null, staff.id, otDetail.amount.ToString(), overtime.ToString(), "修改超时费"));
                        otDetail.amount = overtime;
                        otDirty = true;
                    }
                    if (otDetail.valid != 1)
                    {
                        await _db.coreDataModLog.AddAsync(CoreDataModLog.CreateManualLog("rental_detail", "valid",
                            otDetail.id, scene, null, staff.id, otDetail.valid.ToString(), "1", "恢复超时费"));
                        otDetail.valid = 1;
                        otDirty = true;
                    }
                    if (otDirty)
                    {
                        otDetail.update_date = DateTime.Now;
                        _db.Entry(otDetail).State = EntityState.Modified;
                        await _db.SaveChangesAsync();
                    }
                }
            }
            else if (overtime > 0)
            {
                Models.RentalDetail newOt = new Models.RentalDetail()
                {
                    id = 0,
                    rental_id = rentalId,
                    rent_item_id = null,
                    charge_type = "超时费",
                    rental_date = rentDetail.rental_date,
                    rent_price_id = null,
                    amount = overtime,
                    memo = "",
                    staff_id = staff.id,
                    valid = 1,
                    create_date = DateTime.Now
                };
                await _db.rentalDetail.AddAsync(newOt);
                await _db.SaveChangesAsync();
                await _db.coreDataModLog.AddAsync(CoreDataModLog.CreateManualLog("rental_detail", "amount",
                    newOt.id, scene, null, staff.id, "0", overtime.ToString(), "新增超时费"));
                await _db.SaveChangesAsync();
            }
            Rental updatedRental = await GetRental(rentalId);
            return Ok(new ApiResult<Models.Rental?>() { code = 0, message = "", data = updatedRental });
        }

        // 员工在订单详情页把某个 rental 设/撤「招待」。招待是派生豁免：
        // rental.entertain=true 时 totalRentalAmount→0、totalSummary 不计租金，
        // 订单应收（Order.cs 计算属性）也自动排除该 rental，无需改动 rental_detail。
        [HttpPost("{rentalId}")]
        public async Task<ActionResult<ApiResult<Models.Rental?>>> SetRentalEntertainByStaff(
            [FromRoute] int rentalId, [FromQuery] bool entertain, [FromQuery] string scene,
            [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<Models.Rental?>() { code = 1, message = "没有权限", data = null });
            }
            scene = Util.UrlDecode(scene);
            Models.Rental rental = await _db.rental.Where(r => r.id == rentalId).FirstOrDefaultAsync();
            if (rental == null)
            {
                return Ok(new ApiResult<Models.Rental?>() { code = 1, message = "无此租赁", data = null });
            }
            if (rental.entertain != entertain)
            {
                await _db.coreDataModLog.AddAsync(CoreDataModLog.CreateManualLog("rental", "entertain",
                    rental.id, scene, null, staff.id, rental.entertain.ToString(), entertain.ToString(),
                    entertain ? "设为招待" : "取消招待"));
                rental.entertain = entertain;
                rental.update_date = DateTime.Now;
                _db.rental.Entry(rental).State = EntityState.Modified;
                await _db.SaveChangesAsync();
            }
            return Ok(new ApiResult<Models.Rental?>() { code = 0, message = "", data = await GetRental(rentalId) });
        }

        [HttpGet]
        public async Task ContinueRentOrder(DateTime? rentDate = null)
        {
            if (rentDate == null)
            {
                rentDate = DateTime.Now.Date.AddDays(1).Date;
            }
            List<Models.Order> orders = await _db.order
                .Include(o => o.rentals).ThenInclude(r => r.rentItems).ThenInclude(i => i.logs)
                .Include(o => o.rentals).ThenInclude(r => r.details).ThenInclude(d => d.rentPrice)
                .Include(o => o.rentals).ThenInclude(r => r.pricePresets)
                .Where(o => o.valid == 1 && o.type == "租赁"
                    && o.rentals.Any(r => r.valid == 1 && r.settled == 0
                    && r.rentItems != null && r.rentItems.Count > 0
                    //&& o.id == 67390
                    //&& r.rentItems.Where(i => i.status != "已归还").ToList().Count > 0
                ))
                .AsNoTracking().ToListAsync();
            for (int i = 0; i < orders.Count; i++)
            {
                if (((DateTime)rentDate).Date < orders[i].biz_date.Date)
                {
                    continue;
                }
                for (int j = 0; orders[i].rentals != null && j < orders[i].rentals.Count; j++)
                {
                    Rental rental = orders[i].rentals[j];
                    rental.order = orders[i];
                    await ContinueRental(rental, (DateTime)rentDate);
                }
            }
        }
        [NonAction]
        public async Task ContinueRental(Models.Rental rental, DateTime rentDate)
        {

            List<Models.RentalDetail> details = await _db.rentalDetail
                .Where(d => d.valid == 1 && d.rental_date.Date == rentDate.Date && d.rental_id == rental.id)
                .AsNoTracking().ToListAsync();
            if (details.Count > 0)
            {
                return;
            }
            // 只有「生效」的 rental 才按天计费：至少一件装备当前真在外（已发放/暂存）。
            // 旧条件 i.status != "已归还" 会把「未发放」「已更换」也算成在租 → 从没发出去的装备也天天计租金（虚账根因）。
            // status 由 RentItemLog 派生（无领还事件=「未发放」），外层 ContinueRentOrder 已 Include rentItems.logs。
            if (rental.settled == 1 || rental.valid == 0
            || rental.rentItems.Where(i => i.status == "已发放" || i.status == "暂存").Count() == 0
            || rental.details.Where(d => ((DateTime)d.rental_date).Date == rentDate.Date).Count() > 0)
            {
                return;
            }
            string? rentType = null;
            string? scene = "门市";
            string? dayType = rentDate.DayOfWeek == DayOfWeek.Saturday || rentDate.DayOfWeek == DayOfWeek.Sunday ? "周末" : "平日";
            double? discountAmount = null;
            RentalPricePreset preset = rental.pricePresets
                .Where(p => p.rent_date.Date == rentDate.Date).FirstOrDefault();
            if (preset != null)
            {
                rentType = preset.rent_type;
                scene = preset.scene;
                dayType = preset.day_type;
                discountAmount = preset.discount == 0 ? null : preset.discount;
            }
            else
            {
                rentType = "多日";
            }
            int shopId = 10;
            Models.Order order = await _db.order.Where(o => o.id == rental.order_id).AsNoTracking().FirstOrDefaultAsync();
            if (order != null)
            {
                Shop shop = await _db.shop.Where(s => s.name == order.shop).AsNoTracking().FirstOrDefaultAsync();
                if (shop != null)
                {
                    shopId = shop.id;
                }

            }
            RentPrice price = await _db.rentPrice.Where(p => p.day_type == dayType
                && p.rent_type == rentType && p.scene == scene && p.valid == 1 && p.shop_id == shopId
                && ((rental.package_id != null && p.package_id == rental.package_id)
                || (rental.category_id != null && p.category_id == rental.category_id)))
                .AsNoTracking().FirstOrDefaultAsync();
            double newPrice = (double)(price == null ? 0 : price.price);
            if (preset != null)
            {
                newPrice = preset.price;
            }
            else
            {
                RentalPricePreset manualPreset = rental.pricePresets
                    .Where(p => p.manual).FirstOrDefault();
                if (manualPreset != null)
                {
                    newPrice = manualPreset.price;
                }
            }
            Models.RentalDetail detail = new Models.RentalDetail()
            {
                id = 0,
                rental_id = rental.id,
                rent_price_id = price == null ? null : price.id,
                charge_type = "租金",
                rental_date = rentDate,
                rent_item_id = null,
                amount = newPrice,
                memo = "系统续租",
                staff_id = rental.staff_id,
                valid = 1,
                create_date = DateTime.Now
            };
            await _db.rentalDetail.AddAsync(detail);
            await _db.SaveChangesAsync();
            if (discountAmount != null && discountAmount > 0)
            {
                Discount discountObj = new Discount()
                {
                    id = 0,
                    amount = (double)discountAmount,
                    order_id = rental.order_id,
                    biz_type = "租赁",
                    biz_id = rental.id,
                    sub_biz_type = "日租金",
                    sub_biz_id = detail.id,
                    staff_id = rental.staff_id,
                    member_id = null,
                    valid = 1,
                    create_date = DateTime.Now

                };

                await _db.discount.AddAsync(discountObj);
                await _db.SaveChangesAsync();
            }
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<List<Models.Order>?>>> GetConfirmedRentOrder(string shop,
            DateTime startDate, DateTime endDate, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff.title_level == 50)
            {
                shop = "万龙体验中心";
            }
            if (staff.title_level < 50)
            {
                return Ok(new ApiResult<List<Models.Order>?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            OrderController _orderHelper = new OrderController(_db, _config, _httpContextAccessor);
            List<Models.Order> orders = (await _orderHelper.GetCommonOrders(null, shop, null, null, "租赁",
                null, null, null, false, null, false, false, null, null, startDate, endDate))
                .OrderByDescending(o => o.biz_date).ToList();
            List<Models.Order> confirmedOrders = new List<Models.Order>();
            for (int i = 0; i < orders.Count; i++)
            {
                Models.Order order = orders[i];
                for (int j = 0; j < order.orderShares.Count; j++)
                {
                    OrderShare orderShare = order.orderShares[j];
                    for (int k = 0; k < orderShare.paymentShares.Count; k++)
                    {
                        PaymentShare pShare = orderShare.paymentShares[k];
                        pShare.payment = order.availablePayments.Where(p => p.id == pShare.payment_id).FirstOrDefault();
                    }
                }
                if (order.paidAmount > 0 && order.closed == 1 && order.close_date != null && !order.hide)
                {
                    if ((staff.title_level == 50 && ((DateTime)order.close_date).Date < DateTime.Now.AddDays(-1).Date)
                        || staff.title_level > 50)
                    {
                        if (order.availablePayments.Where(p => p.pay_method != "微信支付" && p.pay_method != "支付宝").ToList().Count <= 0)
                        {
                            confirmedOrders.Add(order);
                        }
                    }
                }
            }
            return Ok(new ApiResult<List<Models.Order>>()
            {
                code = 0,
                message = "",
                data = confirmedOrders
            });
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<List<Models.Order>?>>> CloseOrder()
        {
            OrderShareController _shareHelper = new OrderShareController(_db, _oriConfig, _httpContextAccessor);
            List<Models.Order> orders = await _db.order.Include(o => o.payments).ThenInclude(p => p.refunds)
                .Where(o => o.valid == 1 && o.closed == 0 && o.close_date == null && o.type == "租赁"
                && o.create_date.Date > DateTime.Parse("2025-10-01").Date).OrderByDescending(o => o.id)
                .AsNoTracking().ToListAsync();
            List<Models.Order> newList = new List<Models.Order>();
            OrderController _orderHelper = new OrderController(_db, _oriConfig, _httpContextAccessor);
            for (int i = 0; i < orders.Count; i++)
            {
                // 有押金要求但从未付款：废单，直接关闭（¥0 订单不在此处处理，走完整检查）
                if (orders[i].availablePayments.Count <= 0
                    && orders[i].paying_amount.HasValue && orders[i].paying_amount.Value > 0)
                {
                    orders[i].closed = 1;
                    orders[i].close_date = null;
                    orders[i].update_date = DateTime.Now;
                    _db.order.Entry(orders[i]).State = EntityState.Modified;
                    continue;
                }
                Models.Order order = await _orderHelper.GetOrder(orders[i].id);
                bool allSettled = true;
                for (int j = 0; order.rentals != null && j < order.rentals.Count; j++)
                {
                    if (order.rentals[j].settled != 1)
                    {
                        allSettled = false;
                    }
                }
                // 未足额收款不关单。paying_amount 表示「尚欠应收」：DealSuccessPaidOrder 在支付成功后会将其置 null（已收齐），
                // 故 null 或 <=0 视为已收齐、允许关单；仍为正值说明还有应收未到账，不关单。
                // 注意：不能用 paidAmount - paying_amount == 0 判定——支付成功后 paying_amount 恒为 null，
                // 会退化成「paidAmount==0」，导致任何已付款订单都永远关不掉（2026-06-21~ 全停关单的根因）。
                bool paymentFulfilled = order.paying_amount == null || Math.Round((double)order.paying_amount, 2) <= 0;
                bool finished = false;
                if (allSettled && paymentFulfilled)
                {
                    if (order.rentProperties == null && order.refundAmount > 0)
                    {
                        finished = true;
                    }
                    if (order.rentProperties != null && order.totalRentUnRefund != null && Math.Round((double)order.totalRentUnRefund, 2) == 0)
                    {
                        finished = true;
                    }
                }

                if (finished)
                {
                    order.closed = 1;
                    DateTime closeDate = DateTime.Now;
                    if (order.refundAmount > 0)
                    {
                        closeDate = order.availableRefunds[order.availableRefunds.Count - 1].create_date;
                    }
                    order.close_date = closeDate;
                    order.update_date = DateTime.Now;
                    _db.order.Entry(order).State = EntityState.Modified;
                    newList.Add(order);
                    try
                    {
                        if (order.type == "租赁" && order.shop == "万龙体验中心")
                        {
                            OrderShare share = await _db.orderShare.Where(s => s.valid && s.order_id == order.id)
                                .AsNoTracking().FirstOrDefaultAsync();
                            await _shareHelper.CreatePaymentShare(share);
                        }
                    }
                    catch
                    {

                    }
                }
            }
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<List<Models.Order>>()
            {
                code = 0,
                message = "",
                data = newList
            });
        }
        [NonAction]
        public async Task<List<Models.RentItem>> GetUnReturnedRentItems()
        {
            List<Models.RentItem> rentItems = await _db.rentItem
                .Include(r => r.logs.Where(l => l.valid == 1).OrderByDescending(l => l.id)).ThenInclude(l => l.staff)
                .Where(r => r.valid == 1 && r.logs.Count > 0)
                .Include(r => r.rental).ThenInclude(r => r.order).ThenInclude(o => o.member)
                    .ThenInclude(m => m.memberSocialAccounts)
                .Include(r => r.category).OrderBy(r => r.id)
                .Where(r => r.rental.valid == 1 && r.rental.order.valid == 1 && r.rental.order.is_test == 0)
                .AsSplitQuery().AsNoTracking().OrderByDescending(r => r.id).ToListAsync();
            List<Models.RentItem> unreturned = rentItems
                .Where(r => r.status != "已归还" && r.status != "已更换" && r.status != "未发放" && r.noNeed == false)
                .ToList();

            // 对齐订单明细手机号口径：确保未归还列表里的 order.contact_num 至少等于 order.customerCell。
            for (int i = 0; i < unreturned.Count; i++)
            {
                Models.Order? order = unreturned[i].rental?.order;
                if (order == null)
                {
                    continue;
                }
                string normalizedCell = (order.customerCell ?? "").Trim();
                if (string.IsNullOrWhiteSpace(normalizedCell)
                    && order.member != null
                    && order.member.memberSocialAccounts != null)
                {
                    MemberSocialAccount? msaCell = order.member.memberSocialAccounts
                        .Where(m => m.valid == 1 && m.type.Trim().Equals(MemberSocialAccount.TYPE_CELL))
                        .OrderByDescending(m => m.id)
                        .FirstOrDefault();
                    if (msaCell != null && !string.IsNullOrWhiteSpace(msaCell.num))
                    {
                        normalizedCell = msaCell.num.Trim();
                    }
                }
                if (string.IsNullOrWhiteSpace(order.contact_num) && !string.IsNullOrWhiteSpace(normalizedCell))
                {
                    order.contact_num = normalizedCell;
                }
            }
            return unreturned;
        }
        [HttpGet]
        public async Task<ActionResult<List<Models.CategoryRentItem>?>> GetUnReturnedRentItemsByStaff(
            string? shop, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<List<Models.RentItem>?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            if (shop != null)
            {
                shop = Util.UrlDecode(shop);
            }
            List<Models.RentItem> list = (await GetUnReturnedRentItems()).Where(l => (l.rental.order.shop == shop || shop == null)).ToList();
            var g = list.GroupBy(r => r.category_id).Select(g => new { category_id = g.Key, items = g.ToList() }).OrderByDescending(g => g.items.Count).ToList();
            List<CategoryRentItem> finalList = new List<CategoryRentItem>();
            for (int i = 0; i < g.Count; i++)
            {
                CategoryRentItem item = new CategoryRentItem()
                {
                    category_id = (int)g[i].category_id,
                    category = g[i].items[0].category,
                    items = g[i].items
                };
                finalList.Add(item);
            }
            return Ok(new ApiResult<List<Models.CategoryRentItem>>()
            {
                code = 0,
                message = "",
                data = finalList
            });
        }
        [NonAction]
        public async Task<List<Models.Order>?> GetOrdersFuzzy(string key)
        {
            key = key.ToLower();
            List<Models.RentCategory> categories = await _db.rentCategory
                .Include(c => c.rentItems.Where(i => i.valid == 1)).ThenInclude(r => r.rental)
                .ThenInclude(o => o.order).ThenInclude(o => o.staff)
                .Where(c => c.valid == 1 && c.name != null && c.name.ToLower().IndexOf(key) >= 0)
                .AsNoTracking().ToListAsync();
            List<Models.RentItem> items = await _db.rentItem.Where(r => r.name != null && r.name.ToLower().IndexOf(key) >= 0 && r.valid == 1)
                .Include(r => r.rental).ThenInclude(o => o.order).ThenInclude(o => o.staff)
                .AsNoTracking().ToListAsync();
            List<Models.Order> orders = new List<Models.Order>();
            for (int i = 0; i < categories.Count; i++)
            {
                for (int j = 0; categories[i].rentItems != null && j < categories[i].rentItems.Count; j++)
                {
                    Models.Order order = categories[i].rentItems[j].rental.order;
                    if (!orders.Any(o => o.id == order.id) && order.valid == 1)
                    {
                        orders.Add(order);
                    }
                }
            }
            for (int i = 0; i < items.Count; i++)
            {
                Models.Order order = items[i].rental.order;
                if (!orders.Any(o => o.id == order.id) && order.valid == 1)
                {
                    orders.Add(order);
                }
            }
            return orders.OrderByDescending(o => o.id).ToList();
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<List<Models.Order>?>>> GetOrdersFuzzyByStaff(string key,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<List<Models.Order>?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            List<Models.Order>? orders = await GetOrdersFuzzy(key);
            return Ok(new ApiResult<List<Models.Order>?>()
            {
                code = 0,
                message = "",
                data = orders
            });
        }
        [NonAction]
        public async Task<List<RentCategory>> GetChangeCompatibleCategory(int categoryId)
        {
            RentCategory? oriCategory = await _db.rentCategory.Where(c => c.id == categoryId)
                .Include(c => c.priceList).AsNoTracking().FirstOrDefaultAsync();
            string oriCode = oriCategory.code;
            string fatherCode = oriCode.Substring(0, oriCode.Length - 2);
            if (fatherCode == "")
            {
                return new List<RentCategory>() { oriCategory };
            }
            RentCategory? fatherCategory = await _db.rentCategory.Where(c => c.code == fatherCode)
                .AsNoTracking().FirstOrDefaultAsync();
            if (fatherCategory == null)
            {
                return new List<RentCategory>() { oriCategory };
            }
            oriCategory.father = fatherCategory;
            List<RentCategory> children = new List<RentCategory>();
            children.Add(oriCategory);
            List<RentCategory> others = await _db.rentCategory.Where(c => c.id != oriCategory.id
                && c.code.Length == oriCategory.code.Length && c.code.StartsWith(fatherCode))
                .Include(c => c.priceList)
                .AsNoTracking().ToListAsync();
            if (oriCategory.father.code != "07" && oriCategory.father.code != "08" && oriCategory.father.code != "09")
            {
                for (int i = 0; i < others.Count; i++)
                {
                    RentCategory otherCategory = others[i];

                    RentPrice oriPrice = oriCategory.priceList.Where(p => p.shop_id == 10 && p.valid == 1 && p.scene == "门市"
                        && p.day_type == "平日" && p.rent_type == "日场").FirstOrDefault();
                    RentPrice otherPrice = otherCategory.priceList.Where(p => p.shop_id == 10 && p.valid == 1 && p.scene == "门市"
                        && p.day_type == "平日" && p.rent_type == "日场").FirstOrDefault();
                    if (otherPrice.price <= oriPrice.price)
                    {
                        otherCategory.father = oriCategory.father;
                        children.Add(otherCategory);
                    }
                }
            }
            return children;
        }
        [HttpGet("{categoryId}")]
        public async Task<ActionResult<ApiResult<RentCategory>>> QueryChangeCompatibleCategory(int categoryId)
        {
            List<RentCategory> children = await GetChangeCompatibleCategory(categoryId);
            return Ok(new ApiResult<List<RentCategory>>()
            {
                code = 0,
                message = "",
                data = children
            });
        }
        [NonAction]
        public async Task<Models.RentItem> ChangeRentItem(Models.RentItem oriItem,
            Models.RentItem newItem, int? staffId, string? scene = null)
        {
            newItem.valid = 0;
            newItem.category = null;
            newItem.rental = null;
            newItem.logs = null;
            await _db.AddAsync(newItem);
            int i = await _db.SaveChangesAsync();
            if (i != 1)
            {
                return null;
            }
            //DateTime nowDate = DateTime.Now;
            RentItemLog oriLog = new RentItemLog()
            {
                rent_item_id = oriItem.id,
                status = Models.RentItem.RentItemStatus.已更换.ToString(),
                staff_id = staffId,
                member_id = null,
                valid = 1,
                create_date = DateTime.Now
            };
            await _db.rentItemLog.AddAsync(oriLog);
            RentItemLog newLog = new RentItemLog()
            {
                rent_item_id = newItem.id,
                status = Models.RentItem.RentItemStatus.已发放.ToString(),
                staff_id = staffId,
                member_id = null,
                valid = 1,
                create_date = DateTime.Now
            };
            await _db.rentItemLog.AddAsync(newLog);
            
            oriItem.update_date = DateTime.Now;
            oriItem.next_id = newItem.id;
            _db.rentItem.Entry(oriItem).State = EntityState.Modified;
            newItem.prev_id = oriItem.id;
            newItem.valid = 1;
            newItem.update_date = DateTime.Now;
            _db.rentItem.Entry(newItem).State = EntityState.Modified;
            CoreDataModLog dataLog = CoreDataModLog.CreateManualLog("rent_item", "id", oriItem.id, scene, null, staffId,
                oriItem.id.ToString(), newItem.id.ToString(), "更换租赁物");
            await _db.coreDataModLog.AddAsync(dataLog);
            await _db.SaveChangesAsync();
            _db.rentItem.Entry(newItem).State = EntityState.Detached;
            _db.rentItem.Entry(oriItem).State = EntityState.Detached;
            _db.rentItemLog.Entry(oriLog).State = EntityState.Detached;
            _db.rentItemLog.Entry(newLog).State = EntityState.Detached;
            await _db.SaveChangesAsync();
            return newItem;
        }
        [HttpPost("{oriRentItemId}")]
        public async Task<ActionResult<ApiResult<Rental?>>> ChangeRentItemByStaff([FromRoute] int oriRentItemId, [FromBody] Models.RentItem newRentItem,
            [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<Rental?>()
                {
                    code = 0,
                    message = "",
                    data = null
                });
            }
            Models.RentItem oriRentItem = await _db.rentItem.Where(r => r.id == oriRentItemId)
                .Include(r => r.logs).AsNoTracking().FirstOrDefaultAsync();
            if (oriRentItem == null || oriRentItem.category_id == null
                || newRentItem == null || newRentItem.category_id == null)
            {
                return Ok(new ApiResult<Rental?>()
                {
                    code = 1,
                    message = "非法租赁物",
                    data = null
                });
            }
            newRentItem.rental_id = oriRentItem.rental_id;
            List<RentCategory> othersCategory = await GetChangeCompatibleCategory((int)oriRentItem.category_id);
            if (othersCategory.Where(r => r.id == newRentItem.category_id).ToList().Count == 0)
            {
                return Ok(new ApiResult<Rental?>()
                {
                    code = 1,
                    message = "分类不符",
                    data = null
                });
            }
            if (oriRentItem.status != Models.RentItem.RentItemStatus.已发放.ToString())
            {
                return Ok(new ApiResult<Rental?>()
                {
                    code = 1,
                    message = "状态不对",
                    data = null
                });
            }
            Models.RentItem changedItem = await ChangeRentItem(oriRentItem, newRentItem, staff.id, "租赁详情页更换租赁物");
            if (changedItem == null)
            {
                return Ok(new ApiResult<Rental?>()
                {
                    code = 1,
                    message = "更换失败",
                    data = null
                });
            }
            Rental rental = await GetRental((int)changedItem.rental_id);
            return Ok(new ApiResult<Rental?>()
            {
                code = 0,
                message = "",
                data = rental
            });
        }
        [HttpGet("{orderId}")]
        public async Task<ActionResult<ApiResult<Models.Order>>> AppendRental(int orderId, string sessionKey,
            int? categoryId = null, int? packageId = null, string sessionType = "wechat_mini_openid")
        {
            // 分类/套餐二选一；两者都不传 = 无码物品（建无分类空白草稿）；都传则冲突
            if (categoryId != null && packageId != null)
            {
                return Ok(new ApiResult<List<Models.Order>?>()
                {
                    code = 1,
                    message = "参数冲突",
                    data = null
                });
            }

            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<List<Models.Order>?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            OrderController _orderHelper = new OrderController(_db, _config, _httpContextAccessor);
            Models.Order order = await _orderHelper.GetOrder(orderId);
            if (order.type != "租赁")
            {
                return Ok(new ApiResult<List<Models.Order>?>()
                {
                    code = 1,
                    message = "订单类型不符",
                    data = null
                });
            }
            if (packageId != null)
            {
                order = await AppendPackage(order, (int)packageId);
            }
            else if (categoryId != null)
            {
                order = await AppendCategory(order, (int)categoryId);
            }
            else
            {
                order = await AppendBlank(order);
            }
            return Ok(new ApiResult<Models.Order>()
            {
                code = 0,
                message = "",
                data = order
            });
        }
        [NonAction]
        public async Task<Models.Order> AppendBlank(Models.Order order)
        {
            // 无码物品：建一个无分类草稿 rental + 一个 noCode 主项 rentItem（category_id=null）。
            // 前端加载后默认展开，用户点卡片「分类」行选定品类后由组件联动拉价格 / 重建附件（同开单无码物品流程）。
            Models.Rental rental = new Rental()
            {
                id = 0,
                order_id = order.id,
                start_date = DateTime.Now,
                category_id = null,
                valid = 1,
                appending = true,
                name = "",
                guaranty = 0,
                expectDays = 1,
                pick_type = "立即租赁",
                create_date = DateTime.Now
            };
            Models.RentItem item = new Models.RentItem()
            {
                id = 0,
                rental_id = rental.id,
                category_id = null,
                valid = 1,
                noCode = true,
                is_associate = false,
                pick_type = "立即租赁",
                atOnce = true,
                create_date = DateTime.Now
            };
            rental.rentItems.Add(item);
            await _db.rental.AddAsync(rental);
            await _db.SaveChangesAsync();
            order.appendingRentals.Add(rental);
            return order;
        }
        [NonAction]
        public async Task<Models.Order> AppendCategory(Models.Order order, int categoryId)
        {
            Shop shop = await _db.shop.Where(s => s.name == order.shop).FirstOrDefaultAsync();

            string dayType = "平日";
            string scene = "门市";
            DateTime nowDate = DateTime.Now.Date;
            switch (nowDate.DayOfWeek)
            {
                case DayOfWeek.Sunday:
                case DayOfWeek.Saturday:
                    dayType = "周末";
                    break;
                default:
                    break;
            }
            RentPrice price = await _db.rentPrice
                .Where(p => p.category_id == categoryId && p.shop_id == shop.id && p.scene == scene && p.rent_type == "日场" && p.day_type == dayType)
                    .AsNoTracking().FirstOrDefaultAsync();
            RentCategory category = await _db.rentCategory.Where(c => c.id == categoryId).AsNoTracking().FirstOrDefaultAsync();
            Models.Rental rental = new Rental()
            {
                id = 0,
                order_id = order.id,
                start_date = DateTime.Now,
                category_id = categoryId,
                valid = 1,
                appending = true,
                name = category.name,
                guaranty = category.deposit,
                expectDays = 1,
                pick_type = "立即租赁",
                create_date = DateTime.Now
            };
            RentalPricePreset preset = new RentalPricePreset()
            {
                id = 0,
                rental_id = rental.id,
                rent_type = price.rent_type,
                rent_date = DateTime.Now.Date,
                price = (double)price.price,
                discount = 0,
                day_type = dayType,
                scene = scene
            };
            rental.pricePresets = new List<RentalPricePreset>() { preset };

            Models.RentItem item = new Models.RentItem()
            {
                id = 0,
                category_id = categoryId,
                //category = category,
                class_name = category?.name,
                rental_id = rental.id,
                valid = 1,
                noCode = true,
                pick_type = "立即租赁",
                atOnce = true,
                create_date = DateTime.Now
            };
            rental.rentItems.Add(item);
            await _db.rental.AddAsync(rental);
            await _db.SaveChangesAsync();
            item.category = category;

            _db.rental.Entry(rental).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            rental.priceList = await _db.rentPrice.Where(p => (p.valid == 1 && p.category_id == categoryId && p.shop_id == shop.id))
                .AsNoTracking().ToListAsync();

            order.appendingRentals.Add(rental);
            return order;
        }
        [NonAction]
        public async Task<Models.Order> AppendPackage(Models.Order order, int packageId)
        {
            RentPackage package = await _db.rentPackage
                .Include(p => p.rentPackageCategoryList).ThenInclude(c => c.rentCategory)
                .Where(p => p.id == packageId).AsNoTracking().FirstOrDefaultAsync();
            Shop shop = await _db.shop.Where(s => s.name == order.shop).FirstOrDefaultAsync();
            string dayType = "平日";
            string scene = "门市";
            DateTime nowDate = DateTime.Now.Date;
            switch (nowDate.DayOfWeek)
            {
                case DayOfWeek.Sunday:
                case DayOfWeek.Saturday:
                    dayType = "周末";
                    break;
                default:
                    break;
            }
            RentPrice price = await _db.rentPrice
                .Where(p => p.package_id == packageId && p.shop_id == shop.id && p.scene == scene && p.rent_type == "日场" && p.day_type == dayType)
                    .AsNoTracking().FirstOrDefaultAsync();

            Models.Rental rental = new Rental()
            {
                id = 0,
                name = package.name,
                order_id = order.id,
                start_date = DateTime.Now,
                package_id = packageId,
                valid = 1,
                appending = true,
                guaranty = package.deposit,
                expectDays = 1,
                pick_type = "立即租赁",
                create_date = DateTime.Now
            };
            RentalPricePreset preset = new RentalPricePreset()
            {
                id = 0,
                rental_id = rental.id,
                rent_type = price.rent_type,
                rent_date = DateTime.Now.Date,
                price = (double)price.price,
                discount = 0,
                day_type = dayType,
                scene = scene
            };
            rental.pricePresets = new List<RentalPricePreset>() { preset };
            for (int i = 0; i < package.rentPackageItemCategories.Count; i++)
            {
                Models.RentItem item = new Models.RentItem()
                {
                    id = 0,
                    rental_id = rental.id,
                    category_id = package.rentPackageItemCategories[i].categories[0].id,
                    //category = package.rentPackageItemCategories[i].categories[0],
                    valid = 1,
                    noCode = true,
                    pick_type = "立即租赁",
                    atOnce = true,
                    create_date = DateTime.Now
                };
                rental.rentItems.Add(item);
            }
            await _db.rental.AddAsync(rental);
            await _db.SaveChangesAsync();
            for (int i = 0; i < rental.rentItems.Count; i++)
            {
                Models.RentItem item = rental.rentItems[i];
                item.category = (package.rentPackageCategoryList.Where(c => c.category_id == item.category_id).FirstOrDefault()).rentCategory;
                item.class_name = item.category?.name;
            }
            _db.rental.Entry(rental).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            rental.priceList = await _db.rentPrice.Where(p => p.package_id == packageId && p.valid == 1 && p.shop_id == shop.id)
                .AsNoTracking().ToListAsync();

            order.appendingRentals.Add(rental);
            return order;
        }
        [HttpGet("{rentalId}")]
        public async Task<ActionResult<ApiResult<Models.Order>>> RemoveAppendingRental(int rentalId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<List<Models.Order>?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            Rental rental = await _db.rental
                .Where(r => r.id == rentalId && r.appending != null && r.append_commit_time == null)
                .AsNoTracking().FirstOrDefaultAsync();
            rental.valid = 0;
            _db.rental.Entry(rental).State = EntityState.Modified;
            // 清掉该追加项的押金应收记录（已确认待支付项可能已建 Guaranty），避免删除后残留虚账
            List<Models.Guaranty> gs = await _db.guaranty
                .Where(g => g.valid == 1 && g.order_id == rental.order_id && g.biz_type == "租赁" && g.biz_id == rental.id)
                .ToListAsync();
            for (int gi = 0; gi < gs.Count; gi++)
            {
                gs[gi].valid = 0;
                gs[gi].update_date = DateTime.Now;
                _db.guaranty.Entry(gs[gi]).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();
            OrderController _orderH = new OrderController(_db, _config, _httpContextAccessor);
            Models.Order order = await _orderH.GetOrder((int)rental.order_id);
            // 重算订单待支付金额 = 剩余待支付追加项的未支付 Guaranty 合计（删光则置 0）
            double remainPay = 0;
            for (int ri = 0; order.appendingRentals != null && ri < order.appendingRentals.Count; ri++)
            {
                Rental ar = order.appendingRentals[ri];
                for (int gj = 0; ar.guaranties != null && gj < ar.guaranties.Count; gj++)
                {
                    if (ar.guaranties[gj].valid == 1 && ar.guaranties[gj].payStatus == "未支付")
                    {
                        remainPay += (double)ar.guaranties[gj].amount;
                    }
                }
            }
            order.paying_amount = remainPay > 0 ? remainPay : 0;
            _db.order.Entry(order).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            order = await _orderH.GetOrder((int)rental.order_id);
            return Ok(new ApiResult<Models.Order>()
            {
                code = 0,
                message = "",
                data = order
            });
        }
        [HttpPost("{orderId}")]
        public async Task<ActionResult<ApiResult<Models.Order>>> SaveAppendings([FromRoute] int orderId, [FromBody] List<Rental> appendings,
            [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid", [FromQuery] bool commit = true)
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<List<Models.Order>?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            for (int i = 0; i < appendings.Count; i++)
            {
                if (orderId == appendings[i].order_id)
                {
                    await SaveAppendingRental(appendings[i], commit);
                }
            }
            OrderController _orderH = new OrderController(_db, _config, _httpContextAccessor);
            Models.Order order = await _orderH.GetOrder(orderId);
            // commit=false：实时保存草稿（只持久化字段，保持 appending=true，不提交/不生效/不算应付）
            if (commit)
            {
            double appendPayAmount = 0;
            for (int i = 0; order.appendingRentals != null && i < order.appendingRentals.Count; i++)
            {
                Rental rental = order.appendingRentals[i];
                for (int j = 0; rental.guaranties != null && j < rental.guaranties.Count; j++)
                {
                    Models.Guaranty guaranty = rental.guaranties[j];
                    if (guaranty.valid == 1 && guaranty.payStatus == "未支付")
                    {
                        appendPayAmount += (double)guaranty.amount;
                    }
                }
                double guarantyAmount = 0;
                if (rental.guaranty != null)
                {
                    guarantyAmount = (double)rental.guaranty;
                }
                if (rental.guaranty_discount != null)
                {
                    guarantyAmount = guarantyAmount - (double)rental.guaranty_discount;
                }
                if (rental.noGuaranty || guarantyAmount == 0)
                {
                    rental.appending = false;
                    rental.append_commit_time = DateTime.Now;
                    _db.rental.Entry(rental).State = EntityState.Modified;
                    await _db.SaveChangesAsync();
                    _db.rental.Entry(rental).State = EntityState.Detached;
                    for (int k = 0; rental.rentItems != null && k < rental.rentItems.Count; k++)
                    {
                        _db.rentItem.Entry(rental.rentItems[k]).State = EntityState.Detached;
                    }
                    await _db.SaveChangesAsync();
                    await EffectRental(rental.id, staff.id);
                }
            }
            if (appendPayAmount > 0)
            {
                order.paying_amount = appendPayAmount;
                _db.order.Entry(order).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();
            }
            return Ok(new ApiResult<Models.Order>()
            {
                code = 0,
                message = "",
                data = order
            });
        }
        [NonAction]
        public async Task<Rental> SaveAppendingRental(Rental rental, bool commit = true)
        {
            rental.priceList = new List<RentPrice>();
            rental.appending = commit ? false : true;
            rental.update_date = DateTime.Now;
            for (int i = 0; rental.pricePresets != null && i < rental.pricePresets.Count; i++)
            {
                RentalPricePreset preset = rental.pricePresets[i];
                if (preset.id == 0)
                {
                    await _db.rentalPricePreset.AddAsync(preset);
                }
                else
                {
                    _db.rentalPricePreset.Entry(preset).State = EntityState.Modified;
                }
            }
            for (int i = 0; rental.rentItems != null && i < rental.rentItems.Count; i++)
            {
                Models.RentItem rentItem = rental.rentItems[i];
                if (rentItem.id == 0)
                {
                    await _db.rentItem.AddAsync(rentItem);
                }
                else
                {
                    _db.rentItem.Entry(rentItem).State = EntityState.Modified;
                }
            }
            // 押金应收记录只在确认提交（commit）时建/改；实时保存草稿阶段不建，避免中途删草稿残留 Guaranty
            if (commit)
            {
                await SyncRentalGuaranty(rental);
            }
            _db.rental.Entry(rental).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            _db.rental.Entry(rental).State = EntityState.Detached;
            for (int i = 0; rental.rentItems != null && i < rental.rentItems.Count; i++)
            {
                _db.rentItem.Entry(rental.rentItems[i]).State = EntityState.Detached;
            }
            await _db.SaveChangesAsync();
            return rental;
        }
        // 依据 rental.guaranty - guaranty_discount 同步未支付的 Guaranty 记录（>0 建/改，否则置 valid=0）。
        // 仅暂存改动，由调用方负责 SaveChangesAsync。
        [NonAction]
        public async Task SyncRentalGuaranty(Rental rental)
        {
            if (rental.noGuaranty != true)
            {
                double gAmount = Math.Round((double)rental.guaranty - (double)rental.guaranty_discount, 2);
                Models.Guaranty? guaranty = await _db.guaranty
                    .Where(g => g.valid == 1 && g.order_id == rental.order_id && g.biz_type == "租赁" && g.biz_id == rental.id)
                    .AsNoTracking().FirstOrDefaultAsync();
                if (gAmount > 0)
                {
                    if (guaranty == null)
                    {
                        guaranty = new Models.Guaranty()
                        {
                            order_id = rental.order_id,
                            biz_type = "租赁",
                            biz_id = rental.id,
                            amount = gAmount,
                            valid = 1,
                            create_date = DateTime.Now
                        };
                        await _db.guaranty.AddAsync(guaranty);
                    }
                    else
                    {
                        if (guaranty.payStatus == "未支付")
                        {
                            guaranty.amount = gAmount;
                            guaranty.update_date = DateTime.Now;
                            _db.guaranty.Entry(guaranty).State = EntityState.Modified;
                        }
                    }
                }
                else
                {
                    if (guaranty != null)
                    {
                        guaranty.valid = 0;
                        guaranty.update_date = DateTime.Now;
                        _db.guaranty.Entry(guaranty).State = EntityState.Modified;
                    }
                }
            }
        }
        // 员工在订单详情页调整某 rental 的押金（仅未支付押金；已支付须走退款）。amount=0 即全免。
        [HttpPost("{rentalId}")]
        public async Task<ActionResult<ApiResult<Models.Rental?>>> UpdateRentalGuarantyByStaff(
            [FromRoute] int rentalId, [FromQuery] double amount, [FromQuery] string scene,
            [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<Models.Rental?>() { code = 1, message = "没有权限", data = null });
            }
            if (amount < 0)
            {
                return Ok(new ApiResult<Models.Rental?>() { code = 1, message = "押金不能为负", data = null });
            }
            scene = Util.UrlDecode(scene);
            Models.Rental rental = await GetRental(rentalId);
            if (rental == null)
            {
                return Ok(new ApiResult<Models.Rental?>() { code = 1, message = "无此租赁", data = null });
            }
            Models.Guaranty existingGuaranty = null;
            for (int i = 0; rental.guaranties != null && i < rental.guaranties.Count; i++)
            {
                Models.Guaranty g = rental.guaranties[i];
                if (g.valid == 1 && g.biz_type == "租赁" && g.biz_id == rentalId)
                {
                    existingGuaranty = g;
                    break;
                }
            }
            if (existingGuaranty != null && existingGuaranty.payStatus != "未支付")
            {
                return Ok(new ApiResult<Models.Rental?>() { code = 1, message = "押金已支付，请走退款流程", data = null });
            }
            if (rental.guaranty == null)
            {
                rental.guaranty = 0;
            }
            rental.guaranty_discount = (double)rental.guaranty - amount;
            _db.rental.Entry(rental).State = EntityState.Modified;
            await SyncRentalGuaranty(rental);
            await _db.SaveChangesAsync();
            _db.rental.Entry(rental).State = EntityState.Detached;
            List<Models.Guaranty> orderGuaranties = await _db.guaranty
                .Where(g => g.valid == 1 && g.order_id == rental.order_id)
                .Include(g => g.guarantyPayments).ThenInclude(p => p.payment)
                .AsNoTracking().ToListAsync();
            double payAmount = 0;
            for (int i = 0; i < orderGuaranties.Count; i++)
            {
                if (orderGuaranties[i].payStatus == "未支付")
                {
                    payAmount += (double)orderGuaranties[i].amount;
                }
            }
            Models.Order order = await _db.order.Where(o => o.id == rental.order_id).AsNoTracking().FirstOrDefaultAsync();
            if (order != null)
            {
                order.paying_amount = payAmount;
                _db.order.Entry(order).State = EntityState.Modified;
                await _db.SaveChangesAsync();
            }
            return Ok(new ApiResult<Models.Rental?>()
            {
                code = 0,
                message = "",
                data = await GetRental(rentalId)
            });
        }
        [HttpPost("{packageId}")]
        public async Task<ActionResult<ApiResult<RentPackage>>> UpdatePackageRentItemCategories([FromRoute] int packageId,
            [FromBody] List<RentPackageItemCategories> rentItemCategories,
            [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<RentPackage>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            RentPackage package = await _db.rentPackage.Where(p => p.id == packageId).AsNoTracking().FirstOrDefaultAsync();
            package.item_count = rentItemCategories.Count;
            _db.rentPackage.Entry(package).State = EntityState.Modified;
            List<RentPackageCategory> existingCategories = await _db.rentPackageCategory
                .Where(c => c.package_id == packageId && c.valid).AsNoTracking().ToListAsync();
            for (int i = 0; i < existingCategories.Count; i++)
            {
                existingCategories[i].valid = false;
                existingCategories[i].update_date = DateTime.Now;
                _db.rentPackageCategory.Entry(existingCategories[i]).State = EntityState.Modified;
            }
            for (int i = 0; i < rentItemCategories.Count; i++)
            {
                RentPackageItemCategories itemC = rentItemCategories[i];
                for (int j = 0; j < itemC.categories.Count; j++)
                {
                    RentPackageCategory packageCategory = new RentPackageCategory()
                    {
                        id = 0,
                        package_id = packageId,
                        item_index = itemC.itemIndex,
                        category_id = itemC.categories[j].id,
                        valid = true,
                        update_date = DateTime.Now,
                        create_date = DateTime.Now
                    };
                    await _db.rentPackageCategory.AddAsync(packageCategory);
                }
            }
            await _db.SaveChangesAsync();
            return await GetRentPackage(packageId);
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<List<ShopRentPackage>>>> GetShopRentPackages(string? key = null)
        {
            List<Shop> shops = await _db.shop.AsNoTracking().ToListAsync();
            List<ShopRentPackage> shopPackages = new List<ShopRentPackage>();
            for (int i = 0; i < shops.Count; i++)
            {
                List<RentPackage> packages = await _db.rentPackage
                    .Where(p => p.shop == shops[i].name && p.valid == 1 && (key == null || p.name.IndexOf(key) >= 0))
                    .AsNoTracking().ToListAsync();
                if (packages != null && packages.Count > 0)
                {
                    ShopRentPackage srp = new ShopRentPackage()
                    {
                        shop = shops[i],
                        rentPackages = packages
                    };
                    shopPackages.Add(srp);
                }
            }
            List<RentPackage> oldPackages = await _db.rentPackage
                    .Where(p => p.shop == null && p.valid == 1 && (key == null || p.name.IndexOf(key) >= 0))
                    .AsNoTracking().ToListAsync();
            ShopRentPackage srpOld = new ShopRentPackage()
            {
                shop = null,
                rentPackages = oldPackages
            };
            shopPackages.Add(srpOld);
            shopPackages = shopPackages.OrderByDescending(p => p.rentPackages.Count).ToList();
            return Ok(new ApiResult<List<ShopRentPackage>>()
            {
                code = 0,
                message = "",
                data = shopPackages
            });
        }
        [NonAction]
        public async Task<List<RentCategoryAssociate>> SetAssociateCategories(int categoryId, List<RentCategory> associateCategories)
        {
            List<RentCategoryAssociate> oldListAll = await _db.rentCategoryAssociate
                .Where(c => c.category_id == categoryId).AsNoTracking().ToListAsync();
            for (int i = 0; i < oldListAll.Count; i++)
            {
                RentCategoryAssociate associate = oldListAll[i];
                if (associateCategories.Where(c => c.id == associate.category_id).ToList().Count > 0)
                {
                    if (!associate.valid)
                    {
                        associate.valid = true;
                        associate.update_date = DateTime.Now;
                        _db.rentCategoryAssociate.Entry(associate).State = EntityState.Modified;
                    }
                }
                else
                {
                    if (associate.valid)
                    {
                        associate.valid = false;
                        associate.update_date = DateTime.Now;
                        _db.rentCategoryAssociate.Entry(associate).State = EntityState.Modified;
                    }
                }

            }
            for (int i = 0; i < associateCategories.Count; i++)
            {
                RentCategory category = associateCategories[i];
                RentCategoryAssociate asso = oldListAll.Where(o => o.associate_id == category.id).FirstOrDefault();
                if (asso == null)
                {
                    asso = new RentCategoryAssociate()
                    {
                        id = 0,
                        category_id = categoryId,
                        associate_id = category.id,
                        create_date = DateTime.Now
                    };
                    await _db.rentCategoryAssociate.AddAsync(asso);
                }
                else
                {
                    if (!asso.valid)
                    {
                        asso.valid = true;
                        asso.update_date = DateTime.Now;
                        _db.rentCategoryAssociate.Entry(asso).State = EntityState.Modified;
                    }
                }
            }
            await _db.SaveChangesAsync();
            return await _db.rentCategoryAssociate.Include(c => c.category)
                .Where(c => c.category_id == categoryId && c.valid).AsNoTracking().ToListAsync();
        }
        [HttpPost("{categoryId}")]
        public async Task<ActionResult<ApiResult<RentCategory?>>> SetAssociateCategoriesByStaff([FromRoute] int categoryId,
            [FromBody] List<RentCategory> categories, [FromQuery] string sessionKey, [FromQuery] string? sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<RentCategory?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            RentCategory category = await _db.rentCategory.Where(c => c.id == categoryId).AsNoTracking().FirstOrDefaultAsync();
            if (category == null)
            {
                return Ok(new ApiResult<RentCategory?>()
                {
                    code = 1,
                    message = "未找到品类",
                    data = null
                });
            }
            List<RentCategoryAssociate> assoList = await SetAssociateCategories(categoryId, categories);
            category.associateCategories = assoList;
            return Ok(new ApiResult<RentCategory>()
            {
                code = 0,
                message = "",
                data = category
            });
        }
        [NonAction]
        public async Task<Models.Rental> UpdateRentalDetails(int rentalId, List<Models.RentalDetail> details, int staffId, string? scene = null)
        {
            Models.Rental rental = await _db.rental.Where(r => r.id == rentalId).AsNoTracking().FirstOrDefaultAsync();
            List<Models.RentalDetail> oriList = await _db.rentalDetail.Where(d => d.rental_id == rentalId)
                .Include(d => d.discounts).OrderBy(d => d.rental_date).AsNoTracking().ToListAsync();
            for (int i = 0; i < details.Count; i++)
            {
                Models.RentalDetail detail = details[i];
                if (detail.id == 0)
                {
                    if (detail._filledOthersDiscountAmount != null && detail._filledOthersDiscountAmount > 0)
                    {
                        Discount discount = new Discount()
                        {
                            order_id = rental.order_id,
                            amount = (double)detail._filledOthersDiscountAmount,
                            biz_id = rentalId,
                            biz_type = "租赁",
                            sub_biz_id = detail.id,
                            valid = 1,
                            create_date = DateTime.Now
                        };
                        detail.discounts = new List<Discount>() { discount };
                    }
                    detail.rental_id = rentalId;
                    await _db.rentalDetail.AddAsync(detail);
                }
                else
                {
                    if (detail.valid == 0)
                    {
                        for (int k = 0; detail.availableDiscounts != null && k < detail.availableDiscounts.Count; k++)
                        {
                            Discount discount = detail.availableDiscounts[k];
                            discount.valid = 0;
                            discount.update_date = DateTime.Now;
                            _db.discount.Entry(discount).State = EntityState.Modified;
                        }
                    }
                    Models.RentalDetail oriDetail = oriList.Where(d => d.id == detail.id).FirstOrDefault();
                    if (oriDetail.othersDiscountAmount != detail._filledOthersDiscountAmount)
                    {
                        for (int j = 0; j < oriDetail.discounts.Count; j++)
                        {
                            Discount oriDiscount = oriDetail.discounts[j];
                            oriDiscount.valid = 0;
                            oriDiscount.update_date = DateTime.Now;
                            _db.discount.Entry(oriDiscount).State = EntityState.Modified;
                        }

                        if (detail._filledOthersDiscountAmount != null && detail._filledOthersDiscountAmount > 0)
                        {
                            Discount discount = new Discount()
                            {
                                order_id = rental.id,
                                amount = (double)detail._filledOthersDiscountAmount,
                                biz_id = rentalId,
                                biz_type = "租赁",
                                sub_biz_id = detail.id,
                                valid = 1,
                                create_date = DateTime.Now
                            };
                            await _db.discount.AddAsync(discount);
                        }

                    }
                    List<CoreDataModLog> dataLogs = Util.GetUpdateDifferenceLog<Models.RentalDetail>(oriDetail, detail, null, staffId, scene);
                    for (int j = 0; j < dataLogs.Count; j++)
                    {
                        await _db.coreDataModLog.AddAsync(dataLogs[j]);
                    }
                    _db.rentalDetail.Entry(detail).State = EntityState.Modified;
                }
            }
            await _db.SaveChangesAsync();
            return await GetRental(rentalId);
        }
        [HttpPost("{rentalId}")]
        public async Task<ActionResult<ApiResult<Models.Rental?>>> UpdateRentalDetailsByStaff([FromRoute] int rentalId,
            [FromBody] List<Models.RentalDetail> details, [FromQuery] string sessionKey, [FromQuery] string? sessionType = "wechat_mini_openid",
            [FromQuery] string? scene = null)
        {
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<Models.Rental?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            List<Models.RentalDetail> newList = new List<Models.RentalDetail>();
            for (int i = 0; i < details.Count; i++)
            {
                if (details[i].id != 0 || details[i].valid == 1)
                {
                    newList.Add(details[i]);
                }
            }
            Models.Rental rental = await UpdateRentalDetails(rentalId, newList, staff.id, scene);
            return Ok(new ApiResult<Models.Rental?>()
            {
                code = 0,
                message = "",
                data = rental
            });
        }
        [NonAction]
        public async Task<List<RentItemLog>> GetRentItemLog(int itemId)
        {
            Models.RentItem item = await _db.rentItem.Include(r => r.logs).ThenInclude(l => l.staff)
            .Where(r => r.id == itemId).AsNoTracking().FirstOrDefaultAsync();
            if (item.prev_id != null)
            {
                List<Models.RentItem> prevItems = await GetRentItemChangesLog(item);
                for(int i = 0; i < prevItems.Count; i++)
                {
                    Models.RentItem prevItem = prevItems[i];
                    prevItem.logs = await _db.rentItemLog.Where(l => l.rent_item_id == prevItem.id)
                        .Include(l => l.staff).AsNoTracking().ToListAsync();
                    for(int j = 0; j < prevItem.logs.Count; j++)
                    {
                        item.logs.Add(prevItem.logs[j]);
                    }
                }
            }
            return item.logs.OrderBy(l => l.rent_item_id).OrderBy(l => l.id).ToList();
        }
        [HttpGet("{itemId}")]
        public async Task<ActionResult<ApiResult<List<RentItemLog>?>>> GetRentItemLogByStaff(int itemId,
            string sessionKey, string? sessionType = "wechat_mini_openid" )
        {
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<List<RentItemLog>?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });    
            }
            List<RentItemLog> logs = await GetRentItemLog(itemId);
            return Ok(new ApiResult<List<RentItemLog>?>()
            {
                code = 0,
                message = "",
                data = logs
            });
        }
            
    }
}
