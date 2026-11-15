using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SnowmeetApi.Controllers.Fnb;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Ops;

namespace SnowmeetApi.Controllers.Ops
{
    /// <summary>
    /// 数据库备份脚本（Tools/db_backup）出问题时调用，由服务器给企业微信发提醒。
    /// 鉴权：请求头 X-Backup-Token，与服务器工作目录 config.backupAlertToken 的内容一致才接受；
    /// 没有配置令牌时一律拒绝。
    /// </summary>
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class BackupAlertController : ControllerBase
    {
        readonly BackupAlertService _service;

        public BackupAlertController(BackupAlertService service)
        {
            _service = service;
        }

        [HttpPost]
        public async Task<ActionResult<ApiResult<object>>> Notify([FromBody] BackupAlertService.Alert body)
        {
            if (!MeituanCollectorController.TokenMatches(BackupAlertService.ReadToken(), Request.Headers["X-Backup-Token"].ToString()))
                return Unauthorized();
            string reason = await _service.Notify(body?.level, body?.message, BackupAlertService.ReadReceivers());
            bool benign = reason == "sent" || reason == "duplicate";
            return Ok(new ApiResult<object> { code = benign ? 0 : 1, message = reason, data = null });
        }
    }
}
