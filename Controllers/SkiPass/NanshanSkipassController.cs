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
using Microsoft.Extensions.Configuration;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Models;
using SnowmeetApi.Controllers.User;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Product;
using SnowmeetApi.Models.SkiPass;
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
        
        [HttpPost]
        public async Task<ActionResult<Models.SkiPass.SkiPass>> UpdateSkiPass([FromBody] Models.SkiPass.SkiPass skipass, 
            [FromQuery] string sessionKey, [FromQuery] string sessionType = "wechat_mini_openid")
        {
            if (!(await _memberHelper.isStaff(sessionKey, sessionType)))
            {
                return BadRequest();
            }
            _db.skiPass.Entry(skipass).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(skipass);
        }

    }
}