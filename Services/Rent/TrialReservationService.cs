using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.CoachPlatform;
using SnowmeetApi.Services.Tickets;

namespace SnowmeetApi.Services.Rent;

public sealed class TrialReservationService(ApplicationDBContext db, IConfiguration config)
{
    public async Task<bool> EligibleAsync(int memberId, DateTime now, int? introducerId = null)
    {
        return await (from board in db.trialBoard.AsNoTracking()
                      join intro in db.introducer.AsNoTracking() on board.introducer_id equals intro.id
                      join card in db.coachCard.AsNoTracking() on intro.coach_id equals card.coach_id
                      where board.valid == 1 && intro.valid == 1 && intro.type == "机构"
                          && (introducerId == null || intro.id == introducerId)
                          && card.member_id == memberId && card.status == CoachCard.StatusClaimed
                          && (card.start_date == null || card.start_date <= now)
                          && (card.end_date == null || card.end_date >= now)
                      select card.id).AnyAsync();
    }

    public async Task<bool> RentalConflictAsync(RentProduct product, DateTime day)
    {
        var barcode = product.barcode?.Trim();
        var candidates = await (from item in db.rentItem.AsNoTracking()
                                join rental in db.rental.AsNoTracking() on item.rental_id equals rental.id
                                join order in db.order.AsNoTracking() on rental.order_id equals order.id
                                where order.valid == 1 && rental.valid == 1 && rental.appending != true && !item.noNeed
                                    && (item.rent_product_id == product.id ||
                                        (item.code != null && barcode != null && item.code.Trim() == barcode))
                                    && (item.atOnce || rental.start_date == null || rental.start_date < day.Date.AddDays(1))
                                select item.id).ToListAsync();
        if (candidates.Count == 0) return false;
        var logs = await db.rentItemLog.AsNoTracking().Where(l => candidates.Contains(l.rent_item_id) && l.valid == 1)
            .OrderByDescending(l => l.id).ToListAsync();
        return candidates.Any(id =>
        {
            var last = logs.FirstOrDefault(l => l.rent_item_id == id);
            return last == null || (last.status.Trim() != "已归还" && last.status.Trim() != "已更换");
        });
    }

    public async Task<bool> BoardUnavailableAsync(int productId, DateTime day, int? exceptReservation = null)
    {
        day = day.Date;
        var product = await db.rentProduct.AsNoTracking().FirstOrDefaultAsync(p => p.id == productId && p.valid == 1);
        if (product == null || (!string.IsNullOrWhiteSpace(product.status) && product.status.Trim() != "正常"))
            return true;
        if (await db.rentProductUnavailability.AsNoTracking().AnyAsync(x => x.valid == 1 && x.rent_product_id == productId
            && (x.start_time == null || x.start_time < day.AddDays(1)) && (x.end_time == null || x.end_time > day)))
            return true;
        if (await db.trialReservation.AsNoTracking().AnyAsync(r => r.rent_product_id == productId && r.id != exceptReservation
            && (r.status == TrialReservation.InUse || (r.blocks_day == 1 && r.reservation_date == day))))
            return true;
        return await RentalConflictAsync(product, day);
    }

    public async Task<bool> BlocksRentalAsync(int productId, DateTime start)
    {
        return await db.trialReservation.AsNoTracking().AnyAsync(r => r.rent_product_id == productId
            && (r.status == TrialReservation.InUse ||
                (r.status == TrialReservation.Pending && r.reservation_date >= start.Date && r.reservation_date >= DateTime.Today)));
    }

