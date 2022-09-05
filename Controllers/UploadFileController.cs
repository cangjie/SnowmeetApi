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
using static Org.BouncyCastle.Math.EC.ECCurve;
using SnowmeetApi.Models.Users;

namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
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

        [HttpPost("{sessionKey}")]
        public async Task<ActionResult<string>> Upload(string sessionKey, IFormFile file)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser._context = _db;
            UnicUser user = UnicUser.GetUnicUser(sessionKey);
            

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
                file_path_name = returnFileName
            };
            await _db.UploadFile.AddAsync(fileSave);
            await _db.SaveChangesAsync();

            return returnFileName.Trim();
        }


        /*
        // GET: api/UploadFile
        [HttpGet]
        public async Task<ActionResult<IEnumerable<UploadFile>>> GetUploadFile()
        {
            return await _context.UploadFile.ToListAsync();
        }

        // GET: api/UploadFile/5
        [HttpGet("{id}")]
        public async Task<ActionResult<UploadFile>> GetUploadFile(int id)
        {
            var uploadFile = await _context.UploadFile.FindAsync(id);

            if (uploadFile == null)
            {
                return NotFound();
            }

            return uploadFile;
        }

        // PUT: api/UploadFile/5
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPut("{id}")]
        public async Task<IActionResult> PutUploadFile(int id, UploadFile uploadFile)
        {
            if (id != uploadFile.id)
            {
                return BadRequest();
            }

            _context.Entry(uploadFile).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!UploadFileExists(id))
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

        // POST: api/UploadFile
        // To protect from overposting attacks, see https://go.microsoft.com/fwlink/?linkid=2123754
        [HttpPost]
        public async Task<ActionResult<UploadFile>> PostUploadFile(UploadFile uploadFile)
        {
            _context.UploadFile.Add(uploadFile);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetUploadFile", new { id = uploadFile.id }, uploadFile);
        }

        // DELETE: api/UploadFile/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUploadFile(int id)
        {
            var uploadFile = await _context.UploadFile.FindAsync(id);
            if (uploadFile == null)
            {
                return NotFound();
            }

            _context.UploadFile.Remove(uploadFile);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        */
        private bool UploadFileExists(int id)
        {
            return _db.UploadFile.Any(e => e.id == id);
        }
    }
}
