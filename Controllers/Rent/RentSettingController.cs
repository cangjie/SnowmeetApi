using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aop.Api.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Models;
using SnowmeetApi.Controllers.User;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Rent;
using SnowmeetApi.Models.Users;

namespace SnowmeetApi.Controllers.Rent
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class RentSettingController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private IConfiguration _config;
        public string _appId = "";
        private IConfiguration _oriConfig;
        private readonly IHttpContextAccessor _httpContextAccessor;

        private MemberController _memberHelper;

        public RentSettingController(ApplicationDBContext db, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = db;
            _oriConfig = config;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
            _httpContextAccessor = httpContextAccessor;
            _memberHelper = new MemberController(db, config);
        }

        [HttpGet("{code}")]
        public async Task<ActionResult<RentCategory>> ModCategory(string code, string name, string sessionKey, string sessionType)
        {
            name = Util.UrlDecode(name);
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            SnowmeetApi.Models.Users.Member member = await _memberHelper.GetMember(sessionKey, sessionType);
            if (member.is_admin != 1)
            {
                return BadRequest();
            }
            RentCategory rc = await _db.rentCategory.FindAsync(code);
            if (rc == null)
            {
                return NotFound();
            }
            rc.name = name;
            _db.rentCategory.Entry(rc).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(rc);
        }

        [HttpGet("{code}")]
        public async Task<ActionResult<RentCategory>> AddCategoryManual(string code, string name, string sessionKey, string sessionType)
        {
            name = Util.UrlDecode(name);
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            SnowmeetApi.Models.Users.Member member = await _memberHelper.GetMember(sessionKey, sessionType);
            if (member.is_admin != 1)
            {
                return BadRequest();
            }
            RentCategory rc = await _db.rentCategory.FindAsync(code);
            if (rc != null)
            {
                return NoContent();
            }
            RentCategory rcFather = await _db.rentCategory.FindAsync(code.Substring(0, code.Length - 2));
            if (rcFather == null)
            {
                return NotFound();
            }
            RentCategory rcNew = new RentCategory()
            {
                name = name,
                code = code
            };
            await _db.rentCategory.AddAsync(rcNew);
            await _db.SaveChangesAsync();
            return Ok(rcNew);
        }

        [HttpGet]
        public async Task<ActionResult<RentCategory>> AddCategory(string code, string name, string sessionKey, string sessionType)
        {
            name = Util.UrlDecode(name);
            code = code == null? "": code.Trim();
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            SnowmeetApi.Models.Users.Member member = await _memberHelper.GetMember(sessionKey, sessionType);
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
                newCode = newCode + (maxV+1).ToString().PadLeft(2, '0');
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

        [HttpGet]
        public async Task<ActionResult<ICollection<RentCategory>>> GetAllCategories()
        {
            var topL = await _db.rentCategory.Where(r => (r.code.Trim().Length == 2)).ToListAsync();
            if (topL == null || topL.Count == 0)
            {
                return BadRequest();
            }
            List<RentCategory> rl = new List<RentCategory>();
            for (int i = 0; i < topL.Count; i++)
            {
                RentCategory rc = (RentCategory)((OkObjectResult)(await GetCategory(topL[i].code)).Result).Value;
                rl.Add(rc);
            }
            return Ok(rl);
        }

        [HttpGet("{code}")]
        public async Task<ActionResult<RentCategory>> GetCategory(string code = "")
        {
            code = code.Trim();
            RentCategory rc = await _db.rentCategory.Include(r => r.priceList).Where(r => r.code.Trim().Equals(code.Trim())).FirstAsync();
            if (rc == null)
            {
                return NotFound();
            }
            var rcL = await _db.rentCategory.Include(r => r.priceList).Where(r => r.code.Trim().Length == code.Length + 2
                && r.code.StartsWith(code)).ToListAsync();
            if (rcL != null && rcL.Count > 0)
            {
                List<RentCategory> children = new List<RentCategory>();
                for (int i = 0; i < rcL.Count; i++)
                {
                    RentCategory child = (RentCategory)((OkObjectResult)(await GetCategory(rcL[i].code)).Result).Value;
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

        [HttpGet("{code}")]
        public async Task<ActionResult<RentPrice>> SetRentCategoryPrice(string code, string shop, string dayType, string scene, double price, string sessionKey, string sessionType="wchat_mini_openid")
        {
            RentCategory category = await _db.rentCategory.FindAsync(code);
            if (category==null)
            {
                return NotFound();
            }
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            shop = Util.UrlDecode(shop);
            dayType = Util.UrlDecode(dayType);
            scene = Util.UrlDecode(scene);
            SnowmeetApi.Models.Users.Member member = await _memberHelper.GetMember(sessionKey, sessionType);
            if (member.is_admin != 1)
            {
                return BadRequest();
            }
            if (!dayType.Trim().Equals("平日") && !dayType.Trim().Equals("周末") && !dayType.Trim().Equals("节假日"))
            {
                return BadRequest();
            }
            if (!scene.Equals("门市") && !scene.Equals("会员") && !scene.Equals("预约"))
            {
                return BadRequest();
            }

            List<RentPrice> rpL = await _db.rentPrice
                .Where(r => (r.type.Trim().Equals("分类") && r.category_code.Trim().Equals(code.Trim())
                && r.shop.Trim().Equals(shop.Trim()) && r.day_type.Trim().Equals(dayType.Trim() ) 
                && r.scene.Trim().Equals(scene.Trim()))).ToListAsync();
            if (rpL.Count == 0)
            {
                RentPrice rp = new RentPrice()
                {
                    id = 0,
                    type = "分类",
                    shop = shop.Trim(),
                    category_code = code.Trim(),
                    day_type = dayType.Trim(),
                    price = price,
                    scene = scene
                };
                await _db.rentPrice.AddAsync(rp);
                await _db.SaveChangesAsync();
                return Ok(rp);
            }
            else
            {
                RentPrice rp = rpL[0];
                if (price == 0)
                {
                    _db.rentPrice.Remove(rp);
                }
                else
                {
                    rp.price = price;
                    rp.update_date = DateTime.Now;
                    _db.rentPrice.Entry(rp).State = EntityState.Modified;
                }
                await _db.SaveChangesAsync();
                return Ok(rp);
            }
        }

        [HttpGet("{code}")]
        public async Task<ActionResult<RentCategory>> SetShopCategoryRentPrice(string code, string shop, string dayType, string scene, double price, string sessionKey, string sessionType)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            SnowmeetApi.Models.Users.Member member = await _memberHelper.GetMember(sessionKey, sessionType);
            if (member.is_admin != 1)
            {
                return BadRequest();
            }
            shop = Util.UrlDecode(shop);
            dayType = Util.UrlDecode(dayType);
            scene = Util.UrlDecode(scene);

            RentCategory cate = await _db.rentCategory.FindAsync(code);
            if (cate == null || (cate.children != null && cate.children.Count > 0))
            {
                return NotFound();
            }

            var priceL = await _db.rentPrice.Where(p => (p.type.Trim().Equals("分类")
                && p.category_code.Trim().Equals(code) && p.day_type.Trim().Equals(dayType)
                && p.scene.Trim().Equals(scene) && p.shop.Trim().Equals(shop))).ToListAsync();
            if (priceL == null || priceL.Count == 0)
            {
                RentPrice rp = new RentPrice()
                {
                    shop = shop,
                    type = "分类",
                    category_code = code,
                    day_type = dayType,
                    scene = scene,
                    price = price,
                    update_date = DateTime.Now
                };
                await _db.rentPrice.AddAsync(rp);
            }
            else
            {
                RentPrice rp = priceL[0];
                rp.price = price;
                rp.update_date = DateTime.Now;
                _db.rentPrice.Entry(rp).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();
            RentCategory rc = (RentCategory)((OkObjectResult)(await GetCategory(code)).Result).Value;
            return Ok(rc);
        }

        [HttpGet("{code}")]
        public async Task<ActionResult<RentCategory>> UpdateCategory(string code, string name, double deposit, string sessionKey, string sessionType)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            SnowmeetApi.Models.Users.Member member = await _memberHelper.GetMember(sessionKey, sessionType);
            if (member.is_admin != 1)
            {
                return BadRequest();
            }
            RentCategory cate = await _db.rentCategory.FindAsync(code.Trim());
            if (cate == null)
            {
                return NotFound();
            }
            cate.update_date = DateTime.Now;
            cate.name = name.Trim();
            cate.deposit = deposit;
            _db.Entry(cate).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(cate);
        }

        [HttpGet]
        public async Task<ActionResult<RentPackage>> AddRentPackage(string name, string description, string sessionKey, string sessionType)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            SnowmeetApi.Models.Users.Member member = await _memberHelper.GetMember(sessionKey, sessionType);
            if (member.is_admin != 1)
            {
                return BadRequest();
            }
            RentPackage rp = new RentPackage()
            {
                name = Util.UrlDecode(name),
                description = Util.UrlDecode(description),
                is_delete = 0,
                update_date = DateTime.Now
            };
            await _db.rentPackage.AddAsync(rp);
            await _db.SaveChangesAsync();
            return Ok(rp);
        }

        [HttpGet("{packageId}")]
        public async Task<ActionResult<RentPackage>> RentPackageCategoryAdd(int packageId, string code, string sessionKey, string sessionType)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            SnowmeetApi.Models.Users.Member member = await _memberHelper.GetMember(sessionKey, sessionType);
            if (member.is_admin != 1)
            {
                return BadRequest();
            }
            RentPackageCategory rpc = new RentPackageCategory()
            {
                package_id = packageId,
                category_code = code,
                update_date = DateTime.Now
            };
            await _db.rentPackageCategory.AddAsync(rpc);
            await _db.SaveChangesAsync();
            RentPackage pr = await _db.rentPackage.Include(r => r.rentPackageCategoryList).Where(r => r.id == packageId).FirstAsync();
            return Ok(pr);
        }

        [HttpGet("{packageId}")]
        public async Task<ActionResult<RentPackage>> RentPackageCategoryDel(int packageId, string code, string sessionKey, string sessionType)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            SnowmeetApi.Models.Users.Member member = await _memberHelper.GetMember(sessionKey, sessionType);
            if (member.is_admin != 1)
            {
                return BadRequest();
            }
            RentPackageCategory rpc = await _db.rentPackageCategory.FindAsync(packageId, code);
            _db.rentPackageCategory.Remove(rpc);
            await _db.SaveChangesAsync();

            RentPackage pr = await _db.rentPackage.Include(r => r.rentPackageCategoryList).Where(r => r.id == packageId).FirstAsync();
            return Ok(pr);
        }

        [HttpGet("{packageId}")]
        public async Task<ActionResult<RentPackage>> GetRentPackage(int packageId)
        {
            RentPackage rp = await _db.rentPackage.Include(r => r.rentPackageCategoryList)
                .Include( r => r.rentPackagePriceList)
                .Where(r => r.id == packageId).FirstAsync();
            return Ok(rp);
        }

        [HttpGet]
        public async Task<ActionResult<List<RentPackage>>> GetRentPackageList()
        {
            List<RentPackage> list = await _db.rentPackage.Include(r => r.rentPackageCategoryList)
                .Include(r => r.rentPackagePriceList).Where(r => r.is_delete == 0)
                .OrderByDescending(r => r.id).ToListAsync();
            return Ok(list);
        }

        [HttpGet("{packageId}")]
        public async Task<ActionResult<RentPackage>> UpdateRentPackageBaseInfo(int packageId, string name, string description, double deposit, string sessionKey, string sessionType)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            SnowmeetApi.Models.Users.Member member = await _memberHelper.GetMember(sessionKey, sessionType);
            if (member.is_admin != 1)
            {
                return BadRequest();
            }
            RentPackage p = await _db.rentPackage.FindAsync(packageId);
            if (p == null)
            {
                return NotFound();
            }
            p.name = Util.UrlDecode(name);
            p.description = Util.UrlDecode(description);
            p.deposit = deposit;
            _db.rentPackage.Entry(p).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return await GetRentPackage(packageId);
        }
        [HttpGet("{packageId}")]
        public async Task<ActionResult<RentPackage>> SetPackageRentPrice(int packageId, string shop, string dayType, string scene, double price, string sessionKey, string sessionType)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            SnowmeetApi.Models.Users.Member member = await _memberHelper.GetMember(sessionKey, sessionType);
            if (member.is_admin != 1)
            {
                return BadRequest();
            }
            shop = Util.UrlDecode(shop);
            dayType = Util.UrlDecode(dayType);
            scene = Util.UrlDecode(scene);

            //RentCategory cate = await _db.rentCategory.FindAsync(code);
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
                    price = price,
                    update_date = DateTime.Now
                };
                await _db.rentPrice.AddAsync(rp);
            }
            else
            {
                RentPrice rp = priceL[0];
                rp.price = price;
                rp.update_date = DateTime.Now;
                _db.rentPrice.Entry(rp).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();
            return await GetRentPackage(packageId);
        }



        /*
        [HttpGet("{code}")]
        public async Task<ActionResult<RentPrice>> GetCategoryPrice(string code, string shop, DateTime date, string scene="门市")
        {
            string dayType = (date.DayOfWeek == DayOfWeek.Sunday || date.DayOfWeek == DayOfWeek.Saturday)? "周末" : "平日";
            List<RentPrice> rentPriceList = (List<RentPrice>)((OkObjectResult)(await GetRentPriceList(code)).Result).Value;
            RentPrice rp = GetCorrectRentPrice(rentPriceList, shop, dayType, scene);
            if (rp != null)
            {
                return Ok(rp);
            }
            return NotFound();
        }
        
        [NonAction]
        public RentPrice GetCorrectRentPrice(List<RentPrice> rentPriceList, string shop, string dayType, string scene)
        {
            List<RentPrice> rentPriceShop = new List<RentPrice>();
            for(int i = 0; i < rentPriceList.Count; i++)
            {
                if (rentPriceList[i].shop.Trim().Equals(shop.Trim()))
                {
                    rentPriceShop.Add(rentPriceList[i]);
                }
            }
            if (rentPriceShop.Count == 0)
            {
                for(int i = 0; i < rentPriceList.Count; i++)
                {
                    if (rentPriceList[i].shop.Trim().Equals(""))
                    {
                        rentPriceShop.Add(rentPriceList[i]);
                    }
                }

            }
            List<RentPrice> rentPriceDay = new List<RentPrice>();
            for(int i = 0; i < rentPriceShop.Count; i++)
            {
                if (rentPriceShop[i].day_type.Trim().Equals(dayType.Trim()))
                {
                    rentPriceDay.Add(rentPriceShop[i]);
                }
            }
            if (rentPriceDay.Count == 0)
            {
                for(int i = 0; i < rentPriceShop.Count; i++)
                {
                    if (rentPriceShop[i].day_type.Trim().Equals("平日"))
                    {
                        rentPriceDay.Add(rentPriceShop[i]);
                    }
                }
            }
            //bool find = false;
            for(int i = 0; i < rentPriceDay.Count; i++)
            {
                if (rentPriceDay[i].scene.Trim().Equals(scene.Trim()))
                {
                    return rentPriceDay[i];
                }
            }
            for(int i = 0; i < rentPriceDay.Count; i++)
            {
                if (rentPriceDay[i].scene.Trim().Equals("门市"))
                {
                    return rentPriceDay[i];
                }
            }
            return null;
        }
        */
        /*
        [HttpGet("{code}")]
        public async Task<ActionResult<List<RentPrice>>> GetRentPriceList(string code)
        {
            code = code.Trim();
            RentCategory rentCat = (RentCategory)((OkObjectResult)(await GetCategory(code)).Result).Value;
            if (rentCat == null || rentCat.children != null)
            {
                return NotFound();
            }
            List<RentPrice> pList = await _db.rentPrice
                .Where(r => r.type.Trim().Equals("分类") && r.category_code.Trim().Equals(code.Trim()))
                .AsNoTracking().ToListAsync();
            return Ok(pList);
            
        }
        
        */


        /*

        // GET: api/RentSetting
        [HttpGet]
        public async Task<ActionResult<IEnumerable<RentCategory>>> GetRentCategory()
        {
            return await _context.RentCategory.ToListAsync();
        }

        // GET: api/RentSetting/5
        [HttpGet("{id}")]
        public async Task<ActionResult<RentCategory>> GetRentCategory(string id)
        {
            var rentCategory = await _context.RentCategory.FindAsync(id);

            if (rentCategory == null)
            {
                return NotFound();
            }

            return rentCategory;
        }

        // PUT: api/RentSetting/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutRentCategory(string id, RentCategory rentCategory)
        {
            if (id != rentCategory.code)
            {
                return BadRequest();
            }

            _context.Entry(rentCategory).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!RentCategoryExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // POST: api/RentSetting
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<RentCategory>> PostRentCategory(RentCategory rentCategory)
        {
            _context.RentCategory.Add(rentCategory);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (RentCategoryExists(rentCategory.code))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtAction("GetRentCategory", new { id = rentCategory.code }, rentCategory);
        }

        // DELETE: api/RentSetting/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRentCategory(string id)
        {
            var rentCategory = await _context.RentCategory.FindAsync(id);
            if (rentCategory == null)
            {
                return NotFound();
            }

            _context.RentCategory.Remove(rentCategory);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        
        private bool RentCategoryExists(string id)
        {
            return _context.RentCategory.Any(e => e.code == id);
        }
        */
    }
}
