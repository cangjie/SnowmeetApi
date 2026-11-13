using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services;

namespace SnowmeetApi.Controllers
{
    /// <summary>日养护工作列表（统计规则见 Helpers/CareWorkloadRules.cs）。</summary>
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class CareWorkloadController : ControllerBase
    {
        private readonly ApplicationDBContext _db;

        public CareWorkloadController(ApplicationDBContext context)
        {
            _db = context;
        }

        /// <summary>
        /// 某一天的养护进出统计和各工序工作量。date 不传 = 今天；shop 不传 = 全部门店。
        /// 今天永远现算（22:00 前算到当前，22:00 后冻结在 22:00）；过去的日子优先读每晚 22:00 存下的快照，没有就按当天 22:00 现算。
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<ApiResult<CareWorkloadStat>>> GetDailyWorkloadByStaff(string? date, string? shop,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, Util.UrlDecode(sessionKey), sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<CareWorkloadStat>() { code = 1, message = "没有权限", data = null });
            }
            DateTime now = DateTime.Now;
            DateTime day = now.Date;
            if (!string.IsNullOrWhiteSpace(date) && !DateTime.TryParse(Util.UrlDecode(date), out day))
            {
                return Ok(new ApiResult<CareWorkloadStat>() { code = 1, message = "日期格式不对", data = null });
            }
            day = day.Date;
            if (day > now.Date)
            {
                return Ok(new ApiResult<CareWorkloadStat>() { code = 1, message = "不能查询还没到的日期", data = null });
            }
            string? shopName = string.IsNullOrWhiteSpace(shop) ? null : Util.UrlDecode(shop).Trim();
            CareWorkloadStat stat = await new CareWorkloadService(_db).GetAsync(day, now, shopName);
            return Ok(new ApiResult<CareWorkloadStat>() { code = 0, message = "", data = stat });
        }
        /// <summary>
        /// 页面上某个数的明细。metric：initialPending / added / completed / pending / idlePending / worked，
        /// 或工序 edge / wax / scrape / patchBottom / repair；staffId 只对工序有效（只看这个人完成的，0 = 没记录完成人）。
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<ApiResult<CareWorkloadItems>>> GetDailyWorkloadItemsByStaff(string? date, string? shop,
            string metric, int? staffId, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, Util.UrlDecode(sessionKey), sessionType);
            if (staff == null || staff.title_level < 100)
            {
                return Ok(new ApiResult<CareWorkloadItems>() { code = 1, message = "没有权限", data = null });
            }
            if (!CareWorkloadRules.IsMetric(metric))
            {
                return Ok(new ApiResult<CareWorkloadItems>() { code = 1, message = "不认识的统计项", data = null });
            }
            DateTime now = DateTime.Now;
            DateTime day = now.Date;
            if (!string.IsNullOrWhiteSpace(date) && !DateTime.TryParse(Util.UrlDecode(date), out day))
            {
                return Ok(new ApiResult<CareWorkloadItems>() { code = 1, message = "日期格式不对", data = null });
            }
            day = day.Date;
            if (day > now.Date)
            {
                return Ok(new ApiResult<CareWorkloadItems>() { code = 1, message = "不能查询还没到的日期", data = null });
            }
            string? shopName = string.IsNullOrWhiteSpace(shop) ? null : Util.UrlDecode(shop).Trim();
            CareWorkloadItems items = await new CareWorkloadService(_db).GetItemsAsync(day, now, shopName, metric, staffId);
            return Ok(new ApiResult<CareWorkloadItems>() { code = 0, message = "", data = items });
        }
    }
}