    public Task<TrialReservation> ReserveAsync(int memberId, int productId, DateTime date, DateTime now) =>
        MiniTicketTransaction.Run(db, async () =>
        {
            var day = date.Date;
            if (day < now.Date || day > CoachCardRules.SeasonEnd(now, config).Date)
                throw new CoachException("只能预约本雪季内当天或之后的日期");
            var board = await db.trialBoard.AsNoTracking().FirstOrDefaultAsync(b => b.rent_product_id == productId && b.valid == 1);
            if (board == null) throw new CoachException("该单板不支持试滑预约");
            if (!await EligibleAsync(memberId, now, board.introducer_id)) throw new CoachException("仅武士团会籍会员可以预约");
            if (await db.trialReservation.AsNoTracking().AnyAsync(r => r.member_id == memberId && r.reservation_date == day && r.counts_for_day == 1))
                throw new CoachException("每天只能预约一块单板");
            if (await BoardUnavailableAsync(productId, day)) throw new CoachException("该单板在所选日期暂不可用");
            var reservation = new TrialReservation { member_id = memberId, rent_product_id = productId,
                introducer_id = board.introducer_id, reservation_date = day, create_date = now };
            db.trialReservation.Add(reservation);
            await db.SaveChangesAsync();
            return reservation;
        });

    public Task CancelAsync(int id, int? memberId, int? staffId, DateTime now) =>
        MiniTicketTransaction.Run(db, async () =>
        {
            var row = await db.trialReservation.AsTracking().FirstOrDefaultAsync(r => r.id == id);
            if (row == null || (memberId != null && row.member_id != memberId))
                throw new CoachException("预约不存在或没有权限");
            if (row.status != TrialReservation.Pending || row.reservation_date < now.Date)
                throw new CoachException("只能取消未开始且未过期的预约");
            row.status = TrialReservation.Cancelled;
            row.counts_for_day = 0;
            row.blocks_day = 0;
            row.cancel_date = now;
            row.cancel_member_id = memberId;
            row.cancel_staff_id = staffId;
            row.qr_token = null;
            await db.SaveChangesAsync();
            return true;
        });

    public Task<TrialReservation> CreateQrAsync(int id, int staffId, DateTime now) =>
        MiniTicketTransaction.Run(db, async () =>
        {
            var row = await db.trialReservation.AsTracking().FirstOrDefaultAsync(r => r.id == id);
            if (row == null || row.status != TrialReservation.Pending || row.reservation_date != now.Date)
                throw new CoachException("仅当天待签到预约可以生成二维码");
            if (await BoardUnavailableAsync(row.rent_product_id, row.reservation_date, row.id))
                throw new CoachException("单板暂不可用，请先确认归还或租赁状态");
            row.qr_token = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
            row.qr_expires_at = now.AddMinutes(2);
            row.qr_staff_id = staffId;
            await db.SaveChangesAsync();
            return row;
        });

    public Task CheckInAsync(string token, int memberId, DateTime now) =>
        MiniTicketTransaction.Run(db, async () =>
        {
            var row = await db.trialReservation.AsTracking().FirstOrDefaultAsync(r => r.qr_token == token);
            if (row == null || row.qr_expires_at == null || row.qr_expires_at <= now
                || row.status != TrialReservation.Pending || row.reservation_date != now.Date)
                throw new CoachException("二维码已失效，请让店员重新生成");
            if (row.member_id != memberId) throw new CoachException("请预约会员本人扫码确认");
            if (await BoardUnavailableAsync(row.rent_product_id, row.reservation_date, row.id))
                throw new CoachException("单板暂不可用，请联系店员");
            row.status = TrialReservation.InUse;
            row.checkin_date = now;
            row.qr_token = null;
            await db.SaveChangesAsync();
            return true;
        });

    public Task ReturnAsync(int id, int staffId, DateTime now) =>
        MiniTicketTransaction.Run(db, async () =>
        {
            var row = await db.trialReservation.AsTracking().FirstOrDefaultAsync(r => r.id == id);
            if (row == null || row.status != TrialReservation.InUse) throw new CoachException("只有试滑中的预约可以归还");
            row.status = TrialReservation.Returned;
            row.return_date = now;
            row.return_staff_id = staffId;
            row.blocks_day = 0;
            await db.SaveChangesAsync();
            return true;
        });
}
