using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Fnb;

namespace SnowmeetApi.Controllers.Fnb
{
    /// <summary>
    /// 美团管家采集程序（门店电脑上的 Tools/meituan_collector）上报心跳和状态。
    /// 鉴权：请求头 X-Collector-Token，与服务器工作目录 config.meituanCollectorToken 的内容一致才接受。
    /// 企业微信提醒由 MeituanCollectorMonitor 在服务器上发出（企业微信接口有 IP 白名单）。
    /// </summary>
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class MeituanCollectorController : ControllerBase
    {
        readonly MeituanCollectorMonitor _monitor;

        public MeituanCollectorController(MeituanCollectorMonitor monitor)
        {
            _monitor = monitor;
        }

        public static bool TokenMatches(string? expected, string? given)
        {
            if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(given)) return false;
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(given));
        }

        bool Authorized() => TokenMatches(MeituanCollectorMonitor.ReadToken(), Request.Headers["X-Collector-Token"].ToString());

        [HttpPost]
        public async Task<ActionResult<ApiResult<object>>> Heartbeat([FromBody] MeituanCollectorMonitor.Heartbeat body)
        {
            if (!Authorized()) return Unauthorized();
            var sent = await _monitor.OnHeartbeat(body);
            return Ok(new ApiResult<object> { code = 0, message = "", data = new { notified = sent.Count } });
        }

        [HttpGet]
        public ActionResult<ApiResult<object>> Status()
        {
            if (!Authorized()) return Unauthorized();
            return Ok(new ApiResult<object> { code = 0, message = "", data = _monitor.Current });
        }
    }
}
