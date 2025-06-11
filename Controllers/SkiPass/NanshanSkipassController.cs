using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using AlipaySDKNet.OpenAPI.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Configuration;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Models;
using SnowmeetApi.Controllers.User;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

using SnowmeetApi.Models.Product;
using SnowmeetApi.Models.SkiPass;
using SnowmeetApi.Models.Users;
using System.Text.RegularExpressions;
using SnowmeetApi.Controllers.Order;
using NPOI.XSSF.UserModel;
using NPOI.SS.UserModel;
namespace SnowmeetApi.Controllers.SkiPass
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class NanshanSkipassController : ControllerBase
    {


        public class ReserveSummary
        {
            public int product_id { get; set; }
            public string product_name { get; set; }
            public double deal_price { get; set; }
            public bool isDaylight { get; set; }


            public int count { get; set; } = 0;
            public double sumDealPrice { get; set; } = 0;
            public int pickCount { get; set; } = 0;
            public int memberPickCount { get; set; } = 0;
            public int returnCount { get; set; } = 0;
            public double sumRefunded { get; set; } = 0;
            public double sumNeedRefund { get; set; } = 0;

            public ReserveProduct productDetail { get; set; }
        }

        public class ReserveMemberProduct
        {
            public int member_id { get; set; }
            public string? wechat_mini_openid { get; set; }
            public string name { get; set; }
            public string cell { get; set; }
            public List<Models.SkiPass.SkiPass> skiPasses { get; set; } = new List<Models.SkiPass.SkiPass>();
        }

        public class ReserveProduct
        {
            public int product_id { get; set; }
            public string product_name { get; set; }
            public DateTime reserveDate { get; set; }
            //public int memberCount {get; set;}
            public int skiPassCount { get; set; }
            public List<ReserveMemberProduct> memberList { get; set; } = new List<ReserveMemberProduct>();
        }

        public class ReserveDateProduct
        {
            public DateTime reserveDate { get; set; }
            public int product_id { get; set; }
            public string product_name { get; set; }
            public string name { get; set; }
            public string cell { get; set; }
            public List<Models.SkiPass.SkiPass> skiPasses { get; set; } = new List<Models.SkiPass.SkiPass>();
        }

        public class ReserveDateProductMember
        {
            public DateTime reserveDate { get; set; }
            public int product_id { get; set; }
            public string product_name { get; set; }
            public int memberId { get; set; }
            public string wechatMiniOpenId { get; set; }
            public string name { get; set; }
            public string cell { get; set; }
            public List<Models.SkiPass.SkiPass> skipasses { get; set; } = new List<Models.SkiPass.SkiPass>();
        }
        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _http;
        private readonly MemberController _memberHelper;
        //private readonly OrderOnlinesController _orderHelper;
        private readonly OrderPaymentController _paymentHelper;
        public NanshanSkipassController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = context;
            _config = config;
            _http = httpContextAccessor;
            _memberHelper = new MemberController(context, config);
            _paymentHelper = new OrderPaymentController(context, config, httpContextAccessor);
        }


        [HttpGet]
        public async Task<ActionResult<List<ReserveSummary>>> GetReserve(DateTime date, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            /*
            if (!(await _memberHelper.isStaff(sessionKey, sessionType)))
            {
                return BadRequest();
            }
            */
            List<Models.SkiPass.SkiPass> skiPassList = await _db.skiPass
                .Where(sp => (sp.resort.Trim().Equals("南山") && sp.valid == 1 && sp.is_cancel == 0 && ((DateTime)sp.reserve_date).Date == date.Date))
                .AsNoTracking().ToListAsync();
            var summary = from skiPass in skiPassList
                          group skiPass by new { skiPass.product_id, skiPass.product_name, skiPass.deal_price }
                into sum
                          select new { sum.Key, count = sum.Count(), sumDealPrice = sum.Sum(s => (s.deal_price * s.count)), pickCount = 0 };
            //var sumE = summary.GetEnumerator();
            var retList = summary.ToList();
            List<ReserveSummary> newList = new List<ReserveSummary>();
            for (int i = 0; i < retList.Count; i++)
            {
                var l = retList[i];

                int pickCount = 0;
                for (int j = 0; j < skiPassList.Count; j++)
                {
                    if (skiPassList[j].product_id == l.Key.product_id
                        && (skiPassList[j].card_image_url != null || skiPassList[j].card_no != null))
                    {
                        pickCount++;
                    }
                }

                ReserveSummary s = new ReserveSummary()
                {
                    product_id = l.Key.product_id,
                    product_name = l.Key.product_name,
                    deal_price = (double)l.Key.deal_price,
                    count = l.count,
                    sumDealPrice = (double)l.sumDealPrice,
                    pickCount = pickCount
                };
                newList.Add(s);
            }
            return Ok(newList);
        }
        [HttpGet("{skiPassId}")]
        public async Task CancelByStaff(int skiPassId, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            /*
            if (!(await _memberHelper.isStaff(sessionKey, sessionType)))
            {
                return;
            }
            */
            await Cancel(skiPassId, sessionKey, sessionType);
        }

        [NonAction]
        public async Task Cancel(int skiPassId, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            sessionKey = Util.UrlDecode(sessionKey);
            sessionType = Util.UrlDecode(sessionType);
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            int canelMemberId = member.id;
            Models.SkiPass.SkiPass sp = await _db.skiPass.FindAsync(skiPassId);
            sp.cancel_member_id = canelMemberId;
            sp.is_cancel = 1;
            _db.skiPass.Entry(sp).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            double? dealPrice = sp.deal_price;
            int? orderId = sp.order_id;
            if (dealPrice == null || orderId == null)
            {
                return;
            }
            List<OrderPayment> payments = await _db.OrderPayment
                .Where(p => p.order_id == orderId && p.status.Equals("支付成功"))
                .AsNoTracking().ToListAsync();
            if (payments.Count == 0)
            {
                return;
            }
            await _paymentHelper.Refund(payments[0].id, (double)dealPrice, "退票", sessionKey, sessionType);
            return;


        }

        [HttpGet("{productId}")]
        public async Task<ActionResult<ReserveProduct>> GetReserveProductDetail(int productId, DateTime reserveDate, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            /*
            if (!(await _memberHelper.isStaff(sessionKey, sessionType)))
            {
                return BadRequest();
            }
            */
            List<Models.SkiPass.SkiPass> skiPassList = await _db.skiPass
                .Where(sp => (sp.resort.Trim().Equals("南山") && sp.valid == 1 && sp.is_cancel == 0
                && ((DateTime)sp.reserve_date).Date == reserveDate.Date && sp.product_id == productId))
                .AsNoTracking().ToListAsync();
            var l = (from reserveDetail in skiPassList
                     group reserveDetail
                by new { reserveDetail.member_id, reserveDetail.wechat_mini_openid, reserveDetail.contact_name, reserveDetail.contact_cell }
                into reserveSum
                     select new { reserveSum.Key, count = reserveSum.Count() }).ToList();
            Models.Product.Product p = await _db.Product.FindAsync(productId);
            ReserveProduct ret = new ReserveProduct()
            {
                product_id = productId,
                product_name = p.name.Trim(),
                skiPassCount = skiPassList.Count(),
                reserveDate = reserveDate
            };
            List<ReserveMemberProduct> members = ret.memberList;
            for (int i = 0; i < l.Count; i++)
            {

                ReserveMemberProduct member = new ReserveMemberProduct()
                {
                    member_id = l[i].Key.member_id,
                    name = l[i].Key.contact_name,
                    cell = l[i].Key.contact_cell,
                    wechat_mini_openid = l[i].Key.wechat_mini_openid
                };
                for (int j = 0; j < skiPassList.Count; j++)
                {
                    if (skiPassList[j].member_id == member.member_id
                        || skiPassList[j].wechat_mini_openid.Trim().Equals(member.wechat_mini_openid.Trim()))
                    {
                        member.skiPasses.Add(skiPassList[j]);
                    }
                }
                members.Add(member);
            }
            ret.memberList = members;
            return Ok(ret);

        }

        [NonAction]
        public async Task<List<Models.SkiPass.SkiPass>> GetSkipassesByMember(int memberId, string num = "")
        {
            return await _db.skiPass.Where(s => (((memberId != 0 && s.member_id == memberId)
                || (!num.Trim().Equals("") && s.wechat_mini_openid.Trim().Equals(num))) && s.resort.Trim().Equals("南山")))
                .AsNoTracking().ToListAsync();
        }



        [HttpPost]
        public async Task<ActionResult<Models.SkiPass.SkiPass>> UpdateSkiPass([FromBody] Models.SkiPass.SkiPass skipass,
            [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            /*
            if (!(await _memberHelper.isStaff(sessionKey, sessionType)))
            {
                return BadRequest();
            }
            */
            bool needFinish = false;

            try
            {
                //TicketController _tHelper = new TicketController(_db, _config);
                Models.SkiPass.SkiPass oriSkipass = await _db.skiPass.Where(s => s.id == skipass.id).AsNoTracking().FirstAsync();
                if ((oriSkipass.card_no == null || oriSkipass.card_no.Trim().Equals("")) && !skipass.card_no.Trim().Equals(""))
                {
                    //南山出票后激活
                    //await _tHelper.ActiveTicket((int)oriSkipass.order_id);
                    //SkiPassController _skpHelper = new SkiPassController(_db, _config, _http);
                    if (skipass.order_id != null)
                    {
                        needFinish = true;
                        //await _skpHelper.CommitSkipassOrder((int)skipass.order_id);
                    }

                }
            }
            catch
            {

            }



            var l = await _db.skiPass.Where(s => (s.card_no.Trim().Equals(skipass.card_no.Trim())
                && ((DateTime)s.reserve_date).Date == ((DateTime)skipass.reserve_date).Date))
                .AsNoTracking().ToListAsync();
            if (l != null && l.Count > 0 && l[0].id != skipass.id)
            {
                return NoContent();
            }


            _db.skiPass.Entry(skipass).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            if (needFinish)
            {
                SkiPassController _skpHelper = new SkiPassController(_db, _config, _http);
                await _skpHelper.CommitSkipassOrder((int)skipass.order_id);
            }
            return Ok(skipass);
        }

        [HttpGet]
        public async Task<ActionResult<List<ReserveDateProduct>>> GetMemberCard(int memberId,
            string wechatMiniOpenId, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            /*
            if (!(await _memberHelper.isStaff(sessionKey, sessionType)))
            {
                return BadRequest();
            }
            */
            List<Models.SkiPass.SkiPass> skipasses = await GetSkipassesByMember(memberId, wechatMiniOpenId);
            var reserveList = (from s in skipasses
                               where s.valid == 1
                               group s by
                new { s.reserve_date, s.product_id, s.product_name, s.contact_cell, s.contact_name }
                into rl
                               select new { rl.Key }).OrderByDescending(r => r.Key.reserve_date).ToList();
            List<ReserveDateProduct> ret = new List<ReserveDateProduct>();
            for (int i = 0; i < reserveList.Count; i++)
            {
                ReserveDateProduct item = new ReserveDateProduct()
                {
                    name = reserveList[i].Key.contact_name,
                    cell = reserveList[i].Key.contact_cell,
                    reserveDate = (DateTime)reserveList[i].Key.reserve_date,
                    product_id = reserveList[i].Key.product_id,
                    product_name = reserveList[i].Key.product_name
                };
                for (int j = 0; j < skipasses.Count; j++)
                {
                    Models.SkiPass.SkiPass skp = skipasses[j];
                    if (skp.contact_cell.Trim().Equals(item.cell.Trim())
                        && skp.contact_name.Trim().Equals(item.name.Trim())
                        && ((DateTime)skp.reserve_date).Date == item.reserveDate.Date
                        && skp.product_id == item.product_id
                        && skp.product_name.Trim().Equals(item.product_name.Trim()))
                    {
                        item.skiPasses.Add(skp);
                    }
                }
                ret.Add(item);
            }
            return Ok(ret);
        }

        [HttpGet("{productId}")]
        public async Task<ActionResult<object>> ReserveSkiPass(int productId, DateTime date,
            int count, string cell, string name, string sessionKey, int refereeMemberId = 0, string sessionType = "wechat_mini_openid")
        {
            Models.Product.Product product = await _db.Product.FindAsync(productId);
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (user == null || product == null)
            {
                return BadRequest();
            }
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            double totalPrice = 0;
            Models.SkiPass.SkiPass[] skipassArr = new Models.SkiPass.SkiPass[count];
            for (int i = 0; i < count; i++)
            {
                Models.SkiPass.SkiPass skipass = new Models.SkiPass.SkiPass()
                {
                    member_id = member.id,
                    wechat_mini_openid = member.wechatMiniOpenId,
                    product_id = productId,
                    resort = "南山",
                    product_name = product.name,
                    count = 1,
                    //order_id = orderId,
                    deal_price = product.sale_price + product.deposit,
                    ticket_price = product.sale_price,
                    deposit = product.deposit,
                    valid = 0,
                    contact_cell = cell,
                    contact_name = name,
                    reserve_date = date.Date
                };
                skipassArr[i] = skipass;
                totalPrice += (double)skipass.deal_price;
            }

            OrderOnline order = new OrderOnline()
            {
                type = "雪票",
                shop = product.shop.Trim(),
                order_price = totalPrice,
                order_real_pay_price = totalPrice,
                final_price = totalPrice,
                open_id = member.wechatMiniOpenId,
                staff_open_id = "",
                memo = "",
                pay_method = "微信支付",
                referee_member_id = refereeMemberId
            };
            await _db.OrderOnlines.AddAsync(order);
            await _db.SaveChangesAsync();
            string outTradeNo = "";
            if (order.shop.Trim().Equals("南山"))
            {
                outTradeNo = "NS";
            }
            else
            {
                outTradeNo = "QJ";
            }
            outTradeNo += "_XP_" + DateTime.Now.ToString("yyyyMMdd") + "_" + order.id.ToString().PadLeft(6, '0') + "_ZF_01";

            OrderPayment payment = new OrderPayment()
            {
                order_id = order.id,
                pay_method = order.pay_method.Trim(),
                amount = order.final_price,
                status = "待支付",
                staff_open_id = "",
                out_trade_no = outTradeNo
            };
            await _db.OrderPayment.AddAsync(payment);

            for (int i = 0; i < skipassArr.Length; i++)
            {
                Models.SkiPass.SkiPass skipass = skipassArr[i];
                skipass.order_id = order.id;
                await _db.skiPass.AddAsync(skipass);
            }
            await _db.SaveChangesAsync();
            order.paymentList = (new OrderPayment[] { payment }).ToList();

            member.real_name = name;
            _db.member.Entry(member).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            //await _memberHelper.UpdateDetailInfo(member.id, cell, "cell", false);
            return Ok(order);
        }
        [HttpGet]
        public async Task<ActionResult<List<ReserveDateProductMember>>> SearchSkipass(
            string key, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            /*
            if (!(await _memberHelper.isStaff(sessionKey, sessionType)))
            {
                return BadRequest();
            }
            */
            key = Util.UrlDecode(key);
            bool isNum = Regex.IsMatch(key, @"\d+");
            List<Models.SkiPass.SkiPass> skipasses = await _db.skiPass.Where(s => ((
                (isNum && (s.card_no.Trim().IndexOf(key) >= 0 || s.contact_cell.IndexOf(key) >= 0))
                ||
                (!isNum && s.contact_name.IndexOf(key) >= 0)
            ) && s.valid == 1 && s.resort.Trim().Equals("南山"))).AsNoTracking().ToListAsync();
            var strucList = (from s in skipasses
                             group s
                by new { s.product_id, s.product_name, reserveDate = ((DateTime)s.reserve_date).Date, s.contact_name, s.contact_cell, s.member_id, s.wechat_mini_openid }
                into sl
                             select new { sl.Key }).OrderByDescending(s => s.Key.reserveDate).ToList();
            List<ReserveDateProductMember> ret = new List<ReserveDateProductMember>();
            foreach (var item in strucList)
            {
                ReserveDateProductMember struc = new ReserveDateProductMember()
                {
                    product_id = item.Key.product_id,
                    product_name = item.Key.product_name,
                    reserveDate = item.Key.reserveDate,
                    name = item.Key.contact_name,
                    cell = item.Key.contact_cell,
                    memberId = item.Key.member_id,
                    wechatMiniOpenId = item.Key.wechat_mini_openid
                };
                for (int i = 0; i < skipasses.Count; i++)
                {
                    Models.SkiPass.SkiPass skipass = skipasses[i];
                    if (((DateTime)skipass.reserve_date).Date == struc.reserveDate.Date
                        && skipass.product_id == struc.product_id
                        && (skipass.wechat_mini_openid.Trim().Equals(struc.wechatMiniOpenId.Trim()) || skipass.member_id == struc.memberId))
                    {
                        struc.skipasses.Add(skipass);
                    }
                }
                ret.Add(struc);
            }

            return Ok(ret);
        }
        [HttpGet]
        public async Task<ActionResult<List<ReserveSummary>>> GetDailyRefundSummary
            (DateTime date, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            List<ReserveSummary> sum = (List<ReserveSummary>)((OkObjectResult)(await GetReserve(date, sessionKey, sessionType)).Result).Value;
            for (int i = 0; i < sum.Count; i++)
            {
                if (sum[i].product_name.IndexOf("夜") >= 0)
                {
                    sum[i].isDaylight = false;
                }
                else
                {
                    sum[i].isDaylight = true;
                }
                ReserveProduct p = (ReserveProduct)((OkObjectResult)(await GetReserveProductDetail(sum[i].product_id, date, sessionKey, sessionType)).Result).Value;
                sum[i].productDetail = p;
                for (int k = 0; k < p.memberList.Count; k++)
                {
                    for (int l = 0; l < p.memberList[k].skiPasses.Count; l++)
                    {
                        Models.SkiPass.SkiPass skipass = p.memberList[k].skiPasses[l];
                        //sum[i].count++;
                        sum[i].sumDealPrice += (double)skipass.deal_price;
                        if (skipass.card_member_pick_time != null)
                        {
                            sum[i].memberPickCount++;
                        }
                        if (skipass.card_member_return_time != null)
                        {
                            sum[i].returnCount++;
                            sum[i].sumNeedRefund += skipass.needRefund;
                        }
                        if (skipass.refund_amount != null)
                        {
                            sum[i].sumRefunded += (double)skipass.refund_amount;
                        }
                    }
                }

            }
            return Ok(sum);
        }

        [HttpGet("{skiPassId}")]
        public async Task<ActionResult<OrderPaymentRefund>> SkipassRefundDeposit(int skiPassId,
            string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Models.SkiPass.SkiPass skipass = await _db.skiPass.FindAsync(skiPassId);
            if (skipass.valid == 0 || skipass.card_member_return_time == null)
            {
                return BadRequest();
            }
            List<Models.OrderPayment> payments = await _db.OrderPayment
                .Where(p => (p.order_id == skipass.order_id && p.status.Trim().Equals("支付成功")))
                .AsNoTracking().ToListAsync();
            //double paidAmount = 0;
            foreach (OrderPayment payment in payments)
            {
                //paidAmount += payment.amount;
                if (payment.amount >= skipass.needRefund)
                {
                    skipass.have_refund = 1;
                    skipass.refund_amount = skipass.needRefund;
                    _db.skiPass.Entry(skipass).State = EntityState.Modified;
                    await _db.SaveChangesAsync();
                    OrderPaymentRefund refund = (OrderPaymentRefund)((OkObjectResult)(
                        await _paymentHelper.Refund(payment.id, skipass.needRefund, "退押金", sessionKey, sessionType)).Result).Value;
                    return Ok(refund);

                }
            }
            return NoContent();
        }

        [HttpGet]
        public async Task<ActionResult<List<Models.SkiPass.SkiPass>>> GetMySkipass
            (string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            if (member == null)
            {
                return BadRequest();
            }
            List<Models.SkiPass.SkiPass> l = await _db.skiPass.Where(s => (s.resort.Trim().Equals("南山") && s.valid == 1
                && (s.member_id == member.id || s.wechat_mini_openid.Trim().Equals(member.wechatMiniOpenId.Trim()))))
                .OrderByDescending(s => s.reserve_date).AsNoTracking().ToListAsync();

            return Ok(l);
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
                            sheet.SetColumnWidth(i, 4 * 256);
                            break;
                        case 1:
                        case 7:
                        case 14:
                            sheet.SetColumnWidth(i, 11 * 256);
                            break;
                        case 8:
                        case 2:
                        case 5:
                        case 6:
                        case 10:
                        case 11:
                        case 12:
                            sheet.SetColumnWidth(i, 9 * 256);
                            break;
                        case 3:
                            sheet.SetColumnWidth(i, 6 * 256);
                            break;
                        case 4:
                            sheet.SetColumnWidth(i, 40 * 256);
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
                        case 1:
                            sheet.SetColumnWidth(i, 28 * 256);
                            break;
                        case 2:
                            sheet.SetColumnWidth(i, 9 * 256);
                            break;
                        case 3:
                            sheet.SetColumnWidth(i, 11 * 256);
                            break;
                        case 4:
                            sheet.SetColumnWidth(i, 9 * 256);
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
                            sheet.SetColumnWidth(i, 30 * 256);
                            break;
                        case 1:
                            sheet.SetColumnWidth(i, 42 * 256);
                            break;
                        case 2:
                            sheet.SetColumnWidth(i, 9 * 256);
                            break;
                        case 3:
                            sheet.SetColumnWidth(i, 11 * 256);
                            break;
                        case 4:
                            sheet.SetColumnWidth(i, 9 * 256);
                            break;
                        default:
                            break;
                    }

                }
            }
        }
        [NonAction]
        public void ExportExcelInsertData(XSSFWorkbook workbook, ISheet sheet, List<OrderOnline> l,
            string[] commonHead, string[] paymentHead, string[] refundHead, int maxPaymentCount, int maxRefundCount)
        {
            string nullStr = "【-】";
            int index = 0;
            int subIndex = 0;
            IFont fontProblem = workbook.CreateFont();
            fontProblem.Color = NPOI.HSSF.Util.HSSFColor.Red.Index;
            IFont fontCancel= workbook.CreateFont();
            fontCancel.Color = NPOI.HSSF.Util.HSSFColor.Grey50Percent.Index;
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
                OrderOnline order = l[i];
                index++;
                for (int j = 0; j < order.skiPasses.Count; j++)
                {
                    subIndex++;
                    bool cancel = false;
                    double earn = order.paidAmount - order.refundAmount;
                    if (earn == 0 && order.skiPasses[j].card_member_pick_time == null)
                    {
                        cancel = true;
                        styleDate.SetFont(fontCancel);
                        styleTime.SetFont(fontCancel);
                        styleText.SetFont(fontCancel);
                        styleMoney.SetFont(fontCancel);
                        styleText.SetFont(fontCancel);
                    }
                    IRow dr = sheet.CreateRow(subIndex);
                    dr.Height = 500;
                    for (int k = 0; k < commonHead.Length + maxPaymentCount * paymentHead.Length + maxRefundCount * refundHead.Length; k++)
                    {
                        ICell cell = dr.CreateCell(k);
                        bool needMerge = false;
                        if (k < commonHead.Length)
                        {


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
                                case 3:
                                    cell.SetCellValue(subIndex);
                                    cell.CellStyle = styleNum;
                                    break;
                                case 1:
                                    if (j == 0)
                                    {
                                        cell.SetCellValue((DateTime)order.pay_time);
                                        cell.CellStyle = styleDate;

                                    }
                                    needMerge = true;
                                    break;
                                case 2:
                                    if (j == 0)
                                    {
                                        cell.SetCellValue((DateTime)order.pay_time);
                                        cell.CellStyle = styleTime;
                                    }
                                    needMerge = true;
                                    break;
                                case 4:
                                    cell.SetCellValue(order.skiPasses[j].product_name.Trim());
                                    cell.CellStyle = styleText;
                                    break;
                                case 5:
                                    cell.SetCellValue((double)order.skiPasses[j].ticket_price);
                                    cell.CellStyle = styleMoney;
                                    break;
                                case 6:
                                    cell.SetCellValue((double)order.skiPasses[j].deposit);
                                    cell.CellStyle = styleMoney;
                                    break;
                                case 7:
                                    if (order.skiPasses[j].card_member_pick_time != null)
                                    {
                                        cell.SetCellValue((DateTime)order.skiPasses[j].card_member_pick_time);
                                        cell.CellStyle = styleDate;
                                    }
                                    else
                                    {
                                        cell.SetCellValue(nullStr);
                                        cell.CellStyle = styleText;
                                    }
                                    break;
                                case 8:
                                    if (order.skiPasses[j].card_member_pick_time != null)
                                    {
                                        cell.SetCellValue((DateTime)order.skiPasses[j].card_member_pick_time);
                                        cell.CellStyle = styleTime;
                                    }
                                    else
                                    {
                                        cell.SetCellValue(nullStr);
                                        cell.CellStyle = styleText;
                                    }
                                    break;
                                case 9:

                                    cell.SetCellValue(order.skiPasses[j].card_no == null ? nullStr : order.skiPasses[j].card_no.Trim());
                                    cell.CellStyle = styleText;
                                    break;
                                case 10:
                                    if (j == 0)
                                    {
                                        cell.SetCellValue(order.paidAmount);
                                        cell.CellStyle = styleMoney;
                                    }
                                    needMerge = true;
                                    break;
                                case 11:
                                    if (j == 0)
                                    {
                                        cell.SetCellValue(order.refundAmount);
                                        cell.CellStyle = styleMoney;
                                    }
                                    needMerge = true;
                                    break;
                                case 12:
                                    if (j == 0)
                                    {
                                        //double earn = order.paidAmount - order.refundAmount;
                                        cell.SetCellValue(earn);
                                        if (earn <= 0)
                                        {
                                            cell.CellStyle = styleMoneyProblem;
                                        }
                                        else
                                        {
                                            cell.CellStyle = styleMoney;
                                        }

                                    }
                                    needMerge = true;
                                    break;
                                case 13:
                                    cell.SetCellValue(order.skiPasses[j].contact_name == null ? "" : order.skiPasses[j].contact_name.Trim());
                                    cell.CellStyle = styleText;
                                    break;
                                case 14:
                                    cell.SetCellValue(order.skiPasses[j].contact_cell == null ? "" : order.skiPasses[j].contact_cell.Trim());
                                    cell.CellStyle = styleText;
                                    break;
                                case 15:
                                    cell.SetCellValue(cancel?"退票":"正常");
                                    cell.CellStyle = styleText;
                                    break;
                                default:
                                    break;
                            }
                        }
                        else if (k < commonHead.Length + maxPaymentCount * paymentHead.Length)
                        {
                            int paymentIndex = (k - commonHead.Length) / paymentHead.Length;
                            if (paymentIndex < order.paymentList.Count)
                            {
                                switch ((k - commonHead.Length) % paymentHead.Length)
                                {
                                    case 0:
                                        if (j == 0)
                                        {

                                            cell.SetCellValue(order.paymentList[paymentIndex].wepay_trans_id == null ? nullStr : order.paymentList[paymentIndex].wepay_trans_id.Trim());
                                            cell.CellStyle = styleText;
                                        }


                                        needMerge = true;
                                        break;
                                    case 1:
                                        if (j == 0)
                                        {
                                            cell.SetCellValue(order.paymentList[paymentIndex].out_trade_no == null ? nullStr : order.paymentList[paymentIndex].out_trade_no.Trim());
                                            cell.CellStyle = styleText;
                                        }
                                        needMerge = true;
                                        break;
                                    case 2:
                                        if (j == 0)
                                        {
                                            cell.SetCellValue(order.paymentList[paymentIndex].amount);
                                            cell.CellStyle = styleMoney;
                                        }
                                        needMerge = true;
                                        break;

                                    case 3:
                                        if (j == 0)
                                        {
                                            cell.SetCellValue((DateTime)order.paymentList[paymentIndex].create_date);
                                            cell.CellStyle = styleDate;
                                        }
                                        needMerge = true;
                                        break;
                                    case 4:
                                        if (j == 0)
                                        {
                                            cell.SetCellValue((DateTime)order.paymentList[paymentIndex].create_date);
                                            cell.CellStyle = styleTime;
                                        }
                                        needMerge = true;
                                        break;

                                    default:
                                        break;

                                }
                            }
                            else
                            {
                                cell.SetCellValue(nullStr);
                                cell.CellStyle = styleText;
                            }

                        }
                        else
                        {
                            needMerge = true;
                            int refundIndex = (k - commonHead.Length - paymentHead.Length * maxPaymentCount) / refundHead.Length;
                            if (refundIndex < order.refundList.Count)
                            {
                                switch ((k - commonHead.Length - paymentHead.Length * maxPaymentCount) % refundHead.Length)
                                {
                                    case 0:
                                        cell.SetCellValue(order.refundList[refundIndex].refund_id == null ? nullStr : order.refundList[refundIndex].refund_id.Trim());
                                        cell.CellStyle = styleText;
                                        break;
                                    case 1:
                                        cell.SetCellValue(order.refundList[refundIndex].out_refund_no == null ? nullStr : order.refundList[refundIndex].out_refund_no.Trim());
                                        cell.CellStyle = styleText;
                                        break;
                                    case 2:
                                        cell.SetCellValue(order.refundList[refundIndex].amount);
                                        cell.CellStyle = styleMoney;
                                        break;
                                    case 3:
                                        cell.SetCellValue((DateTime)order.refundList[refundIndex].create_date);
                                        cell.CellStyle = styleDate;
                                        break;
                                    case 4:
                                        cell.SetCellValue((DateTime)order.refundList[refundIndex].create_date);
                                        cell.CellStyle = styleTime;
                                        break;

                                    default:
                                        break;

                                }
                            }
                            else
                            {
                                cell.SetCellValue(nullStr);
                                cell.CellStyle = styleText;
                            }
                        }
                        if (needMerge)
                        {
                            if (order.skiPasses.Count > 1 && j == order.skiPasses.Count - 1)
                            {
                                sheet.AddMergedRegion(new NPOI.SS.Util.CellRangeAddress(subIndex - order.skiPasses.Count + 1, subIndex, k, k));
                            }
                        }
                    }
                }

            }
        }
        [HttpGet]
        public async Task ExportExcel()
        {
            DateTime startDate = DateTime.Parse("2024-11-15");
            List<OrderOnline> ol = await _db.OrderOnlines
                .Include(o => o.skiPasses)
                .Include(o => o.paymentList.Where(p => p.status.Trim().Equals("支付成功")))
                    .ThenInclude(p => p.refunds.Where(r => r.state == 1 || !r.refund_id.Trim().Equals("")))
                        //.ThenInclude(r => r.msa).ThenInclude(m => m.member)
                .Where(o => o.type.Trim().Equals("雪票") && o.shop.Trim().Equals("南山") && o.pay_state == 1 && o.create_date >= startDate)
                .OrderByDescending(o => o.id).AsNoTracking().ToListAsync();
            int maxPaymentCount = 0;
            int maxRefundCount = 0;
            for (int i = 0; i < ol.Count; i++)
            {
                OrderOnline order = ol[i];
                maxPaymentCount = Math.Max(maxPaymentCount, order.paymentList.Count);
                maxRefundCount = Math.Max(maxRefundCount, order.refundList.Count);

            }
            string[] commonHead = new string[] { "序号", "业务日期", "业务时间", "子序号", "票种", "票价", "押金", "核销日期", "核销时间", "卡号", "支付", "退款", "结余", "联系人", "电话", "状态" };
            string[] paymentHead = new string[] { "微信支付单号", "商户订单号", "金额", "支付日期", "支付时间" };
            string[] refundHead = new string[] { "微信退款单号", "商户退款单号", "金额", "退款日期", "退款时间" };

            XSSFWorkbook workbook = new XSSFWorkbook();
            ISheet sheet = workbook.CreateSheet("南山雪票");
            ExportExcelCreateHead(workbook, sheet, commonHead, paymentHead, refundHead, maxPaymentCount, maxRefundCount);
            ExportExcelInsertData(workbook, sheet, ol, commonHead, paymentHead, refundHead, maxPaymentCount, maxRefundCount);
            string filePath = $"{Environment.CurrentDirectory}" + "/nanshan.xlsx";
            using (var file = System.IO.File.Create(filePath))
            {
                workbook.Write(file);
            }

        }


    }


}