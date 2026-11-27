using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services;

namespace SnowmeetApi.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class InternalCareController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        public InternalCareController(ApplicationDBContext db) { _db = db; }

        public class EquipmentInput
        {
            public string? equipment { get; set; }
            public string? serials { get; set; }
            public string? brand { get; set; }
            public string? scale { get; set; }
            public bool edge { get; set; }
            public string? edgeDegree { get; set; }
            public bool wax { get; set; }
            public bool machineWax { get; set; }
            public bool maintainBoard { get; set; }
            public bool unwax { get; set; }
            public bool repair { get; set; }
            public string? repairMemo { get; set; }
            public List<int> imageIds { get; set; } = new();
        }
        public class CreateRequest
        {
            public string? requestId { get; set; }
            public string? shop { get; set; }
            public List<EquipmentInput> equipment { get; set; } = new();
        }

        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> List(string sessionKey, int page = 1, string sessionType = "wechat_mini_openid")
        {
            var staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null || staff.title_level < 100) return Ok(new ApiResult<object> { code = 1, message = "没有权限", data = null });
            var query = _db.care.AsNoTracking().Where(c => c.valid == 1 && c.order != null
                    && c.order.valid == 1 && c.order.type == InternalCareRules.OrderType);
            var total = await query.CountAsync();
            var completed = await query.CountAsync(c => c.tasks.Any(t => t.valid == 1)
                && !c.tasks.Any(t => t.valid == 1 && t.status != "已完成"));
            var staffStats = await _db.careTask.AsNoTracking().Where(t => t.valid == 1 && t.status == "已完成"
                && t.task_name != "安全检查" && t.care.valid == 1 && t.care.order != null
                && t.care.order.valid == 1 && t.care.order.type == InternalCareRules.OrderType)
                .GroupBy(t => new { t.staff_id, name = t.staff == null ? null : t.staff.name, t.task_name })
                .Select(g => new { staff = g.Key.name, task = g.Key.task_name, count = g.Count() }).ToListAsync();
            var cares = await query
                .Include(c => c.order).Include(c => c.tasks.Where(t => t.valid == 1)).ThenInclude(t => t.staff)
                .OrderByDescending(c => c.id).Skip((Math.Max(page, 1) - 1) * 30).Take(30).AsSplitQuery().ToListAsync();
            var items = cares.Select(c => new { c.id, c.order_id, c.task_flow_code, c.equipment, c.serials, c.brand,
                c.scale, c.create_date, shop = c.order?.shop, c.status,
                tasks = c.tasks.OrderBy(t => t.sort).Select(t => new { t.task_name, t.status, t.end_time, staffName = t.staff?.name }) }).ToList();
            return Ok(new ApiResult<object> { code = 0, message = "", data = new { items, total, completed, staffStats } });
        }

        [HttpPost]
        public async Task<ActionResult<ApiResult<object>>> Create([FromBody] CreateRequest input, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            var staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            ApiResult<object> Fail(string message) => new() { code = 1, message = message, data = null };
            if (staff == null || staff.title_level < 100) return Ok(Fail("没有权限"));
            if (input == null || !Guid.TryParse(input.requestId, out _) || string.IsNullOrWhiteSpace(input.shop)
                || input.equipment == null || input.equipment.Count == 0 || input.equipment.Count > 50)
                return Ok(Fail("请填写门店并添加装备"));
            if (!await _db.shop.AsNoTracking().AnyAsync(s => s.name == input.shop && s.care == 1))
                return Ok(Fail("请选择支持养护的门店"));
            foreach (var e in input.equipment)
            {
                if (e == null || (e.equipment != "单板" && e.equipment != "双板") || string.IsNullOrWhiteSpace(e.serials)
                    || string.IsNullOrWhiteSpace(e.brand) || string.IsNullOrWhiteSpace(e.scale)
                    || !(e.edge || e.wax || e.machineWax || e.maintainBoard || e.unwax || e.repair) || (e.repair && string.IsNullOrWhiteSpace(e.repairMemo))
                    || e.imageIds == null || e.imageIds.Count == 0)
                    return Ok(Fail("每件装备均需填写类型、编号、品牌、长度、项目和照片"));
                if ((e.wax ? 1 : 0) + (e.machineWax ? 1 : 0) + (e.maintainBoard ? 1 : 0) > 1)
                    return Ok(Fail("热蜡、机打蜡和养板只能选择一项"));
                if (!decimal.TryParse(e.scale, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal length) || length <= 0 || length > 300)
                    return Ok(Fail("请输入有效的装备长度"));
                if (e.edge && (!decimal.TryParse(e.edgeDegree ?? "89", NumberStyles.Number, CultureInfo.InvariantCulture, out decimal degree)
                    || degree < 0 || degree > 90 || decimal.Round(degree, 1) != degree))
                    return Ok(Fail("修刃角度应为 0 到 90，最多一位小数"));
            }
            var imageIds = input.equipment.SelectMany(e => e.imageIds).Distinct().ToList();
            if (imageIds.Any(i => i <= 0) || imageIds.Count != input.equipment.Sum(e => e.imageIds.Count)
                || await _db.UploadFile.CountAsync(f => imageIds.Contains(f.id) && f.staff_id == staff.id && f.purpose == "内部养护开单") != imageIds.Count)
                return Ok(Fail("照片未上传成功，请重新上传"));
            await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            await CareCardSaleService.LockAsync(_db, "internal-care:" + input.requestId);
            var previous = await _db.order.AsNoTracking().FirstOrDefaultAsync(o => o.type == InternalCareRules.OrderType && o.source_order_no == input.requestId);
            if (previous != null) { await tx.CommitAsync(); return Ok(new ApiResult<object> { code = 0, data = new { orderId = previous.id }, message = "" }); }
            var now = DateTime.Now;
            var order = new Models.Order { type = InternalCareRules.OrderType, order_source = "internal_care", source_order_no = input.requestId,
                shop = input.shop.Trim(), staff_id = staff.id, valid = 1, dealed = 1, biz_date = now, create_date = now };
            _db.order.Add(order);
            await _db.SaveChangesAsync();
            order.code = "NB_" + now.ToString("yyMMdd") + "_" + order.id;
            foreach (var e in input.equipment)
            {
                var care = new Care { order_id = order.id, biz_type = InternalCareRules.OrderType, equipment = e.equipment!,
                    serials = e.serials!.Trim(), brand = e.brand!.Trim(), scale = e.scale!.Trim(), valid = 1,
                    need_edge = e.edge ? 1 : 0, edge_degree = e.edge ? (string.IsNullOrWhiteSpace(e.edgeDegree) ? "89" : e.edgeDegree.Trim()) : null,
                    need_wax = e.wax ? 1 : 0, free_wax = e.machineWax ? 1 : 0, maintain_board = e.maintainBoard ? 1 : 0,
                    need_unwax = e.unwax ? 1 : 0, need_repair = e.repair ? 1 : 0,
                    repair_memo = e.repair ? e.repairMemo!.Trim() : null, create_date = now };
                _db.care.Add(care);
                await _db.SaveChangesAsync();
                care.task_flow_code = "NB-" + now.ToString("yyMMdd") + "-" + care.id;
                foreach (int id in e.imageIds.Distinct()) _db.careImage.Add(new CareImage { care_id = care.id, image_id = id, title = "开单照片" });
                void Step(string name, int sort, string memo = "") => _db.careTask.Add(new CareTask { care_id = care.id, task_name = name, sort = sort, memo = memo, create_date = now });
                Step("安全检查", 10);
                if (e.edge) Step("修刃", 20, care.edge_degree ?? "89");
                if (e.repair) Step("维修", 30, care.repair_memo ?? "");
                if (e.wax) Step("热蜡", 40);
                if (e.machineWax) Step("机打蜡", 40);
                if (e.maintainBoard) for (int i = 0; i < CareController.CareMaintainBoardSteps.Length; i++) Step(CareController.CareMaintainBoardSteps[i], 40 + i);
                if (e.unwax) Step("刮蜡", 50);
            }
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(new ApiResult<object> { code = 0, data = new { orderId = order.id }, message = "" });
        }
    }
}
