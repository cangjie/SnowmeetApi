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
namespace SnowmeetApi.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class CategoryController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _http;
        public CategoryController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = context;
            _config = config;
            _http = httpContextAccessor;
        }
        private async Task<ApiResult<object?>> CheckStaff(int level,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff == null || staff.title_level < level)
            {
                return new ApiResult<object?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                };
            }

            return new ApiResult<object?>()
            {
                code = 0,
                message = "",
                data = staff
            };
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<Category>>> AddNewCategory(string name, string bizType,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            ApiResult<object?> checkStaffResult = await CheckStaff(100, sessionKey, sessionType);
            if (checkStaffResult != null && checkStaffResult.code == 1)
            {
                return Ok(checkStaffResult);
            }
            Staff staff = (Staff)checkStaffResult.data;
            bool isDup = false;
            if (bizType.Trim().Equals("餐饮"))
            {
                List<Category> cl = await _db.category
                    .Where(c => c.biz_type.Equals(bizType.Trim()) && c.name.Trim().Equals(name.Trim()))
                    .AsNoTracking().ToListAsync();
                if (cl != null && cl.Count > 0)
                {
                    isDup = true;
                }
            }
            if (isDup)
            {
                return Ok(new ApiResult<object?>()
                {
                    code = 1,
                    message = "名称重复",
                    data = null
                });
            }
            string? code = null;
            if (bizType.Equals("餐饮"))
            {
                code = null;
            }
            List<Category> sameLevelCategories = await _db.category.Where(c => c.biz_type.Trim().Equals(bizType)
                && (code == null || c.code.Length == code.Length)).ToListAsync();
            for (int i = 0; sameLevelCategories != null && i < sameLevelCategories.Count; i++)
            {
                Category c = sameLevelCategories[i];
                c.sort = c.sort + 100;
                _db.category.Entry(c).State = EntityState.Modified;
            }
            Category category = new Category()
            {
                id = 0,
                biz_type = bizType.Trim(),
                code = null,
                name = name.Trim(),
                valid = 1,
                hide = 0,
                sort = 0
            };
            CoreDataModLog log = new CoreDataModLog()
            {
                id = 0,
                table_name = "category",
                field_name = null,
                key_value = 0,
                scene = "添加新分类",
                member_id = null,
                staff_id = staff.id,
                prev_value = null,
                current_value = name,
                trace_id = 0,
                is_manual = 1,
                manual_memo = bizType.Trim()
            };
            await _db.category.AddAsync(category);
            await _db.coreDataModLog.AddAsync(log);
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<Category>()
            {
                code = 0,
                message = "",
                data = category
            });
        }
        [HttpPost]
        public async Task<ActionResult<ApiResult<Category>>> UpdateCategory([FromBody] Category category, [FromQuery] string scene,
            [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            ApiResult<object?> checkStaffResult = await CheckStaff(100, sessionKey, sessionType);
            if (checkStaffResult != null && checkStaffResult.code == 1)
            {
                return Ok(checkStaffResult);
            }
            Staff staff = (Staff)checkStaffResult.data;
            Category? ori = await _db.category.FindAsync(category.id);
            _db.category.Entry(ori).State = EntityState.Detached;
            await _db.SaveChangesAsync();
            List<CoreDataModLog> logList = Util.GetUpdateDifferenceLog<Category>(ori, category, null, staff.id, scene);
            category.update_date = DateTime.Now;
            _db.category.Entry(category).State = EntityState.Modified;
            for (int i = 0; i < logList.Count; i++)
            {
                await _db.coreDataModLog.AddAsync(logList[i]);
            }
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<Category>()
            {
                code = 0,
                message = "",
                data = category
            });
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<List<Category>>>> GetSingleLevelCategory(string bizType)
        {
            List<Category> cateList = await _db.category.Where(c => c.biz_type.Trim().Equals(bizType)
                && c.valid == 1).AsNoTracking().OrderByDescending(c => c.sort).ThenBy(c => c.id).ToListAsync();
            return Ok(new ApiResult<List<Category>>()
            {
                code = 0,
                message = "",
                data = cateList
            });
        }
    }
}