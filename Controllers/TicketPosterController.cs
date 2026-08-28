using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace SnowmeetApi.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class TicketPosterController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;

        public TicketPosterController(ApplicationDBContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        [HttpGet]
        public async Task<IActionResult> Generate(int batchId, string sessionKey,
            string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, Util.UrlDecode(sessionKey), sessionType);
            if (staff == null || staff.title_level < 200)
            {
                return BadRequest(new { code = 1, message = "没有权限" });
            }

            TicketShareBatch batch = await _db.ticketShareBatch.AsNoTracking()
                .FirstOrDefaultAsync(x => x.id == batchId && x.valid == 1);
            if (batch == null)
            {
                return BadRequest(new { code = 1, message = "分享批次不存在" });
            }
            TicketTemplate template = await _db.ticketTemplate.AsNoTracking()
                .FirstOrDefaultAsync(x => x.id == batch.template_id);
            if (template == null || template.cover_upload_id == null)
            {
                return BadRequest(new { code = 1, message = "模板海报不存在" });
            }
            UploadFile cover = await _db.UploadFile.AsNoTracking()
                .FirstOrDefaultAsync(x => x.id == template.cover_upload_id);
            if (cover == null || string.IsNullOrWhiteSpace(cover.file_path_name))
            {
                return BadRequest(new { code = 1, message = "模板海报文件不存在" });
            }

            string qrUrl = "https://mini.snowmeet.top/api/MediaHelper/ShowImageFromOfficialAccount?img="
                + Uri.EscapeDataString("show_wechat_temp_qrcode.aspx?scene=" + batch.share_scene);
            using HttpClient client = new HttpClient();
            string coverUrl = "https://mini.snowmeet.top" + cover.file_path_name.Trim();
            byte[] coverBytes = await client.GetByteArrayAsync(coverUrl);
            byte[] qrBytes = await client.GetByteArrayAsync(qrUrl);
            using Image poster = Image.Load(coverBytes);
            using Image qr = Image.Load(qrBytes);

            int referenceWidth = template.poster_width > 0 ? template.poster_width : 1080;
            int referenceHeight = template.poster_height > 0 ? template.poster_height : 1440;
            // 二维码必须保持正方形：位置可分别按横纵坐标映射，尺寸统一按横向比例缩放。
            double qrScale = poster.Width / (double)referenceWidth;
            int qrWidth = Math.Max(1, (int)Math.Round(template.qr_width * qrScale));
            int qrHeight = Math.Max(1, (int)Math.Round(template.qr_height * qrScale));
            int qrX = (int)Math.Round(template.qr_x * poster.Width / (double)referenceWidth);
            int qrY = (int)Math.Round(template.qr_y * poster.Height / (double)referenceHeight);
            if (qrX < 0 || qrY < 0 || qrX + qrWidth > poster.Width || qrY + qrHeight > poster.Height)
            {
                return BadRequest(new { code = 1, message = "二维码布局超出海报范围" });
            }
            qr.Mutate(x => x.Resize(qrWidth, qrHeight));
            poster.Mutate(x => x.DrawImage(qr,
                new SixLabors.ImageSharp.Point(qrX, qrY), 1f));

            string date = DateTime.Now.ToString("yyyyMMdd");
            string directory = Path.Combine(Util.workingPath, "wwwroot", "upload", date);
            Directory.CreateDirectory(directory);
            string fileName = "ticket_poster_" + batch.id + "_" + DateTime.Now.Ticks + ".png";
            string outputPath = Path.Combine(directory, fileName);
            await poster.SaveAsPngAsync(outputPath, new PngEncoder());
            return Ok(new ApiResult<object>()
            {
                code = 0,
                message = "",
                data = new { url = "https://mini.snowmeet.top/upload/" + date + "/" + fileName }
            });
        }
    }
}
