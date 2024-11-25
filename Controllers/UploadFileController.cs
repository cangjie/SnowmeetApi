using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Models.Users;


using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using System.IO;
using System.Threading.Tasks;
using RestSharp.Extensions;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Text.RegularExpressions;
namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class UploadFileController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private IConfiguration _config;
        private readonly User.MemberController _memberHelper;

        public UploadFileController(ApplicationDBContext context, IConfiguration config)
        {
            _db = context;
            _config = config.GetSection("Settings");
            UnicUser._context = context;
            _memberHelper = new User.MemberController(_db, _config);
        }

        [HttpPost]
        //[Route(nameof(UploadFile))]
        public async Task<ActionResult<string>> UploadLargeFile([FromQuery] string sessionKey)
        {
            var request = HttpContext.Request;

            string sessionType="wl_wechat_mini_openid";
            string[] pathArr = request.Path.ToString().Split('/');

            /*
            sessionKey = pathArr[pathArr.Length - 1].Trim();
            sessionKey = Util.UrlDecode(sessionKey);
            */

            
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return NotFound();
            }
            var staffList = await _db.schoolStaff.Where(s => s.member_id == member.id).AsNoTracking().ToListAsync();
            if (staffList.Count <= 0)
            {
                return BadRequest();
            }

            string dateStr = DateTime.Now.Year.ToString() + DateTime.Now.Month.ToString().PadLeft(2, '0') + DateTime.Now.Day.ToString().PadLeft(2, '0');
            
            

            

            




            // validation of Content-Type
            // 1. first, it must be a form-data request
            // 2. a boundary should be found in the Content-Type
            bool tryParse = System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaTypeHeader) ;
            string bStr = "";
            foreach(System.Net.Http.Headers.NameValueHeaderValue nvhv in mediaTypeHeader.Parameters)
            {
                if (nvhv.Name.Trim().Equals("boundary"))
                {
                    bStr = nvhv.Value.Trim();
                    break;
                }
            }
            
            if (!request.HasFormContentType || !tryParse ||  bStr.Equals(""))
                
            {
                return  BadRequest();
            }

            var boundary = HeaderUtilities.RemoveQuotes(bStr).Value;
            //var boundary = HeaderUtilities.RemoveQuotes(mediaTypeHeader.CharSet).Value;
            var reader = new MultipartReader(boundary, request.Body);
            var section = await reader.ReadNextSectionAsync();
            string disPosition = section.ContentDisposition.Trim();

            Match match = Regex.Match(disPosition, @"filename=\""(.)+\""");
            string ext = "";
            if (match.Success)
            {
                string[] extArr = match.Value.Trim().Split('.');
                ext = extArr[extArr.Length - 1].Replace("\"", "").Trim();
            }
            // This sample try to get the first file from request and save it
            // Make changes according to your needs in actual use
            while (section != null)
            {
                var hasContentDispositionHeader = System.Net.Http.Headers.ContentDispositionHeaderValue.TryParse(section.ContentDisposition,
                    out var contentDisposition);

                if (hasContentDispositionHeader && contentDisposition.DispositionType.Equals("form-data") &&
                    !string.IsNullOrEmpty(contentDisposition.FileName))
                {
                    // Don't trust any file name, file extension, and file data from the request unless you trust them completely
                    // Otherwise, it is very likely to cause problems such as virus uploading, disk filling, etc
                    // In short, it is necessary to restrict and verify the upload
                    // Here, we just use the temporary folder and a random file name

                    // Get the temporary folder, and combine a random file name with it
                    string fileName = Util.GetLongTimeStamp(DateTime.Now).Trim() + "." + ext.Trim();
                    string returnFileName = "/upload/" + dateStr + "/" + fileName.Trim();
                    //var saveToPath = Path.Combine(Path.GetTempPath(), fileName);
                    string filePath = Util.workingPath + "/wwwroot/upload/" + dateStr;
                    if (!Directory.Exists(filePath))
                    {
                        Directory.CreateDirectory(filePath);
                    }
                    string saveToPath = filePath + "/" + fileName.Trim();

                    using (var targetStream = System.IO.File.Create(saveToPath))
                    {
                        await section.Body.CopyToAsync(targetStream);
                    }
                    UploadFile fileSave = new UploadFile()
                    {
                        id = 0,
                        member_id = member.id,
                        file_path_name = returnFileName,
                        purpose = "万龙滑雪学校"
                    };
                    await _db.UploadFile.AddAsync(fileSave);
                    await _db.SaveChangesAsync();
                    return Ok(returnFileName);
                }

                section = await reader.ReadNextSectionAsync();
            }

            // If the code runs to this location, it means that no files have been saved
            return BadRequest("No files data in the request.");
        }

        [HttpPost]
        public async Task<ActionResult<UploadFile>> UploadFile(string sessionKey, string purpose, bool isWeb, IFormFile file)
        {
            
            sessionKey = Util.UrlDecode(sessionKey);
            purpose = Util.UrlDecode(purpose);
            //UnicUser._context = _db;
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _db)).Value;
            if (user == null)
            {
                return BadRequest();
            }

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
                owner = user.miniAppOpenId.Trim(),
                file_path_name = returnFileName,
                is_web = isWeb? 1:0,
                purpose = purpose
            };
            await _db.UploadFile.AddAsync(fileSave);
            await _db.SaveChangesAsync();

            return fileSave;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<UploadFile>>> GetUploadList(string sessionKey, string purpose)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            purpose = Util.UrlDecode(purpose);
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _db)).Value;
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

        [HttpPost]
        public async Task<ActionResult<string>> FileUpload([FromForm]IFormFile file, [FromQuery]string sessionKey, [FromQuery]string purpose = "")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            purpose = Util.UrlDecode(purpose);
            UnicUser._context = _db;
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _db)).Value;
            

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
                owner = user.miniAppOpenId.Trim(),
                file_path_name = returnFileName,
                purpose = purpose.Trim()
            };
            await _db.UploadFile.AddAsync(fileSave);
            await _db.SaveChangesAsync();

            return returnFileName.Trim();
        }


        [HttpPost("{sessionKey}")]
        public async Task<ActionResult<string>> Upload([FromRoute]string sessionKey, [FromForm]IFormFile file, [FromQuery]string purpose = "")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            purpose = Util.UrlDecode(purpose);
            UnicUser._context = _db;
            UnicUser user = (await UnicUser.GetUnicUserAsync(sessionKey, _db)).Value;
            

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
                owner = user.miniAppOpenId.Trim(),
                file_path_name = returnFileName,
                purpose = purpose.Trim()
            };
            await _db.UploadFile.AddAsync(fileSave);
            await _db.SaveChangesAsync();

            return returnFileName.Trim();
        }


       
        private bool UploadFileExists(int id)
        {
            return _db.UploadFile.Any(e => e.id == id);
        }
    }
}
