using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Fnb;
using TencentCloud.Common;
using TencentCloud.Common.Profile;
using TencentCloud.Ocr.V20181119;
using TencentCloud.Ocr.V20181119.Models;

namespace SnowmeetApi.Controllers.Fnb
{
    /// <summary>
    /// 餐饮食材过期提醒（H5：wwwroot/fnb/mat_expire/）。
    /// 认证：企业微信 OAuth（snsapi_base）换 UserId → mini_session（session_type='wecom_userid'，UserId 存 wechat_openid 列）。
    /// 鉴权：必须关联到在职 staff 才能使用 —— 企微 UserId → member_social_account(type='wecom', num=UserId)
    /// → member → social_account_for_job → staff_social_account(时间窗) → staff(valid=1)。
    /// OAuthLogin 关联不上直接拒绝不发 session；业务接口每次请求都重新校验（离职后旧 session 立即失效，
    /// 统一返 code=2 让前端重走 OAuth 并在 OAuthLogin 处收到明确拒绝话术）。新增批次落录入人 staff_id。
    /// 推送接收人：app 目录纯文本文件 config.fnbAlertReceivers（@all 或 企微UserId|企微UserId…），每次推送现读，缺省 @all。
    /// </summary>
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class FnbMaterialController : ControllerBase
    {
        public const string SESSION_TYPE_WECOM = "wecom_userid";
        public const string H5_URL = "https://mini.snowmeet.top/fnb/mat_expire/index.html";
        public const string H5_BATCH_URL = "https://mini.snowmeet.top/fnb/mat_expire/new.html";  // 批次详情/编辑页，?id={batch.id}
        public const string PIC_HOST = "https://mini.snowmeet.top";  // 图文消息图片前缀（批次照片/占位 logo）

        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;
        private readonly FnbWeComController _wecomHelper;
        private readonly MiniAppHelperController _mH;
        private readonly StaffController _staffHelper;

        public FnbMaterialController(ApplicationDBContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
            _wecomHelper = new FnbWeComController(db, config);
            _mH = new MiniAppHelperController(db, config);
            _staffHelper = new StaffController(db);
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
                // 进入闸门：必须关联到在职 staff，否则不发 session
                int? staffId = await _resolveStaffId(userid.Trim());
                if (staffId == null)
                {
                    return Ok(new ApiResult<object>() { code = 1, message = "仅限在职员工使用，请联系管理员开通", data = null });
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

        // 企微 UserId → member_social_account(type='wecom') → member → 在职时间窗 staff（且 staff.valid=1）。
        // 未录入 wecom MSA / 无在职绑定 / staff 已停用 → null
        [NonAction]
        private async Task<int?> _resolveStaffId(string wecomUserId)
        {
            try
            {
                Staff staff = await _staffHelper.GetStaffBySocialNum(wecomUserId, MemberSocialAccount.TYPE_WECOM);
                return (staff == null || staff.valid != 1) ? (int?)null : staff.id;
            }
            catch
            {
                return null;
            }
        }

        // 业务接口统一鉴权：会话有效 且 当前仍关联在职 staff。失败返 null（调用方统一 code=2 →
        // 前端清 key 重走 OAuth，离职员工在 OAuthLogin 处被明确拒绝）
        [NonAction]
        private async Task<(string userId, int staffId)?> _requireStaff(string sessionKey)
        {
            string userId = await _getWecomUserId(sessionKey);
            if (userId == null)
            {
                return null;
            }
            int? staffId = await _resolveStaffId(userId);
            if (staffId == null)
            {
                return null;
            }
            return (userId, (int)staffId);
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
            var ctx = await _requireStaff(sessionKey);
            if (ctx == null)
            {
                return _sessionExpired();
            }
            string userId = ctx.Value.userId;
            List<FnbMaterialBatch> batches = await _db.fnbMaterialBatch
                .Where(b => b.valid).OrderBy(b => b.expire_date).ThenBy(b => b.id)
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
            var ctx = await _requireStaff(sessionKey);
            if (ctx == null)
            {
                return _sessionExpired();
            }
            string userId = ctx.Value.userId;
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
                posted.staff_id = ctx.Value.staffId;
                posted.valid = true;
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
                .Where(b => b.id == posted.id && b.valid).FirstOrDefaultAsync();
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
            var ctx = await _requireStaff(sessionKey);
            if (ctx == null)
            {
                return _sessionExpired();
            }
            string userId = ctx.Value.userId;
            action = Util.UrlDecode(action ?? "").Trim();
            if (!action.Equals("用完") && !action.Equals("报废"))
            {
                return Ok(new ApiResult<object>() { code = 1, message = "处置类型不支持", data = null });
            }
            FnbMaterialBatch batch = await _db.fnbMaterialBatch
                .Where(b => b.id == id && b.valid).FirstOrDefaultAsync();
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
            var ctx = await _requireStaff(sessionKey);
            if (ctx == null)
            {
                return _sessionExpired();
            }
            string userId = ctx.Value.userId;
            FnbMaterialBatch batch = await _db.fnbMaterialBatch
                .Where(b => b.id == id && b.valid).FirstOrDefaultAsync();
            if (batch == null)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "批次不存在", data = null });
            }
            batch.valid = false;
            batch.update_date = DateTime.Now;
            _db.fnbMaterialBatch.Entry(batch).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<object>() { code = 0, message = "", data = null });
        }

        // 批次号发号：B{yyMMdd}-{当日已发数+1，2位}。仅参考号，不保证并发唯一（手输允许重复）
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> GenBatchNo(string sessionKey)
        {
            var ctx = await _requireStaff(sessionKey);
            if (ctx == null)
            {
                return _sessionExpired();
            }
            string userId = ctx.Value.userId;
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
            var ctx = await _requireStaff(sessionKey);
            if (ctx == null)
            {
                return _sessionExpired();
            }
            string userId = ctx.Value.userId;
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
            var ctx = await _requireStaff(sessionKey);
            if (ctx == null)
            {
                return _sessionExpired();
            }
            string userId = ctx.Value.userId;
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

        // ====== 名称实时扫描（OCR） ======

        public class OcrScanBody
        {
            public string image { get; set; }   // JPEG base64（不含 data: 前缀）
        }

        // H5 相机逐帧识别名称候选：前端 ~1.3s/帧节流调用、有候选即暂停，帧不落盘。
        // 返回按字高降序的候选文本行（滤掉日期/纯数字/净含量/包装说明等噪声），店员点选确认
        [HttpPost]
        public async Task<ActionResult<ApiResult<object>>> OcrScanName([FromBody] OcrScanBody body, [FromQuery] string sessionKey)
        {
            var ctx = await _requireStaff(sessionKey);
            if (ctx == null)
            {
                return _sessionExpired();
            }
            if (body == null || string.IsNullOrWhiteSpace(body.image))
            {
                return Ok(new ApiResult<object>() { code = 1, message = "缺少图像", data = null });
            }
            try
            {
                Credential cred = new Credential
                {
                    SecretId = _config.GetSection("Settings").GetSection("TencentCloudId").Value.Trim(),
                    SecretKey = _config.GetSection("Settings").GetSection("TencentCloudSecret").Value.Trim()
                };
                ClientProfile clientProfile = new ClientProfile();
                HttpProfile httpProfile = new HttpProfile();
                httpProfile.Endpoint = "ocr.tencentcloudapi.com";
                clientProfile.HttpProfile = httpProfile;
                OcrClient client = new OcrClient(cred, "", clientProfile);
                GeneralBasicOCRRequest req = new GeneralBasicOCRRequest();
                req.ImageBase64 = body.image;
                GeneralBasicOCRResponse resp = client.GeneralBasicOCRSync(req);
                var allLines = (resp.TextDetections ?? new TextDetection[0])
                    .Where(t => !string.IsNullOrWhiteSpace(t.DetectedText)).ToList();
                var candidates = allLines
                    .Select(t => new
                    {
                        text = t.DetectedText.Trim(),
                        height = t.ItemPolygon == null ? 0L : (t.ItemPolygon.Height ?? 0L)
                    })
                    .Where(t => IsNameCandidate(t.text))
                    .OrderByDescending(t => t.height)
                    .Take(5).ToList();
                // 日期候选用全部原始行提取（名称过滤会剔掉含日期的行）；expireDates=带到期锚词行的日期
                var (dates, expireDates) = ExtractDates(allLines.Select(t => t.DetectedText));
                return Ok(new ApiResult<object>() { code = 0, message = "", data = new { candidates, dates, expireDates } });
            }
            catch (Exception ex)
            {
                return Ok(new ApiResult<object>() { code = 1, message = "识别失败：" + ex.Message, data = null });
            }
        }

        // 行内含到期锚词（保质期至/前食用/到期/EXP/BEST BEFORE…）时，该行日期视为到期日期首选。
        // 「保质期」不带日期的行（如 保质期：12个月）没有日期可提取，宽锚词无副作用
        private static readonly string[] EXPIRE_HINTS = {
            "此日期前", "前食用", "前使用", "前饮用", "保质期", "到期", "有效期", "赏味",
            "EXP", "BEST BEFORE", "USE BY", "BBE" };

        private static bool _hasExpireHint(string line)
        {
            string up = line.ToUpper();
            return EXPIRE_HINTS.Any(h => up.Contains(h));
        }

        // 从 OCR 文本行提取日期候选，归一化 yyyy-MM-dd 去重（最多 6 个）。
        // 覆盖：2026年7月16日 / 2026-07-16 / 2026/7/16 / 2026.07.16 / 20260716 / 260716（喷码）
        //      / 16-07-2026（DD/MM/YYYY，>12 侧判日）/ 16 JUL 2026 / JUL 16, 2026 / 16JUL26
        // 返回 (all=全部日期, expire=其中带到期锚词行的日期)
        [NonAction]
        public static (List<string> all, List<string> expire) ExtractDates(IEnumerable<string> lines)
        {
            var all = new List<string>();
            var expire = new List<string>();
            foreach (string raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }
                string s = raw.Trim();
                var found = new List<string>();   // 本行提取结果
                // 2026年7月16日（「日」可省）
                foreach (Match m in Regex.Matches(s, @"(20\d{2})\s*年\s*(\d{1,2})\s*月\s*(\d{1,2})\s*日?"))
                {
                    _addDate(found, m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
                }
                // 2026-07-16 / 2026/7/16 / 2026.07.16
                foreach (Match m in Regex.Matches(s, @"(?<!\d)(20\d{2})\s*[./\-]\s*(\d{1,2})\s*[./\-]\s*(\d{1,2})(?!\d)"))
                {
                    _addDate(found, m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
                }
                // 8 位连续喷码 20260716
                foreach (Match m in Regex.Matches(s, @"(?<!\d)(20\d{2})(\d{2})(\d{2})(?!\d)"))
                {
                    _addDate(found, m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
                }
                // 6 位连续喷码 260716 → 2026-07-16（首位 2 限定 202x-203x 年代，降低条码误匹配）
                foreach (Match m in Regex.Matches(s, @"(?<!\d)(2\d)(\d{2})(\d{2})(?!\d)"))
                {
                    _addDate(found, "20" + m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
                }
                // 16-07-2026 / 16.07.2026（日月年；两段都 ≤12 时默认 DD/MM）
                foreach (Match m in Regex.Matches(s, @"(?<!\d)(\d{1,2})\s*[./\-]\s*(\d{1,2})\s*[./\-]\s*(20\d{2})(?!\d)"))
                {
                    int a = int.Parse(m.Groups[1].Value), b = int.Parse(m.Groups[2].Value);
                    if (a > 12)
                    {
                        _addDate(found, m.Groups[3].Value, b.ToString(), a.ToString());
                    }
                    else if (b > 12)
                    {
                        _addDate(found, m.Groups[3].Value, a.ToString(), b.ToString());
                    }
                    else
                    {
                        _addDate(found, m.Groups[3].Value, b.ToString(), a.ToString());
                    }
                }
                // 英文：16 JUL 2026 / 16JUL26 / 16 JUL. 2026
                string up = s.ToUpper();
                foreach (Match m in Regex.Matches(up, @"(?<!\d)(\d{1,2})\s*(JAN|FEB|MAR|APR|MAY|JUN|JUL|AUG|SEP|OCT|NOV|DEC)[A-Z]*\.?[,\s]*(\d{4}|\d{2})(?!\d)"))
                {
                    _addDate(found, _fixYear(m.Groups[3].Value), _monthNum(m.Groups[2].Value), m.Groups[1].Value);
                }
                // 英文：JUL 16, 2026
                foreach (Match m in Regex.Matches(up, @"(JAN|FEB|MAR|APR|MAY|JUN|JUL|AUG|SEP|OCT|NOV|DEC)[A-Z]*\.?\s*(\d{1,2})[,\s]+(\d{4}|\d{2})(?!\d)"))
                {
                    _addDate(found, _fixYear(m.Groups[3].Value), _monthNum(m.Groups[1].Value), m.Groups[2].Value);
                }
                all.AddRange(found);
                if (found.Count > 0 && _hasExpireHint(s))
                {
                    expire.AddRange(found);
                }
            }
            return (all.Distinct().Take(6).ToList(), expire.Distinct().Take(6).ToList());
        }

        private static string _fixYear(string y)
        {
            return y.Length == 2 ? "20" + y : y;
        }

        private static string _monthNum(string mon)
        {
            string[] names = { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };
            return (Array.IndexOf(names, mon) + 1).ToString();
        }

        // 合法 + 合理（2015~2039）才收，归一化 yyyy-MM-dd
        private static void _addDate(List<string> list, string ys, string ms, string ds)
        {
            if (!int.TryParse(ys, out int y) || !int.TryParse(ms, out int mo) || !int.TryParse(ds, out int d))
            {
                return;
            }
            if (y < 2015 || y > 2039 || mo < 1 || mo > 12 || d < 1 || d > 31)
            {
                return;
            }
            try
            {
                DateTime dt = new DateTime(y, mo, d);
                list.Add(dt.ToString("yyyy-MM-dd"));
            }
            catch { }
        }

        // 名称候选过滤：剔除日期/纯数字（条码/喷码）/净含量/包装常见说明行
        [NonAction]
        public static bool IsNameCandidate(string s)
        {
            if (s.Length < 2 || s.Length > 20)
            {
                return false;
            }
            if (Regex.IsMatch(s, @"^[\d\s./:>-]+$"))
            {
                return false;   // 纯数字/条码/喷码日期
            }
            if (Regex.IsMatch(s, @"\d{4}[年/.-]\d{1,2}|\d{1,2}月\d{1,2}日"))
            {
                return false;   // 含日期
            }
            if (Regex.IsMatch(s, @"^净含量|\d+\s*(g|kg|ml|mL|L|克|千克|毫升|升)\s*$"))
            {
                return false;   // 净含量/规格
            }
            string[] stop = { "生产日期", "保质期", "配料", "贮存", "储存", "执行标准", "生产许可", "地址", "电话",
                "营养成分", "食用方法", "此日期前", "有效期", "产品标准", "生产商", "制造商", "经销商", "客服", "扫码", "官网" };
            foreach (string w in stop)
            {
                if (s.Contains(w))
                {
                    return false;
                }
            }
            return true;
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
        // 当天已成功提醒过的批次跳过（防重复骚扰）。
        // 无鉴权（用户拍板 2026-07-16）：供 crontab 定时 curl 直接触发，不受 session 30 天过期影响；
        // 滥用风险由当天去重兜底（同批次一天最多推一次）。sessionKey 选传，仅用于日志记录触发人。
        // touser 仅联调覆盖用（传自己的 UserId 避免打扰全员），缺省走配置文件
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> PushExpireAlert(string touser = null, string sessionKey = null)
        {
            string userId = await _getWecomUserId(sessionKey);  // 可为 null：定时任务/无 session 触发
            string receivers = string.IsNullOrWhiteSpace(touser) ? _loadAlertReceivers() : Util.UrlDecode(touser).Trim();
            DateTime today = DateTime.Now.Date;

            List<FnbMaterialBatch> all = await _db.fnbMaterialBatch
                .Where(b => b.valid && (b.dispose_status == null || b.dispose_status.Trim() == ""))
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

            // 批次首图：image_ids 第一个 id → upload_file 路径 → 完整 URL；无照片用站内占位图
            var firstImgIds = new Dictionary<int, int>();  // batch.id → 首图 upload_file.id
            foreach (FnbMaterialBatch b in candidates)
            {
                string first = (b.image_ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (first != null && int.TryParse(first.Trim(), out int imgId))
                {
                    firstImgIds[b.id] = imgId;
                }
            }
            var imgPaths = firstImgIds.Count == 0
                ? new Dictionary<int, string>()
                : await _db.UploadFile.Where(f => firstImgIds.Values.Contains(f.id))
                    .AsNoTracking().ToDictionaryAsync(f => f.id, f => f.file_path_name);

            // 每个批次一条独立图文消息（图=该批次首图）；单批失败不阻断其余批次
            int sent = 0;
            var failed = new List<string>();
            foreach (FnbMaterialBatch b in candidates)
            {
                string status = DeriveStatus(b, today);
                int days = (b.expire_date.Date - today).Days;
                string daysText = days < 0 ? "已逾期 " + (-days) + " 天" : (days == 0 ? "今日到期" : "还剩 " + days + " 天");
                string picUrl = PIC_HOST + "/images/logo.png";
                if (firstImgIds.TryGetValue(b.id, out int fid) && imgPaths.TryGetValue(fid, out string p) && !string.IsNullOrWhiteSpace(p))
                {
                    picUrl = PIC_HOST + p.Trim();
                }
                var articles = new List<FnbWeComController.NewsArticle>()
                {
                    new FnbWeComController.NewsArticle()
                    {
                        title = "【" + status + "】" + b.name.Trim(),
                        description = "批次 " + b.batch_no.Trim() + " · 到期 " + b.expire_date.ToString("yyyy/MM/dd") + " · " + daysText,
                        url = H5_BATCH_URL + "?id=" + b.id,
                        picurl = picUrl
                    }
                };
                FnbWeComController.WeComSendResponse res = await _wecomHelper.SendNews(articles, receivers, "食材过期提醒");
                bool ok = res != null && res.errcode == 0;
                string errMsg = res == null ? "企业微信接口调用失败" : (res.errcode == 0 ? null : (res.errcode + " " + (res.errmsg ?? "")));
                if (ok) { sent++; } else { failed.Add(b.name.Trim() + "：" + errMsg); }
                await _db.fnbMaterialAlertLog.AddAsync(new FnbMaterialAlertLog()
                {
                    id = 0,
                    batch_id = b.id,
                    alert_status = status,
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

            if (sent == 0)
            {
                return Ok(new ApiResult<object>()
                {
                    code = 1,
                    message = "推送失败：" + string.Join("；", failed),
                    data = new { count = candidates.Count, sent, failed }
                });
            }
            return Ok(new ApiResult<object>()
            {
                code = 0,
                message = "",
                data = new { count = candidates.Count, sent, failed, expired, dueToday, warning, touser = receivers }
            });
        }
    }
}
