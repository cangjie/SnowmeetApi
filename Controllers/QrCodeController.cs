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
                purpose = purpose
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
    }
}