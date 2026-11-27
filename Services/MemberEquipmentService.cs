using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Storage;

namespace SnowmeetApi.Services
{
    public class MemberEquipmentView
    {
        public int id { get; set; }
        public int member_id { get; set; }
        public string equipment { get; set; } = "";
        public string? brand { get; set; }
        public string? scale { get; set; }
        public string? serials { get; set; }
        public int? image_id { get; set; }
        public string? photoUrl { get; set; }
        public string? photoFullUrl { get; set; }
        public DateTime? last_care_date { get; set; }
        public DateTime create_date { get; set; }
        public DateTime? update_date { get; set; }
    }

    public class MemberEquipmentService
    {
        public const int ExplicitNewSentinel = -1;

        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            "mp4", "mov", "m4v", "avi", "3gp", "webm", "mpeg", "mpg"
        };

        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;

        public MemberEquipmentService(ApplicationDBContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        private string BaseUrl => FileStorageOptions.From(_config).PublicBaseUrl;

        public static string? NormalizeEquipmentType(string? raw)
        {
            string s = (raw ?? "").Trim();
            return s == "双板" || s == "单板" ? s : null;
        }

        public static string? NormalizeText(string? raw)
        {
            string s = (raw ?? "").Trim();
            return s.Length == 0 ? null : s;
        }

        public static string? NormalizeSerials(string? raw)
        {
            string s = (raw ?? "").Trim();
            if (s.Length == 0 || s == "|") return null;
            if (s.Contains('|'))
            {
                string[] parts = s.Split('|', 2);
                string left = parts[0].Trim();
                string right = parts.Length > 1 ? parts[1].Trim() : "";
                if (left.Length == 0 && right.Length == 0) return null;
                return left + "|" + right;
            }
            return s;
        }

        public static bool IsRepairStage(string? title)
        {
            string t = (title ?? "").Trim();
            return t == "维修前" || t == "维修后";
        }

        public static bool IsVideoFile(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            string ext = UploadPaths.SafeExtension(filePath.Split('?')[0]);
            return VideoExtensions.Contains(ext);
        }

        public static string SummaryText(MemberEquipment e)
        {
            List<string> parts = new List<string> { e.equipment };
            if (!string.IsNullOrWhiteSpace(e.brand)) parts.Add(e.brand.Trim());
            if (!string.IsNullOrWhiteSpace(e.scale)) parts.Add(e.scale.Trim() + "cm");
            if (!string.IsNullOrWhiteSpace(e.serials)) parts.Add("序列号 " + e.serials.Trim());
            return string.Join(" · ", parts);
        }

        public async Task<int> CountActiveAsync(int memberId)
        {
            return await _db.memberEquipment.AsNoTracking()
                .CountAsync(e => e.member_id == memberId && e.valid == 1);
        }

        public async Task<List<MemberEquipmentView>> ListAsync(int memberId, string? equipment = null)
        {
            string? equipType = string.IsNullOrWhiteSpace(equipment) ? null : NormalizeEquipmentType(equipment);
            if (!string.IsNullOrWhiteSpace(equipment) && equipType == null)
            {
                return new List<MemberEquipmentView>();
            }

            IQueryable<MemberEquipment> q = _db.memberEquipment.AsNoTracking()
                .Include(e => e.image)
                .Where(e => e.member_id == memberId && e.valid == 1);

            if (equipType != null)
            {
                q = q.Where(e => e.equipment == equipType);
            }

            List<MemberEquipment> rows = await q.OrderByDescending(e => e.id).ToListAsync();
            if (rows.Count == 0) return new List<MemberEquipmentView>();

            // 关联查询该会员历次有效非取消养护单时间，兼容回填前的历史单（按类型+品牌+长度匹配）与新单（按 member_equipment_id 匹配）
            var careDates = await (
                from c in _db.care.AsNoTracking()
                join o in _db.order.AsNoTracking() on c.order_id equals o.id
                where o.member_id == memberId && o.valid == 1 && c.valid == 1 && !c.is_cancel
                select new
                {
                    c.member_equipment_id,
                    c.equipment,
                    c.brand,
                    c.scale,
                    c.create_date
                }
            ).ToListAsync();

            string baseUrl = BaseUrl;
            List<MemberEquipmentView> views = rows.Select(r =>
            {
                DateTime? byId = careDates
                    .Where(c => c.member_equipment_id == r.id)
                    .Select(c => (DateTime?)c.create_date)
                    .Max();

                DateTime? byLegacyMatch = null;
                if (!string.IsNullOrWhiteSpace(r.brand))
                {
                    string rBrand = r.brand.Trim();
                    string rScale = (r.scale ?? "").Trim();
                    byLegacyMatch = careDates
                        .Where(c => c.member_equipment_id == null
                            && (c.equipment ?? "").Trim() == r.equipment
                            && (c.brand ?? "").Trim() == rBrand
                            && (c.scale ?? "").Trim() == rScale)
                        .Select(c => (DateTime?)c.create_date)
                        .Max();
                }

                DateTime? lastCare = byId != null && byLegacyMatch != null
                    ? (byId > byLegacyMatch ? byId : byLegacyMatch)
                    : (byId ?? byLegacyMatch);

                return ToView(r, baseUrl, lastCare);
            })
            .OrderByDescending(v => v.last_care_date ?? v.create_date)
            .ThenByDescending(v => v.id)
            .ToList();

            return views;
        }

        public async Task<MemberEquipmentView> CreateByStaffAsync(
            int memberId, string? equipment, string? brand, string? scale, string? serials, int? imageId,
            int staffId, string scene = "会员装备管理新增", string? memo = null)
        {
            Member? member = await _db.member.AsNoTracking()
                .FirstOrDefaultAsync(m => m.id == memberId && m.valid == 1);
            if (member == null) throw new InvalidOperationException("会员不存在");
            if (member.is_merge == 1 || member.merge_id != null)
                throw new InvalidOperationException("该会员已被合并，请到合并后的会员名下操作");

            string? equipType = NormalizeEquipmentType(equipment);
            if (equipType == null) throw new InvalidOperationException("请选择装备类型（双板或单板）");

            string? normBrand = NormalizeText(brand);
            string? normScale = NormalizeText(scale);
            string? normSerials = NormalizeSerials(serials);
            int? validImageId = await ValidateImageIdAsync(imageId);

            DateTime now = DateTime.Now;
            MemberEquipment row = new MemberEquipment
            {
                member_id = memberId,
                equipment = equipType,
                brand = normBrand,
                scale = normScale,
                serials = normSerials,
                image_id = validImageId,
                source_care_id = null,
                valid = 1,
                create_date = now,
                update_date = null
            };

            await _db.memberEquipment.AddAsync(row);
            await _db.SaveChangesAsync();

            await _db.coreDataModLog.AddAsync(CoreDataModLog.CreateManualLog(
                MemberEquipmentHistoryRules.TABLE_NAME,
                "create",
                row.id,
                scene,
                memberId,
                staffId,
                null,
                SummaryText(row),
                memo));
            await _db.SaveChangesAsync();

            if (validImageId != null)
            {
                row.image = await _db.UploadFile.AsNoTracking().FirstOrDefaultAsync(u => u.id == validImageId.Value);
            }
            return ToView(row, BaseUrl, null);
        }

        public async Task<MemberEquipmentView> UpdateByStaffAsync(
            int id, string? equipment, string? brand, string? scale, string? serials, int? imageId,
            int staffId, string scene = "会员装备管理修改", string? memo = null)
        {
            MemberEquipment? row = await _db.memberEquipment.AsTracking()
                .FirstOrDefaultAsync(e => e.id == id && e.valid == 1);
            if (row == null) throw new InvalidOperationException("未找到该装备或已删除");

            string? equipType = NormalizeEquipmentType(equipment);
            if (equipType == null) throw new InvalidOperationException("请选择装备类型（双板或单板）");

            string? normBrand = NormalizeText(brand);
            string? normScale = NormalizeText(scale);
            string? normSerials = NormalizeSerials(serials);
            int? validImageId = await ValidateImageIdAsync(imageId);

            DateTime now = DateTime.Now;
            List<CoreDataModLog> logs = BuildUpdateLogs(
                row, equipType, normBrand, normScale, normSerials, validImageId,
                includeImage: true, staffId: staffId, scene: scene, memo: memo, now: now);

            if (logs.Count > 0)
            {
                row.equipment = equipType;
                row.brand = normBrand;
                row.scale = normScale;
                row.serials = normSerials;
                row.image_id = validImageId;
                row.update_date = now;
                _db.memberEquipment.Entry(row).State = EntityState.Modified;
                await _db.coreDataModLog.AddRangeAsync(logs);
                await _db.SaveChangesAsync();
            }

            if (row.image_id != null)
            {
                row.image = await _db.UploadFile.AsNoTracking().FirstOrDefaultAsync(u => u.id == row.image_id.Value);
            }
            return ToView(row, BaseUrl, null);
        }

        public async Task DeleteByStaffAsync(int id, int staffId, string scene = "会员装备管理删除")
        {
            MemberEquipment? row = await _db.memberEquipment.AsTracking()
                .FirstOrDefaultAsync(e => e.id == id && e.valid == 1);
            if (row == null) throw new InvalidOperationException("未找到该装备或已删除");

            DateTime now = DateTime.Now;
            row.valid = 0;
            row.update_date = now;
            _db.memberEquipment.Entry(row).State = EntityState.Modified;

            CoreDataModLog log = CoreDataModLog.CreateManualLog(
                MemberEquipmentHistoryRules.TABLE_NAME,
                "valid",
                row.id,
                scene,
                row.member_id,
                staffId,
                "1",
                "0",
                SummaryText(row));
            log.create_date = now;
            await _db.coreDataModLog.AddAsync(log);
            await _db.SaveChangesAsync();
        }

        public async Task<List<MemberEquipmentHistoryItem>> GetHistoryAsync(int id)
        {
            bool exists = await _db.memberEquipment.AsNoTracking().AnyAsync(e => e.id == id);
            if (!exists) throw new InvalidOperationException("未找到该装备");

            List<CoreDataModLog> logs = await _db.coreDataModLog.AsNoTracking()
                .Include(l => l.staff)
                .Where(l => l.key_value == id && l.table_name == MemberEquipmentHistoryRules.TABLE_NAME)
                .ToListAsync();

            HashSet<int> imageIds = new HashSet<int>();
            foreach (CoreDataModLog log in logs.Where(l => l.field_name == "image_id"))
            {
                if (int.TryParse((log.prev_value ?? "").Trim(), out int pId) && pId > 0) imageIds.Add(pId);
                if (int.TryParse((log.current_value ?? "").Trim(), out int cId) && cId > 0) imageIds.Add(cId);
            }

            Dictionary<int, string> photoUrlById = new Dictionary<int, string>();
            if (imageIds.Count > 0)
            {
                string baseUrl = BaseUrl;
                var files = await _db.UploadFile.AsNoTracking()
                    .Where(u => imageIds.Contains(u.id))
                    .ToListAsync();
                foreach (UploadFile f in files)
                {
                    string rel = !string.IsNullOrWhiteSpace(f.thumbUrl) ? f.thumbUrl : (f.file_path_name ?? "");
                    if (!string.IsNullOrWhiteSpace(rel))
                    {
                        photoUrlById[f.id] = UploadPaths.PublicUrl(baseUrl, rel);
                    }
                }
            }

            return MemberEquipmentHistoryRules.Build(logs, photoUrlById);
        }

        /// <summary>
        /// 养护下单（PlaceCareOrder / CareCardSale.Submit / 散客后绑会员 EffectCareOrder）同步会员装备列表：
        ///   · care.member_equipment_id &gt; 0：关联已有装备；若品牌/长度/序列号/类型发生变更，更新该装备并写逐字段审计历史（不覆盖首次照片）；
        ///   · care.member_equipment_id == -1（新版开单明确录入新装备）：取本次开单首张普通装备照片新建装备并回填 care.member_equipment_id；
        ///   · care.member_equipment_id == null（旧版小程序兼容或散客后绑会员）：若该会员已有同类型+同品牌+同长度装备则直接关联，否则新建并关联。
        /// </summary>
        public async Task SyncOrderEquipmentsAsync(
            Models.Order? order, IList<Care>? cares, int? staffId, string scenePrefix = "养护开单")
        {
            if (order == null || order.member_id == null || cares == null || cares.Count == 0)
            {
                return;
            }

            int memberId = order.member_id.Value;
            List<Care> activeCares = cares.Where(c => c != null && !c.is_cancel).ToList();
            if (activeCares.Count == 0) return;

            List<int> careIds = activeCares.Where(c => c.id > 0).Select(c => c.id).ToList();
            Dictionary<int, int?> firstPhotoByCareId = await LoadFirstEquipmentPhotoIdsAsync(careIds);

            string orderMemo = !string.IsNullOrWhiteSpace(order.code)
                ? ("关联养护单 " + order.code.Trim())
                : (order.id > 0 ? ("关联养护单 #" + order.id) : null) ?? "";
            string? memoOrNull = orderMemo == "" ? null : orderMemo;

            foreach (Care care in activeCares)
            {
                string? equipType = NormalizeEquipmentType(care.equipment);
                if (equipType == null) continue;

                string? normBrand = NormalizeText(care.brand);
                string? normScale = NormalizeText(care.scale);
                string? normSerials = NormalizeSerials(care.serials);
                int? firstPhotoId = ResolveFirstPhotoId(care, firstPhotoByCareId);

                int? refId = care.member_equipment_id;
                if (refId != null && refId > 0)
                {
                    MemberEquipment? existing = await _db.memberEquipment.AsTracking()
                        .FirstOrDefaultAsync(e => e.id == refId.Value && e.member_id == memberId && e.valid == 1);

                    if (existing != null)
                    {
                        DateTime now = DateTime.Now;
                        List<CoreDataModLog> logs = BuildUpdateLogs(
                            existing, equipType, normBrand, normScale, normSerials, existing.image_id,
                            includeImage: false, staffId: staffId,
                            scene: scenePrefix + "修改装备", memo: memoOrNull, now: now);

                        if (logs.Count > 0)
                        {
                            existing.equipment = equipType;
                            existing.brand = normBrand;
                            existing.scale = normScale;
                            existing.serials = normSerials;
                            existing.update_date = now;
                            _db.memberEquipment.Entry(existing).State = EntityState.Modified;
                            await _db.coreDataModLog.AddRangeAsync(logs);
                            await _db.SaveChangesAsync();
                        }
                        continue;
                    }
                    // 原选装备已被删除或不属于当前会员（如中途换了会员残留）：降级按新装备入库
                }
                else if (refId == null && normBrand != null)
                {
                    // 旧版客户端未传 member_equipment_id（或散客单付款后绑定会员）：同类型+品牌+长度已有记录则直接关联，避免重复建档
                    string scaleKey = normScale ?? "";
                    MemberEquipment? matched = await _db.memberEquipment.AsNoTracking()
                        .Where(e => e.member_id == memberId && e.valid == 1
                            && e.equipment == equipType
                            && e.brand == normBrand
                            && (e.scale ?? "") == scaleKey)
                        .OrderByDescending(e => e.id)
                        .FirstOrDefaultAsync();

                    if (matched != null)
                    {
                        await UpdateCareEquipmentLinkAsync(care, matched.id);
                        continue;
                    }
                }

                // 新建装备（首次开单时的照片作为装备照片）
                DateTime createdNow = DateTime.Now;
                MemberEquipment created = new MemberEquipment
                {
                    member_id = memberId,
                    equipment = equipType,
                    brand = normBrand,
                    scale = normScale,
                    serials = normSerials,
                    image_id = firstPhotoId,
                    source_care_id = care.id > 0 ? care.id : null,
                    valid = 1,
                    create_date = createdNow,
                    update_date = null
                };
                await _db.memberEquipment.AddAsync(created);
                await _db.SaveChangesAsync();

                CoreDataModLog createLog = CoreDataModLog.CreateManualLog(
                    MemberEquipmentHistoryRules.TABLE_NAME,
                    "create",
                    created.id,
                    scenePrefix + "新增装备",
                    memberId,
                    staffId,
                    null,
                    SummaryText(created),
                    memoOrNull);
                createLog.create_date = createdNow;
                await _db.coreDataModLog.AddAsync(createLog);
                await _db.SaveChangesAsync();

                await UpdateCareEquipmentLinkAsync(care, created.id);
            }
        }

        private async Task UpdateCareEquipmentLinkAsync(Care care, int equipmentId)
        {
            care.member_equipment_id = equipmentId;
            if (care.id <= 0) return;

            Care? tracked = _db.care.Local.FirstOrDefault(c => c.id == care.id);
            if (tracked != null)
            {
                tracked.member_equipment_id = equipmentId;
                if (_db.Entry(tracked).State == EntityState.Unchanged)
                {
                    _db.Entry(tracked).State = EntityState.Modified;
                }
                await _db.SaveChangesAsync();
                return;
            }

            Care? dbCare = await _db.care.AsTracking().FirstOrDefaultAsync(c => c.id == care.id);
            if (dbCare != null)
            {
                dbCare.member_equipment_id = equipmentId;
                _db.care.Entry(dbCare).State = EntityState.Modified;
                await _db.SaveChangesAsync();
                _db.Entry(dbCare).State = EntityState.Detached;
            }
        }

        private async Task<Dictionary<int, int?>> LoadFirstEquipmentPhotoIdsAsync(List<int> careIds)
        {
            Dictionary<int, int?> result = new Dictionary<int, int?>();
            if (careIds.Count == 0) return result;

            var images = await _db.careImage.AsNoTracking()
                .Include(ci => ci.image)
                .Where(ci => careIds.Contains(ci.care_id) && ci.valid)
                .OrderBy(ci => ci.id)
                .ToListAsync();

            foreach (var group in images.GroupBy(ci => ci.care_id))
            {
                var hit = group.FirstOrDefault(ci =>
                    ci.image_id > 0
                    && !IsRepairStage(ci.title)
                    && (ci.image == null || (!string.IsNullOrWhiteSpace(ci.image.file_path_name) && !IsVideoFile(ci.image.file_path_name))));
                result[group.Key] = hit?.image_id;
            }
            return result;
        }

        private static int? ResolveFirstPhotoId(Care care, Dictionary<int, int?> firstPhotoByCareId)
        {
            if (care.id > 0 && firstPhotoByCareId.TryGetValue(care.id, out int? dbPhotoId) && dbPhotoId != null)
            {
                return dbPhotoId;
            }
            if (care.careImages != null)
            {
                var memHit = care.careImages.FirstOrDefault(ci =>
                    ci != null && ci.valid && ci.image_id > 0 && !IsRepairStage(ci.title)
                    && (ci.image == null || !IsVideoFile(ci.image.file_path_name)));
                if (memHit != null) return memHit.image_id;
            }
            return null;
        }

        private async Task<int?> ValidateImageIdAsync(int? imageId)
        {
            if (imageId == null || imageId <= 0) return null;
            bool exists = await _db.UploadFile.AsNoTracking().AnyAsync(u => u.id == imageId.Value);
            if (!exists) throw new InvalidOperationException("所选照片不存在，请重新上传");
            return imageId.Value;
        }

        private static List<CoreDataModLog> BuildUpdateLogs(
            MemberEquipment current,
            string newEquipment,
            string? newBrand,
            string? newScale,
            string? newSerials,
            int? newImageId,
            bool includeImage,
            int? staffId,
            string scene,
            string? memo,
            DateTime now)
        {
            List<CoreDataModLog> logs = new List<CoreDataModLog>();

            void CheckField(string field, string? prev, string? next)
            {
                string p = prev ?? "";
                string n = next ?? "";
                if (p == n) return;
                CoreDataModLog log = CoreDataModLog.CreateManualLog(
                    MemberEquipmentHistoryRules.TABLE_NAME,
                    field,
                    current.id,
                    scene,
                    current.member_id,
                    staffId,
                    prev,
                    next,
                    memo);
                log.create_date = now;
                logs.Add(log);
            }

            CheckField("equipment", NormalizeEquipmentType(current.equipment) ?? current.equipment.Trim(), newEquipment);
            CheckField("brand", NormalizeText(current.brand), newBrand);
            CheckField("scale", NormalizeText(current.scale), newScale);
            CheckField("serials", NormalizeSerials(current.serials), newSerials);
            if (includeImage)
            {
                CheckField("image_id", current.image_id?.ToString(), newImageId?.ToString());
            }

            return logs;
        }

        private static MemberEquipmentView ToView(MemberEquipment e, string baseUrl, DateTime? lastCareDate)
        {
            string? photoUrl = null;
            string? photoFullUrl = null;
            if (e.image != null && !string.IsNullOrWhiteSpace(e.image.file_path_name))
            {
                string thumbRel = !string.IsNullOrWhiteSpace(e.image.thumbUrl) ? e.image.thumbUrl : e.image.file_path_name.Trim();
                photoUrl = UploadPaths.PublicUrl(baseUrl, thumbRel);
                photoFullUrl = UploadPaths.PublicUrl(baseUrl, e.image.file_path_name.Trim());
            }

            return new MemberEquipmentView
            {
                id = e.id,
                member_id = e.member_id,
                equipment = e.equipment,
                brand = e.brand,
                scale = e.scale,
                serials = e.serials,
                image_id = e.image_id,
                photoUrl = photoUrl,
                photoFullUrl = photoFullUrl,
                last_care_date = lastCareDate,
                create_date = e.create_date,
                update_date = e.update_date
            };
        }
    }
}
