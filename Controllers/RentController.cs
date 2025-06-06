using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Order;
using SnowmeetApi.Models.Rent;
using SnowmeetApi.Models.Users;
using System.Collections;
using SnowmeetApi.Controllers.User;
using SnowmeetApi.Controllers.Order;
using Mono.TextTemplating;
using TencentCloud.Ocr.V20181119.Models;
using NPOI.XSSF.UserModel;
using NPOI.SS.UserModel;
namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class RentController : ControllerBase
    {
        private readonly ApplicationDBContext _context;

        private IConfiguration _config;

        public string _appId = "";

        public bool isStaff = false;

        private IConfiguration _oriConfig;

        private readonly IHttpContextAccessor _httpContextAccessor;

        private readonly DateTime startDate = DateTime.Parse("2020-10-20");

        private MemberController _memberHelper;

        private OrderOnlinesController _orderHelper;

        public class Balance
        {
            public int id { get; set; }
            public string shop { get; set; }
            public string name { get; set; } = "";
            public string cell { get; set; } = "";
            public DateTime? settleDate { get; set; }
            public double deposit { get; set; } = 0;
            public double refund { get; set; } = 0;
            public double earn { get; set; } = 0;
            public string staff { get; set; } = "";
            public double reparation { get; set; } = 0;
            public double rental { get; set; } = 0;
            public string payMethod { get; set; } = "";
        }

        public RentController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _oriConfig = config;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
            _httpContextAccessor = httpContextAccessor;
            _memberHelper = new MemberController(context, config);
            _orderHelper = new OrderOnlinesController(_context, _oriConfig);
        }
        [NonAction]
        public async Task StartRent(int rentId)
        {
            RentOrder order = await _context.RentOrder.FindAsync(rentId);

            List<RentOrderDetail> rentItemList = await _context.RentOrderDetail
                .Where(i => i.rent_list_id == rentId && i.valid == 1).ToListAsync();
            for (int i = 0; rentItemList != null && i < rentItemList.Count; i++)
            {
                RentOrderDetail detail = rentItemList[i];
                RentOrderDetailLog log;
                switch (detail.deposit_type.Trim())
                {
                    case "立即租赁":
                        detail.start_date = DateTime.Now;
                        detail.rent_status = RentOrderDetail.RentStatus.已发放.ToString();
                        detail.update_date = DateTime.Now;
                        detail.pick_date = DateTime.Now;
                        _context.RentOrderDetail.Entry(detail).State = EntityState.Modified;
                        log = new RentOrderDetailLog()
                        {
                            id = 0,
                            detail_id = detail.id,
                            status = RentOrderDetailLog.Status.已发放.ToString(),
                            staff_open_id = order.staff_open_id,
                            create_date = DateTime.Now
                        };
                        await _context.rentOrderDetailLog.AddAsync(log);
                        log = new RentOrderDetailLog()
                        {
                            id = 0,
                            detail_id = detail.id,
                            status = RentOrderDetailLog.Status.开始计费.ToString(),
                            staff_open_id = order.staff_open_id,
                            create_date = DateTime.Now
                        };
                        await _context.rentOrderDetailLog.AddAsync(log);
                        break;
                    case "延时租赁":
                        detail.update_date = DateTime.Now;
                        detail.rent_status = RentOrderDetail.RentStatus.已发放.ToString();
                        detail.pick_date = DateTime.Now;
                        detail.update_date = DateTime.Now;
                        _context.RentOrderDetail.Entry(detail).State = EntityState.Modified;
                        log = new RentOrderDetailLog()
                        {
                            id = 0,
                            detail_id = detail.id,
                            status = RentOrderDetailLog.Status.已发放.ToString(),
                            staff_open_id = order.staff_open_id,
                            create_date = DateTime.Now
                        };
                        await _context.rentOrderDetailLog.AddAsync(log);
                        break;
                    case "预约租赁":
                        detail.rent_status = RentOrderDetail.RentStatus.未领取.ToString();
                        detail.update_date = DateTime.Now;
                        _context.RentOrderDetail.Entry(detail).State = EntityState.Modified;
                        break;
                    case "先租后取":
                        detail.start_date = DateTime.Now;
                        detail.update_date = DateTime.Now;
                        detail.rent_status = RentOrderDetail.RentStatus.未领取.ToString();
                        _context.RentOrderDetail.Entry(detail).State = EntityState.Modified;
                        log = new RentOrderDetailLog()
                        {
                            id = 0,
                            detail_id = detail.id,
                            status = RentOrderDetailLog.Status.开始计费.ToString(),
                            staff_open_id = order.staff_open_id,
                            create_date = DateTime.Now
                        };
                        await _context.rentOrderDetailLog.AddAsync(log);
                        break;
                    case "预付押金":
                        detail.update_date = DateTime.Now;
                        detail.rent_status = RentOrderDetail.RentStatus.未领取.ToString();
                        _context.RentOrderDetail.Entry(detail).State = EntityState.Modified;
                        break;
                    default:
                        break;
                }
            }
            await _context.SaveChangesAsync();
        }

        [NonAction]
        public async Task StartRent_bak(int rentId)
        {
            var rentItemList = await _context.RentOrderDetail.Where(i => i.rent_list_id == rentId).ToListAsync();
            for (int i = 0; rentItemList != null && i < rentItemList.Count; i++)
            {
                RentOrderDetail detail = rentItemList[i];
                if (detail.deposit_type.Trim().Equals("立即租赁"))
                {
                    DateTime nowTime = DateTime.Now;
                    if (detail.start_date == null)
                    {
                        detail.start_date = DateTime.Now;
                    }
                    else
                    {
                        DateTime startDate = (DateTime)detail.start_date;
                        startDate = startDate.AddHours(nowTime.Hour).AddMinutes(nowTime.Minute)
                            .AddSeconds(nowTime.Second).AddMilliseconds(nowTime.Millisecond);
                        detail.start_date = startDate;
                    }
                    _context.RentOrderDetail.Entry(detail).State = EntityState.Modified;
                }
            }
            await _context.SaveChangesAsync();
        }


        [HttpGet("{code}")]
        public async Task<ActionResult<RentItem>> GetRentItem(string code, string shop)
        {
            var rentItemList = await _context.RentItem.Where(r => r.code.Trim().Equals(code.Trim())).ToListAsync();
            if (rentItemList != null && rentItemList.Count > 0)
            {
                RentItem item = rentItemList[0];
                if (item.rental == 0)
                {
                    item.rental = item.GetRental(shop);
                }
                //item.rental_reserve = item.rental_member;
                return Ok(item);
            }
            else
            {
                return NotFound();
            }
        }
        [NonAction]
        public async Task<List<RentOrder>> GetAllFinishedRentOrder()
        {
            DateTime startDate = DateTime.Parse("2024-10-1");
            DateTime endDate = DateTime.Parse("2025-5-1");
            List<RentOrder> rList = await _context.RentOrder
                .Where(r =>   (r.finish_date >= startDate.Date && r.finish_date <= endDate.Date && r.closed == 0) )

                
                .Include(r => r.receptMsa).ThenInclude(m => m.member)

                .Include(r => r.order).ThenInclude(o => o.paymentList.Where(p => p.status.Equals("支付成功")))
                    .ThenInclude(p => p.msa).ThenInclude(m => m.member)

                .Include(r => r.order).ThenInclude(o => o.paymentList.Where(p => p.status.Equals("支付成功")))
                    .ThenInclude(p => p.refunds.Where(r => r.state == 1 || !r.refund_id.Trim().Equals("")))
                        .ThenInclude(r => r.msa).ThenInclude(m => m.member)

                .Include(r => r.additionalPayments.Where(a => a.is_paid == 1))
                    .ThenInclude(a => a.order).ThenInclude(o => o.paymentList.Where(p => p.status.Equals("支付成功")))
                        .ThenInclude(p => p.msa).ThenInclude(m => m.member)

                .Include(r => r.additionalPayments.Where(a => a.is_paid == 1))
                    .ThenInclude(a => a.order).ThenInclude(o => o.paymentList.Where(p => p.status.Equals("支付成功")))
                        .ThenInclude(p => p.refunds.Where(r => r.state == 1 || !r.refund_id.Trim().Equals("")))
                            .ThenInclude(r => r.msa).ThenInclude(m => m.member)
                .Include(r => r.details.Where(d => d.valid == 1).OrderByDescending(d => d.id)).ThenInclude(d => d.log)
                    .ThenInclude(d => d.msa).ThenInclude(m => m.member)
                
                .OrderByDescending(o => o.id).AsNoTracking().ToListAsync();
            return rList;
        }
        [NonAction]
        public void ExportExcelCreateHead(XSSFWorkbook workbook, ISheet sheet, string[] commonHead, string[] paymentHead,
            string[] refundHead, int maxPaymentCount, int maxRefundCount)
        {
            List<string> headList = new List<string>();
            for (int i = 0; i < commonHead.Length; i++)
            {
                headList.Add(commonHead[i]);

            }
            for (int i = 0; i < maxPaymentCount; i++)
            {
                for (int j = 0; j < paymentHead.Length; j++)
                {
                    headList.Add(paymentHead[j] + (i + 1).ToString());
                }
            }
            for (int i = 0; i < maxRefundCount; i++)
            {
                for (int j = 0; j < refundHead.Length; j++)
                {
                    headList.Add(refundHead[j] + (i + 1).ToString());
                }
            }
            IFont headFont = workbook.CreateFont();
            headFont.Color = NPOI.HSSF.Util.HSSFColor.White.Index;
            headFont.IsBold = true;
            ICellStyle headStyle = workbook.CreateCellStyle();
            headStyle.Alignment = HorizontalAlignment.Center;
            headStyle.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Black.Index;
            headStyle.FillPattern = FillPattern.SolidForeground;
            headStyle.SetFont(headFont);
            headStyle.VerticalAlignment = VerticalAlignment.Center;
            IRow headRow = sheet.CreateRow(0);
            headRow.Height = 500;
            for (int i = 0; i < headList.Count; i++)
            {
                ICell headCell = headRow.CreateCell(i);
                headCell.SetCellValue(headList[i].Trim());
                headCell.SetCellType(CellType.String);
                headCell.CellStyle = headStyle;
                if (i < commonHead.Length)
                {
                    switch (i)
                    {
                        case 0:
                        
                        case 2:
                        
                            sheet.SetColumnWidth(i, 1000);
                            break;
                        case 3:
                        case 22:
                        case 25:
                        case 26:
                        case 9:
                        case 10:
                        case 14:
                        case 15:
                        case 16:
                        case 1:
                            sheet.SetColumnWidth(i, 3500);
                            break;

                        case 5:
                        case 7:
                        case 27:
                        case 30:
                        case 32:
                        case 34:
                            sheet.SetColumnWidth(i, 3000);
                            break;
                        case 6:
                        case 8:
                        case 28:
                        case 31:
                        case 33:
                        case 35:
                            sheet.SetColumnWidth(i, 2500);
                            break;
                        case 4:
                        case 18:
                        case 29:
                        case 36:

                            sheet.SetColumnWidth(i, 3500);
                            break;
                        case 17:
                            sheet.SetColumnWidth(i, 10000);
                            break;
                        default:

                            break;
                    }
                }
                else if (i < commonHead.Length + maxPaymentCount * paymentHead.Length)
                {
                    switch ((i - commonHead.Length) % paymentHead.Length)
                    {
                        case 0:
                            sheet.SetColumnWidth(i, 3500);
                            break;
                        case 2:
                        case 3:
                            sheet.SetColumnWidth(i, 7200);
                            break;
                        case 4:
                            sheet.SetColumnWidth(i, 2900);
                            break;
                        case 5:
                            sheet.SetColumnWidth(i, 3000);
                            break;
                        case 6:
                            sheet.SetColumnWidth(i, 2500);
                            break;
                        case 7:
                            sheet.SetColumnWidth(i, 3500);
                            break;
                        default:
                            break;
                    }
                }
                else
                {
                    switch ((i - commonHead.Length - maxPaymentCount * paymentHead.Length) % refundHead.Length)
                    {
                        case 0:
                            sheet.SetColumnWidth(i, 7200);
                            break;
                        case 1:
                            sheet.SetColumnWidth(i, 11000);
                            break;
                        case 2:
                            sheet.SetColumnWidth(i, 2900);
                            break;
                        case 3:
                            sheet.SetColumnWidth(i, 3000);
                            break;
                        case 4:
                            sheet.SetColumnWidth(i, 2500);
                            break;
                        case 5:
                            sheet.SetColumnWidth(i, 3500);
                            break;
                        default:
                            break;
                    }

                }
            }
        }
        [NonAction]
        public async Task ExportExcelInsertData(XSSFWorkbook workbook, ISheet sheet, List<RentOrder> l,
            string[] commonHead, string[] paymentHead, string[] refundHead, int maxPaymentCount, int maxRefundCount)
        {
            string nullStr = "【-】";
            int index = 0;
            int subIndex = 0;
            IFont fontProblem = workbook.CreateFont();
            fontProblem.Color = NPOI.HSSF.Util.HSSFColor.Red.Index;

            IFont fontHidden = workbook.CreateFont();
            fontHidden.Color = NPOI.HSSF.Util.HSSFColor.Grey50Percent.Index;

            IFont fontUseDeposit = workbook.CreateFont();
            fontUseDeposit.Color = NPOI.HSSF.Util.HSSFColor.Blue.Index;

            IFont fontUseCard = workbook.CreateFont();
            fontUseDeposit.Color = NPOI.HSSF.Util.HSSFColor.Green.Index;

            IDataFormat format = workbook.CreateDataFormat();
            for (int i = 0; i < l.Count; i++)
            {
                ICellStyle styleText = workbook.CreateCellStyle();
                styleText.Alignment = HorizontalAlignment.Center;
                styleText.DataFormat = format.GetFormat("General");
                ICellStyle styleMoney = workbook.CreateCellStyle();
                styleMoney.DataFormat = format.GetFormat("¥#,##0.00");
                ICellStyle styleMoneyProblem = workbook.CreateCellStyle();
                styleMoneyProblem.DataFormat = format.GetFormat("¥#,##0.00");
                styleMoneyProblem.SetFont(fontProblem);
                ICellStyle styleNum = workbook.CreateCellStyle();
                styleNum.DataFormat = format.GetFormat("0");
                ICellStyle styleDate = workbook.CreateCellStyle();
                styleDate.DataFormat = format.GetFormat("yyyy-MM-dd");
                ICellStyle styleTime = workbook.CreateCellStyle();
                styleTime.DataFormat = format.GetFormat("HH:mm:ss");

                RentOrder o = l[i];
                string type = "正常";
                if (o.hide == 1)
                {
                    styleText.SetFont(fontHidden);
                    styleDate.SetFont(fontHidden);
                    styleMoney.SetFont(fontHidden);
                    styleTime.SetFont(fontHidden);
                    styleNum.SetFont(fontHidden);
                    type = "隐匿";
                }
                for (int j = 0; j < o.payments.Count; j++)
                {
                    if (o.payments[j].pay_method.Trim().Equals("储值支付"))
                    {
                        styleText.SetFont(fontUseDeposit);
                        styleDate.SetFont(fontUseDeposit);
                        styleMoney.SetFont(fontUseDeposit);
                        styleTime.SetFont(fontUseDeposit);
                        styleNum.SetFont(fontUseDeposit);
                        type = "储值";
                        break;
                    }
                }
                if (o.totalCharge - o.totalCharge < 0)
                {
                    styleText.SetFont(fontProblem);
                    styleDate.SetFont(fontProblem);
                    styleMoney.SetFont(fontProblem);
                    styleTime.SetFont(fontProblem);
                    styleNum.SetFont(fontProblem);
                }
                if (o.pay_option.Equals("次卡支付"))
                {
                    type = "次卡";
                    styleText.SetFont(fontUseCard);
                    styleDate.SetFont(fontUseCard);
                    styleMoney.SetFont(fontUseCard);
                    styleTime.SetFont(fontUseCard);
                    styleNum.SetFont(fontUseCard);
                }
                if (o.pay_option.Equals("招待"))
                {
                    type = "招待";
                    styleDate.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Yellow.Index;
                    styleDate.FillPattern = FillPattern.SolidForeground;
                    styleTime.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Yellow.Index;
                    styleTime.FillPattern = FillPattern.SolidForeground;
                    styleNum.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Yellow.Index;
                    styleNum.FillPattern = FillPattern.SolidForeground;
                    styleMoney.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Yellow.Index;
                    styleMoney.FillPattern = FillPattern.SolidForeground;
                    styleText.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Yellow.Index;
                    styleText.FillPattern = FillPattern.SolidForeground;
                }
                index++;
                for (int j = 0; j < o.details.Count; j++)
                {
                    RentOrderDetail detail = o.details[j];
                    if (detail.returnStaffName.Trim().Equals(""))
                    {
                        await _context.RentOrderDetail.Entry(detail).Reference(d => d.returnMsa).LoadAsync();
                        if (detail.returnMsa != null)
                        {
                            await _context.memberSocialAccount.Entry(detail.returnMsa).Reference(m => m.member).LoadAsync();
                        }
                    }
                    subIndex++;
                    IRow dr = sheet.CreateRow(subIndex);
                    dr.Height = 500;
                    for (int k = 0; k < commonHead.Length; k++)
                    {
                        ICell cell = dr.CreateCell(k);
                        bool needMerge = false;
                        switch (k)
                        {
                            case 0:
                                if (j == 0)
                                {
                                    cell.SetCellValue(index);
                                    cell.CellStyle = styleNum;
                                }
                                needMerge = true;
                                break;
                            case 1:
                                cell.SetCellValue(subIndex);
                                cell.CellStyle = styleNum;
                                break;
                            case 2:
                                if (j == 0)
                                {
                                    cell.SetCellValue(type);
                                    cell.CellStyle = styleText;
                                }
                                needMerge = true;
                                break;
                            case 3:
                                if (j == 0)
                                {
                                    cell.SetCellValue(o.id);
                                    cell.CellStyle = styleNum;
                                }

                                needMerge = true;
                                break;
                            case 4:
                                if (j == 0)
                                {
                                    cell.SetCellValue(o.shop.Trim());
                                    cell.CellStyle = styleText;
                                }

                                needMerge = true;

                                break;
                            case 5:
                                if (j == 0)
                                {
                                    cell.SetCellValue(o.create_date);
                                    cell.CellStyle = styleDate;
                                }

                                needMerge = true;

                                break;
                            case 6:
                                if (j == 0)
                                {
                                    cell.SetCellValue(o.create_date);
                                    cell.CellStyle = styleTime;
                                }

                                needMerge = true;

                                break;
                            case 7:
                                if (j == 0)
                                {
                                    cell.SetCellValue((DateTime)o.finish_date);
                                    cell.CellStyle = styleDate;
                                }

                                needMerge = true;

                                break;
                            case 8:
                                if (j == 0)
                                {
                                    cell.SetCellValue((DateTime)o.finish_date);
                                    cell.CellStyle = styleTime;
                                }

                                needMerge = true;

                                break;
                            case 9:

                                cell.SetCellValue(j == 0 ? o.totalDeposit : 0);
                                cell.CellStyle = styleMoney;
                                needMerge = true;

                                break;
                            case 10:
                                double totalRental = o.totalRental;
                                cell.SetCellValue(j == 0 ? totalRental : 0);
                                if (totalRental < 0)
                                {
                                    cell.CellStyle = styleMoneyProblem;
                                }
                                else
                                {
                                    cell.CellStyle = styleMoney;
                                }
                                needMerge = true;
                                break;
                            case 11:
                                cell.SetCellValue(j == 0 ? o.totalReparation : 0);
                                cell.CellStyle = styleMoney;
                                needMerge = true;

                                break;
                            case 12:
                                cell.SetCellValue(j == 0 ? o.totalOvertimeCharge : 0);
                                cell.CellStyle = styleMoney;
                                needMerge = true;

                                break;
                            case 13:
                                cell.SetCellValue(j == 0 ? o.totalDiscount : 0);
                                cell.CellStyle = styleMoney;
                                needMerge = true;

                                break;
                            case 14:
                                cell.SetCellValue(j == 0 ? o.totalCharge : 0);
                                cell.CellStyle = styleMoney;
                                needMerge = true;

                                break;
                            case 15:
                                cell.SetCellValue(j == 0 ? o.totalRefund : 0);
                                cell.CellStyle = styleMoney;
                                needMerge = true;
                                break;
                            case 16:
                                double totalEarn = o.totalEarn;
                                cell.SetCellValue(j == 0 ? totalEarn : 0);
                                if (totalEarn != o.totalRental + o.totalOvertimeCharge + o.totalReparation)
                                {
                                    cell.CellStyle = styleMoneyProblem;
                                }
                                else
                                {
                                    cell.CellStyle = styleMoney;
                                }
                                needMerge = true;
                                break;
                            case 17:
                                string memo = o.memo + (o.order != null && o.order.memo != null ? o.order.memo.Trim() : "");
                                cell.SetCellValue(memo);
                                cell.CellStyle = styleText;
                                needMerge = true;
                                break;
                            case 18:
                                if (j == 0)
                                {
                                    cell.SetCellValue((o.receptMsa == null) ? "" : o.receptMsa.member.real_name);
                                    cell.CellStyle = styleText;
                                }

                                needMerge = true;
                                break;
                            case 19:
                                cell.SetCellValue(detail.rent_item_code);
                                cell.CellStyle = styleText;
                                break;
                            case 20:
                                cell.SetCellValue(detail.rent_item_class);
                                cell.CellStyle = styleText;
                                break;
                            case 21:
                                cell.SetCellValue(detail.rent_item_name);
                                cell.CellStyle = styleText;
                                break;
                            case 22:
                                cell.SetCellValue(detail.deposit);
                                cell.CellStyle = styleMoney;
                                break;
                            case 23:
                                cell.SetCellValue(j == 0 ? detail.unit_rental : 0);
                                cell.CellStyle = styleMoney;
                                break;
                            case 24:
                                double summary = 0;
                                for (int m = 0; m < o.rentalDetails.Count; m++)
                                {
                                    if (o.rentalDetails[m].item.id == detail.id)
                                    {
                                        summary += o.rentalDetails[m].rental;
                                    }
                                }
                                cell.SetCellValue(j == 0 ? summary : 0);
                                cell.CellStyle = styleMoney;
                                break;
                            case 25:
                                cell.SetCellValue(j == 0 ? detail.reparation : 0);
                                cell.CellStyle = styleMoney;
                                break;
                            case 26:
                                cell.SetCellValue(j == 0 ? detail.overtime_charge : 0);
                                cell.CellStyle = styleMoney;
                                break;
                            case 27:
                                if (detail.pick_date == null)
                                {
                                    cell.SetCellValue(nullStr);
                                    cell.CellStyle = styleText;
                                }
                                else
                                {
                                    cell.SetCellValue((DateTime)detail.pick_date);
                                    cell.CellStyle = styleDate;
                                }

                                break;
                            case 28:
                                if (detail.pick_date == null)
                                {
                                    cell.SetCellValue(nullStr);
                                    cell.CellStyle = styleText;
                                }
                                else
                                {
                                    cell.SetCellValue((DateTime)detail.pick_date);
                                    cell.CellStyle = styleTime;
                                }
                                break;
                            case 29:
                                cell.SetCellValue(detail.pickStaffName);
                                cell.CellStyle = styleText;
                                break;
                            case 30:
                                if (detail.start_date == null)
                                {
                                    cell.SetCellValue(nullStr);
                                    cell.CellStyle = styleText;
                                }
                                else
                                {
                                    cell.SetCellValue((DateTime)detail.start_date);
                                    cell.CellStyle = styleDate;
                                }
                                break;
                            case 31:
                                if (detail.start_date == null)
                                {
                                    cell.SetCellValue(nullStr);
                                    cell.CellStyle = styleText;
                                }
                                else
                                {
                                    cell.SetCellValue((DateTime)detail.start_date);
                                    cell.CellStyle = styleTime;
                                }
                                break;
                            case 32:
                                if (detail.real_end_date == null)
                                {
                                    cell.SetCellValue(nullStr);
                                    cell.CellStyle = styleText;
                                }
                                else
                                {
                                    cell.SetCellValue((DateTime)detail.real_end_date);
                                    cell.CellStyle = styleDate;
                                }
                                break;
                            case 33:
                                if (detail.real_end_date == null)
                                {
                                    cell.SetCellValue(nullStr);
                                    cell.CellStyle = styleText;
                                }
                                else
                                {
                                    cell.SetCellValue((DateTime)detail.real_end_date);
                                    cell.CellStyle = styleTime;
                                }
                                break;
                            case 34:
                                if (detail.return_date == null)
                                {
                                    cell.SetCellValue(nullStr);
                                    cell.CellStyle = styleText;
                                }
                                else
                                {
                                    cell.SetCellValue((DateTime)detail.return_date);
                                    cell.CellStyle = styleDate;
                                }
                                break;
                            case 35:
                                if (detail.return_date == null)
                                {
                                    cell.SetCellValue(nullStr);
                                    cell.CellStyle = styleText;
                                }
                                else
                                {
                                    cell.SetCellValue((DateTime)detail.return_date);
                                    cell.CellStyle = styleTime;
                                }
                                break;
                            case 36:
                                cell.SetCellValue(detail.returnStaffName);
                                cell.CellStyle = styleText;
                                break;
                            default:
                                break;
                        }
                        if (needMerge)
                        {
                            if (o.details.Count > 1 && j == o.details.Count - 1)
                            {
                                sheet.AddMergedRegion(new NPOI.SS.Util.CellRangeAddress(subIndex - o.details.Count + 1, subIndex, k, k));
                            }
                        }

                    }
                    for (int k = 0; k < maxPaymentCount; k++)
                    {
                        if (k < o.payments.Count)
                        {
                            OrderPayment payment = o.payments[k];
                            for (int m = 0; m < paymentHead.Length; m++)
                            {
                                int colIndex = commonHead.Length + k * paymentHead.Length + m;
                                ICell cell = dr.CreateCell(colIndex);
                                switch ((colIndex - commonHead.Length) % paymentHead.Length)
                                {
                                    case 0:
                                        if (j == 0)
                                        {
                                            cell.SetCellValue(payment.shop.Trim());
                                            cell.CellStyle = styleText;
                                        }

                                        break;
                                    case 1:
                                        if (j == 0)
                                        {
                                            cell.SetCellValue(payment.pay_method.Trim());
                                            cell.CellStyle = styleText;
                                        }

                                        break;
                                    case 2:
                                        if (j == 0)
                                        {
                                            cell.SetCellValue(payment.wepay_trans_id != null ? payment.wepay_trans_id.Trim() : "");
                                            cell.CellStyle = styleText;
                                        }

                                        break;
                                    case 3:
                                        if (j == 0)
                                        {
                                            cell.SetCellValue(payment.out_trade_no != null ? payment.out_trade_no.Trim() : "");
                                            cell.CellStyle = styleText;
                                        }

                                        break;
                                    case 4:
                                        cell.SetCellValue(j == 0 ? payment.amount : 0);
                                        cell.CellStyle = styleMoney;
                                        break;
                                    case 5:
                                        if (j == 0)
                                        {
                                            cell.SetCellValue(payment.create_date);
                                            cell.CellStyle = styleDate;
                                        }

                                        break;
                                    case 6:
                                        if (j == 0)
                                        {
                                            cell.SetCellValue(payment.create_date);
                                            cell.CellStyle = styleTime;
                                        }

                                        break;
                                    case 7:
                                        if (j == 0)
                                        {
                                            string staffName = (payment.msa != null && payment.msa.member != null) ? payment.msa.member.real_name : "";
                                            cell.SetCellValue(staffName);
                                            cell.CellStyle = styleText;
                                        }

                                        break;
                                    default:
                                        break;
                                }
                                if (o.details.Count > 1 && j == o.details.Count - 1)
                                {
                                    sheet.AddMergedRegion(new NPOI.SS.Util.CellRangeAddress(subIndex - o.details.Count + 1, subIndex, colIndex, colIndex));
                                }
                            }
                        }
                        else
                        {
                            for (int m = 0; m < paymentHead.Length; m++)
                            {
                                int colIndex = commonHead.Length + k * paymentHead.Length + m;
                                ICell cell = dr.CreateCell(colIndex);
                                cell.SetCellValue(nullStr);
                                cell.CellStyle = styleText;
                                if (o.details.Count > 1 && j == o.details.Count - 1)
                                {
                                    sheet.AddMergedRegion(new NPOI.SS.Util.CellRangeAddress(subIndex - o.details.Count + 1, subIndex, colIndex, colIndex));
                                }
                            }
                        }
                    }
                    for (int k = 0; k < maxRefundCount; k++)
                    {
                        if (k < o.refunds.Count)
                        {
                            OrderPaymentRefund refund = o.refunds[k];
                            for (int m = 0; m < refundHead.Length; m++)
                            {
                                int colIndex = commonHead.Length + maxPaymentCount * paymentHead.Length + k * refundHead.Length + m;
                                ICell cell = dr.CreateCell(colIndex);
                                switch ((colIndex - commonHead.Length - maxPaymentCount * paymentHead.Length) % refundHead.Length)
                                {
                                    case 0:
                                        if (j == 0)
                                        {
                                            cell.SetCellValue(refund.refund_id != null ? refund.refund_id.Trim() : "");
                                            cell.CellStyle = styleText;
                                        }

                                        break;
                                    case 1:
                                        if (j == 0)
                                        {
                                            cell.SetCellValue(refund.out_refund_no != null ? refund.out_refund_no.Trim() : "");
                                            cell.CellStyle = styleText;
                                        }

                                        break;
                                    case 2:

                                        cell.SetCellValue(j == 0 ? refund.amount : 0);
                                        cell.CellStyle = styleMoney;
                                        break;
                                    case 3:
                                        if (j == 0)
                                        {
                                            cell.SetCellValue(refund.create_date);
                                            cell.CellStyle = styleDate;
                                        }

                                        break;
                                    case 4:
                                        if (j == 0)
                                        {
                                            cell.SetCellValue(refund.create_date);
                                            cell.CellStyle = styleTime;
                                        }

                                        break;
                                    case 5:
                                        if (j == 0)
                                        {
                                            string staffName = (refund.msa != null && refund.msa.member != null) ? refund.msa.member.real_name : "";
                                            cell.SetCellValue(staffName);
                                            cell.CellStyle = styleText;
                                        }

                                        break;
                                    default:
                                        break;
                                }
                                if (o.details.Count > 1 && j == o.details.Count - 1)
                                {
                                    sheet.AddMergedRegion(new NPOI.SS.Util.CellRangeAddress(subIndex - o.details.Count + 1, subIndex, colIndex, colIndex));
                                }
                            }
                        }
                        else
                        {
                            for (int m = 0; m < refundHead.Length; m++)
                            {
                                int colIndex = commonHead.Length + maxPaymentCount * paymentHead.Length + k * refundHead.Length + m;
                                ICell cell = dr.CreateCell(colIndex);
                                cell.SetCellValue(nullStr);
                                cell.CellStyle = styleText;
                                if (o.details.Count > 1 && j == o.details.Count - 1)
                                {
                                    sheet.AddMergedRegion(new NPOI.SS.Util.CellRangeAddress(subIndex - o.details.Count + 1, subIndex, colIndex, colIndex));
                                }
                            }
                        }
                    }
                }
            }
        }
        [HttpGet]
        public async Task ExportExcel()
        {
            List<RentOrder> rList = await GetAllFinishedRentOrder();
            int maxPaymentCount = 0;
            int maxRefundCount = 0;
            for (int i = 0; i < rList.Count; i++)
            {
                RentOrder order = rList[i];
                maxPaymentCount = Math.Max(maxPaymentCount, order.payments.Count);
                maxRefundCount = Math.Max(maxRefundCount, order.refunds.Count);
            }
            //类型：正常，招待，储值，隐匿
            string[] commonHead = new string[] { "序号", "子序号", "类型", "订单号", "门店", "业务日期", "业务时间", "结算日期", "结算时间", "总计押金", "总计租金", "总计赔偿", "总计超时",
                "总计减免", "总计实收", "总计退款", "结余", "备注","接待", "物品编号", "物品分类", "物品名称", "押金", "租金单价", "租金小计" ,  "赔偿", "超时", "发放日期", "发放时间", "发放人",
                "起租日期", "起租时间", "退租日期", "退租时间", "归还日期", "归还时间", "接收人" };
            string[] paymentHead = new string[] { "收款门店", "支付方式", "微信支付单号", "商户订单号", "金额", "收款日期", "收款时间", "收款人" };
            string[] refundHead = new string[] { "退款单号", "商户退款单号", "退款金额", "退款日期", "退款时间", "退款人" };


            XSSFWorkbook workbook = new XSSFWorkbook();
            ISheet sheet = workbook.CreateSheet("24-25租赁");
            ExportExcelCreateHead(workbook, sheet, commonHead, paymentHead, refundHead, maxPaymentCount, maxRefundCount);
            await ExportExcelInsertData(workbook, sheet, rList, commonHead, paymentHead, refundHead, maxPaymentCount, maxRefundCount);
            string filePath = $"{Environment.CurrentDirectory}" + "/rent.xlsx";
            using (var file = System.IO.File.Create(filePath))
            {
                workbook.Write(file);
            }
        }
        [HttpGet]
        public async Task<ActionResult<List<Balance>>> GetBalance(string shop, DateTime startDate, DateTime endDate, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            shop = Util.UrlDecode(shop).Replace("'", "").Trim();
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!shop.Trim().Equals("万龙") && user.member.is_admin != 1 && user.member.is_manager != 1)
            {
                return NoContent();
            }
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            /*
            if (shop.Trim().Equals("万龙"))
            {
                endDate = endDate.Date.AddDays(-5);
            }
            */
            var idList = await _context.idList.FromSqlRaw(" select distinct rent_list_id as id from rent_list_detail  "
                + " left join rent_list on rent_list.[id] = rent_list_id "
                + " where finish_date >= '" + startDate.ToShortDateString() + "' "
                + " and finish_date <= '" + endDate.AddDays(1).ToShortDateString() + "' and shop like '" + shop + "%'  "
                + " and finish_date is not null and closed = 0 "
                //+ " and hide = 0 "
                //+ " and rent_list.id = 5533 "
                )
                .AsNoTracking().ToListAsync();
            List<Balance> bList = new List<Balance>();
            for (int i = 0; i < idList.Count; i++)
            {

                RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(idList[i].id, sessionKey, false)).Result).Value;
                if (order.totalDepositPaidAmount > 0)
                {
                    continue;
                }

                if (!order.status.Trim().Equals("已完成"))
                {
                    continue;
                }
                if (order.order == null)
                {
                    continue;
                }

                double totalPayment = 0;
                double totalRefund = 0;
                for (int j = 0; order.order != null && j < order.order.paymentList.Count; j++)
                {
                    OrderPayment payment = order.order.payments[j];
                    if (payment.status.Equals("支付成功") && !payment.pay_method.Equals("储值支付"))
                    {
                        totalPayment += order.order.payments[j].amount;
                    }
                }
                for (int j = 0; order.refunds != null && j < order.refunds.Count; j++)
                {
                    if (!order.refunds[j].refund_id.Trim().Equals("") || order.refunds[j].state == 1)
                    {
                        totalRefund += order.refunds[j].amount;
                    }
                }
                double totalReparation = 0;
                double totalRental = 0;
                for (int j = 0; j < order.details.Count; j++)
                {
                    totalReparation += order.details[j].reparation;
                    RentOrderDetail detail = order.details[j];
                    double subRental = Math.Round(detail.suggestRental, 2) - Math.Round(detail.rental_ticket_discount, 2)
                        - Math.Round(detail.rental_discount, 2) + Math.Round(detail.overtime_charge, 2);
                    totalRental += subRental;
                }

                Balance b = new Balance()
                {
                    id = order.id,
                    shop = order.shop,
                    name = order.real_name.Trim(),
                    cell = order.cell_number.Trim(),
                    settleDate = order.finish_date,
                    deposit = totalPayment,
                    refund = totalRefund,
                    earn = totalPayment - totalRefund,
                    reparation = totalReparation,
                    staff = order.staff_name,
                    payMethod = order.order.pay_method.Trim(),
                    rental = totalRental //totalPayment - totalRefund - totalReparation
                };
                try
                {
                    if (b.settleDate >= startDate && ((DateTime)b.settleDate).Date <= endDate.Date)
                    {
                        bList.Add(b);
                    }
                }
                catch
                {

                }

            }
            return Ok(bList.OrderByDescending(b => b.id).ToList());
        }

        [HttpPost]
        public async Task<ActionResult<RentOrder>> Recept([FromQuery] string sessionKey, [FromBody] RentOrder rentOrder)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }

            //MiniAppUser customerUser = await _context.MiniAppUsers.FindAsync(rentOrder.open_id);
            Member customerUser = await _memberHelper.GetMember(rentOrder.open_id, "wechat_mini_openid");
            if (customerUser != null)
            {
                if (customerUser.real_name.Trim().Equals(""))
                {
                    string realName = rentOrder.real_name.Replace("先生", "").Replace("女士", "").Trim();
                    string gender = "";
                    if (rentOrder.real_name.Replace(realName, "").IndexOf("先生") >= 0)
                    {
                        gender = "男";
                    }
                    else if (rentOrder.real_name.Replace(realName, "").IndexOf("女士") >= 0)
                    {
                        gender = "女";
                    }
                    customerUser.real_name = realName;
                    customerUser.gender = gender;
                    _context.member.Entry(customerUser).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                }
            }

            rentOrder.staff_open_id = user.miniAppOpenId.Trim();
            rentOrder.staff_name = user.miniAppUser.real_name.Trim();

            int orderId = 0;

            if (rentOrder.deposit_final > 0)
            {
                OrderOnline order = new OrderOnline()
                {
                    id = 0,
                    type = "押金",
                    shop = rentOrder.shop.Trim(),
                    open_id = rentOrder.open_id.Trim(),
                    name = rentOrder.real_name.Trim(),
                    cell_number = rentOrder.cell_number.Trim(),
                    pay_method = rentOrder.payMethod.Trim(),
                    pay_memo = "",
                    pay_state = 0,
                    order_price = rentOrder.deposit,
                    order_real_pay_price = rentOrder.deposit_final,
                    ticket_amount = 0,
                    other_discount = 0,
                    final_price = rentOrder.deposit_final,
                    ticket_code = rentOrder.ticket_code.Trim(),
                    staff_open_id = user.miniAppOpenId.Trim(),
                    score_rate = 0,
                    generate_score = 0

                };
                await _context.AddAsync(order);
                await _context.SaveChangesAsync();

                OrderPayment payment = new OrderPayment()
                {
                    order_id = order.id,
                    pay_method = order.pay_method.Trim(),
                    amount = order.final_price,
                    status = "待支付",
                    staff_open_id = user.miniAppOpenId.Trim()
                };
                await _context.OrderPayment.AddAsync(payment);
                await _context.SaveChangesAsync();
                orderId = order.id;
            }
            rentOrder.order_id = orderId;

            await _context.RentOrder.AddAsync(rentOrder);
            await _context.SaveChangesAsync();

            for (int i = 0; i < rentOrder.details.Count; i++)
            {
                RentOrderDetail detail = rentOrder.details[i];
                /*
                if (detail.deposit_type.Trim().Equals("立即租赁"))
                {
                    detail.start_date = DateTime.Now;
                }
                */
                detail.rent_staff = user.miniAppOpenId.Trim();
                detail.return_staff = "";
                detail.rent_list_id = rentOrder.id;
                await _context.RentOrderDetail.AddAsync(detail);
                await _context.SaveChangesAsync();
            }

            OrderOnlinesController orderHelper = new OrderOnlinesController(_context, _oriConfig);
            OrderOnline newOrder = (await orderHelper.GetWholeOrderByStaff(orderId, sessionKey)).Value;

            rentOrder.order = newOrder;

            return rentOrder;
        }

        [HttpGet("{cell}")]
        public async Task<ActionResult<RentOrder[]>> GetRentOrderListByCell(string cell, string sessionKey, string status = "", string shop = "")
        {
            cell = cell.Trim();
            if (cell.Length < 4)
            {
                return BadRequest();
            }
            if (cell.Length > 4)
            {
                RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(int.Parse(cell), sessionKey, false)).Result).Value;
                return Ok(new RentOrder[] { rentOrder });

            }
            shop = Util.UrlDecode(shop).Trim();
            status = Util.UrlDecode(status).Trim();
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            /*
            if (user.member.is_admin != 1 && user.member.is_manager != 1)
            {
                return NoContent();
            }
            */
            if (!user.isAdmin)
            {
                return BadRequest();
            }

            int orderId = 0;
            if (cell.Trim().Length == 4)
            {
                try
                {
                    orderId = int.Parse(cell);
                }
                catch
                {

                }
            }

            var orderListTemp = await _context.RentOrder
                .Where(o => ((o.cell_number.EndsWith(cell) || o.id == orderId) && (shop.Equals("") || o.shop.Trim().Equals(shop)))
                && o.create_date.Date > DateTime.Parse("2024-10-15"))
                .OrderByDescending(o => o.id).ToListAsync();
            if (orderListTemp == null || orderListTemp.Count <= 0)
            {
                return NotFound();
            }

            List<RentOrder> orderList = new List<RentOrder>();
            for (int i = 0; i < orderListTemp.Count; i++)
            {
                RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(orderListTemp[i].id, sessionKey, false)).Result).Value;
                if (status.Equals("") || order.status.Trim().Equals(status))
                {
                    orderList.Add(order);
                }
            }
            return Ok(orderList.ToArray<RentOrder>());
        }


        [HttpGet]
        public async Task<ActionResult<RentOrder[]>> GetRentOrderListByStaff(string shop,
            DateTime start, DateTime end, string status, string sessionKey)
        {
            OrderOnlinesController orderHelper = new OrderOnlinesController(_context, _oriConfig);
            if (shop == null)
            {
                shop = "";
            }
            if (status != null)
            {
                status = Util.UrlDecode(status.Trim());
            }
            shop = Util.UrlDecode(shop.Trim());
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (user == null || !user.isAdmin)
            {
                return BadRequest();
            }
            RentOrder[] orderArr = await _context.RentOrder
                .Include(r => r.order)
                    .ThenInclude(o => o.paymentList.Where(p => p.status.Equals("支付成功")))
                        .ThenInclude(p => p.refunds.Where(r => r.state == 1))
                .Include(o => o.details.Where(d => d.valid == 1))
                    .ThenInclude(d => d.log)
                .Include(o => o.additionalPayments.Where(d => d.is_paid == 1))
                .Where(o => (o.create_date >= start && o.create_date < end.Date.AddDays(1) && (shop.Trim().Equals("") || o.shop.Trim().Equals(shop))))
                .OrderByDescending(o => o.id).ToArrayAsync();

            for (int i = 0; i < orderArr.Length; i++)
            {
                try
                {

                    //RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(orderArr[i].id, sessionKey, false)).Result).Value;
                    if (orderArr[i].staff_name == null || orderArr[i].staff_name.Trim().Equals(""))
                    {
                        orderArr[i].staffMember = (await _memberHelper.GetMember(orderArr[i].staff_open_id, "wechat_mini_openid"));
                        orderArr[i].staff_name = orderArr[i].staffMember == null ? "" : (orderArr[i].staffMember.real_name.Trim());
                        _context.RentOrder.Entry(orderArr[i]).State = EntityState.Modified;
                    }

                }
                catch
                {

                }

            }
            await _context.SaveChangesAsync();
            if (status == null)
            {
                return Ok(orderArr);
            }
            else
            {
                List<RentOrder> newArr = new List<RentOrder>();
                for (int i = 0; i < orderArr.Length; i++)
                {
                    try
                    {
                        if (orderArr[i].status.Trim().Equals(status))
                        {
                            newArr.Add(orderArr[i]);
                        }
                    }
                    catch
                    {
                    }
                }
                return Ok(newArr.ToArray());
            }

        }

        [NonAction]
        public async Task RestoreStaffInfo(RentOrder order)
        {
            var receptList = await _context.Recept.Where(r => r.submit_return_id == order.id)
                .AsNoTracking().ToListAsync();
            if (receptList != null && receptList.Count > 0)
            {
                order.staff_open_id = receptList[0].update_staff.Trim();
                Member? staffUser = await _memberHelper.GetMember(order.staff_open_id, "wechat_mini_openid");
                order.staff_name = staffUser == null ? "" : staffUser.real_name;
                _context.RentOrder.Entry(order);
                await _context.SaveChangesAsync();
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrder>> GetRentOrder(int id, string sessionKey, bool needAuth = true)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            List<RentOrder> rentOrderList = await _context.RentOrder
                .Include(r => r.recept)
                .Include(r => r.details.OrderBy(d => d.package_code).OrderBy(d => d.id))
                    .ThenInclude(d => d.log)
                .Include(r => r.order)
                    .ThenInclude(o => o.paymentList.Where(p => p.status.Trim().Equals("支付成功")).OrderByDescending(p => p.id))
                        .ThenInclude(p => p.refunds.Where(r => (r.state == 1 || !r.refund_id.Trim().Equals(""))).OrderByDescending(r => r.id))
                .Include(r => r.additionalPayments)
                    .ThenInclude(a => a.order)
                        .ThenInclude(o => o.paymentList.Where(p => p.status.Equals("支付成功")).OrderByDescending(p => p.id))
                            .ThenInclude(p => p.refunds.Where(r => r.state == 1).OrderByDescending(r => r.id))
                .Include(r => r.rewards.OrderByDescending(r => r.id))
                    .ThenInclude(r => r.rentRewardRefunds.OrderByDescending(r => r.id))
                        .ThenInclude(r => r.refund)
                .Where(r => r.id == id).ToListAsync();
            if (rentOrderList.Count == 0)
            {
                rentOrderList = await _context.RentOrder
                .Include(r => r.details)
                    .ThenInclude(d => d.log)
                .Where(r => r.id == id).ToListAsync();
            }
            if (rentOrderList.Count == 0)
            {
                return NotFound();
            }
            RentOrder rentOrder = rentOrderList[0];
            if (needAuth)
            {
                if (rentOrder == null)
                {
                    return NotFound();
                }
                if (!user.isAdmin && !rentOrder.open_id.Trim().Equals(user.miniAppOpenId.Trim()))
                {
                    return BadRequest();
                }
                if (rentOrder.staff_open_id.Trim().Equals("") || rentOrder.staff_name == null || rentOrder.staff_name.Trim().Equals(""))
                {
                    try
                    {
                        await RestoreStaffInfo(rentOrder);
                    }
                    catch
                    {

                    }
                }
            }
            if (rentOrder.order != null)
            {
                List<MemberSocialAccount> msaList = await _context.memberSocialAccount
                    .Where(m => (m.num.Trim().Equals(rentOrder.staff_open_id) && m.type.Trim().Equals("wechat_mini_openid")))
                    .Include(m => m.member).ToListAsync();
                if (msaList != null && msaList.Count > 0)
                {
                    rentOrder.order.msa = msaList[0];
                }
                for (int i = 0; i < rentOrder.refunds.Count; i++)
                {
                    OrderPaymentRefund r = rentOrder.refunds[i];
                    msaList = await _context.memberSocialAccount
                        .Where(m => (m.num.Trim().Equals(r.oper) && m.type.Trim().Equals("wechat_mini_openid")))
                        .Include(m => m.member).ToListAsync();
                    if (msaList != null && msaList.Count > 0)
                    {
                        r.msa = msaList[0];
                    }
                }
                rentOrder.order.member = await (_memberHelper.GetMember(rentOrder.open_id, "wechat_mini_openid"));
            }
            bool allReturned = true;
            DateTime returnTime = rentOrder.create_date;
            for (int i = 0; i < rentOrder.details.Count; i++)
            {
                DateTime endDate = DateTime.Now;
                RentOrderDetail detail = rentOrder.details[i];
                if (detail.real_end_date != null)
                {
                    endDate = (DateTime)detail.real_end_date;
                }
                DateTime endTime = DateTime.Now;
                if (detail.real_end_date != null)
                {
                    endTime = (DateTime)detail.real_end_date;
                }

                if (rentOrder.start_date.Hour >= 16 && rentOrder.start_date.Date == endTime.Date)
                {
                    detail.overTime = false;
                }
                else if (endTime.Hour >= 18)
                {
                    detail.overTime = true;
                }
                else
                {
                    detail.overTime = false;

                }

                if (!detail.rent_staff.Trim().Equals(""))
                {
                    detail.rentStaff = (await UnicUser.GetUnicUserByDetailInfo(detail.rent_staff, "wechat_mini_openid", _context)).miniAppUser;

                }
                else
                {
                    if (!rentOrder.staff_open_id.Trim().Equals(""))
                    {
                        detail.rentStaff = (await UnicUser.GetUnicUserByDetailInfo(rentOrder.staff_open_id, "wechat_mini_openid", _context)).miniAppUser;//await _context.MiniAppUsers.FindAsync(rentOrder.staff_open_id);
                    }


                }

                if (!detail.return_staff.Trim().Equals(""))
                {
                    detail.returnStaff = (await UnicUser.GetUnicUserByDetailInfo(detail.return_staff, "wechat_mini_openid", _context)).miniAppUser;
                }
                else
                {
                    detail.returnStaff = null;
                }

                if (!detail.rentStatus.Trim().Equals("已归还"))
                {
                    allReturned = false;
                }
                else
                {
                    if (detail.real_end_date != null)
                    {
                        returnTime = detail.real_end_date > returnTime ? (DateTime)detail.real_end_date : returnTime;
                    }
                }
                switch (rentOrder.shop.Trim())
                {
                    case "南山":
                        TimeSpan ts = endDate - rentOrder.start_date;
                        detail._suggestRental = detail.unit_rental * (ts.Days + 1);
                        detail._timeLength = (ts.Days + 1).ToString() + "天";
                        break;
                    default:

                        if (rentOrder.start_date.Date == endDate.Date && rentOrder.start_date.Hour >= 16)
                        {
                            detail._suggestRental = detail.unit_rental;
                            detail._timeLength = "夜场";
                        }
                        else
                        {
                            if (detail.start_date == null)
                            {
                                detail._timeLength = "--";
                            }
                            else
                            {
                                TimeSpan ts1 = endDate.Date - ((DateTime)detail.start_date).Date;
                                int days = ts1.Days;
                                days++;
                                detail._suggestRental = detail.unit_rental * days;
                                detail._timeLength = days.ToString() + "天";
                            }

                        }
                        break;
                }
            }

            if (allReturned && rentOrder.order != null && !rentOrder.order.pay_method.Trim().Equals("微信支付")
                && rentOrder.end_date == null)
            {
                rentOrder.end_date = returnTime;
                _context.RentOrder.Entry(rentOrder).State = EntityState.Modified;
                await _context.SaveChangesAsync();
            }
            if (rentOrder.staff_name.Trim().Equals(""))
            {
                rentOrder.staff_name = rentOrder.order == null ? "" : rentOrder.order.staffName.Trim();
            }
            if (rentOrder.staff_name.Trim().Equals(""))
            {
                if (rentOrder.recept != null && rentOrder.recept.Count > 0)
                {

                    rentOrder.staff_name = rentOrder.recept[0].update_staff_name.Trim().Equals("") ?
                        rentOrder.recept[0].recept_staff_name : rentOrder.recept[0].update_staff_name.Trim();
                    if (rentOrder.staff_name.Trim().Equals(""))
                    {
                        try
                        {
                            string staffOpenId = rentOrder.recept[0].update_staff.Trim().Equals("") ?
                                rentOrder.recept[0].recept_staff.Trim() : rentOrder.recept[0].update_staff.Trim();
                            //MiniAppUser? staffUser = await _context.MiniAppUsers.FindAsync(staffOpenId.Trim());
                            Member staffUser = await _memberHelper.GetMember(staffOpenId.Trim(), "wechat_mini_openid");
                            if (staffUser != null)
                            {
                                rentOrder.staff_name = staffUser.real_name.Trim();
                            }
                        }
                        catch
                        {

                        }
                    }
                }

            }

            if (rentOrder.pay_option.Trim().Equals("招待"))
            {
                rentOrder.backColor = "yellow";
            }
            if (rentOrder.order != null)
            {
                if (rentOrder.order.pay_state == 0)
                {
                    rentOrder.backColor = "red";
                    if (rentOrder.status.Trim().Equals("已关闭"))
                    {
                        rentOrder.backColor = "";
                    }
                }

                if (!rentOrder.order.pay_method.Trim().Equals("微信支付") && rentOrder.status.Equals("全部归还"))
                {
                    rentOrder.textColor = "red";
                }
            }
            else
            {
                if (rentOrder.pay_option.Trim().Equals("招待"))
                {
                    rentOrder.backColor = "yellow";
                }
                else
                {
                    rentOrder.backColor = "pink";
                }

            }

            if (rentOrder.status.Equals("已退款") || rentOrder.status.Equals("已完成"))
            {
                rentOrder.textColor = "red";
            }
            if (rentOrder.status.Trim().Equals("已关闭"))
            {
                rentOrder.textColor = "#C0C0C0";
            }

            if (!rentOrder.real_name.Trim().EndsWith("先生") && !rentOrder.real_name.Trim().EndsWith("女士"))
            {
                Member member = await _memberHelper.GetMember(rentOrder.open_id.Trim(), "wechat_mini_openid");
                if (member != null)
                {
                    rentOrder.real_name = member.real_name.Trim();
                    switch (member.gender)
                    {
                        case "男":
                            rentOrder.real_name += " 先生";
                            break;
                        case "女":
                            rentOrder.real_name += " 女士";
                            break;
                        default:
                            break;
                    }
                }
            }
            for (int i = 0; rentOrder.additionalPayments != null
                && i < rentOrder.additionalPayments.Count; i++)
            {
                RentAdditionalPayment p = rentOrder.additionalPayments[i];
                List<MemberSocialAccount> msaL = await _context.memberSocialAccount
                    .Where(m => m.type.Trim().Equals("wechat_mini_openid") && m.num.Trim().Equals(p.staff_open_id.Trim()))
                    .Include(m => m.member).AsNoTracking().ToListAsync();
                if (msaL != null && msaL.Count > 0)
                {
                    p.staffMember = msaL[0].member;
                }
            }
            Mi7OrderController _mi7Helper = new Mi7OrderController(_context, _oriConfig, _httpContextAccessor);
            for (int i = 0; rentOrder.rewards != null && i < rentOrder.rewards.Count; i++)
            {
                if (rentOrder.rewards[i].mi7_order_id != null && !rentOrder.rewards[i].mi7_order_id.Trim().Equals(""))
                {
                    Mi7Order mi7Order = (Mi7Order)((OkObjectResult)(await _mi7Helper.GetMi7Order(rentOrder.rewards[i].mi7_order_id, sessionKey)).Result).Value;
                    rentOrder.rewards[i].mi7Order = mi7Order;
                }

            }
            var ret = Ok(rentOrder);
            return ret;
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrderDetailLog>> SetDetailLog(int id, string status,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            status = Util.UrlDecode(status);
            RentOrderDetail detail = await _context.RentOrderDetail.FindAsync(id);
            switch (status)
            {
                case "已发放":
                    if (detail.pick_date == null)
                    {
                        detail.pick_date = DateTime.Now;
                    }
                    break;
                case "已归还":
                    if (detail.return_date == null)
                    {
                        detail.return_date = DateTime.Now;
                    }
                    break;
                default:
                    break;
            }

            RentOrderDetailLog log = new RentOrderDetailLog()
            {
                id = 0,
                detail_id = id,
                status = status,
                staff_open_id = user.miniAppOpenId,
                prev_value = detail.rent_status == null ? "" : detail.rent_status.Trim(),
                create_date = DateTime.Now
            };
            detail.rent_status = status.Trim();
            detail.update_date = DateTime.Now;
            _context.RentOrderDetail.Entry(detail).State = EntityState.Modified;
            await _context.rentOrderDetailLog.AddAsync(log);
            await _context.SaveChangesAsync();
            return Ok(log);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrderDetail>> SetUnReturn(int id, string sessionKey)
        {
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentOrderDetail detail = await _context.RentOrderDetail.FindAsync(id);
            detail.real_end_date = null;
            _context.Entry(detail).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            await SetDetailLog(id, "已发放", sessionKey);
            detail.log = await _context.rentOrderDetailLog.Where(l => l.detail_id == detail.id)
                .OrderByDescending(l => l.id).AsNoTracking().ToListAsync();
            return Ok(detail);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrderDetail>> SetReturn(int id, float rental,
            double reparation, DateTime returnDate, string memo, string sessionKey, double overTimeCharge = 0)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            memo = Util.UrlDecode(memo);
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentOrderDetail detail = await _context.RentOrderDetail.FindAsync(id);
            detail.real_end_date = returnDate;
            detail.real_rental = rental;
            detail.reparation = reparation;
            detail.memo = memo.Trim();
            detail.overtime_charge = overTimeCharge;
            detail.return_staff = user.miniAppOpenId.Trim();
            _context.Entry(detail).State = EntityState.Modified;
            await _context.SaveChangesAsync();


            await SetDetailLog(id, "已归还", sessionKey);
            detail.log = await _context.rentOrderDetailLog.Where(l => l.detail_id == detail.id)
                .OrderByDescending(l => l.id).ToListAsync();


            bool allReturned = true;

            double rentalTotal = 0;

            RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder((int)detail.rent_list_id, sessionKey, false)).Result).Value;

            for (int i = 0; i < rentOrder.details.Count; i++)
            {
                RentOrderDetail item = rentOrder.details[i];
                rentalTotal = rentalTotal + item.real_rental + item.overtime_charge + item.reparation;
                if (detail.status.Trim().Equals("未归还"))
                {
                    allReturned = false;
                    //break;
                }
            }
            if (allReturned && Math.Round(rentalTotal, 2) >= Math.Round(rentOrder.deposit_final, 2))
            {
                rentOrder.end_date = DateTime.Now;
                _context.Entry(rentOrder);
                await _context.SaveChangesAsync();
            }
            return Ok(detail);
        }
        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrderDetail>> SetPick(int id,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentOrderDetail detail = await _context.RentOrderDetail.FindAsync(id);

            RentOrderDetailLog log = new RentOrderDetailLog()
            {
                id = 0,
                detail_id = detail.id,
                status = RentOrderDetail.RentStatus.已发放.ToString(),
                prev_value = detail.rent_status.Trim(),
                staff_open_id = user.miniAppOpenId.Trim(),
                create_date = DateTime.Now
            };
            await _context.rentOrderDetailLog.AddAsync(log);
            detail.rent_status = RentOrderDetail.RentStatus.已发放.ToString();
            detail.update_date = DateTime.Now;
            _context.RentOrderDetail.Entry(detail).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(detail);
        }
        /*
        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrderDetail>> SetRentStart(int id, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentOrderDetail detail = await _context.RentOrderDetail.FindAsync(id);

            DateTime startDate = DateTime.Now;
            if (detail.start_date != null)
            {
                startDate = (DateTime)detail.start_date;
                startDate = startDate.AddHours(DateTime.Now.Hour).AddMinutes(DateTime.Now.Minute);

            }
            else
            {
                startDate = DateTime.Now;
            }

            detail.start_date = startDate;
            detail.rent_staff = user.miniAppOpenId.Trim();
            _context.Entry(detail).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            await SetDetailLog(detail.id, "已发放", sessionKey);

            return Ok(detail);
        }
        */
        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrder>> Refund(int id, double amount,
            double rentalReduce, double rentalReduceTicket, string memo, string sessionKey)
        {
            Order.OrderRefundController refundHelper = new Order.OrderRefundController(
                    _context, _oriConfig, _httpContextAccessor);
            RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(id, sessionKey, false)).Result).Value;
            if (rentOrder == null)
            {
                return NotFound();
            }
            List<OrderPayment> payments = rentOrder.payments.Where(p => !p.pay_method.Trim().Equals("储值支付"))
                .OrderByDescending(p => p.unRefundedAmount).ToList();
            double unRefundAmount = 0;
            for (int i = 0; i < payments.Count; i++)
            {
                unRefundAmount += payments[i].unRefundedAmount;
            }
            if (amount > unRefundAmount)
            {
                return BadRequest();
            }



            memo = Util.UrlDecode(memo);
            sessionKey = Util.UrlDecode(sessionKey);
            amount = Math.Round(amount, 2);
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            rentOrder.memo = memo;
            rentOrder.refund = amount;
            rentOrder.end_date = DateTime.Now;
            rentOrder.rental_reduce = rentalReduce;
            rentOrder.rental_reduce_ticket = rentalReduceTicket;
            rentOrder.update_date = DateTime.Now;
            _context.Entry(rentOrder).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            double needRefundAmount = amount;
            for (int i = 0; needRefundAmount > 0 && i < payments.Count; i++)
            {
                OrderPayment payment = payments[i];
                if (payment.unRefundedAmount >= needRefundAmount)
                {
                    if (payment.pay_method.Trim().Equals("微信支付"))
                    {
                        await refundHelper.TenpayRefund(payment.id, needRefundAmount, memo, sessionKey);
                    }
                    else
                    {
                        OrderPaymentRefund r = new OrderPaymentRefund()
                        {
                            id = 0,
                            payment_id = payment.id,
                            order_id = payment.order_id,
                            memo = memo,
                            amount = needRefundAmount,
                            state = 1,
                            create_date = DateTime.Now,
                            oper = user.miniAppOpenId.Trim()
                        };
                        await _context.OrderPaymentRefund.AddAsync(r);
                        await _context.SaveChangesAsync();
                    }
                    needRefundAmount = 0;
                }
                else
                {
                    needRefundAmount = needRefundAmount - payment.unRefundedAmount;
                    if (payment.pay_method.Trim().Equals("微信支付"))
                    {
                        await refundHelper.TenpayRefund(payment.id, payment.unRefundedAmount, memo, sessionKey);
                    }
                    else
                    {

                        OrderPaymentRefund r = new OrderPaymentRefund()
                        {
                            id = 0,
                            payment_id = payment.id,
                            order_id = payment.order_id,
                            memo = memo,
                            amount = payment.unRefundedAmount,
                            state = 1,
                            create_date = DateTime.Now,
                            oper = user.miniAppOpenId.Trim()
                        };
                        await _context.OrderPaymentRefund.AddAsync(r);
                        await _context.SaveChangesAsync();
                    }

                }

            }




            //List<OrderPayment> payments = rentOrder
            /*


            if (amount > 0 && rentOrder.order_id > 0 && rentOrder.order != null && rentOrder.payMethod.Trim().Equals("微信支付")
                && rentOrder.order.payments != null && rentOrder.order.payments.Count > 0)
            {
                OrderPayment payment = rentOrder.order.payments[0];
                
                Order.OrderRefundController refundHelper = new Order.OrderRefundController(
                    _context, _oriConfig, _httpContextAccessor);
                double paidAmount = payment.amount;
                if (paidAmount >= amount)
                {
                    await refundHelper.TenpayRefund(payment.id, amount,memo, sessionKey);
                }
            }
            */
            return Ok(rentOrder);


        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrder>> SetPaidManual(int id, string payMethod, string sessionKey)
        {
            RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(id, sessionKey, false)).Result).Value;
            sessionKey = Util.UrlDecode(sessionKey);
            payMethod = Util.UrlDecode(payMethod);
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            if (rentOrder == null || rentOrder.order == null)
            {
                return NotFound();
            }
            if (rentOrder.order.payments == null || rentOrder.order.paymentList.Count == 0)
            {
                OrderPayment payment = new OrderPayment()
                {
                    id = 0,
                    pay_method = payMethod,
                    amount = rentOrder.order.final_price,
                    staff_open_id = user.miniAppOpenId,
                    order_id = rentOrder.order.id
                };
                await _context.OrderPayment.AddAsync(payment);
            }
            OrderOnline order = await _context.OrderOnlines.FindAsync(rentOrder.order_id);
            order.pay_method = payMethod;
            _context.Entry(order).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return await SetPaid(id, sessionKey);

        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrder>> SetPaid(int id, string sessionKey)
        {
            RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(id, sessionKey, false)).Result).Value;
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }

            if (rentOrder == null || rentOrder.order == null
                || rentOrder.order.payments == null || rentOrder.order.paymentList.Count <= 0)
            {
                return NotFound();
            }
            OrderPayment payment = rentOrder.order.payments[0];
            OrderOnline order = rentOrder.order;
            payment.status = "支付成功";
            order.pay_state = 1;
            order.pay_time = DateTime.Now;
            _context.Entry(payment).State = EntityState.Modified;
            _context.Entry(order).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(rentOrder);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrder>> Bind(int id, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(id, sessionKey, false)).Result).Value;
            if (rentOrder == null)
            {
                return NotFound();
            }
            if (rentOrder.open_id.Trim().Equals(""))
            {
                rentOrder.open_id = user.miniAppOpenId;
                _context.Entry(rentOrder).State = EntityState.Modified;
            }
            if (rentOrder.order != null && rentOrder.open_id.Trim().Equals(""))
            {
                OrderOnline order = rentOrder.order;
                order.open_id = user.miniAppOpenId.Trim();
                _context.Entry(order).State = EntityState.Modified;
                if (order.payments != null && order.paymentList.Count > 0)
                {
                    OrderPayment pay = order.payments[0];
                    if (pay.open_id.Trim().Equals(""))
                    {
                        pay.open_id = user.miniAppOpenId.Trim();
                        _context.Entry(pay).State = EntityState.Modified;
                    }

                }
            }
            await _context.SaveChangesAsync();
            return Ok(rentOrder);
        }


        [HttpGet]
        public async Task<ActionResult<RentOrderCollection>> GetUnSettledOrderBefore(DateTime date, string sessionKey, string shop = "")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            shop = Util.UrlDecode(shop);
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }

            var rentOrderList = await _context.RentOrder.FromSqlRaw(" select  * from rent_list  "
                + " where create_date < '" + date.ToShortDateString() + "' and create_date > '" + startDate.ToShortDateString() + "' "
                + " and exists ( select 'a' from rent_list_detail  where rent_list_detail.rent_list_id = rent_list.id and "
                + " (real_end_date is null or real_end_date >= '" + date.ToShortDateString() + "' )) "
                + (shop.Trim().Equals("") ? " " : " and shop = '" + shop.Replace("'", "").Trim()
                + "' and closed = 0  and (finish_date >  '" + date.ToShortDateString() + "' or finish_date is null) "
                //+ " and [id] = 6290 "
                ))
                .ToListAsync();

            RentOrder[] orderArr = new RentOrder[rentOrderList.Count];
            double totalDeposit = 0;
            double totalRental = 0;
            List<RentOrder> list = new List<RentOrder>();

            for (int i = 0; i < orderArr.Length; i++)
            {
                RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderList[i].id, sessionKey, false)).Result).Value;
                if (order.status.Equals("已付押金") || order.status.Equals("已退款"))
                {
                    list.Add(order);
                    //list.Append(order);
                }
                else
                {
                    Console.WriteLine(order.id.ToString() + " " + order.status);
                    continue;
                }

                totalDeposit += order.GetChargedDeposit(date);
                double subTotalRental = 0;
                for (int j = 0; j < order.rentalDetails.Count; j++)
                {
                    RentalDetail detail = order.rentalDetails[j];
                    if (detail.date.Date < date.Date)
                    {
                        subTotalRental = subTotalRental + detail.rental;
                    }

                }
                totalRental = totalRental + subTotalRental;
            }

            RentOrderCollection sum = new RentOrderCollection();
            sum.date = date.Date;
            sum.type = "当日前未完结";
            sum.count = list.Count;
            sum.unRefundDeposit = totalDeposit;
            sum.unSettledRental = totalRental;
            sum.orders = list.ToArray();
            return Ok(sum);
        }

        [HttpGet]
        public async Task<ActionResult<RentOrderCollection>> GetCurrentSameDaySettled(DateTime date, string sessionKey, string shop = "")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            shop = Util.UrlDecode(shop);
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            var rentOrderList = await _context.RentOrder
                .Where(r => (r.create_date.Date == date.Date && r.create_date >= startDate
                    && ((DateTime)r.end_date).Date == date.Date
                    && r.order_id != 0 && (shop.Trim().Equals("") || shop.Trim().Equals(r.shop.Trim()))))
                .Join(_context.OrderOnlines, r => r.order_id, o => o.id,
                    (r, o) => new { r.id, r.start_date, r.end_date, o.pay_state, o.final_price, r.deposit_final, r.refund })
                .Where(o => o.pay_state == 1)
                .ToListAsync();

            double totalDeposit = 0;
            double totalRental = 0;
            List<RentOrder> orderArr = new List<RentOrder>();
            //RentOrder[] orderArr = new RentOrder[rentOrderList.Count];
            for (int i = 0; i < rentOrderList.Count; i++)
            {
                RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderList[i].id, sessionKey, false)).Result).Value;
                //orderArr[i] = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderList[i].id, sessionKey)).Result).Value;
                if (!order.status.Trim().Equals("已退款")
                    && !order.status.Trim().Equals("全部归还")
                    && !order.status.Trim().Equals("已完成"))
                {
                    continue;

                }
                orderArr.Add(order);
                totalDeposit = order.GetChargedDeposit(date.AddDays(1)) + totalDeposit;
                double subTotalRental = 0;
                for (int j = 0; j < order.rentalDetails.Count; j++)
                {
                    RentalDetail detail = order.rentalDetails[j];
                    subTotalRental = subTotalRental + detail.rental;
                }
                totalRental = totalRental + subTotalRental;
            }
            RentOrderCollection sum = new RentOrderCollection();
            sum.date = date.Date;
            sum.type = "日租日结";
            sum.totalDeposit = totalDeposit;
            sum.totalRental = totalRental;
            sum.orders = orderArr.ToArray<RentOrder>();
            sum.count = sum.orders.Length;
            return Ok(sum);
        }

        [HttpGet]
        public async Task<ActionResult<RentOrderCollection>> GetCurrentDayPlaced(DateTime date, string sessionKey, string shop = "")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            shop = Util.UrlDecode(shop).Trim();
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            var rentOrderList = await _context.RentOrder
                .Where(r => (r.create_date.Date == date.Date && r.create_date >= startDate
                && (shop.Equals("") || shop.Equals(r.shop.Trim()))))
                .Join(_context.OrderOnlines, r => r.order_id, o => o.id,
                    (r, o) => new { r.id, r.start_date, r.end_date, o.pay_state, o.final_price, r.deposit_final, r.refund })
                .Where(o => o.pay_state == 1)
                .ToListAsync();

            double totalDeposit = 0;
            double totalRental = 0;
            RentOrder[] orderArr = new RentOrder[rentOrderList.Count];
            for (int i = 0; i < orderArr.Length; i++)
            {
                orderArr[i] = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderList[i].id, sessionKey, false)).Result).Value;
                totalDeposit = orderArr[i].GetChargedDeposit(date.AddDays(1)) + totalDeposit;
                double subTotalRental = 0;
                for (int j = 0; j < orderArr[i].rentalDetails.Count; j++)
                {
                    RentalDetail detail = orderArr[i].rentalDetails[j];
                    if (detail.date.Date <= date.Date)
                    {
                        subTotalRental = subTotalRental + detail.rental;
                    }
                }
                totalRental = totalRental + subTotalRental;
            }
            RentOrderCollection sum = new RentOrderCollection();
            sum.date = date.Date;
            sum.type = "当日新订单";
            sum.totalDeposit = totalDeposit;
            sum.totalRental = totalRental;
            sum.orders = orderArr;
            return Ok(sum);
        }

        [HttpGet]
        public async Task<ActionResult<RentOrderCollection>> GetCurrentDaySettledPlacedBefore(DateTime date, string sessionKey, string shop = "")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            shop = Util.UrlDecode(shop).Trim();
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            var rentOrderList = await _context.RentOrder
                .Where(r => ((shop.Equals("") || shop.Equals(r.shop.Trim()))
                    && r.finish_date != null && ((DateTime)r.finish_date).Date == date.Date)
                    && r.create_date.Date < date.Date)
                .Join(_context.OrderOnlines, r => r.order_id, o => o.id,
                    (r, o) => new { r.id, r.start_date, r.finish_date, o.pay_state, o.final_price, r.deposit_final, r.refund })
                .Where(o => o.pay_state == 1)
                .ToListAsync();

            double totalDeposit = 0;
            double totalRental = 0;
            List<RentOrder> orderArr = new List<RentOrder>();

            for (int i = 0; i < rentOrderList.Count; i++)
            {
                RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderList[i].id, sessionKey, false)).Result).Value;
                if (!order.status.Trim().Equals("已退款")
                    && !order.status.Trim().Equals("全部归还")
                    && !order.status.Trim().Equals("已完成"))
                {
                    continue;

                }
                orderArr.Add(order);
                totalDeposit = order.totalCharge + totalDeposit;
                double subTotalRental = 0;
                for (int j = 0; j < order.rentalDetails.Count; j++)
                {
                    RentalDetail detail = order.rentalDetails[j];
                    if (detail.date.Date <= date.Date)
                    {
                        subTotalRental = subTotalRental + detail.rental;
                    }
                }
                totalRental = totalRental + subTotalRental;
            }
            RentOrderCollection sum = new RentOrderCollection();
            sum.date = date.Date;
            sum.type = "当日新订单";
            sum.totalDeposit = totalDeposit;
            sum.totalRental = totalRental;
            sum.orders = orderArr.ToArray<RentOrder>();
            return Ok(sum);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrderDetail>> ModItemInfo(int id, float rental,
            double reparation, string memo, double overTimeCharge, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            memo = Util.UrlDecode(memo);
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentOrderDetail detail = await _context.RentOrderDetail.FindAsync(id);
            //detail.real_end_date = returnDate;
            detail.real_rental = rental;
            detail.reparation = reparation;
            detail.memo = memo.Trim();
            detail.overtime_charge = overTimeCharge;
            _context.Entry(detail).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(detail);
        }

        [HttpGet]
        public async Task<ActionResult<List<string>>> GetClassList()
        {
            List<string> list = new List<string>();
            list.Add("双板");
            list.Add("双板鞋");
            list.Add("雪杖");
            list.Add("单板");
            list.Add("单板鞋");
            list.Add("头盔");
            list.Add("雪镜");
            list.Add("雪服");
            list.Add("雪裤");
            list.Add("连体雪服");
            list.Add("手套");
            list.Add("护具");
            list.Add("电加热马甲");
            list.Add("运动相机");
            list.Add("无人机");
            list.Add("对讲机");


            var oriList = await _context.RentItem.Select(r => r.@class)
                .AsNoTracking().Distinct().ToListAsync();
            foreach (var ori in oriList)
            {
                bool exists = false;
                foreach (var l in list)
                {
                    if (ori.ToString().Equals(l.ToString()))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists && !ori.ToString().Equals("其他")
                    && ori.ToString().IndexOf("电子") < 0
                    && ori.ToString().IndexOf("雪服上衣") < 0)
                {
                    list.Add(ori.ToString());
                }
            }
            list.Add("其他");
            return Ok(list);
        }


        [HttpGet]
        public async Task<ActionResult<IEnumerable<RentalDetail>>> GetRentDetailReport(DateTime start, DateTime end, string sessionKey)
        {

            //RentalDetail[] details = new RentalDetail[];
            ArrayList details = new ArrayList();
            //RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(detail.rent_list_id, sessionKey)).Result).Value;
            RentOrderCollection beforeOrders = (RentOrderCollection)((OkObjectResult)(await GetUnSettledOrderBefore(start, sessionKey)).Result).Value;
            for (int i = 0; i < beforeOrders.orders.Length; i++)
            {
                RentOrder order = beforeOrders.orders[i];
                for (int j = 0; j < order.rentalDetails.Count; j++)
                {
                    if (order.rentalDetails[j].date >= start.Date && order.rentalDetails[j].date <= end.Date)
                    {
                        RentalDetail dtl = order.rentalDetails[j];
                        dtl._name = order.real_name;
                        dtl._cell = order.cell_number;
                        dtl._shop = order.shop.Trim();
                        dtl._staff = order.staff_name.Trim();
                        details.Add(dtl);
                    }
                }
            }


            var rentOrderIdList = await _context.RentOrder
                .Where(r => (r.create_date.Date >= start.Date && r.create_date.Date <= end.Date))
                .Join(_context.OrderOnlines, r => r.order_id, o => o.id,
                    (r, o) => new { r.id, r.start_date, r.end_date, o.pay_state, o.final_price, r.deposit_final, r.refund, r.staff_name })
                .Where(o => o.pay_state == 1).ToListAsync();
            for (int i = 0; i < rentOrderIdList.Count; i++)
            {
                RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderIdList[i].id, sessionKey, false)).Result).Value;

                for (int j = 0; j < order.rentalDetails.Count; j++)
                {
                    DateTime rentDate = order.rentalDetails[j].date;
                    if (rentDate.Date >= start && rentDate.Date <= end)
                    {
                        RentalDetail dtl = order.rentalDetails[j];
                        dtl._name = order.real_name;
                        dtl._cell = order.cell_number;
                        dtl._shop = order.shop.Trim();
                        dtl._staff = order.staff_name.Trim();
                        details.Add(dtl);
                    }
                }
            }


            RentalDetail[] detailArr = new RentalDetail[details.Count];

            for (int i = 0; i < detailArr.Length; i++)
            {
                var dtl = details[i];
                detailArr[i] = (RentalDetail)dtl;
            }

            return Ok(detailArr);

        }

        [HttpGet("{orderId}")]
        public async Task<ActionResult<RentOrder>> SetMemo(int orderId, string memo, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            memo = Util.UrlDecode(memo).Trim();

            RentOrder? order = await _context.RentOrder.FindAsync(orderId);
            if (order == null)
            {
                return BadRequest();
            }
            string prevMemo = order.memo.Trim();
            order.memo = memo;
            _context.RentOrder.Entry(order).State = EntityState.Modified;
            RentOrderLog log = new RentOrderLog()
            {
                id = 0,
                rent_list_id = order.id,
                memo = "修改备注",
                field_name = "memo",
                prev_value = prevMemo.Trim(),
                oper_member_id = user.member.id
            };
            await _context.rentOrderLog.AddAsync(log);
            await _context.SaveChangesAsync();
            return Ok(order);
        }

        [HttpPost]
        public async Task<ActionResult<RentOrderDetail>> AppendDetail(string sessionKey, RentOrderDetail detail)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            switch (detail.deposit_type.Trim())
            {
                case "立即租赁":
                    detail.start_date = DateTime.Now;
                    detail.pick_date = DateTime.Now;
                    detail.rent_status = RentOrderDetail.RentStatus.已发放.ToString();
                    break;
                case "延时租赁":
                    detail.pick_date = DateTime.Now;
                    detail.start_date = ((DateTime)detail.pick_date).Date.AddDays(1);
                    detail.rent_status = RentOrderDetail.RentStatus.已发放.ToString();
                    break;
                case "先租后取":
                    detail.pick_date = null;
                    detail.start_date = DateTime.Now;
                    detail.rent_status = RentOrderDetail.RentStatus.未领取.ToString();
                    break;
                case "预约租赁":
                    detail.pick_date = null;
                    detail.start_date = null;
                    detail.rent_status = RentOrderDetail.RentStatus.未领取.ToString();
                    break;
                default:
                    break;
            }
            detail.memo = DateTime.Now.ToString() + " " + user.miniAppUser.real_name + " 追加";
            detail.rent_staff = user.miniAppOpenId.Trim();
            await _context.RentOrderDetail.AddAsync(detail);
            await _context.SaveChangesAsync();
            return Ok(detail);
        }

        [HttpPost]
        public async Task<ActionResult<RentalDetail>> UpdateDetail([FromQuery] string sessionKey, [FromBody] RentOrderDetail detail)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }

            RentOrderDetailLog log = new RentOrderDetailLog()
            {
                id = 0,
                detail_id = detail.id,
                status = "修改",
                staff_open_id = user.miniAppOpenId.Trim(),
                create_date = DateTime.Now
            };

            RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder((int)detail.rent_list_id, sessionKey, false)).Result).Value;
            for (int i = 0; i < order.details.Count; i++)
            {
                if (order.details[i].id == detail.id)
                {
                    if (order.details[i].start_date != null && order.details[i].start_date != detail.start_date)
                    {
                        log.status = "修改起租时间";
                        log.prev_value = order.details[i].start_date.ToString();
                    }
                    if (order.details[i].real_end_date != null && order.details[i].real_end_date != detail.real_end_date)
                    {
                        log.status = "修改退租时间";
                        log.prev_value = order.details[i].start_date.ToString();
                    }
                    if (order.details[i].pick_date != null && order.details[i].pick_date != detail.pick_date)
                    {
                        log.status = "修改发放时间";
                        log.prev_value = order.details[i].pick_date.ToString();
                    }
                    if (order.details[i].return_date != null && order.details[i].return_date != detail.return_date)
                    {
                        log.status = "修改归还时间";
                        log.prev_value = order.details[i].return_date.ToString();
                    }
                }
                _context.RentOrderDetail.Entry(order.details[i]).State = EntityState.Detached;
            }
            _context.RentOrder.Entry(order).State = EntityState.Detached;
            await _context.SaveChangesAsync();
            detail.rental_count = order.rentalDetails.Count;
            detail.update_date = DateTime.Now;
            _context.RentOrderDetail.Entry(detail).State = EntityState.Modified;
            _context.RentOrder.Entry(order).State = EntityState.Modified;

            await _context.rentOrderDetailLog.AddAsync(log);
            await _context.SaveChangesAsync();
            return Ok(detail);
        }

        [HttpGet("{detailId}")]
        public async Task<ActionResult<RentOrderDetail>> ReserveMore(int detailId, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentOrderDetail item = await _context.RentOrderDetail.FindAsync(detailId);
            item.id = 0;
            item.real_end_date = null;
            item.start_date = DateTime.Now.Date.AddDays(1);
            item.reparation = 0;
            item.overtime_charge = 0;
            item.deposit_type = "预约租赁";
            await _context.AddAsync(item);
            await _context.SaveChangesAsync();
            return Ok(item);

        }

        [HttpGet("{id}")]
        public async Task<ActionResult<RentOrder>> SetClose(int id, string sessionKey)
        {
            UnicUser user = await Util.GetUser(sessionKey, _context);

            if (!user.isAdmin)
            {
                return BadRequest();
            }

            var result = (await GetRentOrder(id, sessionKey, false)).Result;
            if (!result.GetType().Name.Trim().Equals("OkObjectResult"))
            {
                return NotFound();
            }

            RentOrder order = (RentOrder)((OkObjectResult)result).Value;
            if (order.status.Trim().Equals("未支付"))
            {
                order.closed = 1;
                _context.Entry(order).State = EntityState.Modified;
                await _context.SaveChangesAsync();
            }
            return Ok(order);
        }


        [NonAction]
        public async Task<UnicUser> GetUser(string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            return user;
        }

        [HttpGet]
        public async Task<ActionResult<List<RentOrder>>> GetUnReturnedItems(string sessionKey, string shop)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            shop = Util.UrlDecode(shop);
            UnicUser user = await Util.GetUser(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            List<RentOrder> list = await GetUnReturnedItems(shop);
            return Ok(list);
        }


        [NonAction]
        public async Task<List<RentOrder>> GetUnReturnedItems(string shop = "")
        {
            var rentItemList = await _context.RentOrderDetail
                .FromSqlRaw(" select * from rent_list_detail  "
                + "  where  datepart(hh,rent_list_detail.start_date) <> 0 and  "
                + " datepart(mi,rent_list_detail.start_date) <> 0 "
                + " and datepart(s,rent_list_detail.start_date) <> 0 "
                + " and real_end_date is null order by [id] desc ")
                .AsNoTracking().ToListAsync();
            List<RentOrder> ret = new List<RentOrder>();
            for (int i = 0; i < rentItemList.Count; i++)
            {
                RentOrderDetail item = rentItemList[i];
                if (!item.status.Trim().Equals("已发放"))
                {
                    continue;
                }
                var rL = await _context.RentOrder.Where(r => r.id == item.rent_list_id)
                    .AsNoTracking().ToListAsync();
                if (rL == null || rL.Count == 0)
                {
                    continue;
                }
                RentOrder rentOrder = rL[0];
                if (rentOrder == null)
                {
                    continue;
                }
                if (rentOrder.order_id > 0)
                {
                    rentOrder.order = await _context.OrderOnlines.FindAsync(rentOrder.order_id);
                    rentOrder.order.paymentList = await _context.OrderPayment
                        .Where(p => p.order_id == rentOrder.order_id).ToListAsync();
                    rentOrder.order.refunds = await _context.OrderPaymentRefund
                        .Where(r => r.order_id == rentOrder.order_id).ToListAsync();

                }
                rentOrder.details = (new RentOrderDetail[] { item }).ToList();
                if (!rentOrder.status.Equals("已关闭")
                    && !rentOrder.status.Equals("未支付")
                    && !rentOrder.status.Equals("已退款")
                    && !rentOrder.status.Equals("全部归还"))
                {
                    if (shop.Trim().Equals("") || rentOrder.shop.Trim().Equals(shop))
                    {

                        ret.Add(rentOrder);
                    }
                }
            }
            return ret;
        }

        [HttpGet]
        public async Task<ActionResult<RentOrderList>> GetRentOrderList(DateTime startDate, DateTime endDate, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await Util.GetUser(sessionKey, _context);
            if (user.member.is_admin != 1 && user.member.is_manager != 1)
            {
                return NoContent();
            }
            if (!user.isAdmin)
            {
                return BadRequest();
            }

            RentOrderList list = new RentOrderList();
            list.items = new List<RentOrderList.ListItem>();

            var rentList = await _context.RentOrder.FromSqlRaw(" select * from rent_list where ( pay_option = '招待' "
                + " or exists ( select 'a' from order_online where rent_list.order_id = order_online.id and pay_state = 1 ) ) "
                + " and create_date >= '" + startDate.ToShortDateString() + "' and create_date < '" + endDate.AddDays(1).ToShortDateString() + "' "
                //+ " and [id] = 4297 "

                ).OrderBy(r => r.shop).OrderByDescending(r => r.create_date.Date)
                //.OrderBy(r => r.shop)
                .AsNoTracking().ToListAsync();
            for (int i = 0; i < rentList.Count; i++)
            {
                RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(rentList[i].id, sessionKey, false)).Result).Value;
                if (!rentOrder.pay_option.Trim().Equals("招待")
                    && (rentOrder.order_id == 0 || rentOrder.order == null || rentOrder.order.pay_state != 1))
                {
                    continue;
                }
                if (!rentOrder.status.Equals("全部归还") && !rentOrder.status.Equals("已退款"))
                {
                    continue;
                }

                RentOrderList.ListItem item = new RentOrderList.ListItem();
                item.cell = rentOrder.cell_number.Trim();
                item.name = rentOrder.real_name.Trim();
                item.dayOfWeek = Util.GetDayOfWeek(rentOrder.create_date);
                item.staffOpenId = rentOrder.staff_open_id;
                item.staffName = rentOrder.staff_name;
                item.status = rentOrder.status;
                item.shop = rentOrder.shop.Trim();
                item.orderDate = rentOrder.create_date;
                item.payDate = (rentOrder.order != null && rentOrder.order.pay_state == 1) ?
                    rentOrder.order.pay_time : null;
                item.id = rentOrder.id;
                item.memo = rentOrder.memo;
                item.entertain = rentOrder.pay_option.IndexOf("招待") >= 0 ? "是" : "否";
                for (int j = 0; rentOrder.order_id > 0
                    && rentOrder.order != null && j < rentOrder.order.paymentList.Count; j++)
                {
                    if (rentOrder.order.payments[j].status.Trim().Equals("支付成功")
                        && rentOrder.order.payments[j].out_trade_no != null)
                    {
                        item.out_trade_no = rentOrder.order.payments[j].out_trade_no.Trim();
                        break;
                    }
                }
                //item.out_trade_no = rentOrder.order.payments
                for (int j = 0; rentOrder.order_id != 0 && rentOrder.order != null
                    && j < rentOrder.order.paymentList.Count; j++)
                {
                    if (rentOrder.order.payments[j].status.Trim().Equals("支付成功"))
                    {
                        RentOrderList.RentDeposit deposit = new RentOrderList.RentDeposit();
                        deposit.id = rentOrder.order.payments[j].id;
                        deposit.payDate = (DateTime)rentOrder.order.pay_time;
                        deposit.payMethod = rentOrder.order.payments[j].pay_method.Trim();
                        deposit.amount = rentOrder.order.payments[j].amount;
                        item.deposits = new RentOrderList.RentDeposit[] { deposit };
                    }
                }

                List<RentOrderList.RentRefund> refundList = new List<RentOrderList.RentRefund>();
                //item.refunds = new RentOrderList.RentRefund[rentOrder.order.refunds.Length];
                for (int j = 0; rentOrder.order_id != 0 && rentOrder.order != null
                    && rentOrder.order.refunds != null && j < rentOrder.order.refunds.Count; j++)
                {
                    RentOrderList.RentRefund r = new RentOrderList.RentRefund();
                    r.id = rentOrder.order.refunds[j].id;
                    r.refundDate = rentOrder.order.refunds[j].create_date;
                    r.depositId = rentOrder.order.refunds[j].payment_id;
                    r.amount = rentOrder.order.refunds[j].amount;
                    r.refund_id = rentOrder.order.refunds[j].refund_id.Trim();
                    string operOpenId = rentOrder.order.refunds[j].oper;
                    Member refundUser = await _memberHelper.GetMember(operOpenId, "wechat_mini_openid");
                    if (refundUser != null)
                    {
                        r.staffName = refundUser.real_name.Trim();
                    }
                    else
                    {
                        r.staffName = "";
                    }
                    //r.staffName = rentOrder.order.refunds[j].s
                    //item.refunds[j] = r;
                    refundList.Add(r);
                }
                item.refunds = refundList.ToArray<RentOrderList.RentRefund>();

                List<RentOrderList.Rental> rentalList = new List<RentOrderList.Rental>();
                //item.rental = new RentOrderList.Rental[rentOrder.rentalDetails.Count];
                for (int j = 0; rentOrder.order_id != 0 && rentOrder.order != null
                    && j < rentOrder.rentalDetails.Count; j++)
                {
                    if (rentOrder.rentalDetails[j] == null)
                    {
                        continue;
                    }
                    RentalDetail rentalDtl = rentOrder.rentalDetails[j];
                    bool exists = false;
                    for (int k = 0; k < rentalList.Count; k++)
                    {
                        if (rentalList[k].rentalDate.Date == rentalDtl.date.Date)
                        {
                            rentalList[k].rental += rentalDtl.rental;
                            exists = true;
                            break;
                        }
                    }
                    if (!exists)
                    {
                        RentOrderList.Rental r = new RentOrderList.Rental();
                        r.rental = rentOrder.rentalDetails[j].rental;
                        r.rentalDate = rentOrder.rentalDetails[j].date;
                        rentalList.Add(r);
                    }

                }
                item.rental = rentalList.ToArray();
                list.items.Add(item);

            }

            list.startDate = startDate;
            list.endDate = endDate;
            int dayIndex = 1;
            for (int i = list.items.Count - 1; i >= 0; i--)
            {
                list.items[i].indexOfDay = dayIndex;
                if (i > 0)
                {
                    if (list.items[i].orderDate.Date < list.items[i - 1].orderDate.Date || !list.items[i - 1].shop.Trim().Equals(list.items[i].shop.Trim()))
                    {
                        dayIndex = 1;
                    }
                    else
                    {
                        dayIndex++;
                    }
                }
                list.maxDepositsLength = Math.Max(list.maxDepositsLength,
                    (list.items[i].deposits != null) ? list.items[i].deposits.Length : 0);
                list.maxRefundLength = Math.Max(list.maxRefundLength,
                    (list.items[i].refunds != null) ? list.items[i].refunds.Length : 0);
                list.maxRentalLength = Math.Max(list.maxRentalLength,
                    (list.items[i].rental != null) ? list.items[i].rental.Length : 0);
            }
            return Ok(list);
        }
        [HttpGet("{addPayId}")]
        public async Task<ActionResult<RentAdditionalPayment>> ConfirmAdditionPayment(int addPayId, string payMethod,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            if (payMethod.Trim().Equals("微信支付"))
            {
                return BadRequest();
            }
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await Util.GetUser(sessionKey, _context);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            if (!user.isStaff)
            {
                return BadRequest();
            }
            payMethod = Util.UrlDecode(payMethod);
            RentAdditionalPayment addPay = await _context.rentAdditionalPayment.FindAsync(addPayId);
            RentOrder rentOrder = await _context.RentOrder.FindAsync(addPay.rent_list_id);
            if (addPay == null || rentOrder == null)
            {
                return NotFound();
            }
            double amount = addPay.amount;
            OrderOnline order = new OrderOnline()
            {
                id = 0,
                type = "押金",
                shop = rentOrder.shop.Trim(),
                open_id = user.wlMiniOpenId.Trim(),
                name = rentOrder.real_name.Trim(),
                cell_number = rentOrder.cell_number.Trim(),
                pay_method = payMethod.Trim(),
                pay_memo = "追加押金",
                pay_state = 0,
                order_price = addPay.amount,
                order_real_pay_price = amount,
                ticket_amount = 0,
                other_discount = 0,
                final_price = amount,
                ticket_code = rentOrder.ticket_code.Trim(),
                staff_open_id = addPay.staff_open_id,
                score_rate = 0,
                generate_score = 0

            };
            await _context.OrderOnlines.AddAsync(order);
            await _context.SaveChangesAsync();
            OrderPayment payment = new OrderPayment()
            {
                id = 0,
                order_id = order.id,
                amount = addPay.amount,
                pay_method = payMethod.Trim(),
                staff_open_id = addPay.staff_open_id.Trim(),
                status = "支付成功"
            };
            //order.paymentList.Add(payment);
            await _context.OrderPayment.AddAsync(payment);
            await _context.SaveChangesAsync();
            addPay.order_id = order.id;
            addPay.is_paid = 1;
            addPay.pay_method = payMethod.Trim();
            _context.rentAdditionalPayment.Entry(addPay).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(addPay);
        }
        [HttpGet("{rentListId}")]
        public async Task<ActionResult<RentAdditionalPayment>> CreateAdditionalPayment(int rentListId, double amount, string reason,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await Util.GetUser(sessionKey, _context);
            if (!user.isAdmin)
            {
                return NoContent();
            }
            if (!user.isStaff)
            {
                return BadRequest();
            }
            RentAdditionalPayment addPay = new RentAdditionalPayment()
            {
                rent_list_id = rentListId,
                amount = amount,
                reason = Util.UrlDecode(reason),
                staff_open_id = user.member.wechatMiniOpenId.Trim(),
                pay_method = "微信支付",
                create_date = DateTime.Now
            };
            await _context.rentAdditionalPayment.AddAsync(addPay);
            await _context.SaveChangesAsync();
            return Ok(addPay);
        }
        [HttpGet("{rentAddPayId}")]
        public async Task<ActionResult<OrderOnline>> PlaceAdditionalOrder(int rentAddPayId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            List<RentAdditionalPayment> rentAddPayList = await _context.rentAdditionalPayment
                .Where(r => r.id == rentAddPayId).Include(r => r.rentOrder).AsNoTracking().ToListAsync();
            if (rentAddPayList == null || rentAddPayList.Count == 0)
            {
                return BadRequest();
            }
            RentAdditionalPayment payment = rentAddPayList[0];
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            string payMethod = payment.pay_method;
            UnicUser user = await Util.GetUser(sessionKey, _context);
            if (user == null)
            {
                return NoContent();
            }
            RentOrder rentOrder = payment.rentOrder;
            if (rentOrder == null)
            {
                return NotFound();
            }
            string shop = rentOrder.shop.Trim();
            if (shop.StartsWith("万龙"))
            {
                shop = "万龙体验中心";
            }
            OrderOnline order = new OrderOnline()
            {
                id = 0,
                type = "押金",
                shop = rentOrder.shop.Trim(),
                open_id = user.wlMiniOpenId.Trim(),
                name = rentOrder.real_name.Trim(),
                cell_number = rentOrder.cell_number.Trim(),
                pay_method = payMethod.Trim(),
                pay_memo = "追加押金",
                pay_state = 0,
                order_price = payment.amount,
                order_real_pay_price = payment.amount,
                ticket_amount = 0,
                other_discount = 0,
                final_price = payment.amount,
                ticket_code = rentOrder.ticket_code.Trim(),
                staff_open_id = payment.staff_open_id,
                score_rate = 0,
                generate_score = 0

            };
            await _context.OrderOnlines.AddAsync(order);
            await _context.SaveChangesAsync();
            payment.order_id = order.id;
            payment.order = order;
            payment.update_date = DateTime.Now;
            _context.rentAdditionalPayment.Entry(payment).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            if (order.id == 0)
            {
                return BadRequest();
            }
            OrderPaymentController _orderHelper = new OrderPaymentController(_context, _oriConfig, _httpContextAccessor);
            OrderPayment paymentReal = (OrderPayment)((OkObjectResult)(await _orderHelper.CreatePayment(order.id, payMethod, order.final_price)).Result).Value;
            paymentReal.staff_open_id = order.staff_open_id;
            order.paymentList = (new OrderPayment[] { paymentReal }).ToList();
            return Ok(order);
        }
        [HttpGet("addPayId")]
        public async Task<ActionResult<RentAdditionalPayment>> GetAddPayment(int addPayId, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await Util.GetUser(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            List<RentAdditionalPayment> addPayList = await _context.rentAdditionalPayment.Where(r => r.id == addPayId)
            /*
                .Include(a => a.rentOrder)
                    .ThenInclude(r => r.details)
                .Include(a => a.order)
                    .ThenInclude(o => o.payments)
            */
                .AsNoTracking().ToListAsync();
            if (addPayList == null || addPayList.Count == 0)
            {
                return NotFound();
            }
            return Ok(addPayList[0]);
        }
        [NonAction]
        public async Task AdditionalOrderPaid(int orderId)
        {
            List<RentAdditionalPayment> addPayList = await _context.rentAdditionalPayment
                .Where(r => r.order_id == orderId).ToListAsync();
            for (int i = 0; i < addPayList.Count; i++)
            {
                RentAdditionalPayment addPay = addPayList[i];
                addPay.is_paid = 1;
                _context.rentAdditionalPayment.Entry(addPay).State = EntityState.Modified;
            }
            await _context.SaveChangesAsync();
        }
        [HttpGet("{orderId}")]
        public async Task<ActionResult<RentOrder>> SetFinish(int orderId, DateTime? finishDate,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await Util.GetUser(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            if (finishDate == null)
            {
                if (user.member.is_manager == 0
                && user.member.is_admin == 0
                && user.member.is_staff == 0
                )
                {
                    return BadRequest();
                }
            }
            RentOrder rentOrder = (RentOrder)((OkObjectResult)(await GetRentOrder(orderId, sessionKey)).Result).Value;
            if (rentOrder == null)
            {
                return NotFound();
            }

            if (rentOrder.finish_date == null && !rentOrder.status.Trim().Equals("已退款") && !rentOrder.status.Trim().Equals("全部归还"))
            {
                return NoContent();
            }
            RentOrderLog log = new RentOrderLog()
            {
                id = 0,
                rent_list_id = rentOrder.id,
                memo = finishDate == null ? "订单重开" : "订单完成",
                field_name = "finish_date",
                prev_value = rentOrder.finish_date == null ? null : rentOrder.finish_date.ToString(),
                oper_member_id = user.member.id
            };
            await _context.rentOrderLog.AddAsync(log);
            rentOrder.finish_date = finishDate;
            if (rentOrder.hide == 0 && finishDate != null && rentOrder.shop.Trim().Equals("万龙体验中心"))
            {
                List<RentReward> rentRewards = await _context.rentReward
                    .Where(r => r.need_correct == 1 && r.correct_rent_list_id == null)
                    .OrderBy(r => r.id).ToListAsync();
                if (rentRewards.Count > 0)
                {
                    RentReward r = rentRewards[0];
                    r.correct_rent_list_id = rentOrder.id;
                    r.update_date = DateTime.Now;
                    _context.rentReward.Entry(r).State = EntityState.Modified;
                    rentOrder.hide = 1;

                }
            }
            rentOrder.update_date = DateTime.Now;
            _context.RentOrder.Entry(rentOrder).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return Ok(rentOrder);
        }
        [HttpGet("{orderId}")]
        public async Task<ActionResult<List<RentOrderLog>>> GetRentOrderLogs(int orderId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await Util.GetUser(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            return await _context.rentOrderLog.Where(l => l.rent_list_id == orderId)
                .Include(l => l.member).OrderByDescending(l => l.id).ToListAsync();
        }
        [HttpPost("{rentOrderId}")]
        public async Task<ActionResult<RentOrder>> Refund([FromRoute] int rentOrderId,
            [FromBody] List<OrderPaymentRefund> refundList, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await Util.GetUser(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentOrder rentOrder = await _context.RentOrder.FindAsync(rentOrderId);
            if (rentOrder == null || rentOrder.order_id == null || rentOrder.order_id <= 0)
            {
                return NotFound();
            }
            OrderPaymentController _refunder = new OrderPaymentController(_context, _oriConfig, _httpContextAccessor);
            for (int i = 0; i < refundList.Count; i++)
            {
                OrderPaymentRefund refund = refundList[i];
                try
                {
                    await _refunder.Refund(refund.payment_id, refund.amount, "租赁退押金", sessionKey, sessionType);
                }
                catch
                {

                }
            }
            RentOrder order = (RentOrder)((OkObjectResult)(await GetRentOrder(rentOrderId, sessionKey, false)).Result).Value;
            return Ok(order);
        }

        [HttpPost]
        public async Task<ActionResult<RentReward>> RewardRefund([FromBody] RentReward reward,
            [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await Util.GetUser(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            reward.oper_member_id = user.member.id;
            await _context.rentReward.AddAsync(reward);
            await _context.SaveChangesAsync();
            OrderPaymentController _refunder = new OrderPaymentController(_context, _oriConfig, _httpContextAccessor);
            bool allSuccess = true;
            for (int i = 0; i < reward.rentRewardRefunds.Count; i++)
            {
                RentRewardRefund r = reward.rentRewardRefunds[i];
                try
                {
                    OrderPaymentRefund refund = (OrderPaymentRefund)((OkObjectResult)(await _refunder.Refund(r.payment_id, r.amount, "销售抵扣租赁", sessionKey, sessionType)).Result).Value;
                    r.refund_id = refund.id;
                    r.update_date = DateTime.Now;
                    _context.rentRewardRefund.Entry(r).State = EntityState.Modified;
                    if (refund.refund_id.Trim().Equals(""))
                    {
                        allSuccess = false;
                    }
                }
                catch
                {
                    allSuccess = false;
                }
            }
            if (allSuccess)
            {
                reward.refund_finish = 1;
                reward.update_date = DateTime.Now;
                RentOrder rentOrder = await _context.RentOrder.FindAsync(reward.rent_list_id);
                if (rentOrder.finish_date != null)
                {
                    DateTime fDate = (DateTime)rentOrder.finish_date;
                    if (fDate.Year == reward.create_date.Year
                        && fDate.Month == reward.create_date.Month)
                    {
                        reward.need_correct = 0;
                    }
                }
                else
                {
                    reward.need_correct = 0;
                }
                _context.rentReward.Entry(reward).State = EntityState.Modified;
            }
            await _context.SaveChangesAsync();

            for (int i = 0; i < reward.rentRewardRefunds.Count; i++)
            {
                RentRewardRefund rewardRefund = reward.rentRewardRefunds[i];
                await _context.rentRewardRefund.Entry(rewardRefund).Reference(r => r.payment).LoadAsync();
                await _context.rentRewardRefund.Entry(rewardRefund).Reference(r => r.refund).LoadAsync();
            }

            return Ok(reward);
        }
        [HttpGet("{addPayId}")]
        public async Task<ActionResult<RentAdditionalPayment>> GetAdditionalPayment(int addPayId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await Util.GetUser(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            RentAdditionalPayment payment = await _context.rentAdditionalPayment.FindAsync(addPayId);
            if (payment == null)
            {
                return NotFound();
            }
            return Ok(payment);
        }
        private bool RentOrderExists(int id)
        {
            return _context.RentOrder.Any(e => e.id == id);
        }
    }
}
