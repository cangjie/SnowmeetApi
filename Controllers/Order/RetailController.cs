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
            for(int i = 0; i < rl.Count; i++)
            {
                Retail r = rl[i];
                r.details = details.Where(d => d.单据编号.Trim().Equals(r.mi7OrderId.Trim())).ToList();
                maxPaymentNum = Math.Max(r.payments.Count, maxPaymentNum);
                maxRefundNum = Math.Max(r.refunds.Count, maxRefundNum);
            }
            List<string> head = [
                "序号", "七色米订单号",	"店铺", "业务类型", "业务日期", "业务时间", "开单日期", "开单时间", "发货日期",
                "开单明细数", "零售总价", "成交总价", "支付笔数", "支付金额", "退款笔数", "退款金额", "支付方式", "商品类别"];
            string[] headPayment = ["商品名称支付方式", "收款单号", "收款日期", "收款时间"];
            string[] headRefund = ["退款单号", "退款金额", "退款日期", "退款时间"];
            for(int i = 0; i < maxPaymentNum; i++)
            {
                for(int j = 0; j < headPayment.Length; j++)
                {
                    head.Add(headPayment[j] + (i+1).ToString());
                }
            }
            for(int i = 0; i < maxRefundNum; i++)
            {
                for(int j = 0; j < headRefund.Length; j++)
                {
                    head.Add(headRefund[j] + (i+1).ToString());
                }
            }
            string nullStr = "【-】";
            XSSFWorkbook workbook = new XSSFWorkbook();
            ISheet sheet = workbook.CreateSheet("Sheet1");
            IRow headRow = sheet.CreateRow(0);
            IFont headFont = workbook.CreateFont();
            headFont.Color = NPOI.HSSF.Util.HSSFColor.White.Index;
            headFont.IsBold = true;
            ICellStyle headStyle = workbook.CreateCellStyle();
            headStyle.Alignment = HorizontalAlignment.Center;
            headStyle.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Black.Index;
            headStyle.FillPattern = FillPattern.SolidForeground;
            headStyle.SetFont(headFont);
            //workbook.get
            //headStyle.FillPattern = FillPatternType
            for(int i = 0; i < head.Count; i++)
            {
                ICell headCell = headRow.CreateCell(i);
                headCell.SetCellValue(head[i].Trim());
                headCell.SetCellType(CellType.String);
                headCell.CellStyle = headStyle;
               
            }
            using(var file = System.IO.File.Create("mi7.xlsx"))
            {
                workbook.Write(file);
            }
        }
        [HttpGet]
        public async Task<ActionResult<List<Models.Order.Retail>>> ShowMi7Order(DateTime startDate)
        {
            List<Models.Order.Retail> retailList = new List<Retail>();
            var mi7List  =  await (from m in _db.mi7Order
                .Where(m => m.create_date.Date >= startDate.Date && m.mi7_order_id.Trim().StartsWith("XSD") //&& m.mi7_order_id.Trim().Equals("XSD20250326000A")
                )
                group m by m.mi7_order_id into g
                select new { mi7OrderId = g.Key, salePrie = g.Sum(g => g.sale_price), charge = g.Sum(g => g.real_charge), count = g.Count() })
                .AsNoTracking().ToListAsync();
            List<Mi7Order> mi7Orders = await _db.mi7Order.Where(m => m.create_date.Date >= startDate.Date)
                .Include(m => m.order).ThenInclude(o => o.paymentList.Where(p => p.status.Trim().Equals("支付成功")))
                .ThenInclude(p => p.refunds.Where(r => r.state == 1 || !r.refund_id.Trim().Equals("")))
                .AsNoTracking().ToListAsync();
            //List<Models.Mi7ExportedSaleDetail> detail = await _db.mi7ExportedSaleDetail.ToListAsync();
            for(int i = 0; i < mi7List.Count; i++)
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
                List<Mi7Order> subMi7Orders = mi7Orders.Where(m => m.mi7_order_id.Trim().Equals(r.mi7OrderId.Trim())).ToList();
                List<OrderOnline> orderList = new List<OrderOnline>();
                for(int j = 0; j < subMi7Orders.Count; j++)
                {
                    Mi7Order mi7Order = subMi7Orders[j];
                    if (mi7Order == null || mi7Order.order == null || mi7Order.order.paymentList == null || mi7Order.order.paymentList.Count == 0)
                    {
                        continue;
                    }
                    if (mi7Order != null &&  orderList.Where(o => o.id == mi7Order.order_id).ToList().Count == 0)
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