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
            List<ScanQrCode> sqList = await _db.scanQrCode.Where(s => s.id == id).AsNoTracking().ToListAsync();
            ScanQrCode? sq = sqList.Count == 0 ? null : sqList[0];
            for (int times = 0;
                times < 600 && DateTime.Now < sq.expire_time && sq.scaned == 0
                && sq.stoped == 0 && sq.authed == 0; times++)
            {
                System.Threading.Thread.Sleep(1000);
                try
                {
                    sqList = await _db.scanQrCode.Where(s => s.id == id).AsNoTracking().ToListAsync();
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
            List<ScanQrCode> scanQrCodeList = await _db.scanQrCode
                .Where(s => s.cell != null && s.create_date >= DateTime.Now.Date)
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
    }
}