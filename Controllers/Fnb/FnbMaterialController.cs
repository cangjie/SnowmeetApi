using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Fnb;

namespace SnowmeetApi.Controllers.Fnb
{
    /// <summary>
    /// 餐饮食材过期提醒（H5：wwwroot/fnb/mat_expire/）。
    /// 认证：企业微信 OAuth（snsapi_base）换 UserId → mini_session（session_type='wecom_userid'，UserId 存 wechat_openid 列）。
    /// 鉴权粒度：应用可见范围内的企业成员即可读写，不接 staff 权限体系。
    /// 推送接收人：app 目录纯文本文件 config.fnbAlertReceivers（@all 或 userid|userid…），每次推送现读，缺省 @all。
    /// </summary>
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class FnbMaterialController : ControllerBase
    {
        public const string SESSION_TYPE_WECOM = "wecom_userid";
        public const string H5_URL = "https://mini.snowmeet.top/fnb/mat_expire/index.html";

        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;
        private readonly FnbWeComController _wecomHelper;
        private readonly MiniAppHelperController _mH;

        public FnbMaterialController(ApplicationDBContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
            _wecomHelper = new FnbWeComController(db, config);
            _mH = new MiniAppHelperController(db, config);
        }

        // ====== 认证 ======

        // 企微 OAuth 回跳 code 换 UserId，发放 30 天 sessionKey（H5 存 localStorage）
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> OAuthLogin(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return Ok(new ApiResult<object>() { code = 1, message = "缺少 code", data = null });
            }
            string batchId = DateTime.Now.ToString("yyyyMMddHHmmssfff");
            string token = await _wecomHelper.GetToken(batchId, "食材过期提醒登录");
            if (token == null)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "企业微信接口调用失败", data = null });
            }
            WebApiLog log = await _mH.PerformRequest(
                "https://qyapi.weixin.qq.com/cgi-bin/auth/getuserinfo?access_token=" + token + "&code=" + code.Trim(),
                "", "", "GET", "企业微信", "食材过期提醒登录", "OAuth code 换 UserId", batchId);
            try
            {
                JObject obj = JObject.Parse(log.response);
                int errcode = (int?)obj["errcode"] ?? 0;
                string userid = (string)obj["userid"];
                if (errcode != 0)
                {
                    return Ok(new ApiResult<object>()
                    {
                        code = 1,
                        message = "企业微信返回错误：" + errcode + " " + ((string)obj["errmsg"] ?? ""),
                        data = null
                    });
                }
                if (string.IsNullOrWhiteSpace(userid))
                {
                    // 非企业成员（企微只返 openid 不返 userid）
                    return Ok(new ApiResult<object>() { code = 1, message = "仅限企业成员使用", data = null });
                }
                MiniSession sess = new MiniSession()
                {
                    session_key = Guid.NewGuid().ToString("N"),
                    session_type = SESSION_TYPE_WECOM,
                    wechat_openid = userid.Trim(),
                    valid = 1,
                    expire_date = DateTime.Now.AddDays(30),
                    create_date = DateTime.Now
                };
                await _db.miniSession.AddAsync(sess);
                await _db.SaveChangesAsync();
                return Ok(new ApiResult<object>()
                {
                    code = 0,
                    message = "",
                    data = new { sessionKey = sess.session_key, userid = userid.Trim() }
                });
            }
            catch
            {
                return Ok(new ApiResult<object>() { code = 1, message = "企业微信响应解析失败", data = null });
            }
        }

        // 会话校验：返回企微 UserId；null=会话失效（接口统一返 code=2 让前端重走 OAuth）
        [NonAction]
        private async Task<string> _getWecomUserId(string sessionKey)
        {
            if (string.IsNullOrWhiteSpace(sessionKey))
            {
                return null;
            }
            string key = Util.UrlDecode(sessionKey).Trim();
            MiniSession sess = await _db.miniSession
                .Where(s => s.session_key == key && s.session_type == SESSION_TYPE_WECOM
                    && s.valid == 1 && s.expire_date >= DateTime.Now)
                .AsNoTracking().FirstOrDefaultAsync();
            if (sess == null || string.IsNullOrWhiteSpace(sess.wechat_openid))
            {
                return null;
            }
            return sess.wechat_openid.Trim();
        }

        private ActionResult<ApiResult<object>> _sessionExpired()
        {
            return Ok(new ApiResult<object>() { code = 2, message = "会话失效", data = null });
        }

        // ====== 状态派生（唯一口径，前端 deriveStatus 与此一致） ======

        [NonAction]
        public static string DeriveStatus(FnbMaterialBatch b, DateTime today)
        {
            if (b.dispose_status != null && !b.dispose_status.Trim().Equals(""))
            {
                return "已处理";
            }
            DateTime e = b.expire_date.Date;
            if (e < today)
            {
                return "已过期";
            }
            if (e == today)
            {
                return "今日";
            }
            if (e <= today.AddDays(b.warn_days))
            {
                return "临期";
            }
            return "正常";
        }

        // ====== 批次 CRUD ======

        // 全量有效批次 + 服务器今天（状态派生统一以服务器日期为准，防手机时区/改时间错乱）
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> GetBatches(string sessionKey)
        {
            string userId = await _getWecomUserId(sessionKey);
            if (userId == null)
            {
                return _sessionExpired();
            }
            List<FnbMaterialBatch> batches = await _db.fnbMaterialBatch
                .Where(b => b.valid == 1).OrderBy(b => b.expire_date).ThenBy(b => b.id)
                .AsNoTracking().ToListAsync();
            return Ok(new ApiResult<object>()
            {
                code = 0,
                message = "",
                data = new { today = DateTime.Now.ToString("yyyy-MM-dd"), batches }
            });
        }

        // id=0 新增 / id>0 编辑（保留 create_* / dispose_*，全局 NoTracking 必须显式 Modified）
        [HttpPost]
        public async Task<ActionResult<ApiResult<object>>> SaveBatch([FromBody] FnbMaterialBatch posted, [FromQuery] string sessionKey)
        {
            string userId = await _getWecomUserId(sessionKey);
            if (userId == null)
            {
                return _sessionExpired();
            }
            if (posted == null || string.IsNullOrWhiteSpace(posted.name) || string.IsNullOrWhiteSpace(posted.batch_no))
            {
                return Ok(new ApiResult<object>() { code = 1, message = "名称和批次号必填", data = null });
            }
            if (posted.expire_date == default(DateTime))
            {
                return Ok(new ApiResult<object>() { code = 1, message = "到期日期必填", data = null });
            }
            if (posted.warn_days < 0)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "预警天数不能为负", data = null });
            }
            if (posted.id == 0)
            {
                posted.create_userid = userId;
                posted.valid = 1;
                posted.create_date = DateTime.Now;
                posted.update_date = null;
                posted.dispose_status = null;
                posted.dispose_userid = null;
                posted.dispose_date = null;
                await _db.fnbMaterialBatch.AddAsync(posted);
                await _db.SaveChangesAsync();
                return Ok(new ApiResult<object>() { code = 0, message = "", data = posted });
            }
            FnbMaterialBatch batch = await _db.fnbMaterialBatch
                .Where(b => b.id == posted.id && b.valid == 1).FirstOrDefaultAsync();
            if (batch == null)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "批次不存在", data = null });
            }
            batch.name = posted.name.Trim();
            batch.batch_no = posted.batch_no.Trim();
            batch.produce_date = posted.produce_date;
            batch.shelf_life_value = posted.shelf_life_value;
            batch.shelf_life_unit = posted.shelf_life_unit;
            batch.expire_date = posted.expire_date;
            batch.warn_days = posted.warn_days;
            batch.image_ids = posted.image_ids;
            batch.update_date = DateTime.Now;
            _db.fnbMaterialBatch.Entry(batch).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<object>() { code = 0, message = "", data = batch });
        }

        // 处置：用完 / 报废（幂等：已处置直接返当前行）
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> DisposeBatch(int id, string action, string sessionKey)
        {
            string userId = await _getWecomUserId(sessionKey);
            if (userId == null)
            {
                return _sessionExpired();
            }
            action = Util.UrlDecode(action ?? "").Trim();
            if (!action.Equals("用完") && !action.Equals("报废"))
            {
                return Ok(new ApiResult<object>() { code = 1, message = "处置类型不支持", data = null });
            }
            FnbMaterialBatch batch = await _db.fnbMaterialBatch
                .Where(b => b.id == id && b.valid == 1).FirstOrDefaultAsync();
            if (batch == null)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "批次不存在", data = null });
            }
            if (batch.dispose_status != null && !batch.dispose_status.Trim().Equals(""))
            {
                return Ok(new ApiResult<object>() { code = 0, message = "", data = batch });
            }
            batch.dispose_status = action;
            batch.dispose_userid = userId;
            batch.dispose_date = DateTime.Now;
            batch.update_date = DateTime.Now;
            _db.fnbMaterialBatch.Entry(batch).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<object>() { code = 0, message = "", data = batch });
        }

        // 软删（valid=0，列表不显示，不做恢复入口）
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> DeleteBatch(int id, string sessionKey)
        {
            string userId = await _getWecomUserId(sessionKey);
            if (userId == null)
            {
                return _sessionExpired();
            }
            FnbMaterialBatch batch = await _db.fnbMaterialBatch
                .Where(b => b.id == id && b.valid == 1).FirstOrDefaultAsync();
            if (batch == null)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "批次不存在", data = null });
            }
            batch.valid = 0;
            batch.update_date = DateTime.Now;
            _db.fnbMaterialBatch.Entry(batch).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<object>() { code = 0, message = "", data = null });
        }

        // 批次号发号：B{yyMMdd}-{当日已发数+1，2位}。仅参考号，不保证并发唯一（手输允许重复）
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> GenBatchNo(string sessionKey)
        {
            string userId = await _getWecomUserId(sessionKey);
            if (userId == null)
            {
                return _sessionExpired();
            }
            string prefix = "B" + DateTime.Now.ToString("yyMMdd") + "-";
            int count = await _db.fnbMaterialBatch.Where(b => b.batch_no.StartsWith(prefix)).CountAsync();
            return Ok(new ApiResult<object>()
            {
                code = 0,
                message = "",
                data = new { batchNo = prefix + (count + 1).ToString().PadLeft(2, '0') }
            });
        }

        // 现场照片薄上传：存盘 + UploadFile 落库逻辑照抄 UploadFileController.UploadFileWithThumb，
        // 但鉴权走 wecom 会话（原接口要求 staff，与本 H5 用户体系不兼容）。staff_id=null、owner=企微 UserId
        [HttpPost]
        public async Task<ActionResult<ApiResult<object>>> UploadPhoto(IFormFile file, [FromQuery] string sessionKey)
        {
            string userId = await _getWecomUserId(sessionKey);
            if (userId == null)
            {
                return _sessionExpired();
            }
            if (file == null || file.Length == 0)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "缺少文件", data = null });
            }
            string dateStr = DateTime.Now.Year.ToString() + DateTime.Now.Month.ToString().PadLeft(2, '0') + DateTime.Now.Day.ToString().PadLeft(2, '0');
            string filePath = Util.workingPath + "/wwwroot/upload/" + dateStr;
            if (!Directory.Exists(filePath))
            {
                Directory.CreateDirectory(filePath);
            }
            string[] fileNameArr = file.FileName.Split('.');
            string ext = fileNameArr[fileNameArr.Length - 1].Trim();
            string fileName = Util.GetLongTimeStamp(DateTime.Now).Trim() + "." + ext;
            string returnFileName = "/upload/" + dateStr + "/" + fileName;
            using (Stream s = System.IO.File.Create(filePath + "/" + fileName))
            {
                await file.CopyToAsync(s);
            }
            UploadFile fileSave = new UploadFile()
            {
                id = 0,
                staff_id = null,
                owner = userId,
                file_path_name = returnFileName,
                is_web = 1,
                purpose = "食材批次"
            };
            await _db.UploadFile.AddAsync(fileSave);
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<object>()
            {
                code = 0,
                message = "",
                data = new { id = fileSave.id, file_path_name = returnFileName }
            });
        }

        // 按 id 批量查照片路径（编辑模式回显 image_ids 用）。ids 逗号分隔
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> GetImages(string ids, string sessionKey)
        {
            string userId = await _getWecomUserId(sessionKey);
            if (userId == null)
            {
                return _sessionExpired();
            }
            List<int> idList = new List<int>();
            foreach (string s in (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(s.Trim(), out int v))
                {
                    idList.Add(v);
                }
            }
            if (idList.Count == 0)
            {
                return Ok(new ApiResult<object>() { code = 0, message = "", data = new List<object>() });
            }
            var images = await _db.UploadFile.Where(f => idList.Contains(f.id))
                .Select(f => new { f.id, f.file_path_name }).AsNoTracking().ToListAsync();
            return Ok(new ApiResult<object>() { code = 0, message = "", data = images });
        }

        // ====== 推送提醒 ======

        // 接收人配置：app 目录 config.fnbAlertReceivers（镜像 config.sqlServer 模式：服务器本地、gitignored、
        // publish 不覆盖、免重启生效）。内容一行 @all 或 userid|userid…；文件缺失/空 → @all
        [NonAction]
        private string _loadAlertReceivers()
        {
            try
            {
                string path = Util.workingPath + "/config.fnbAlertReceivers";
                if (System.IO.File.Exists(path))
                {
                    string s = System.IO.File.ReadAllText(path).Trim();
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        return s;
                    }
                }
            }
            catch { }
            return "@all";
        }

        // 扫「未处置 且 已过期/今日/临期」批次 → 组图文推企微 → 逐批次写 alert_log。
        // 当天已成功提醒过的批次跳过（防重复骚扰）。本期手动触发，未来 crontab 定时 curl 同一接口。
        // touser 仅联调覆盖用（传自己的 UserId 避免打扰全员），缺省走配置文件
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> PushExpireAlert(string sessionKey, string touser = null)
        {
            string userId = await _getWecomUserId(sessionKey);
            if (userId == null)
            {
                return _sessionExpired();
            }
            string receivers = string.IsNullOrWhiteSpace(touser) ? _loadAlertReceivers() : Util.UrlDecode(touser).Trim();
            DateTime today = DateTime.Now.Date;

            List<FnbMaterialBatch> all = await _db.fnbMaterialBatch
                .Where(b => b.valid == 1 && (b.dispose_status == null || b.dispose_status.Trim() == ""))
                .AsNoTracking().ToListAsync();
            List<FnbMaterialBatch> candidates = all
                .Where(b => DeriveStatus(b, today) != "正常")
                .OrderBy(b => b.expire_date).ToList();

            // 当天已成功提醒过的批次去重
            List<int> ids = candidates.Select(b => b.id).ToList();
            List<int> alertedToday = await _db.fnbMaterialAlertLog.AsNoTracking()
                .Where(l => ids.Contains(l.batch_id) && l.success == 1 && l.create_date >= today)
                .Select(l => l.batch_id).Distinct().ToListAsync();
            candidates = candidates.Where(b => !alertedToday.Contains(b.id)).ToList();

            if (candidates.Count == 0)
            {
                return Ok(new ApiResult<object>() { code = 0, message = "", data = new { count = 0 } });
            }

            int expired = candidates.Count(b => DeriveStatus(b, today) == "已过期");
            int dueToday = candidates.Count(b => DeriveStatus(b, today) == "今日");
            int warning = candidates.Count(b => DeriveStatus(b, today) == "临期");
            string topNames = string.Join("、", candidates.Take(3).Select(b => b.name.Trim()));
            if (candidates.Count > 3)
            {
                topNames += " 等";
            }
            var articles = new List<FnbWeComController.NewsArticle>()
            {
                new FnbWeComController.NewsArticle()
                {
                    title = "食材到期提醒：" + candidates.Count + " 项需处理",
                    description = "已过期 " + expired + " · 今日到期 " + dueToday + " · 临期 " + warning + "。" + topNames,
                    url = H5_URL
                }
            };
            FnbWeComController.WeComSendResponse res = await _wecomHelper.SendNews(articles, receivers, "食材过期提醒");
            bool ok = res != null && res.errcode == 0;
            string errMsg = res == null ? "企业微信接口调用失败" : (res.errcode == 0 ? null : (res.errcode + " " + (res.errmsg ?? "")));

            foreach (FnbMaterialBatch b in candidates)
            {
                await _db.fnbMaterialAlertLog.AddAsync(new FnbMaterialAlertLog()
                {
                    id = 0,
                    batch_id = b.id,
                    alert_status = DeriveStatus(b, today),
                    expire_date = b.expire_date.Date,
                    touser = receivers,
                    msgid = res == null ? null : res.msgid,
                    success = ok ? 1 : 0,
                    err_msg = errMsg,
                    send_userid = userId,
                    create_date = DateTime.Now
                });
            }
            await _db.SaveChangesAsync();

            if (!ok)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "推送失败：" + errMsg, data = new { count = candidates.Count } });
            }
            return Ok(new ApiResult<object>()
            {
                code = 0,
                message = "",
                data = new { count = candidates.Count, expired, dueToday, warning, touser = receivers, msgid = res.msgid }
            });
        }
    }
}
