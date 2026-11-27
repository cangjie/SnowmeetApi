using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services;

namespace SnowmeetApi.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class MemberEquipmentController : ControllerBase
    {
        private const int STAFF_LEVEL = 100;

        private readonly ApplicationDBContext _db;
        private readonly MemberEquipmentService _service;

        public MemberEquipmentController(ApplicationDBContext db, IConfiguration config)
        {
            _db = db;
            _service = new MemberEquipmentService(db, config);
        }

        private async Task<Staff?> AuthorizeStaffAsync(string sessionKey, string sessionType)
        {
            Staff? staff = await Util.GetStaffBySessionKey(_db, Util.UrlDecode(sessionKey ?? ""), sessionType);
            return staff != null && staff.title_level >= STAFF_LEVEL ? staff : null;
        }

        public class SaveMemberEquipmentRequest
        {
            public int id { get; set; }
            public int memberId { get; set; }
            public string? equipment { get; set; }
            public string? brand { get; set; }
            public string? scale { get; set; }
            public string? serials { get; set; }
            public int? imageId { get; set; }
        }

        public class DeleteMemberEquipmentRequest
        {
            public int id { get; set; }
        }

        [HttpGet]
        public async Task<ActionResult<ApiResult<List<MemberEquipmentView>?>>> ListByStaff(
            int memberId, string? equipment = null, string sessionKey = "", string sessionType = "wechat_mini_openid")
        {
            Staff? staff = await AuthorizeStaffAsync(sessionKey, sessionType);
            if (staff == null)
            {
                return Ok(new ApiResult<List<MemberEquipmentView>?> { code = 1, message = "没有权限", data = null });
            }
            string? decodedEquip = equipment == null ? null : Util.UrlDecode(equipment);
            List<MemberEquipmentView> list = await _service.ListAsync(memberId, decodedEquip);
            return Ok(new ApiResult<List<MemberEquipmentView>?> { code = 0, message = "", data = list });
        }

        [HttpPost]
        public async Task<ActionResult<ApiResult<MemberEquipmentView?>>> CreateByStaff(
            [FromBody] SaveMemberEquipmentRequest? req,
            [FromQuery] string sessionKey = "", [FromQuery] string sessionType = "wechat_mini_openid")
        {
            Staff? staff = await AuthorizeStaffAsync(sessionKey, sessionType);
            if (staff == null)
            {
                return Ok(new ApiResult<MemberEquipmentView?> { code = 1, message = "没有权限", data = null });
            }
            if (req == null || req.memberId <= 0)
            {
                return Ok(new ApiResult<MemberEquipmentView?> { code = 1, message = "参数为空", data = null });
            }
            try
            {
                MemberEquipmentView created = await _service.CreateByStaffAsync(
                    req.memberId, req.equipment, req.brand, req.scale, req.serials, req.imageId, staff.id);
                return Ok(new ApiResult<MemberEquipmentView?> { code = 0, message = "", data = created });
            }
            catch (InvalidOperationException ex)
            {
                return Ok(new ApiResult<MemberEquipmentView?> { code = 1, message = ex.Message, data = null });
            }
        }

        [HttpPost]
        public async Task<ActionResult<ApiResult<MemberEquipmentView?>>> UpdateByStaff(
            [FromBody] SaveMemberEquipmentRequest? req,
            [FromQuery] string sessionKey = "", [FromQuery] string sessionType = "wechat_mini_openid")
        {
            Staff? staff = await AuthorizeStaffAsync(sessionKey, sessionType);
            if (staff == null)
            {
                return Ok(new ApiResult<MemberEquipmentView?> { code = 1, message = "没有权限", data = null });
            }
            if (req == null || req.id <= 0)
            {
                return Ok(new ApiResult<MemberEquipmentView?> { code = 1, message = "参数为空", data = null });
            }
            try
            {
                MemberEquipmentView updated = await _service.UpdateByStaffAsync(
                    req.id, req.equipment, req.brand, req.scale, req.serials, req.imageId, staff.id);
                return Ok(new ApiResult<MemberEquipmentView?> { code = 0, message = "", data = updated });
            }
            catch (InvalidOperationException ex)
            {
                return Ok(new ApiResult<MemberEquipmentView?> { code = 1, message = ex.Message, data = null });
            }
        }

        [HttpPost]
        public async Task<ActionResult<ApiResult<object?>>> DeleteByStaff(
            [FromBody] DeleteMemberEquipmentRequest? req,
            [FromQuery] string sessionKey = "", [FromQuery] string sessionType = "wechat_mini_openid")
        {
            Staff? staff = await AuthorizeStaffAsync(sessionKey, sessionType);
            if (staff == null)
            {
                return Ok(new ApiResult<object?> { code = 1, message = "没有权限", data = null });
            }
            if (req == null || req.id <= 0)
            {
                return Ok(new ApiResult<object?> { code = 1, message = "参数为空", data = null });
            }
            try
            {
                await _service.DeleteByStaffAsync(req.id, staff.id);
                return Ok(new ApiResult<object?> { code = 0, message = "", data = new { id = req.id } });
            }
            catch (InvalidOperationException ex)
            {
                return Ok(new ApiResult<object?> { code = 1, message = ex.Message, data = null });
            }
        }

        [HttpGet]
        public async Task<ActionResult<ApiResult<List<MemberEquipmentHistoryItem>?>>> GetHistoryByStaff(
            int id, string sessionKey = "", string sessionType = "wechat_mini_openid")
        {
            Staff? staff = await AuthorizeStaffAsync(sessionKey, sessionType);
            if (staff == null)
            {
                return Ok(new ApiResult<List<MemberEquipmentHistoryItem>?> { code = 1, message = "没有权限", data = null });
            }
            try
            {
                List<MemberEquipmentHistoryItem> history = await _service.GetHistoryAsync(id);
                return Ok(new ApiResult<List<MemberEquipmentHistoryItem>?> { code = 0, message = "", data = history });
            }
            catch (InvalidOperationException ex)
            {
                return Ok(new ApiResult<List<MemberEquipmentHistoryItem>?> { code = 1, message = ex.Message, data = null });
            }
        }
    }
}
