using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Tickets;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace SnowmeetApi.Controllers
{
    public partial class TicketPosterController
    {
        [HttpGet]
        public async Task<IActionResult> GetMiniQrCode(int batchId, string sessionKey,
            [FromServices] IMiniTicketQrService qrService,
            string sessionType = "wechat_mini_openid", string envVersion = "release")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, Util.UrlDecode(sessionKey ?? ""), sessionType);
            if (staff == null || staff.title_level < 200) return BadRequest(new { code = 1, message = "没有权限" });
            try { return File(await qrService.GetAsync(batchId, envVersion), "image/png"); }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is IOException || e is HttpRequestException)
            { return BadRequest(new { code = 1, message = e.Message }); }
        }

        [HttpGet]
        public async Task<IActionResult> GenerateMini(int batchId, string sessionKey,
            [FromServices] Services.Storage.IFileStorage storage,
            [FromServices] IMiniTicketQrService qrService,
            string sessionType = "wechat_mini_openid", string envVersion = "release")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, Util.UrlDecode(sessionKey), sessionType);
            if (staff == null || staff.title_level < 200)
            {
                return BadRequest(new { code = 1, message = "没有权限" });
            }

            TicketShareBatch batch = await _db.ticketShareBatch.AsNoTracking()
                .FirstOrDefaultAsync(x => x.id == batchId && x.valid == 1);
            if (batch == null || !MiniTicketRules.IsMini(batch.share_type))
            {
                return BadRequest(new { code = 1, message = "分享批次不存在" });
            }
            TicketTemplate template = await _db.ticketTemplate.AsNoTracking()
                .FirstOrDefaultAsync(x => x.id == batch.template_id);
            if (template == null || template.valid != 1 || template.cover_upload_id == null)
            {
                return BadRequest(new { code = 1, message = "模板海报不存在" });
            }
            UploadFile cover = await _db.UploadFile.AsNoTracking()
                .FirstOrDefaultAsync(x => x.id == template.cover_upload_id);
            if (cover == null || string.IsNullOrWhiteSpace(cover.file_path_name))
            {
                return BadRequest(new { code = 1, message = "模板海报文件不存在" });
            }

            // 封面直接从存储读（S3，过渡期回退本机磁盘），不再绕公网回源自己
            byte[]? coverBytes = await storage.ReadAsync(cover.file_path_name.Trim(), cover.is_web == 1);
            if (coverBytes == null)
            {
                return BadRequest(new { code = 1, message = "模板海报文件不存在" });
            }
            byte[] qrBytes;
            try { qrBytes = await qrService.GetAsync(batchId, envVersion); }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is IOException || e is HttpRequestException)
            { return BadRequest(new { code = 1, message = e.Message }); }
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

            using MemoryStream png = new MemoryStream();
            await poster.SaveAsPngAsync(png, new PngEncoder());
            png.Position = 0;
            string relativePath = await storage.SaveAsync(png, "poster.png", true, "ticket_poster_" + batch.id + "_");
            return Ok(new ApiResult<object>()
            {
                code = 0,
                message = "",
                data = new { url = storage.PublicUrl(relativePath) }
            });
        }
    }
}
