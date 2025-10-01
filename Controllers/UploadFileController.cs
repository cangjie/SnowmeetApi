using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using System.IO;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Models.Users;

namespace SnowmeetApi.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class UploadFileController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private IConfiguration _config;
        public UploadFileController(ApplicationDBContext context, IConfiguration config)
        {
            _db = context;
            _config = config.GetSection("Settings");
        }
        [HttpGet("id")]
        public async Task<ActionResult<UploadFile>> GetFile(int id)
        {
            return await _db.UploadFile.FindAsync(id);
        }
        [HttpPost]
        public async Task<ActionResult<UploadFile>> UploadFile([FromQuery]string sessionKey, [FromQuery]string purpose, [FromQuery]bool isWeb, IFormFile file)
        {
            ApiResult<object?> result = await CheckStaff(100, sessionKey, "wechat_mini_openid");
            if (result.code != 0)
            {
                return BadRequest();
            }
            Staff staff = (Staff)result.data;
            sessionKey = Util.UrlDecode(sessionKey);
            purpose = Util.UrlDecode(purpose);
            string dateStr = DateTime.Now.Year.ToString() + DateTime.Now.Month.ToString().PadLeft(2, '0') + DateTime.Now.Day.ToString().PadLeft(2, '0');
            string filePath = Util.workingPath + (isWeb? "/wwwroot/":"") + "/upload/" + dateStr;
            if (!Directory.Exists(filePath))
            {
                Directory.CreateDirectory(filePath);
            }
            string[] fileNameArr = file.FileName.Split('.');
            string ext = fileNameArr[fileNameArr.Length - 1].Trim();
            string fileName = Util.GetLongTimeStamp(DateTime.Now).Trim() + "." + ext.Trim();
            string returnFileName = "/upload/" + dateStr + "/" + fileName.Trim();
            using (Stream s = System.IO.File.Create(filePath + "/" + fileName.Trim()))
            {
                await file.CopyToAsync(s);
            }
            UploadFile fileSave = new UploadFile()
            {
                id = 0,
                staff_id = staff.id,
                file_path_name = returnFileName,
                is_web = isWeb? 1:0,
                purpose = purpose
            };
            await _db.UploadFile.AddAsync(fileSave);
            await _db.SaveChangesAsync();
            return Ok(fileSave);
        }
        [HttpGet]
        public async Task<ActionResult<IEnumerable<UploadFile>>> GetUploadList(string sessionKey, string purpose)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            purpose = Util.UrlDecode(purpose);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            if (purpose.Trim().Equals(""))
            {
                return await _db.UploadFile.OrderByDescending(u => u.id).ToListAsync();
            }
            else 
            {
                return await _db.UploadFile.Where(u => u.purpose.IndexOf(purpose) >= 0)
                    .OrderByDescending(u => u.id).ToListAsync();
            }

        }
        [HttpPost("{sessionKey}")]
        public async Task<ActionResult<string>> Upload(string sessionKey, IFormFile file)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            //UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _db);
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, "wechat_mini_openid");
            if (staff == null)
            {
                return BadRequest();
            }

            string dateStr = DateTime.Now.Year.ToString() + DateTime.Now.Month.ToString().PadLeft(2, '0') + DateTime.Now.Day.ToString().PadLeft(2, '0');
            string filePath = Util.workingPath + "/wwwroot/upload/" + dateStr;
            if (!Directory.Exists(filePath))
            {
                Directory.CreateDirectory(filePath);
            }
            string[] fileNameArr = file.FileName.Split('.');
            string ext = fileNameArr[fileNameArr.Length - 1].Trim();
            string fileName = Util.GetLongTimeStamp(DateTime.Now).Trim() + "." + ext.Trim();
            string returnFileName = "/upload/" + dateStr + "/" + fileName.Trim();
            using (Stream s = System.IO.File.Create(filePath + "/" + fileName.Trim()))
            {
                await file.CopyToAsync(s);
            }

            UploadFile fileSave = new UploadFile()
            {
                id = 0,
                staff_id = staff.id,
                file_path_name = returnFileName
            };
            await _db.UploadFile.AddAsync(fileSave);
            await _db.SaveChangesAsync();

            return returnFileName.Trim();
        }
        private async Task<ApiResult<object?>> CheckStaff(int level,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
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

        private bool UploadFileExists(int id)
        {
            return _db.UploadFile.Any(e => e.id == id);
        }
    }
}
