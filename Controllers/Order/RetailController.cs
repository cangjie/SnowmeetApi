using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Order;
using TencentCloud.Ocr.V20181119.Models;
namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class RetailController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private IConfiguration _config;
        public RetailController(ApplicationDBContext context, IConfiguration config)
        {
            _db = context;
            _config = config;
        }
        [HttpGet]
        public async Task ExportMi7Order(DateTime startDate)
        {
            List<Models.Order.Retail> rl = (List<Models.Order.Retail>)((OkObjectResult)(await ShowMi7Order(startDate)).Result).Value;
            List<Models.Mi7ExportedSaleDetail> details = await _db.mi7ExportedSaleDetail.ToListAsync();
            int maxPaymentNum = 0;
            int maxRefundNum = 0;
            for (int i = 0; i < rl.Count; i++)
            {
                Retail r = rl[i];
                r.details = details.Where(d => d.单据编号.Trim().Equals(r.mi7OrderId.Trim())).ToList();
                maxPaymentNum = Math.Max(r.payments.Count, maxPaymentNum);
                maxRefundNum = Math.Max(r.refunds.Count, maxRefundNum);
            }
            List<string> head = [
                "序号", "七色米订单号", "地区" ,"出货店铺", "业务类型", "业务日期", "业务时间", "开单日期", "开单时间",
                "商品类别", "商品编号", "商品名称", "零售单价", "折扣", "折后单价","数量", "总额", "支付笔数", "支付金额", "退款笔数", "退款金额"];
            int commonFieldsNum = head.Count;
            string[] headPayment = ["收款门店", "支付方式", "收款单号", "收款金额", "收款日期", "收款时间"];
            string[] headRefund = ["退款单号", "退款金额", "退款日期", "退款时间"];
            for (int i = 0; i < maxPaymentNum; i++)
            {
                for (int j = 0; j < headPayment.Length; j++)
                {
                    head.Add(headPayment[j] + (i + 1).ToString());
                }
            }
            for (int i = 0; i < maxRefundNum; i++)
            {
                for (int j = 0; j < headRefund.Length; j++)
                {
                    head.Add(headRefund[j] + (i + 1).ToString());
                }
            }
            string nullStr = "【-】";
            XSSFWorkbook workbook = new XSSFWorkbook();
            ISheet sheet = workbook.CreateSheet("Sheet1");
            
            IRow headRow = sheet.CreateRow(0);
            headRow.Height = 500;
            
            IFont headFont = workbook.CreateFont();
            headFont.Color = NPOI.HSSF.Util.HSSFColor.White.Index;
            headFont.IsBold = true;

            ICellStyle headStyle = workbook.CreateCellStyle();
            headStyle.Alignment = HorizontalAlignment.Center;
            headStyle.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Black.Index;
            headStyle.FillPattern = FillPattern.SolidForeground;
            headStyle.SetFont(headFont);
            headStyle.VerticalAlignment = VerticalAlignment.Center;

            IDataFormat format = workbook.CreateDataFormat();
            ICellStyle styleDate = workbook.CreateCellStyle();
            styleDate.DataFormat = format.GetFormat("yyyy-MM-dd");
            styleDate.VerticalAlignment = VerticalAlignment.Center;

            ICellStyle styleTime = workbook.CreateCellStyle();
            styleTime.DataFormat = format.GetFormat("HH:mm:ss");
            styleTime.VerticalAlignment = VerticalAlignment.Center;

            ICellStyle styleNumber = workbook.CreateCellStyle();
            styleNumber.DataFormat = format.GetFormat("0");
            styleNumber.VerticalAlignment = VerticalAlignment.Center;

            ICellStyle styleMoney = workbook.CreateCellStyle();
            //styleMoney.DataFormat = format.GetFormat("¥#,##0");
            styleMoney.DataFormat = 59;
            styleMoney.VerticalAlignment = VerticalAlignment.Center;

            ICellStyle styleMoneyRed = workbook.CreateCellStyle();
            //styleMoney.DataFormat = format.GetFormat("¥#,##0");
            styleMoneyRed.DataFormat = 59;
            styleMoneyRed.VerticalAlignment = VerticalAlignment.Center;

            IFont fontRed = workbook.CreateFont();
            fontRed.Color = NPOI.HSSF.Util.HSSFColor.Red.Index;
            styleMoneyRed.SetFont(fontRed);
            ICellStyle stylePercent = workbook.CreateCellStyle();
            stylePercent.DataFormat = format.GetFormat("0.00%");
            stylePercent.VerticalAlignment = VerticalAlignment.Center;

            ICellStyle styleTextRed = workbook.CreateCellStyle();
            styleTextRed.SetFont(fontRed);
            styleTextRed.VerticalAlignment = VerticalAlignment.Center;

            ICellStyle textCenterStyle = workbook.CreateCellStyle();
            textCenterStyle.Alignment = HorizontalAlignment.Center;
            textCenterStyle.VerticalAlignment = VerticalAlignment.Center;
            //textCenterStyle.VerticalAlignment = VerticalAlignment.Center;
            for (int i = 0; i < head.Count; i++)
            {
                ICell headCell = headRow.CreateCell(i);
                headCell.SetCellValue(head[i].Trim());
                headCell.SetCellType(CellType.String);
                headCell.CellStyle = headStyle;
                
                switch(i)
                {
                    case 0:
                        sheet.SetColumnWidth(i, 1500);
                        break;
                    case 1:
                        sheet.SetColumnWidth(i, 5000);
                        break;
                    default:
                        break;
                }
                
            }
            int fixDetailCount = 0;
            for (int i = 0; i < rl.Count; i++)
            {
                Retail r = rl[i];
                IRow dr = sheet.CreateRow(i + 1 + fixDetailCount);
                dr.Height = 500;
                int mergeBaseIndex = i + 1 + fixDetailCount;
                string mi7Shop = nullStr;
                string region = nullStr;
                string type = nullStr;
                string date = "";
                string time = "";
                DateTime? orderDate = null;
                if (r.details != null && r.details.Count > 0)
                {
                    mi7Shop = r.details[0].所属门店.Replace("】【", "】 【");
                    string[] shopArr = mi7Shop.Split(' ');
                    if (shopArr.Length == 2)
                    {
                        region = shopArr[0];
                        mi7Shop = shopArr[1];
                    }
                    if (r.details[0].出库仓库.IndexOf("零售") >= 0)
                    {
                        type = "零售";
                    }
                    if (r.details[0].出库仓库.IndexOf("租赁") >= 0)
                    {
                        type = "租赁";
                    }
                    date = ((DateTime)r.details[0].业务日期).ToString("yyyy-MM-dd");
                    time = ((DateTime)r.details[0].业务日期).ToString("hh:mm:ss");
                    if (r.orders.Count > 0)
                    {
                        orderDate = r.orders[0].create_date;
                    }
                }
                string unitPriceStr = (r.details.Count == 0) ? "0" : r.details[0].单价;
                double unitPrice = 0;
                try
                {
                    unitPrice = double.Parse(unitPriceStr);
                }
                catch
                {

                }
                double discount = 100;
                try
                {
                    discount = double.Parse(r.details[0].折扣);
                }
                catch
                {

                }
                int count = 0;
                try
                {
                    count = r.details.Count == 0 ? 0 : int.Parse(r.details[0].数量);

                }
                catch
                {
                    //cell.SetCellValue(0);
                }
                double unitPriceAfterDiscount = 0;
                try
                {
                    unitPriceAfterDiscount = double.Parse(r.details[0].折后单价);
                }
                catch
                {

                }
                double summaryPrice = 0;
                try
                {
                    summaryPrice = double.Parse(r.details[0].总额);
                }
                catch
                {

                }

                for (int j = 0; j < commonFieldsNum; j++)
                {
                    ICell cell = dr.CreateCell(j);
                    switch (j)
                    {
                        case 0:
                            cell.SetCellValue(i + 1);
                            cell.CellStyle = styleNumber;
                            //cell.SetCellType(CellType.Numeric);
                            break;
                        case 1:
                            cell.SetCellValue(r.mi7OrderId);
                            cell.SetCellType(CellType.String);
                            cell.CellStyle = textCenterStyle;
                            break;
                        case 2:
                            cell.SetCellValue(region.Replace("【", "").Replace("】", "").Trim());
                            cell.SetCellType(CellType.String);
                            cell.CellStyle = textCenterStyle;
                            break;
                        case 3:
                            cell.SetCellValue(mi7Shop.Replace("【", "").Replace("】", "").Trim());
                            cell.SetCellType(CellType.String);
                            cell.CellStyle = textCenterStyle;
                            break;
                        case 4:
                            cell.SetCellValue(type.Trim());
                            cell.SetCellType(CellType.String);
                            cell.CellStyle = textCenterStyle;
                            break;
                        case 5:
                            try
                            {
                                cell.SetCellValue(DateTime.Parse(date).Date);
                                cell.CellStyle = styleDate;
                            }
                            catch
                            {
                                cell.SetCellValue(nullStr);
                            }
                            break;
                        case 6:
                            cell.SetCellValue(time);
                            cell.CellStyle = styleTime;
                            break;
                        case 7:
                            if (orderDate == null)
                            {
                                cell.SetCellValue(nullStr);
                            }
                            else
                            {
                                cell.SetCellValue(((DateTime)orderDate).ToString("yyyy-MM-dd"));
                                cell.CellStyle = styleDate;
                            }
                            break;
                        case 8:
                            if (orderDate == null)
                            {
                                cell.SetCellValue(nullStr);
                            }
                            else
                            {
                                cell.SetCellValue(((DateTime)orderDate).ToString("hh:mm:ss"));
                                cell.CellStyle = styleTime;
                            }
                            break;
                        case 9:
                            cell.SetCellValue(r.details.Count == 0 ? nullStr : r.details[0].商品分类);
                            break;
                        case 10:
                            cell.SetCellValue(r.details.Count == 0 ? nullStr : r.details[0].商品编号);
                            break;
                        case 11:
                            cell.SetCellValue(r.details.Count == 0 ? nullStr : r.details[0].商品名称);
                            break;
                        case 12:
                            cell.SetCellValue(unitPrice);
                            cell.CellStyle = styleMoney;
                            break;
                        case 15:
                            cell.SetCellValue(count);
                            cell.CellStyle = styleNumber;
                            cell.SetCellType(CellType.Numeric);
                            break;
                        case 13:

                            cell.SetCellValue(discount / 100);
                            cell.CellStyle = stylePercent;
                            break;
                        case 14:
                            cell.SetCellValue(unitPriceAfterDiscount);
                            if (Math.Round(unitPriceAfterDiscount, 0) == Math.Round(unitPrice * discount / 100, 0))
                            {
                                cell.CellStyle = styleMoney;
                            }
                            else
                            {
                                cell.CellStyle = styleMoneyRed;
                            }
                            break;
                        case 16:
                            cell.SetCellValue(summaryPrice);
                            if (Math.Round(summaryPrice, 0) == Math.Round(unitPriceAfterDiscount * count, 0))
                            {
                                cell.CellStyle = styleMoney;
                            }
                            else
                            {
                                cell.CellStyle = styleMoneyRed;
                            }
                            break;
                        case 17:
                            cell.SetCellValue(r.payments.Count);
                            cell.CellStyle = styleNumber;
                            break;
                        case 18:
                            double totalPaidAmount = 0;
                            for (int k = 0; k < r.details.Count; k++)
                            {
                                try
                                {
                                    totalPaidAmount += double.Parse(r.details[k].总额);
                                }
                                catch
                                {

                                }
                            }
                            cell.SetCellValue(r.paidAmount);
                            if (Math.Round(totalPaidAmount, 0) == Math.Round(r.paidAmount, 0))
                            {
                                cell.CellStyle = styleMoney;
                            }
                            else
                            {
                                cell.CellStyle = styleMoneyRed;
                            }
                            break;
                        case 19:
                            cell.SetCellValue(r.refunds.Count);
                            cell.CellStyle = styleNumber;
                            break;
                        case 20:
                            cell.SetCellValue(r.refundAmount);
                            if (r.refundAmount != 0 && r.refundAmount != r.paidAmount)
                            {
                                cell.CellStyle = styleMoneyRed;
                            }
                            else
                            {
                                cell.CellStyle = styleMoney;
                            }
                            break;
                        default:
                            break;
                    }
                }
                int idxBase = 0;
                for (int j = 0; j < maxPaymentNum; j++)
                {
                    for (int k = 0; k < headPayment.Length; k++)
                    {
                        ICell cell = dr.CreateCell(commonFieldsNum + idxBase);
                        if (r.payments.Count <= j)
                        {
                            cell.SetCellValue(nullStr);
                        }
                        else
                        {
                            switch (k)
                            {
                                case 0:
                                    string chargeShop = "未知";
                                    switch (r.payments[j].mch_id)
                                    {
                                        case 6:
                                            chargeShop = "南山店";
                                            break;
                                        case 9:
                                            chargeShop = "旗舰店";
                                            break;
                                        case 12:
                                            chargeShop = "万龙店";
                                            break;
                                        default:
                                            if (r.payments[j].mch_id == null)
                                            {
                                                chargeShop = nullStr;
                                            }
                                            break;
                                    }
                                    cell.SetCellValue(chargeShop);
                                    if (!mi7Shop.Replace("【", "").Replace("】", "").Trim().Equals(chargeShop) && !chargeShop.Equals(nullStr))
                                    {
                                        cell.CellStyle = styleTextRed;
                                    }
                                    break;
                                case 1:
                                    cell.SetCellValue(r.payments[j].pay_method.Trim());
                                    break;
                                case 2:
                                    if (r.payments[j].pay_method.Trim().Equals("微信支付"))
                                    {
                                        cell.SetCellValue(r.payments[j].out_trade_no);
                                    }
                                    else
                                    {
                                        cell.SetCellValue(nullStr);
                                    }
                                    break;
                                case 3:
                                    cell.SetCellValue(r.payments[j].amount);
                                    cell.CellStyle = styleMoney;
                                    break;
                                case 4:
                                    cell.SetCellValue(r.payments[j].create_date.Date);
                                    cell.CellStyle = styleDate;
                                    break;
                                case 5:
                                    cell.SetCellValue(r.payments[j].create_date.ToShortTimeString());
                                    cell.CellStyle = styleDate;
                                    break;
                                default:
                                    break;
                            }
                        }
                        idxBase++;
                    }
                }
                for (int j = 0; j < maxRefundNum; j++)
                {
                    for (int k = 0; k < headRefund.Length; k++)
                    {
                        ICell cell = dr.CreateCell(commonFieldsNum + idxBase);
                        if (r.refunds.Count <= j)
                        {
                            cell.SetCellValue(nullStr);
                        }
                        else
                        {
                            switch(k)
                            {
                                case 0:
                                    cell.SetCellValue(r.refunds[j].refund_id);
                                    break;
                                case 1:
                                    cell.SetCellValue(r.refunds[j].amount);
                                    cell.CellStyle = styleMoney;
                                    break;
                                case 2:
                                    cell.SetCellValue(r.refunds[j].create_date.Date);
                                    cell.CellStyle = styleDate;
                                    break;
                                case 3:
                                    cell.SetCellValue(r.refunds[j].create_date.ToShortTimeString());
                                    cell.CellStyle = styleTime;
                                    break;
                                default:
                                    break;
                            }
                        }
                        idxBase++;
                    }
                }

                //fixDetailCount++;
                for (int k = 1; k < rl[i].details.Count; k++)
                {
                    fixDetailCount++;
                    IRow drDetail = sheet.CreateRow(i + 1 + fixDetailCount);
                    drDetail.Height = 500;
                    unitPriceStr = (r.details.Count == 0) ? "0" : r.details[k].单价;
                    unitPrice = 0;
                    try
                    {
                        unitPrice = double.Parse(unitPriceStr);
                    }
                    catch
                    {

                    }
                    discount = 100;
                    try
                    {
                        discount = double.Parse(r.details[k].折扣);
                    }
                    catch
                    {

                    }
                    count = 0;
                    try
                    {
                        count = r.details.Count == 0 ? 0 : int.Parse(r.details[k].数量);

                    }
                    catch
                    {
                        //cell.SetCellValue(0);
                    }
                    unitPriceAfterDiscount = 0;
                    try
                    {
                        unitPriceAfterDiscount = double.Parse(r.details[k].折后单价);
                    }
                    catch
                    {

                    }
                    summaryPrice = 0;
                    try
                    {
                        summaryPrice = double.Parse(r.details[k].总额);
                    }
                    catch
                    {

                    }
                    for (int j = 0; j < commonFieldsNum; j++)
                    {
                        ICell cell = drDetail.CreateCell(j);
                        switch (j)
                        {
                            case 0:
                            case 1:
                            case 2:
                            case 3:
                            case 4:
                            case 5:
                            case 6:
                            case 7:
                            case 8:
                            case 17:
                            case 18:
                            case 19:
                            case 20:
                            case 21:
                                if (k == rl[i].details.Count - 1)
                                {
                                    sheet.AddMergedRegion(new NPOI.SS.Util.CellRangeAddress(
                                        mergeBaseIndex, i + 1 + fixDetailCount, j, j));
                                }
                                break;
                            case 9:
                                cell.SetCellValue(r.details.Count == 0 ? nullStr : r.details[k].商品分类);
                                break;
                            case 10:
                                cell.SetCellValue(r.details.Count == 0 ? nullStr : r.details[k].商品编号);
                                break;
                            case 11:
                                cell.SetCellValue(r.details.Count == 0 ? nullStr : r.details[k].商品名称);
                                break;
                            case 12:
                                cell.SetCellValue(unitPrice);
                                cell.CellStyle = styleMoney;
                                break;
                            case 15:
                                cell.SetCellValue(count);
                                cell.CellStyle = styleNumber;
                                cell.SetCellType(CellType.Numeric);
                                break;
                            case 13:
                                cell.SetCellValue(discount / 100);
                                cell.CellStyle = stylePercent;
                                break;
                            case 14:


                                cell.SetCellValue(unitPriceAfterDiscount);
                                if (Math.Round(unitPriceAfterDiscount, 0) == Math.Round(unitPrice * discount / 100, 0))
                                {
                                    cell.CellStyle = styleMoney;
                                }
                                else
                                {
                                    cell.CellStyle = styleMoneyRed;
                                }
                                break;
                            case 16:
                                cell.SetCellValue(summaryPrice);
                                if (Math.Round(summaryPrice, 0) == Math.Round(unitPriceAfterDiscount * count, 0))
                                {
                                    cell.CellStyle = styleMoney;
                                }
                                else
                                {
                                    cell.CellStyle = styleMoneyRed;
                                }
                                break;
                            default:
                                break;
                        }
                    }
                    int idx = 0;
                    for (int j = 0; j < maxPaymentNum; j++)
                    {
                        for (int l = 0; l < headPayment.Length; l++)
                        {
                            ICell cell = drDetail.CreateCell(commonFieldsNum + idx);
                            if (k == rl[i].details.Count - 1)
                            {
                                sheet.AddMergedRegion(new NPOI.SS.Util.CellRangeAddress(
                                    mergeBaseIndex, i + 1 + fixDetailCount,
                                    commonFieldsNum + idx, commonFieldsNum + idx));
                            }
                            idx++;
                        }
                    }
                    for (int j = 0; j < maxRefundNum; j++)
                    {
                        for (int l = 0; l < headRefund.Length; l++)
                        {
                            ICell cell = drDetail.CreateCell(commonFieldsNum + idx);
                            if (k == rl[i].details.Count - 1)
                            {
                                sheet.AddMergedRegion(new NPOI.SS.Util.CellRangeAddress(
                                    mergeBaseIndex, i + 1 + fixDetailCount,
                                    commonFieldsNum + idx, commonFieldsNum + idx));
                            }
                            idx++;
                        }
                    }
                }


            }
            string filePath = $"{Environment.CurrentDirectory}" + "/mi7.xlsx";
            using (var file = System.IO.File.Create(filePath))
            {
                workbook.Write(file);
            }
        }
        [HttpGet]
        public async Task<ActionResult<List<Models.Order.Retail>>> ShowMi7Order(DateTime startDate)
        {
            List<Models.Order.Retail> retailList = new List<Retail>();
            var mi7List = await (from m in _db.mi7Order
                .Where(m => m.create_date.Date >= startDate.Date && m.mi7_order_id.Trim().StartsWith("XSD") //&& m.mi7_order_id.Trim().Equals("XSD20250326000A")
                )
                                 group m by m.mi7_order_id into g
                                 select new { mi7OrderId = g.Key, salePrie = g.Sum(g => g.sale_price), charge = g.Sum(g => g.real_charge), count = g.Count() })
                .AsNoTracking().ToListAsync();
            List<Mi7Order> mi7Orders = await _db.mi7Order.Where(m => m.create_date.Date >= startDate.Date)
                .Include(m => m.order).ThenInclude(o => o.paymentList.Where(p => p.status.Trim().Equals("支付成功")).OrderBy(p => p.id))
                .ThenInclude(p => p.refunds.Where(r => r.state == 1 || !r.refund_id.Trim().Equals("")))
                .AsNoTracking().ToListAsync();
            //List<Models.Mi7ExportedSaleDetail> detail = await _db.mi7ExportedSaleDetail.ToListAsync();
            for (int i = 0; i < mi7List.Count; i++)
            {
                Retail r = new Retail()
                {
                    mi7OrderId = mi7List[i].mi7OrderId,
                    salePrie = mi7List[i].salePrie,
                    charge = mi7List[i].charge,
                    count = mi7List[i].count
                };
                int count = 0;
                double charge = 0;
                double salePrie = 0;
                List<Mi7Order> subMi7Orders = mi7Orders.Where(m => m.mi7_order_id.Trim().Equals(r.mi7OrderId.Trim()))
                    .OrderBy(o => o.id).ToList();
                List<OrderOnline> orderList = new List<OrderOnline>();
                for (int j = 0; j < subMi7Orders.Count; j++)
                {
                    Mi7Order mi7Order = subMi7Orders[j];
                    if (mi7Order == null || mi7Order.order == null || mi7Order.order.paymentList == null || mi7Order.order.paymentList.Count == 0)
                    {
                        continue;
                    }
                    if (mi7Order != null && orderList.Where(o => o.id == mi7Order.order_id).ToList().Count == 0)
                    {
                        orderList.Add(mi7Order.order);
                        count++;
                        salePrie += mi7Order.sale_price;
                        charge += mi7Order.real_charge;
                    }
                }
                r.count = count;
                r.orders = orderList;
                r.charge = charge;
                r.salePrie = salePrie;
                if (orderList.Count > 0)
                {
                    retailList.Add(r);
                }
            }
            List<Retail> newList = retailList.OrderBy(r => r.orders[0].create_date).ToList();
            return Ok(newList);
        }
    }
}