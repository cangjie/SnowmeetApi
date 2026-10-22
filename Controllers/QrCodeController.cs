using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers.Order;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
namespace SnowmeetApi.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class QrCodeController : ControllerBase
    {
        private readonly IHttpContextAccessor _http;
        private IConfiguration _config;
        private readonly ApplicationDBContext _db;
        public QrCodeController(ApplicationDBContext db, IConfiguration config, IHttpContextAccessor http)
        {
            _db = db;
            _config = config;
            _http = http;
        }
        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResult<ScanQrCode>>> StopQeryScan(int id,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);

            ScanQrCode sq = await _db.scanQrCode.FindAsync(id);
            if (sq == null)
            {
                return Ok(new ApiResult<object?>()
                {
                    code = 1,
                    message = "未找到",
                    data = null
                });
            }
            if (sq.staff_id != staff.id)
            {
                return Ok(new ApiResult<object?>()
                {
                    code = 1,
                    message = "无权限",
                    data = null
                });
            }
            sq.stoped = 1;
            sq.update_date = DateTime.Now;
            _db.scanQrCode.Entry(sq).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<ScanQrCode>()
            {
                code = 0,
                message = "",
                data = sq
            });
        }
        [NonAction]
        public async Task<ScanQrCode> QueryScan(int id)
        {
            var contextOptions = new DbContextOptionsBuilder<ApplicationDBContext>()
                .UseSqlServer(Util.GetSqlServerConnectionString()).Options;
            var db = new ApplicationDBContext(contextOptions);
            List<ScanQrCode> sqList = await _db.scanQrCode.Where(s => s.id == id).AsNoTracking().ToListAsync();
            ScanQrCode? sq = sqList.Count == 0 ? null : sqList[0];
            for (int times = 0;
                times < 600 && DateTime.Now < sq.expire_time && sq.scaned == 0
                && sq.stoped == 0 && sq.authed == 0; times++)
            {
                System.Threading.Thread.Sleep(1000);
                try
                {
                    sqList = await db.scanQrCode.Where(s => s.id == id).AsNoTracking().ToListAsync();
                    sq = sqList.Count == 0 ? null : sqList[0];
                }
                catch (Exception err)
                {
                    Console.WriteLine(err.ToString());
                }
            }
            return sq;
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<ScanQrCode>>> CreateNewScanQrCodeByStaff(string code, string scene,
            string purpose, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            scene = Util.UrlDecode(scene);
            purpose = Util.UrlDecode(purpose);
            StaffController _staffHelper = new StaffController(_db);
            ApiResult<object?> r = await _staffHelper.CheckStaffLevel(100, sessionKey, sessionType);
            if (r != null)
            {
                return Ok(r);
            }
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            int lastId = 0;
            try
            {
                lastId = await _db.scanQrCode.MaxAsync(s => s.id);
            }
            catch
            {

            }
            lastId++;
            ScanQrCode qrCode = new ScanQrCode()
            {
                id = lastId,
                code = code + "_" + lastId.ToString(),
                platform = "wechat_oa",
                staff_id = staff.id,
                scene = scene,
                purpose = purpose,
                expire_time = DateTime.Now.AddSeconds(120)
            };
            await _db.scanQrCode.AddAsync(qrCode);
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<ScanQrCode>()
            {
                code = 0,
                message = "",
                data = qrCode
            });
        }
        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResult<ScanQrCode>>> QueryCell(int id, string cell,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            List<MemberSocialAccount> msaList = await _db.memberSocialAccount
                .Where(m => m.valid == 1 && m.type.Trim().Equals("cell") && m.num.Trim().Equals(cell.Trim()))
                .AsNoTracking().ToListAsync();
            if (msaList.Count <= 0)
            {
                return Ok(new ApiResult<object?>()
                {
                    code = 1,
                    message = "用户不存在",
                    data = null
                });
            }
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            bool authed = false;
            ScanQrCode sq = await _db.scanQrCode.FindAsync(id);
            if (staff.title_level < 100)
            {
                return Ok(new ApiResult<object?>()
                {
                    code = 1,
                    message = "当前店员无权限",
                    data = null
                });
            }
            else if (staff.title_level < 200)
            {

                List<ScanQrCode> cL = await _db.scanQrCode
                    .Where(c => c.cell.Trim().Equals(cell.Trim()) && c.authed == 1 && c.create_date >= DateTime.Now.Date
                        && c.platform.Trim().Equals(sq.platform.Trim()) && c.scene.Trim().Equals(sq.scene.Trim()) && c.purpose.Trim().Equals(sq.purpose.Trim()))
                    .AsNoTracking().ToListAsync();
                if (cL.Count > 0)
                {
                    authed = true;
                }
            }
            else
            {
                authed = true;
            }
            sq.cell = cell.Trim();
            sq.authed = authed ? 1 : 0;
            sq.update_date = DateTime.Now;
            sq.scaner_member_id = msaList[0].member_id;
            _db.scanQrCode.Entry(sq).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<ScanQrCode>()
            {
                code = 0,
                message = "",
                data = sq
            });
        }
        [HttpGet]
        public async Task<ActionResult<ApiResult<List<ScanQrCode>>>> GetTodayAuthCellList(
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            MemberController _mHelper = new MemberController(_db, _config);
            if (staff.title_level < 200)
            {
                return Ok(new ApiResult<object?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            // 只列店员输手机号提交、还在等的（scaned=0、未作废）；顾客自己扫码验证的记录也带 cell，但不需要授权
            List<ScanQrCode> scanQrCodeList = await _db.scanQrCode
                .Where(s => s.cell != null && s.scaned == 0 && s.stoped == 0 && s.create_date >= DateTime.Now.Date)
                .OrderByDescending(s => s.create_date).AsNoTracking().ToListAsync();
            List<ScanQrCode.AuthCell> cellList = new List<ScanQrCode.AuthCell>();
            foreach (ScanQrCode sq in scanQrCodeList)
            {
                if (cellList.Any(c => c.cell.Trim().Equals(sq.cell.Trim())))
                {
                    continue;
                }
                bool authed = scanQrCodeList.Any(s => s.cell.Trim().Equals(sq.cell) && s.authed == 1);
                Member? member = await _mHelper.GetWholeMemberByNum(sq.cell.Trim(), "cell");
                ScanQrCode.AuthCell item = new ScanQrCode.AuthCell()
                {
                    id = sq.id,
                    cell = sq.cell,
                    authed = authed,
                    member = member,
                    submitTime = sq.create_date,
                    submitStaff = await _db.staff.FindAsync(sq.staff_id)
                };
                cellList.Add(item);
            }
            cellList = cellList.OrderBy(c => c.authed).ThenByDescending(c => c.submitTime).ToList();
            return Ok(new ApiResult<List<ScanQrCode.AuthCell>>()
            {
                code = 0,
                message = "",
                data = cellList
            });
        }
        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResult<ScanQrCode>>> GiveAuth(int id,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff.title_level < 200)
            {
                return Ok(new ApiResult<object?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            ScanQrCode sq = await _db.scanQrCode.FindAsync(id);
            sq.authed = 1;
            sq.auth_staff_id = staff.id;
            sq.update_date = DateTime.Now;
            _db.scanQrCode.Entry(sq).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<ScanQrCode>()
            {
                code = 0,
                message = "",
                data = sq
            });
        }
        [HttpGet("{scanId}")]
        public async Task<ActionResult<ApiResult<ScanQrCode>>> StartReceptWithoutScan(int scanId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff == null)
            {
                return Ok(new ApiResult<object?>()
                {
                    code = 1,
                    message = "没有权限",
                    data = null
                });
            }
            ScanQrCode code = await _db.scanQrCode.FindAsync(scanId);
            if (code == null)
            {
                return Ok(new ApiResult<object?>()
                {
                    code = 1,
                    message = "没查询到对象",
                    data = null
                });
            }
            code.stoped = 1;
            code.no_scan = 1;
            code.update_date = DateTime.Now;
            _db.scanQrCode.Entry(code).State = EntityState.Modified;
            CoreDataModLog log = new CoreDataModLog()
            {
                id = 0,
                table_name = "scan_qrcode",
                field_name = "no_scan",
                key_value = scanId,
                scene = "接待流程扫码",
                staff_id = staff.id,
                prev_value = "0",
                current_value = "1",
                is_manual = 1,
                manual_memo = ""
            };
            await _db.coreDataModLog.AddAsync(log);
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<ScanQrCode>()
            {
                code = 0,
                message = "",
                data = code
            });
        }

        // ====== 店铺接待扫码：顾客扫店员手机上的码，直接进小程序报到（不经公众号） ======
        // 判定规则见 Helpers/ReceptCheckinRules.cs；离开出码页作废码沿用 StopQeryScan。

        public class ReceptCheckinView
        {
            public int id { get; set; }
            public string code { get; set; } = "";
            public string qrText { get; set; } = "";
            public string stage { get; set; } = "";
            public DateTime? expireTime { get; set; }
            public int? memberId { get; set; }
            public string name { get; set; } = "";
            public string gender { get; set; } = "";
            public string cell { get; set; } = "";
            public string maskedCell { get; set; } = "";
            public int? paymentId { get; set; }
        }

        public class ReceptCheckinPhoneBody
        {
            public string code { get; set; } = "";
            public string encData { get; set; } = "";
            public string iv { get; set; } = "";
        }

        [HttpGet]
        public async Task<ActionResult<ApiResult<ReceptCheckinView>>> CreateReceptCheckinByStaff(string sessionKey,
            string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            ApiResult<object?> r = await _staffHelper.CheckStaffLevel(100, sessionKey, sessionType);
            if (r != null)
            {
                return Ok(r);
            }
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            // scan_qrcode.id 不是自增列，沿用 CreateNewScanQrCodeByStaff 的 Max + 1
            int lastId = 0;
            try
            {
                lastId = await _db.scanQrCode.MaxAsync(s => s.id);
            }
            catch
            {

            }
            lastId++;
            ScanQrCode sq = new ScanQrCode()
            {
                id = lastId,
                code = ReceptCheckinRules.BuildCode(ReceptCheckinRules.NewToken(), lastId),
                platform = "wechat_mini",
                staff_id = staff.id,
                scene = "店铺接待",
                purpose = "顾客扫码进小程序",
                expire_time = DateTime.Now.AddSeconds(ReceptCheckinRules.QR_EXPIRE_SECONDS)
            };
            await _db.scanQrCode.AddAsync(sq);
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<ReceptCheckinView>()
            {
                code = 0,
                message = "",
                data = await _staffCheckinView(sq)
            });
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResult<ReceptCheckinView>>> GetReceptCheckinByStaff(int id,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            StaffController _staffHelper = new StaffController(_db);
            Staff staff = await _staffHelper.GetStaffBySessionKey(sessionKey, sessionType);
            if (staff == null)
            {
                return Ok(_checkinError("没有权限"));
            }
            ScanQrCode? sq = await _db.scanQrCode.Where(s => s.id == id).AsNoTracking().FirstOrDefaultAsync();
            if (sq == null || !ReceptCheckinRules.IsCheckinCode(sq.code))
            {
                return Ok(_checkinError("未找到"));
            }
            if (sq.staff_id != staff.id)
            {
                return Ok(_checkinError("无权限"));
            }
            return Ok(new ApiResult<ReceptCheckinView>()
            {
                code = 0,
                message = "",
                data = await _staffCheckinView(sq)
            });
        }

        // 顾客扫码打开小程序后报到：记下扫码人；会员已有手机号的直接算验证通过
        [HttpGet]
        public async Task<ActionResult<ApiResult<ReceptCheckinView>>> ScanReceptCheckin(string code, string sessionKey)
        {
            code = Util.UrlDecode(code ?? "").Trim();
            if (!ReceptCheckinRules.IsCheckinCode(code))
            {
                return Ok(_checkinError("二维码无效，请让店员换一个码"));
            }
            var (openId, unionId, sess) = await _loadWechatSession(sessionKey);
            if (sess == null)
            {
                return Ok(_checkinError("登录信息失效，请重新扫码"));
            }
            ScanQrCode? sq = await _db.scanQrCode.Where(s => s.code == code).FirstOrDefaultAsync();
            if (sq == null)
            {
                return Ok(_checkinError("二维码无效，请让店员换一个码"));
            }
            DateTime now = DateTime.Now;
            string? reject = ReceptCheckinRules.CheckScanAllowed(sq, openId, now);
            if (reject != null)
            {
                return Ok(_checkinError(reject));
            }
            if (sq.scaned == 0)
            {
                sq.scaned = 1;
                sq.scan_time = now;
                sq.scaner_openid = openId;
                sq.scaner_unionid = unionId;
            }
            if (!ReceptCheckinRules.IsVerified(sq))
            {
                MemberController _memberHelper = new MemberController(_db, _config);
                Member? member = await _memberHelper.GetWholeMemberByNum(openId, MemberSocialAccount.TYPE_WECHAT_MINI_OPENID);
                if (member == null && sess.member_id != null)
                {
                    member = await _memberHelper.GetWholeMemberById((int)sess.member_id);
                }
                if (member != null)
                {
                    sq.scaner_member_id = member.id;
                    if (!string.IsNullOrWhiteSpace(member.cell))
                    {
                        sq.cell = member.cell.Trim();
                    }
                }
            }
            sq.update_date = now;
            _db.scanQrCode.Entry(sq).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<ReceptCheckinView>()
            {
                code = 0,
                message = "",
                data = await _customerCheckinView(sq)
            });
        }

        // 顾客用微信手机号一键授权验证身份：找到或建立会员（与支付前身份验证同一套规则）
        [HttpPost]
        public async Task<ActionResult<ApiResult<ReceptCheckinView>>> VerifyReceptCheckinPhone(
            [FromBody] ReceptCheckinPhoneBody body, [FromQuery] string sessionKey)
        {
            string code = Util.UrlDecode(body?.code ?? "").Trim();
            var (openId, unionId, sess) = await _loadWechatSession(sessionKey);
            if (sess == null)
            {
                return Ok(_checkinError("登录信息失效，请重新扫码"));
            }
            ScanQrCode? sq = await _db.scanQrCode.Where(s => s.code == code).FirstOrDefaultAsync();
            if (sq == null || sq.scaned == 0 || !ReceptCheckinRules.SameOpenId(sq.scaner_openid, openId))
            {
                return Ok(_checkinError("请重新扫描店员手机上的二维码"));
            }
            string phone;
            try
            {
                phone = WechatPhoneHelper.Decrypt(body.encData, body.iv, sess.session_key);
            }
            catch (Exception ex)
            {
                return Ok(_checkinError("手机号解析失败: " + ex.Message));
            }
            if (string.IsNullOrEmpty(phone) || phone.Length != 11)
            {
                return Ok(_checkinError("手机号无效"));
            }
            var identityHelper = new PaymentIdentityController(_db, _config);
            int? memberId = await identityHelper.ResolveMemberByVerifiedPhone(phone, openId, "wechat", unionId, "店铺接待扫码");
            if (memberId == null)
            {
                return Ok(_checkinError("会员信息处理失败，请让店员按散客开单"));
            }
            if (sess.member_id != memberId)
            {
                sess.member_id = memberId;
                _db.miniSession.Entry(sess).State = EntityState.Modified;
            }
            sq.scaner_member_id = memberId;
            sq.cell = phone;
            sq.update_date = DateTime.Now;
            _db.scanQrCode.Entry(sq).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(new ApiResult<ReceptCheckinView>()
            {
                code = 0,
                message = "",
                data = await _customerCheckinView(sq)
            });
        }

        // 顾客等待页轮询：店员选了微信支付就返回 paymentId，顾客手机直接跳去付款
        [HttpGet]
        public async Task<ActionResult<ApiResult<ReceptCheckinView>>> GetReceptCheckinByCustomer(string code, string sessionKey)
        {
            code = Util.UrlDecode(code ?? "").Trim();
            var (openId, _, sess) = await _loadWechatSession(sessionKey);
            if (sess == null)
            {
                return Ok(_checkinError("登录信息失效，请重新扫码"));
            }
            ScanQrCode? sq = await _db.scanQrCode.Where(s => s.code == code).AsNoTracking().FirstOrDefaultAsync();
            if (sq == null || !ReceptCheckinRules.SameOpenId(sq.scaner_openid, openId))
            {
                return Ok(_checkinError("请重新扫描店员手机上的二维码"));
            }
            return Ok(new ApiResult<ReceptCheckinView>()
            {
                code = 0,
                message = "",
                data = await _customerCheckinView(sq)
            });
        }

        private async Task<(string? openId, string? unionId, MiniSession? session)> _loadWechatSession(string sessionKey)
        {
            var identityHelper = new PaymentIdentityController(_db, _config);
            var (openId, unionId, sess) = await identityHelper.LoadSessionContext(sessionKey);
            if (sess == null || (sess.session_type ?? "").Trim() != "wechat_mini_openid" || string.IsNullOrWhiteSpace(openId))
            {
                return (null, null, null);
            }
            return (openId.Trim(), unionId, sess);
        }

        private async Task<ReceptCheckinView> _staffCheckinView(ScanQrCode sq)
        {
            ReceptCheckinView view = new ReceptCheckinView()
            {
                id = sq.id,
                code = sq.code,
                qrText = ReceptCheckinRules.BuildQrText(sq.code),
                stage = ReceptCheckinRules.StaffStage(sq, DateTime.Now),
                expireTime = sq.expire_time
            };
            // 扫码验证过的，或店员输手机号匹配到的会员（等授权时也带上，店员页显示是谁）
            if (sq.scaner_member_id != null && (view.stage == ReceptCheckinRules.STAFF_VERIFIED
                || view.stage == ReceptCheckinRules.STAFF_PHONE_AUTHED || view.stage == ReceptCheckinRules.STAFF_NEED_AUTH))
            {
                MemberController _memberHelper = new MemberController(_db, _config);
                Member? member = await _memberHelper.GetWholeMemberById((int)sq.scaner_member_id);
                view.memberId = sq.scaner_member_id;
                view.name = member == null ? "" : (member.real_name ?? "").Trim();
                view.gender = member == null ? "" : (member.gender ?? "").Trim();
                view.cell = (sq.cell ?? "").Trim();
                view.maskedCell = ReceptCheckinRules.MaskCell(sq.cell);
            }
            return view;
        }

        private async Task<ReceptCheckinView> _customerCheckinView(ScanQrCode sq)
        {
            List<OrderPayment> payments = new List<OrderPayment>();
            if (ReceptCheckinRules.IsVerified(sq) && sq.scan_time != null)
            {
                DateTime since = sq.scan_time.Value;
                int? memberId = sq.scaner_member_id;
                payments = await _db.orderPayment.Include(p => p.order)
                    .Where(p => p.valid == 1 && p.status == "待支付" && p.create_date >= since
                        && p.order.valid == 1 && p.order.member_id == memberId)
                    .AsNoTracking().ToListAsync();
            }
            var (stage, paymentId) = ReceptCheckinRules.CustomerStage(sq, payments, DateTime.Now);
            return new ReceptCheckinView()
            {
                code = sq.code,
                stage = stage,
                maskedCell = ReceptCheckinRules.MaskCell(sq.cell),
                paymentId = paymentId
            };
        }

        private static ApiResult<object?> _checkinError(string message)
        {
            return new ApiResult<object?>()
            {
                code = 1,
                message = message,
                data = null
            };
        }

    }
}