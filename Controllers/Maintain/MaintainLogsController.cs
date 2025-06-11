using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Maintain;
using SnowmeetApi.Models.Users;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Ticket;
using SnowmeetApi.Controllers.User;
using NPOI.XSSF.UserModel;
using NPOI.SS.UserModel;

namespace SnowmeetApi.Controllers.Maintain
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class MaintainLogsController : ControllerBase
    {
        private readonly ApplicationDBContext _context;
        private IConfiguration _config;
        private IConfiguration _originConfig;

        public MaintainLogsController(ApplicationDBContext context, IConfiguration config)
        {
            _context = context;
            _config = config.GetSection("Settings");
            _originConfig = config;
            //UnicUser._context = context;
        }

        [HttpGet("{taskId}")]
        public async Task<ActionResult<MaintainLog>> StartStep(int taskId, string stepName, string sessionKey)
        {
            sessionKey = Util.UrlDecode(sessionKey);
            stepName = Util.UrlDecode(stepName);
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            string customerOpenId = "";
            try
            {
                ShopSaleInteract scan = await _context.ShopSaleInteract.Where(s => s.biz_id == taskId && s.scan_type.Trim().Equals("发板"))
                    .OrderByDescending(s => s.id).AsNoTracking().FirstAsync();
                MemberController _memberHelper = new MemberController(_context, _originConfig);
                Member member = await _memberHelper.GetWholeMemberByNum(scan.scaner_oa_open_id, "wechat_oa_openid");
                customerOpenId = member.wechatMiniOpenId.Trim();
            }
            catch
            {

            }

            MaintainLog log = new MaintainLog()
            {
                id = 0,
                task_id = taskId,
                step_name = stepName.Trim(),
                start_time = DateTime.Now,
                staff_open_id = user.miniAppOpenId.Trim(),
                status = "已开始",
                stop_open_id = "",
                memo = "",
                customer_open_id = customerOpenId.Trim(),
                staffName = user.miniAppUser.real_name.Trim()
            };
            await _context.MaintainLog.AddAsync(log);
            await _context.SaveChangesAsync();

            if (stepName.Trim().Equals("发板") || stepName.Trim().Equals("强行索回"))
            {
                MaintainLive task = await _context.MaintainLives.FindAsync(taskId);
                task.finish = 1;
                _context.Entry(task).State = EntityState.Modified;
                await _context.SaveChangesAsync();
            }

            return log;
        }

        [HttpGet]
        public async Task<ActionResult<List<MaintainReport>>> GetReport(DateTime startDate, DateTime endDate, string sessionKey)
        {
            _context.Database.SetCommandTimeout(600);
            List<MaintainReport> list = await _context.maintainReport.FromSqlRaw(" select * from dbo.func_maintain_report('"
                + startDate.ToShortDateString() + "', '" + endDate.AddDays(1).ToShortDateString() + "')  "
                //+ "  order by create_date desc , order_id desc "
                ).OrderByDescending(l => l.task_flow_num).AsNoTracking().ToListAsync();
            for (int i = 0; i < list.Count; i++)
            {
                MaintainReport r = list[i];
                if (r.order_id == 0)
                {
                    continue;
                }
                r.order = await _context.OrderOnlines.FindAsync(r.order_id);
                if (r.order == null)
                {
                    continue;
                }
                r.logs = await _context.MaintainLog.Where(m => m.task_id == r.id)
                    .Include(m => m.msa).ThenInclude(m => m.member).AsNoTracking().ToListAsync();

                r.order.paymentList = await _context.OrderOnlines.Entry(r.order).Collection(o => o.paymentList)
                    .Query().Where(p => p.status.Trim().Equals("支付成功"))
                    .Include(r => r.refunds.Where(r => r.state == 1 || !r.refund_id.Trim().Equals("")))
                    .ToListAsync();


            }

            return Ok(list);
        }
        /*
        [HttpGet]
        public async Task ExportExcel()
        {
            List<MaintainReport> l = (List<MaintainReport>)((OkObjectResult)(await GetReport(DateTime.Parse("2024-10-01"), DateTime.Parse("2025-12-01"), "")).Result).Value;
            l = l.OrderByDescending(l => l.task_flow_num).ToList();
            List<MaintainReport> sortedL = new List<MaintainReport>();
            for(int i = 0; i < l.Count; i++)
            {
                if (sortedL.Where(sl => sl.task_flow_num.Trim().Equals(l[i].task_flow_num.Trim())).ToList().Count > 0)
                {
                    continue;
                }
                //sortedL.Add(l[i]);
                List<MaintainReport> subL = l.Where(subL => subL.order_id == l[i].order_id).ToList();
                for(int j = 0; j < subL.Count; j++)
                {
                    sortedL.Add(subL[j]);
                }
            }
            Console.WriteLine("");
        }
        */
        [HttpGet]
        public async Task ExportExcel()
        {
            DateTime start = DateTime.Parse("2024-10-1");
            DateTime end = DateTime.Parse("2025-5-1");
            List<MaintainLive> oriList = await _context.MaintainLives
                .Where(m => m.create_date.Date >= start.Date && m.create_date.Date <= end.Date
                && m.task_flow_num.IndexOf("-") >= 0
                //&& m.task_flow_num.Equals("250409-00001")
                )
                .Include(m => m.order)
                    .ThenInclude(o => o.paymentList.Where(p => p.status.Trim().Equals("支付成功")))
                        .ThenInclude(p => p.refunds.Where(r => r.state == 1 || !r.refund_id.Trim().Equals("")))
                .Include(m => m.taskLog).ThenInclude(l => l.msa).ThenInclude(m => m.member)
                .Include(m => m.staffMsa).ThenInclude(m => m.member)
                .OrderByDescending(m => m.batch_id).ThenByDescending(m => m.order_id)
                .AsNoTracking().ToListAsync();

            int maxPaymentCount = 1;
            int maxRefundCount = 0;

            for (int i = 0; i < oriList.Count; i++)
            {
                MaintainLive task = oriList[i];
                if (task.order != null && task.order.paymentList != null)
                {
                    maxPaymentCount = Math.Max(maxPaymentCount, task.order.paymentList.Count);
                    if (task.order.refundList != null && task.order.refundList.Count > 0)
                    {
                        maxRefundCount = Math.Max(maxRefundCount, task.order.refundList.Count);
                    }
                }
            }
            string[] commonHead = ["序号", "订单号", "日期", "时间", "门店", "支付订单号", "支付金额", "退款金额", "结余金额", "流水号", "类型", "品牌", "长度",
                "角度", "接待", "修刃", "打蜡", "刮蜡", "其他", "维修", "发板", "备注", "附加费用"];
            string[] paymentHead = ["支付门店", "支付方式" ,"微信支付单号", "商户订单号", "支付金额", "支付日期", "支付时间"];
            string[] refundHead = ["微信退款单号", "商户退款单号", "退款金额", "退款原因", "退款日期", "退款时间"];
            string nullStr = "【-】";
            List<string> realHead = new List<string>();
            for (int i = 0; i < commonHead.Length; i++)
            {
                realHead.Add(commonHead[i].Trim());
            }
            for (int i = 0; i < maxPaymentCount; i++)
            {
                for (int j = 0; j < paymentHead.Length; j++)
                {
                    realHead.Add(paymentHead[j].Trim() + (i + 1).ToString());
                }
            }
            for (int i = 0; i < maxRefundCount; i++)
            {
                for (int j = 0; j < refundHead.Length; j++)
                {
                    realHead.Add(refundHead[j].Trim() + (i + 1).ToString());
                }
            }

            XSSFWorkbook workbook = new XSSFWorkbook();

            IFont fontProblem = workbook.CreateFont();
            fontProblem.Color = NPOI.HSSF.Util.HSSFColor.Red.Index;
            ISheet sheet = workbook.CreateSheet("24-25养护");
            IDataFormat format = workbook.CreateDataFormat();
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

            for (int i = 0; i < realHead.Count; i++)
            {
                ICell headCell = headRow.CreateCell(i);
                headCell.SetCellValue(realHead[i].Trim());
                headCell.SetCellType(CellType.String);
                headCell.CellStyle = headStyle;
                if (i < commonHead.Length)
                {
                    switch(i)
                    {
                        case 0:
                            sheet.SetColumnWidth(i, 1000);
                            break;
                        case 1:
                            sheet.SetColumnWidth(i,1500);
                            break;
                        case 2:
                            sheet.SetColumnWidth(i,2800);
                            break;
                        case 3:
                            sheet.SetColumnWidth(i, 2000);
                            break;
                        case 4:
                        case 9:
                            sheet.SetColumnWidth(i,3000);
                            break;
                        case 11:
                            sheet.SetColumnWidth(i, 4000);
                            break;
                        case 14:
                        case 15:
                        case 16:
                        case 17:
                        case 18:
                        case 19:
                        case 20:
                        case 21:
                            sheet.SetColumnWidth(i, 4300);
                            break;
                        default:
                            break;
                    }
                }
                else if (i < maxPaymentCount * paymentHead.Length + commonHead.Length)
                {
                    int index = (i - commonHead.Length) % commonHead.Length;
                    switch(index)
                    {
                        case 0:
                            sheet.SetColumnWidth(i, 3000);
                            break;
                        case 2:
                        case 3:
                            sheet.SetColumnWidth(i, 7000);
                            break;
                        case 5:
                            sheet.SetColumnWidth(i, 2800);
                            break;
                        case 6:
                            sheet.SetColumnWidth(i, 2000);
                            break;
                        default:
                            break;
                    }
                }
                else
                {
                    int index = (i - commonHead.Length
                        - maxPaymentCount * paymentHead.Length) % refundHead.Length;
                    switch(index)
                    {
                        case 0:
                            sheet.SetColumnWidth(i, 7500);
                            break;
                        case 1:
                            sheet.SetColumnWidth(i,10000);
                            break;
                        case 3:
                            sheet.SetColumnWidth(i, 5000);
                            break;
                        case 4:
                            sheet.SetColumnWidth(i, 2800);
                            break;
                        case 5:
                            sheet.SetColumnWidth(i, 2000);
                            break;
                        default:
                            break;
                    }
                        
                }
            }

            for (int i = 0; i < oriList.Count; i++)
            {
                ICellStyle styleText = workbook.CreateCellStyle();
                styleText.Alignment = HorizontalAlignment.Center;
                styleText.DataFormat = format.GetFormat("General");
                ICellStyle styleMoney = workbook.CreateCellStyle();
                styleMoney.DataFormat = format.GetFormat("¥#,##0.00");
                ICellStyle styleNum = workbook.CreateCellStyle();
                styleNum.DataFormat = format.GetFormat("0");
                ICellStyle styleDate = workbook.CreateCellStyle();
                styleDate.DataFormat = format.GetFormat("yyyy-MM-dd");
                ICellStyle styleTime = workbook.CreateCellStyle();
                styleTime.DataFormat = format.GetFormat("HH:mm:ss");

                IRow dr = sheet.CreateRow(i + 1);
                dr.Height = 500;
                MaintainLive task = oriList[i];
                if (task.order == null)
                {
                    //isEnterain = true;
                    styleText.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Yellow.Index;
                    styleText.FillPattern = FillPattern.SolidForeground;

                    styleMoney.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Yellow.Index;
                    styleMoney.FillPattern = FillPattern.SolidForeground;

                    styleNum.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Yellow.Index;
                    styleNum.FillPattern = FillPattern.SolidForeground;

                    styleDate.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Yellow.Index;
                    styleDate.FillPattern = FillPattern.SolidForeground;

                    styleTime.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Yellow.Index;
                    styleTime.FillPattern = FillPattern.SolidForeground;
                }

                for (int j = 0; j < realHead.Count; j++)
                {
                    ICell cell = dr.CreateCell(j);
                    if (j < commonHead.Length)
                    {
                        switch (j)
                        {
                            case 0:
                                cell.SetCellValue((i + 1));
                                cell.CellStyle = styleNum;
                                break;
                            case 1:
                                cell.SetCellValue(task.batch_id);
                                cell.CellStyle = styleNum;
                                break;
                            case 2:
                                cell.SetCellValue(task.create_date.Date);
                                cell.CellStyle = styleDate;
                                break;
                            case 3:
                                cell.SetCellValue(task.create_date);
                                cell.CellStyle = styleTime;
                                break;
                            case 4:
                                cell.SetCellValue(task.shop);
                                cell.CellStyle = styleText;
                                break;
                            case 5:
                                if (task.order == null)
                                {
                                    cell.SetCellValue(nullStr);
                                    cell.CellStyle = styleText;
                                }
                                else
                                {
                                    cell.SetCellValue(task.order.id);
                                    cell.CellStyle = styleNum;
                                }
                                break;
                            case 6:
                                if (task.order == null)
                                {
                                    cell.SetCellValue(nullStr);
                                    cell.CellStyle = styleText;
                                }
                                else
                                {
                                    cell.SetCellValue(task.order.paidAmount);
                                    cell.CellStyle = styleMoney;
                                }
                                break;
                            case 7:
                                if (task.order == null)
                                {
                                    cell.SetCellValue(nullStr);
                                    cell.CellStyle = styleText;
                                }
                                else
                                {
                                    cell.SetCellValue(task.order.refundAmount);
                                    cell.CellStyle = styleMoney;
                                }
                                break;
                            case 8:
                                if (task.order == null)
                                {
                                    cell.SetCellValue(nullStr);
                                    cell.CellStyle = styleText;
                                }
                                else
                                {
                                    cell.SetCellValue(task.order.paidAmount - task.order.refundAmount);
                                    cell.CellStyle = styleMoney;
                                }
                                break;
                            case 9:
                                cell.SetCellValue(task.task_flow_num.Trim());
                                cell.CellStyle = styleText;
                                break;
                            case 10:
                                cell.SetCellValue(task.confirmed_equip_type.Trim());
                                cell.CellStyle = styleText;
                                break;
                            case 11:
                                cell.SetCellValue(task.confirmed_brand.Trim());
                                cell.CellStyle = styleText;
                                break;
                            case 12:
                                cell.SetCellValue(task.confirmed_scale.Trim());
                                cell.CellStyle = styleText;
                                break;
                            case 13:
                                cell.SetCellValue(task.confirmed_degree.ToString());
                                cell.CellStyle = styleText;
                                break;
                            case 14:
                                cell.SetCellValue(task.staffRecept);
                                cell.CellStyle = styleText;
                                break;
                            case 15:
                                if (task.confirmed_edge == 1)
                                {
                                    if (!task.staffEdge.Trim().Equals(""))
                                    {
                                        cell.SetCellValue(task.staffEdge.Trim());
                                    }
                                    else
                                    {
                                        cell.SetCellValue("——");
                                    }
                                }
                                else
                                {
                                    cell.SetCellValue(nullStr);
                                }
                                cell.CellStyle = styleText;
                                break;
                            case 16:
                                if (task.confirmed_candle == 1)
                                {
                                    if (!task.staffVax.Trim().Equals(""))
                                    {
                                        cell.SetCellValue(task.staffVax.Trim());
                                    }
                                    else
                                    {
                                        cell.SetCellValue("——");
                                    }
                                }
                                else
                                {
                                    cell.SetCellValue(nullStr);
                                }
                                cell.CellStyle = styleText;
                                break;
                            case 17:
                                if (task.confirmed_candle == 1)
                                {
                                    if (!task.staffUnVax.Trim().Equals(""))
                                    {
                                        cell.SetCellValue(task.staffUnVax.Trim());
                                    }
                                    else
                                    {
                                        cell.SetCellValue("——");
                                    }
                                }
                                else
                                {
                                    cell.SetCellValue(nullStr);
                                }
                                cell.CellStyle = styleText;
                                break;
                            case 18:
                                cell.SetCellValue(task.confirmed_more.Trim().Equals("") ? nullStr : task.confirmed_more.Trim());
                                cell.CellStyle = styleText;
                                break;
                            case 19:
                                if (task.confirmed_more.Trim().Equals(""))
                                {
                                    cell.SetCellValue(nullStr);
                                }
                                else
                                {
                                    if (task.staffRepair.Trim().Equals(""))
                                    {
                                        cell.SetCellValue("——");
                                    }
                                    else
                                    {
                                        cell.SetCellValue(task.staffRepair.Trim());
                                    }
                                }
                                cell.CellStyle = styleText;
                                break;
                            case 20:
                                cell.SetCellValue(task.staffGiveOut.Trim().Equals("")?"——": task.staffGiveOut);
                                cell.CellStyle = styleText;
                                break;
                            case 21:
                                string memo = task.confirmed_memo.Trim() + " "
                                    + ((task.order != null && task.order.memo != "") ? task.order.memo.Trim() : "");
                                cell.SetCellValue(memo);
                                cell.CellStyle = styleText;
                                break;
                            case 22:
                                cell.SetCellValue(task.confirmed_additional_fee);
                                cell.CellStyle = styleMoney;
                                break;
                            default:
                                break;
                        }
                    }
                    else if (j < commonHead.Length + maxPaymentCount * paymentHead.Length)
                    {
                        int index = (j - commonHead.Length) / paymentHead.Length;
                        if (task.order == null || task.order.paymentList.Count <= index)
                        {
                            cell.SetCellValue(nullStr);
                            cell.CellStyle = styleText;
                        }
                        else
                        {
                            OrderPayment payment = task.order.paymentList[index];
                            switch ((j - commonHead.Length) % paymentHead.Length)
                            {
                                case 0:
                                    cell.SetCellValue(task.order.shop.Trim());
                                    cell.CellStyle = styleText;
                                    break;
                                case 1:
                                    cell.SetCellValue(payment.pay_method.Trim());
                                    break;
                                case 2:
                                    cell.SetCellValue(payment.wepay_trans_id==null?nullStr:payment.wepay_trans_id);
                                    cell.CellStyle = styleText;
                                    break;
                                case 3:
                                    cell.SetCellValue(payment.out_trade_no==null?nullStr:payment.out_trade_no);
                                    cell.CellStyle = styleText;
                                    break;
                                case 4:
                                    cell.SetCellValue(payment.amount);
                                    cell.CellStyle = styleMoney;
                                    break;
                                case 5:
                                    cell.SetCellValue(payment.create_date);
                                    cell.CellStyle = styleDate;
                                    break;
                                case 6:
                                    cell.SetCellValue(payment.create_date);
                                    cell.CellStyle = styleTime;
                                    break;
                                default:
                                    break;
                            }
                        }
                    }
                    else
                    {
                        int index = (j - commonHead.Length - maxPaymentCount * paymentHead.Length) / refundHead.Length;
                        if (task.order == null || task.order.refundList.Count <= index)
                        {
                            cell.SetCellValue(nullStr);
                            cell.CellStyle = styleText;
                        }
                        else
                        {
                            OrderPaymentRefund refund = task.order.refundList[index];
                            switch ((j - commonHead.Length-maxPaymentCount * paymentHead.Length) % paymentHead.Length)
                            {
                                case 0:
                                    cell.SetCellValue(refund.refund_id);
                                    cell.CellStyle = styleText;
                                    break;
                                case 1:
                                    cell.SetCellValue(refund.out_refund_no);
                                    cell.CellStyle = styleText;
                                    break;
                                
                                case 2:
                                    cell.SetCellValue(refund.amount);
                                    cell.CellStyle = styleMoney;
                                    break;
                                case 3:
                                    cell.SetCellValue(refund.reason);
                                    cell.CellStyle = styleText;
                                    break;
                                case 4:
                                    cell.SetCellValue(refund.create_date);
                                    cell.CellStyle = styleDate;
                                    break;
                                case 5:
                                    cell.SetCellValue(refund.create_date);
                                    cell.CellStyle = styleTime;
                            
                                    break;
                                default:
                                    break;
                            }
                        }
                    }
                }
            }
            MergeSheet(sheet, 1, new int[] {0, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22});
            string filePath = $"{Environment.CurrentDirectory}" + "/maintain.xlsx";
            using (var file = System.IO.File.Create(filePath))
            {
                workbook.Write(file);
            }

        }
        [NonAction]
        private void MergeSheet(ISheet sheet, int keyIndex, int[] avoidMergeColumns )
        {
            int columnsCount = 0;
            int rowCount = sheet.LastRowNum;
            if (rowCount > 0)
            {
                columnsCount = sheet.GetRow(0).Cells.Count;
            }
            if (columnsCount == 0)
            {
                return ;
            }
            int mergeBase = -1;
            for(int i = 2; i < rowCount; i++)
            {
                IRow lastRow = sheet.GetRow(i - 1);
                IRow currentRow = sheet.GetRow(i);
                ICell lastKeyCell = lastRow.GetCell(keyIndex);
                ICell currentKeyCell = currentRow.GetCell(keyIndex);
                if (lastKeyCell.ToString().Equals(currentKeyCell.ToString()))
                {
                    if (mergeBase == -1)
                    {
                        mergeBase = i - 1;
                    }
                }
                else
                {
                    if (mergeBase != -1)
                    {
                        for(int j = 0; j < columnsCount; j++)
                        {
                            bool needMerge = true;
                            for(int k = 0; k < avoidMergeColumns.Length; k++)
                            {
                                if (avoidMergeColumns[k] == j)
                                {
                                    needMerge = false;
                                    break;
                                }
                            }
                            if (!needMerge)
                            {
                                continue;
                            }
                            sheet.AddMergedRegion(
                                new NPOI.SS.Util.CellRangeAddress(mergeBase, i - 1, j, j));
                        }
                        mergeBase = -1;
                    }
                }
            }
        }
        
        [HttpGet("{taskId}")]
        public async Task<ActionResult<IEnumerable<MaintainLog>>> GetSteps(int taskId)
        {
            return await _context.MaintainLog.Where(m => m.task_id == taskId).OrderBy(m => m.id).ToListAsync();
        }

        [HttpGet("{taskId}")]
        public async Task<ActionResult<IEnumerable<MaintainLog>>> GetStepsByStaff(int taskId, string sessionKey)
        {
            MiniAppUserController mUserController = new MiniAppUserController(_context, _originConfig);
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            var logList = await _context.MaintainLog.Where(m => m.task_id == taskId).OrderBy(m => m.id).ToListAsync();
            for (int i = 0; i < logList.Count; i++)
            {
                if (!user.miniAppOpenId.Trim().Equals(logList[i].staff_open_id.Trim()))
                {
                    logList[i].isMine = false;
                }
                MiniAppUser staffUser = (MiniAppUser)((OkObjectResult)(await mUserController.GetMiniAppUser(logList[i].staff_open_id, sessionKey)).Result).Value;
                logList[i].staffName = staffUser.real_name.Trim();
            }
            return Ok(logList);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<MaintainLog>> EndStep(int id, string memo, string sessionKey)
        {
            memo = Util.UrlDecode(memo);
            sessionKey = Util.UrlDecode(sessionKey);
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _context);
            if (!user.isAdmin)
            {
                return BadRequest();
            }
            MaintainLog log = await _context.MaintainLog.FindAsync(id);
            log.memo = memo;
            if (log.staff_open_id.Trim().Equals(user.miniAppOpenId.Trim()))
            {
                log.end_time = DateTime.Now;
                log.status = "已完成";
            }
            else
            {
                log.end_time = DateTime.Now;
                log.status = "强行终止";
                log.stop_open_id = user.miniAppOpenId.Trim();
            }
            _context.Entry(log).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            log.staffName = user.miniAppUser.real_name.Trim();

            try
            {

                int taskId = log.task_id;

                if (taskId > 0)
                {
                    var ticketLogList = await _context.ticketLog.Where(log => log.memo.IndexOf("养护订单获得，ID:" + taskId.ToString()) >= 0).ToListAsync();
                    if (ticketLogList == null || ticketLogList.Count == 0)
                    {


                        MaintainLive task = await _context.MaintainLives.FindAsync(taskId);
                        if (task != null)
                        {
                            OrderOnlinesController orderHelper = new OrderOnlinesController(_context, _originConfig);
                            OrderOnline order = (await orderHelper.GetWholeOrderByStaff((int)task.order_id, sessionKey)).Value;
                            if (order != null)
                            {
                                TicketController ticketHelper = new TicketController(_context, _originConfig);
                                Ticket ticket = (await ticketHelper.GenerateTickets(8, 1, sessionKey, "养护订单")).Value[0];
                                if (ticket == null)
                                {
                                    return BadRequest();
                                }
                                ticket.open_id = order.open_id.Trim();
                                _context.Entry(ticket).State = EntityState.Modified;
                                await _context.SaveChangesAsync();
                                TicketLog tLog = new TicketLog()
                                {
                                    code = ticket.code,
                                    sender_open_id = user.miniAppOpenId,
                                    accepter_open_id = order.open_id.Trim(),
                                    memo = "养护订单获得，ID:" + task.id.ToString(),
                                    transact_time = DateTime.Now
                                };
                                await _context.AddAsync(tLog);
                                await _context.SaveChangesAsync();

                                double paidAmount = order.paidAmount;
                                double orderPrice = order.order_price;

                                ServiceMessageController messageHelper = new ServiceMessageController(_context, _originConfig);

                                await messageHelper.SendTemplateMessage(order.open_id, "zk6Bde8PolaoPQVLytFZRhKIYux3uHABpzK9Oqy_lfk",
                                    "感谢您在易龙雪聚养护装备，特赠送一张养护券。", "" + Util.GetMoneyStr(orderPrice) + "|" + Util.GetMoneyStr(paidAmount)
                                    + "|" + Util.GetMoneyStr(orderPrice - paidAmount) + "|" + order.pay_method.Trim() + "|养护券",
                                    "点击下面👇公众号菜单查看", "", sessionKey);
                            }




                        }
                    }
                }
            }
            catch
            {

            }

            return log;
        }

        [HttpGet("{shopInterActId}")]
        public async Task<ActionResult<bool>> CheckReturnScan(int shopInterActId, int taskId)
        {
            ShopSaleInteract scan = await _context.ShopSaleInteract.FindAsync(shopInterActId);
            if (scan == null)
            {
                return NotFound();
            }
            MemberController _memberHelper = new MemberController(_context, _originConfig);
            Member member = await _memberHelper.GetWholeMemberByNum(scan.scaner_oa_open_id, "wechat_oa_openid");
            if (member == null)
            {
                return NoContent();
            }
            MaintainLive task = await _context.MaintainLives.FindAsync(taskId);
            if (task == null)
            {
                return NotFound();
            }
            if (task.open_id.Trim().Equals(member.wechatMiniOpenId.Trim()))
            {
                return Ok(true);
            }
            else
            {
                return Ok(false);
            }
        }


        private bool MaintainLogExists(int id)
        {
            return _context.MaintainLog.Any(e => e.id == id);
        }
    }
}
