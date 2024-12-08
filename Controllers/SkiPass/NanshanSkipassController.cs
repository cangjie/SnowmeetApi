using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using AlipaySDKNet.OpenAPI.Model;
using Aop.Api.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Configuration;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Models;
using SnowmeetApi.Controllers.User;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Order;
using SnowmeetApi.Models.Product;
using SnowmeetApi.Models.SkiPass;
using SnowmeetApi.Models.Users;
namespace SnowmeetApi.Controllers.SkiPass
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class NanshanSkipassController : ControllerBase
    {
      
        public class ReserveSummary
        {
            public int product_id { get; set; }
            public string product_name {get; set;}
            public double deal_price  {get; set;}
            public int count {get; set; }
            public double sumDealPrice {get; set;}
            public int pickCount {get; set;}
        }

        public class ReserveMemberProduct
        {
            public int member_id { get; set; }
            public string? wechat_mini_openid {get; set; }
            public string name {get; set;}
            public string cell {get; set;}
            public List<Models.SkiPass.SkiPass> skiPasses {get; set;} = new List<Models.SkiPass.SkiPass>();
        }

        public class ReserveProduct
        {
            public int product_id {get; set; }
            public string product_name {get; set;}
            public DateTime reserveDate {get; set;}
            //public int memberCount {get; set;}
            public int skiPassCount {get; set;}
            public List<ReserveMemberProduct> memberList {get; set;} = new List<ReserveMemberProduct>();
        }

        public class ReserveDateProduct
        {
            public DateTime reserveDate { get; set; }
            public int product_id {get; set; }
            public string product_name {get; set;}
            public string name {get; set;}
            public string cell {get; set;}
            public List<Models.SkiPass.SkiPass> skiPasses {get; set;} = new List<Models.SkiPass.SkiPass>();
        }

        /*
        public class NanshanSkiReserveDetailKey
        {
            public int member_id { get; set; }
            public string wechat_mini_openid {get; set;}

            public string realName {get; set;}

            public string cell {get; set;}

            public int count {get;set;}

            //public List<Models.SkiPass.SkiPass> skiPassList {get; set;} = new List<Models.SkiPass.SkiPass>();
            //public DateTime reserveDate {get; set;}
        }
        */
        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _http;

        private readonly MemberController _memberHelper;
        public NanshanSkipassController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = context;
            _config = config;
            _http = httpContextAccessor;
            _memberHelper = new MemberController(context, config);
        }

        [HttpGet]
        public async Task<ActionResult<List<object>>> GetReserve(DateTime date, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            if (!(await _memberHelper.isStaff(sessionKey, sessionType)))
            {
                return BadRequest();
            }
            List<Models.SkiPass.SkiPass> skiPassList = await _db.skiPass
                .Where(sp => (sp.resort.Trim().Equals("南山") && sp.valid == 1 && ((DateTime)sp.reserve_date).Date == date.Date ))
                .AsNoTracking().ToListAsync();
            var summary = from skiPass in skiPassList group skiPass by new {skiPass.product_id, skiPass.product_name , skiPass.deal_price}
                into sum
                select new {sum.Key, count = sum.Count(), sumDealPrice = sum.Sum(s => (s.deal_price * s.count)) , pickCount = 0};
            //var sumE = summary.GetEnumerator();
            var retList = summary.ToList();
            List<ReserveSummary> newList = new List<ReserveSummary>();
            for(int i = 0; i < retList.Count; i++)
            {
                var l =  retList[i];
                int pickCount = 0;
                for(int j = 0; j < skiPassList.Count; j++)
                {
                    if (skiPassList[j].product_id == l.Key.product_id 
                        && (skiPassList[j].card_image_url != null || skiPassList[j].card_no != null  ))
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

        
        [HttpGet("{productId}")]
        public async Task<ActionResult<ReserveProduct>> GetReserveProductDetail(int productId, DateTime reserveDate, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            if (!(await _memberHelper.isStaff(sessionKey, sessionType)))
            {
                return BadRequest();
            }
            List<Models.SkiPass.SkiPass> skiPassList = await _db.skiPass
                .Where(sp => (sp.resort.Trim().Equals("南山") && sp.valid == 1 
                && ((DateTime)sp.reserve_date).Date == reserveDate.Date && sp.product_id == productId ))
                .AsNoTracking().ToListAsync();
            var l = (from reserveDetail in skiPassList group reserveDetail 
                by new {reserveDetail.member_id, reserveDetail.wechat_mini_openid, reserveDetail.contact_name, reserveDetail.contact_cell  }
                into reserveSum select new {reserveSum.Key, count = reserveSum.Count()}).ToList();
            Models.Product.Product p = await _db.Product.FindAsync(productId);
            ReserveProduct ret = new ReserveProduct()
            {
                product_id = productId,
                product_name = p.name.Trim(),
                skiPassCount = skiPassList.Count(),
                reserveDate = reserveDate
            };
            List<ReserveMemberProduct> members = ret.memberList;
            for(int i = 0; i < l.Count; i++)
            {
                
                ReserveMemberProduct member = new ReserveMemberProduct()
                {
                    member_id = l[i].Key.member_id,
                    name = l[i].Key.contact_name,
                    cell = l[i].Key.contact_cell,
                    wechat_mini_openid = l[i].Key.wechat_mini_openid
                };
                for(int j = 0; j < skiPassList.Count; j++)
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
            if (!(await _memberHelper.isStaff(sessionKey, sessionType)))
            {
                return BadRequest();
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
            return Ok(skipass);
        }

        [HttpGet]
        public async Task<ActionResult<List<ReserveDateProduct>>> GetMemberCard(int memberId, 
            string wechatMiniOpenId, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            if (!(await _memberHelper.isStaff(sessionKey, sessionType)))
            {
                return BadRequest();
            }
            List<Models.SkiPass.SkiPass> skipasses = await GetSkipassesByMember(memberId, wechatMiniOpenId);
            var reserveList = (from s in skipasses where s.valid == 1 group s by 
                new {s.reserve_date, s.product_id, s.product_name, s.contact_cell, s.contact_name}
                into rl select new {rl.Key}).OrderByDescending(r => r.Key.reserve_date).ToList();
            List<ReserveDateProduct> ret = new List<ReserveDateProduct>();
            for(int i = 0; i < reserveList.Count; i++)
            {
                ReserveDateProduct item = new ReserveDateProduct()
                {
                    name = reserveList[i].Key.contact_name,
                    cell = reserveList[i].Key.contact_cell,
                    reserveDate = (DateTime)reserveList[i].Key.reserve_date,
                    product_id = reserveList[i].Key.product_id,
                    product_name = reserveList[i].Key.product_name
                };
                for(int j = 0; j < skipasses.Count; j++)
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
            int count, string cell, string name, string sessionKey, string sessionType = "wechat_mini_openid")
        {
            Models.Product.Product product = await _db.Product.FindAsync(productId);
            UnicUser user = await  UnicUser.GetUnicUserAsync(sessionKey, _db);
            if (user == null || product == null)
            {
                return BadRequest();
            }
            Models.Users.Member member = await _memberHelper.GetMemberBySessionKey(sessionKey, sessionType);
            double totalPrice = 0;
            Models.SkiPass.SkiPass[] skipassArr = new Models.SkiPass.SkiPass[count];
            for(int i = 0; i < count; i++)
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
                pay_method = "微信支付"
            };
            await _db.OrderOnlines.AddAsync(order);
            await _db.SaveChangesAsync();

            OrderPayment payment = new OrderPayment()
            {
                order_id = order.id,
                pay_method = order.pay_method.Trim(),
                amount = order.final_price,
                status = "待支付",
                staff_open_id = ""
            };
            await _db.OrderPayment.AddAsync(payment);

            for(int i = 0; i < skipassArr.Length; i++)
            {
                Models.SkiPass.SkiPass skipass = skipassArr[i];
                skipass.order_id = order.id;
                await _db.skiPass.AddAsync(skipass);
            }
            await _db.SaveChangesAsync();
            order.payments = new OrderPayment[] { payment };

            member.real_name = name;
            _db.member.Entry(member).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            await _memberHelper.UpdateDetailInfo(member.id, cell, "cell", false);

            return Ok(order);
        }
    }

    
}