using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Users;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Models.WanLong;
using Newtonsoft.Json;
using Aop.Api.Domain;
using AlipaySDKNet.OpenAPI.Model;
using NuGet.Packaging;
using Microsoft.EntityFrameworkCore.Internal;
using static SKIT.FlurlHttpClient.Wechat.TenpayV3.Models.AddHKSubMerchantRequest.Types;
using SnowmeetApi.Models.Order;
using System.IO;
using SnowmeetApi.Models.Product;

namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class WanlongZiwoyouHelper:ControllerBase
	{
        private readonly ApplicationDBContext _context;

        private IConfiguration _config;

        public string _appId = "";

        public string wlCustId = "6914348";

        public string wlApiKey = "3951EA4CA7BF7B679787F67E6262E1DD";

        public string dhhsCustId = "3230671";

        public string dhhsApiKey = "B71DD78AE810D436D09380505FF28120";

        public string apiKey = "";

        public string custId = "";
        public string source = "大好河山";

        public class Voucher
        { 
            public string code { get; set; }
            public string qrcodeUrl { get; set; }
            public int type { get; set; }
            public int voucherStatus { get; set; }

        }

        public class ZiwoyouOrder
        {
            public int? orderId { get; set; } = null;
            public string? orderSourceId { get; set; } = null;
            public int? orderState { get; set; } = null;
            public string? orderState2 { get; set; } = null;
            public int? productNo { get; set; } = null;
            public string? productName { get; set; } = null;
            public int? num { get; set; } = null;
            public double? settlementPrice { get; set; } = null;
            public double? salePrice { get; set; } = null;
            public double? marketPrice { get; set; } = null;
            public double? orderMoney { get; set; } = null;
            public double? memOrderMoney { get; set; } = null; 
            public string? sendContent1 { get; set; } = null;
            public string? sendContent2 { get; set; } = null;
            public string? sendContent3 { get; set; } = null;
            public Voucher[]? vouchers { get; set; } = null;
        }

        public class ZiwoyouQueryResult
        {
            public string msg { get; set; }
            public int state { get; set; }
            public object data {get; set;}
        }

        public class ZiwoyouAccountBalance
        { 
            public double accountBalance { get; set; }
        }

        public class ZiwoyouCancel
        {
            public int orderId {get; set;}
            public int cancelState {get; set;}
        }
        public class ZiwoyouDailyPrice
        {
            public DateTime date {get; set;}
            public double settlementPrice {get; set;}
            public double salePrice {get; set;}
            public double marketPrice {get; set;}
            public int? num {get; set;}
            public int? seats {get; set;}
        }
        public class ZiwoyouProductDailyPrice
        {
            public int infoId {get; set;}
            public ZiwoyouDailyPrice[] ticketPrices {get; set;}
        }

       

        public WanlongZiwoyouHelper(ApplicationDBContext context, IConfiguration config, string source="大好河山")
		{
            _context = context;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
            apiKey = dhhsApiKey;
            custId = dhhsCustId;
            this.source = source;
            SetParam(source);
        }

        [HttpGet]
        public ActionResult<ProductQueryResult> GetProductList(string keyword)
        {
            int pageSize = 20;
            string custId = dhhsCustId;
            string apiKey = dhhsApiKey;
            /*
            if (keyword.IndexOf("万龙") >= 0)
            {
                custId = wlCustId;
                apiKey = wlApiKey;
            }
            */
            string postJson = "{\"apikey\": \"" + apiKey + "\",\t\"catIds\": \"\",\t\"cityId\": \"\",\t\"cityName\": \"\",\t\"custId\": " + custId + " ,\t\"isConfirm\": \"0\",\t\"isExpress\": \"0\",\t\"isMulti\": \"\",\t\"isPackage\": \"\",\t\"isPay\": \"\",\t\"keyWord\": \"" + keyword.Trim() + "\",\t\"orderBy\": \"\",\t\"page\": 0,\t\"productNos\": \"\",\t\"resultNum\": " + pageSize.ToString() + ",\t\"tagIds\": \"\",\t\"treeId\": \"\",\t\"viewId\": \"\"}";
            //string ret = Util.GetWebContent("https://task-api-stag.zowoyoo.com/api/thirdPaty/prod/list", postJson,"application/json");
            string ret = Util.GetWebContent("https://task-api.zowoyoo.com/api/thirdPaty/prod/list", postJson,"application/json");
            Console.WriteLine(postJson);
            
            string path = $"{Environment.CurrentDirectory}";
            string dateStr = DateTime.Now.Year.ToString() + DateTime.Now.Month.ToString().PadLeft(2, '0')
                + DateTime.Now.Day.ToString().PadLeft(2, '0');
            using (StreamWriter fw = new StreamWriter(path + "/booking_" + dateStr + ".txt", true))
            {
                fw.WriteLine(DateTime.Now.ToString());
                fw.WriteLine("https://task-api.zowoyoo.com/api/thirdPaty/prod/list");
                fw.WriteLine(postJson);
                fw.WriteLine(ret);
                fw.WriteLine("");

            }




            ProductQueryResult r = JsonConvert.DeserializeObject<ProductQueryResult>(ret);
            int pageCount = r.data.pageCount;
            for (int i = 1; i < pageCount; i++)
            {
                ProductQueryResult subR = GetProductListByPage(keyword, i);
                if (subR.state != 1)
                {
                    continue;
                }
                //r.data.results.AddRange(subR.data.results.to);
                SkiPassProduct[] newResults = new SkiPassProduct[r.data.results.Length + subR.data.results.Length];
                for (int j = 0; j < r.data.results.Length; j++)
                {
                    newResults[j] = r.data.results[j];
                }

                for (int j = r.data.results.Length; j < newResults.Length; j++)
                {
                    newResults[j] = subR.data.results[j - r.data.results.Length];
                }
                r.data.results = newResults;


            }
            return Ok(r);
        }



        [NonAction]
        public ProductQueryResult GetProductListByPage(string keyword, int pageNum)
        {
            int pageSize = 20;
            string custId = dhhsCustId;
            string apiKey = dhhsApiKey;
            /*
            if (keyword.IndexOf("万龙") >= 0)
            {
                custId = wlCustId;
                apiKey = wlApiKey;            
            }
            */
            string postJson = "{\"apikey\": \"" + apiKey + "\",\t\"catIds\": \"\",\t\"cityId\": \"\",\t\"cityName\": \"\",\t\"custId\": " + custId + " ,\t\"isConfirm\": \"0\",\t\"isExpress\": \"0\",\t\"isMulti\": \"\",\t\"isPackage\": \"\",\t\"isPay\": \"\",\t\"keyWord\": \"" + keyword.Trim() + "\",\t\"orderBy\": \"\",\t\"page\": " + pageNum.ToString() + ",\t\"productNos\": \"\",\t\"resultNum\": " + pageSize.ToString() + ",\t\"tagIds\": \"\",\t\"treeId\": \"\",\t\"viewId\": \"\"}";
            string ret = Util.GetWebContent("https://task-api.zowoyoo.com/api/thirdPaty/prod/list", postJson, "application/json");

            string path = $"{Environment.CurrentDirectory}";
            string dateStr = DateTime.Now.Year.ToString() + DateTime.Now.Month.ToString().PadLeft(2, '0')
                + DateTime.Now.Day.ToString().PadLeft(2, '0');
            using (StreamWriter fw = new StreamWriter(path + "/booking_" + dateStr + ".txt", true))
            {
                fw.WriteLine(DateTime.Now.ToString());
                fw.WriteLine("https://task-api.zowoyoo.com/api/thirdPaty/prod/list");
                fw.WriteLine(postJson);
                fw.WriteLine(ret);
                fw.WriteLine("");

            }


            Console.WriteLine(postJson);
            ProductQueryResult r = JsonConvert.DeserializeObject<ProductQueryResult>(ret);
            return r;
        }

        [HttpGet("skiPassId")]
        public async Task<ActionResult<ZiwoyouPlaceOrderResult>> Book(int skiPassId)
        {
            Models.SkiPass.SkiPass skipass = await _context.skiPass.FindAsync(skiPassId);
            if (skipass == null)
            {
                return NotFound();
            }
            if (skipass.card_member_pick_time != null || skipass.valid != 1)
            {
                return BadRequest();
            }
            List<OrderPayment> payList = await _context.OrderPayment.Where(p => p.order_id == skipass.order_id)
                .AsNoTracking().ToListAsync();
            string outOrderNo = "";
            foreach(OrderPayment payment in payList)
            {
                if (!payment.out_trade_no.Trim().Equals(""))
                {
                    outOrderNo = payment.out_trade_no;
                    break;
                }
            }
            if (outOrderNo.Trim().Equals(""))
            {
                return NoContent();
            }
            
            Models.Product.SkiPass skipassProduct = await _context.SkiPass.FindAsync(skipass.product_id);
            if (skipassProduct.source.Trim().Equals("大好河山"))
            {
                apiKey = dhhsApiKey;
                custId = dhhsCustId;
            }
            else
            {
                apiKey = wlApiKey;
                custId = wlCustId;
            }
            ZiwoyouPlaceOrderResult bookResult = PlaceOrder(skipassProduct.third_party_no, skipass.contact_name, skipass.contact_cell, skipass.contact_id_type, 
                skipass.contact_id_no,skipass.count, (DateTime)skipass.reserve_date, "", outOrderNo);
            skipass.reserve_no = bookResult.data.orderId.Trim();
            return Ok(bookResult);
        }

        [NonAction]
        public ZiwoyouPlaceOrderResult PlaceOrder(string productNo, string name, string cell, 
            string idType, string idNo, int count, DateTime date, string memo, string orderId)
        {

            string postData = "{\n\t\"apikey\": \"" + apiKey
                + "\",\n\t\"custId\": " + custId + " ,\n\t\"infoId\": " + productNo
                + ",\n\t\"isSend\": \"1\",\n\t\"linkMan\": \"" + name + "\", \"linkCreditType\": 0, \"linkCreditNo\": \"" + idNo.Trim()
                + "\",\n\t\"linkPhone\": \"" + cell + "\",\n\t\"num\": " + count.ToString()
                + ",\n\t\"orderMemo\": \"" + memo + "\",\n\t\"orderSourceId\": \"" + orderId.Trim()
                + "\",\n\t\"travelDate\": \"" + date.ToString("yyyy-MM-dd") + "\"\n}";
            string ret = Util.GetWebContent("https://task-api.zowoyoo.com/api/thirdPaty/order/add",
                postData, "application/json");
            
            string path = $"{Environment.CurrentDirectory}";
            string dateStr = DateTime.Now.Year.ToString() + DateTime.Now.Month.ToString().PadLeft(2, '0')
                + DateTime.Now.Day.ToString().PadLeft(2, '0');
            using (StreamWriter fw = new StreamWriter(path + "/booking_" + dateStr + ".txt", true))
            {
                fw.WriteLine(DateTime.Now.ToString());
                fw.WriteLine("https://task-api.zowoyoo.com/api/thirdPaty/order/add");
                fw.WriteLine(postData);
                fw.WriteLine(ret);
                fw.WriteLine("");

            }

            ZiwoyouPlaceOrderResult r = JsonConvert.DeserializeObject<ZiwoyouPlaceOrderResult>(ret);
            return r;

        }

        /*
        [HttpGet("{productNo}")]
        public ActionResult<string> GetProductDetail(string productNo)
        {
            string postData = "{\"apikey\": \"" + apiKey + "\",\"custId\": " + custId.Trim() + ",\"productNo\": " + productNo.ToString() + "}";
            string ret = Util.GetWebContent("https://task-api-stag.zowoyoo.com/api/thirdPaty/prod/detail",
                postData, "application/json");
            return Ok(ret);
        }
        */

        [NonAction]
        public PayResult Pay(int orderId)
        {
            string postData = "{\"apikey\": \"" + apiKey + "\",\"custId\": " + custId.Trim() + ",\"orderId\": " + orderId.ToString() + "}";
            string ret = Util.GetWebContent("https://task-api.zowoyoo.com/api/thirdPaty/order/pay",
                postData, "application/json");

            string path = $"{Environment.CurrentDirectory}";
            string dateStr = DateTime.Now.Year.ToString() + DateTime.Now.Month.ToString().PadLeft(2, '0')
                + DateTime.Now.Day.ToString().PadLeft(2, '0');
            using (StreamWriter fw = new StreamWriter(path + "/booking_" + dateStr + ".txt", true))
            {
                fw.WriteLine(DateTime.Now.ToString());
                fw.WriteLine("https://task-api.zowoyoo.com/api/thirdPaty/order/pay");
                fw.WriteLine(postData);
                fw.WriteLine(ret);
                fw.WriteLine("");

            }
            

            PayResult p = JsonConvert.DeserializeObject<PayResult>(ret);
            return p;

        }

        [HttpGet]
        public string GetProductDetail(int productId)
        {
            string postData = "{\"apikey\": \"" + apiKey + "\",\"custId\": " + custId.Trim() + ",\"productNo\": " + productId.ToString() + "}";
            string ret = Util.GetWebContent("https://task-api.zowoyoo.com/api/thirdPaty/prod/detail",
                postData, "application/json");

            return ret;
        }

        [HttpGet]
        public ZiwoyouProductDailyPrice GetProductPrice(int productId, DateTime date)
        {
            string postData = "{\"apikey\": \"" + apiKey + "\",\"custId\": " + custId.Trim() + ",\"productNo\": " 
                + productId.ToString() + ", \"travelDate\": \"" + date.ToString("yyyy-MM-dd") + "\" }";
            string ret = Util.GetWebContent("https://task-api.zowoyoo.com/api/thirdPaty/prod/price",
                postData, "application/json");
            ZiwoyouQueryResult r = JsonConvert.DeserializeObject<ZiwoyouQueryResult>(ret);
            ZiwoyouProductDailyPrice price = JsonConvert.DeserializeObject<ZiwoyouProductDailyPrice>(r.data.ToString());
            return price;
        }

        [HttpGet]
        public ZiwoyouOrder GetOrder(int orderId)
        {
            string postData = "{\"apikey\": \"" + apiKey + "\",\"custId\": " + custId.Trim()
                + ",\"orderId\": " + orderId.ToString() + "}";
            string ret = Util.GetWebContent("https://task-api.zowoyoo.com/api/thirdPaty/order/detail",
                postData, "application/json");

            string path = $"{Environment.CurrentDirectory}";
            string dateStr = DateTime.Now.Year.ToString() + DateTime.Now.Month.ToString().PadLeft(2, '0')
                + DateTime.Now.Day.ToString().PadLeft(2, '0');
            using (StreamWriter fw = new StreamWriter(path + "/booking_" + dateStr + ".txt", true))
            {
                fw.WriteLine(DateTime.Now.ToString());
                fw.WriteLine("https://task-api.zowoyoo.com/api/thirdPaty/order/detail");
                fw.WriteLine(postData);
                fw.WriteLine(ret);
                fw.WriteLine("");

            }

            
            ZiwoyouQueryResult r = JsonConvert.DeserializeObject<ZiwoyouQueryResult>(ret);
            ZiwoyouOrder order = JsonConvert.DeserializeObject<ZiwoyouOrder>(r.data.ToString());

            return order;

        }

        [NonAction]
        public ZiwoyouQueryResult CancelOrder(int orderId)
        {
            ZiwoyouOrder order = GetOrder(orderId);
            string postData = "{\"apikey\": \"" + apiKey + "\",\"custId\": " + custId.Trim()
                + ",\"orderId\": " + orderId.ToString() + ", \"cancelNum\": " + order.num.ToString() + "}";
            string ret = Util.GetWebContent("https://task-api.zowoyoo.com/api/thirdPaty/order/cancel",
                postData, "application/json");
            
            string path = $"{Environment.CurrentDirectory}";
            string dateStr = DateTime.Now.Year.ToString() + DateTime.Now.Month.ToString().PadLeft(2, '0')
                + DateTime.Now.Day.ToString().PadLeft(2, '0');
            using (StreamWriter fw = new StreamWriter(path + "/booking_" + dateStr + ".txt", true))
            {
                fw.WriteLine(DateTime.Now.ToString());
                fw.WriteLine("https://task-api.zowoyoo.com/api/thirdPaty/order/cancel");
                fw.WriteLine(postData);
                fw.WriteLine(ret);
                fw.WriteLine("");

            }

            ZiwoyouQueryResult r = JsonConvert.DeserializeObject<ZiwoyouQueryResult>(ret);
            ZiwoyouCancel cancel = JsonConvert.DeserializeObject<ZiwoyouCancel>(r.data.ToString());
            r.data = cancel;
            return r;

        }

        [NonAction]
        public async Task<Models.Product.SkiPass> GetSkipassProductByCode(string code)
        {
            var l = await _context.SkiPass.Where(s => s.third_party_no.Trim().Equals(code.Trim()))
                .AsNoTracking().ToListAsync();
            if (l==null || l.Count <= 0)
            {
                return null;
            }
            Models.Product.SkiPass skipass = l[0];
            skipass.product = await _context.Product.FindAsync(skipass.product_id);
            return skipass;

        }

        [HttpGet]
        public async Task UpdateSkipassProductPrice()
        {
            var l = await _context.SkiPass.Where(s => s.third_party_no != null).AsNoTracking().ToListAsync();
            //var l = await _context.SkiPass.Where(s => s.third_party_no.Equals("80018099")).AsNoTracking().ToListAsync();
            foreach(var item in l)
            {
                try
                {
                    if (item.product_id == 561)
                    {
                        string aa = "aa";
                    }
                    //ZiwoyouProductDailyPrice price = GetProductPrice(int.Parse((string)item.third_party_no), DateTime.Now.Date);
                    string priceStr = Util.GetWebContent("https://mini.snowmeet.top/core/WanlongZiwoyouHelper/GetProductPrice?productId=" + item.third_party_no.Trim() + "&date=" + DateTime.Now.ToString("yyyy-MM-dd") );//+ DateTime.Now.ToString("yyyy-MM-dd"));
                    //string priceStr = Util.GetWebContent("https://mini.snowmeet.top/core/WanlongZiwoyouHelper/GetProductPrice?productId=" + item.third_party_no.Trim() + "&date=2025-02-01"); //+ DateTime..ToString("yyyy-MM-dd") );//+ DateTime.Now.ToString("yyyy-MM-dd"));
                    ZiwoyouProductDailyPrice price = JsonConvert.DeserializeObject<ZiwoyouProductDailyPrice>(priceStr);


                    ZiwoyouDailyPrice[] priceArr = price.ticketPrices;
                    if (priceArr.Length == 0)
                    {
                        continue;
                    }
                    DateTime startDate = priceArr[0].date.Date;
                    var oriPriceList = await _context.skipassDailyPrice
                        .Where(s => s.third_party_id.Trim().Equals(price.infoId.ToString())  && s.valid == 1 && s.reserve_date.Date >= startDate).ToListAsync();
                    for(int i = 0; i < priceArr.Length; i++)
                    {
                        var priceObj = priceArr[i];
                        bool changed = false;
                        bool exists = false;
                        double revenu = 0;
                        string dayType = "";
                        for(int j = 0; j < oriPriceList.Count; j++)
                        {
                            var oriPrice = oriPriceList[j];
                            if (oriPrice.reserve_date.Date == priceObj.date.Date)
                            {
                                exists = true;
                                if (oriPrice.settlementPrice != priceObj.settlementPrice)
                                {
                                    changed = true;
                                    oriPrice.valid = 0;
                                    revenu = oriPrice.deal_price - oriPrice.settlementPrice;
                                    dayType = oriPrice.day_type.Trim();
                                    _context.skipassDailyPrice.Entry(oriPrice).State = EntityState.Modified;
                                    
                                }
                                break;
                            }
                        }
                        if (changed || !exists)
                        {
                            if (dayType.Trim().Equals(""))
                            {
                                dayType = ((priceObj.date.Date.DayOfWeek == DayOfWeek.Sunday || priceObj.date.Date.DayOfWeek == DayOfWeek.Saturday)? "周末" : "平日");
                            }
                            if (revenu == 0)
                            {
                                try
                                {
                                    SkipassDailyPrice lastPrice = await _context.skipassDailyPrice
                                        .Where(s => (s.product_id == item.product_id && s.day_type.Trim().Equals(dayType.Trim()) 
                                        && s.valid == 1 )).OrderByDescending(s => s.reserve_date).FirstAsync();
                                    if (lastPrice != null)
                                    {
                                        revenu = lastPrice.deal_price - lastPrice.settlementPrice;
                                    }
                                    else
                                    {

                                    }
                                }
                                catch
                                {
                                    revenu = 20;
                                }
                            }
                            SkipassDailyPrice newPrice = new SkipassDailyPrice()
                            {
                                product_id = item.product_id,
                                third_party_id = item.third_party_no,
                                salePrice = priceObj.salePrice,
                                settlementPrice = priceObj.settlementPrice,
                                marketPrice = priceObj.marketPrice,
                                valid = 1,
                                deal_price = priceObj.settlementPrice + revenu,
                                reserve_date = priceObj.date.Date,
                                day_type = ((priceObj.date.Date.DayOfWeek == DayOfWeek.Sunday || priceObj.date.Date.DayOfWeek == DayOfWeek.Saturday)? "周末" : "平日")
                            };
                            await _context.skipassDailyPrice.AddAsync(newPrice);
                            await _context.SaveChangesAsync();
                        } 
                        
                    }
                }
                catch(Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        

        [HttpGet]
        public async Task UpdateSkipassProduct(string keyword)
        {
            ProductQueryResult originProductInfo = (ProductQueryResult)((OkObjectResult)GetProductList(keyword).Result).Value;
            for(int i = 0; i < originProductInfo.data.results.Length; i++)
            {
                SkiPassProduct skipassProduct = originProductInfo.data.results[i];
                Models.Product.SkiPass skipass = await GetSkipassProductByCode(skipassProduct.productNo);
                if (skipass != null)
                {
                    skipass.product.market_price = skipassProduct.salePrice;
                    skipass.product.cost = skipassProduct.settlementPrice;
                    _context.Entry<Models.Product.Product>(skipass.product).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                }
                else
                {
                    Models.Product.Product p = new Models.Product.Product()
                    {
                        id = 0,
                        name = skipassProduct.productName.Trim(),
                        sale_price = skipassProduct.settlementPrice + 15,
                        market_price = skipassProduct.salePrice,
                        cost = skipassProduct.settlementPrice,
                        type = "雪票",
                        shop = "崇礼旗舰店",
                        hidden = 0,
                        start_date = DateTime.Parse("2024-10-1"),
                        end_date = DateTime.Parse("2025-6-1"),
                        intro = skipassProduct.orderDesc.Trim(),
                        principal = ""

                    };
                    await _context.Product.AddAsync(p);
                    await _context.SaveChangesAsync();
                    Models.Product.SkiPass ski = new Models.Product.SkiPass()
                    {
                        product_id = p.id,
                        resort = keyword.Trim(),
                        rules = skipassProduct.orderDesc.Trim(),
                        source = this.source.Trim(),
                        third_party_no = skipassProduct.productNo
                    };
                    await _context.SkiPass.AddAsync(ski);
                    await _context.SaveChangesAsync();

                }
            }

        }

        
        [HttpGet("{id}")]
        public async Task<ActionResult<object>> GetProductById(int id)
        {
            //var p = await _context.SkiPass.Include(s => s.dailyPrice).AsNoTracking().ToListAsync();
            var l = await _context.SkiPass.Include(s => s.dailyPrice)
                .Join(_context.Product, s=>s.product_id, p=>p.id,
                (s, p)=> new {s.product_id, s.resort, s.rules, s.source, s.third_party_no, p.name, p.shop, p.sale_price, p.market_price, p.cost, p.type, s.dailyPrice})
                .Where(p => p.type.Trim().Equals("雪票")  && p.product_id == id
                && p.third_party_no != null)
                .AsNoTracking().ToListAsync();
            if (l == null || l.Count <= 0)
            {
                return NotFound();
            }
            //l[0].dailyPrice = await _context.skipassDailyPrice.Where(s => s.product_id == l[0].product_id && s.valid == 1 )
                //.OrderBy(s => s.reserve_date).AsNoTracking().ToListAsync();
            return Ok(l[0]);
        }



        [HttpPost]
        public ActionResult CallBack()
        {
            return Ok();
        }

        [HttpGet]
        public double GetBalance()
        {
            SetParam(source);
            string postData = "{\"apikey\": \"" + apiKey + "\", \"custId\": " + custId + "}";
            string ret = Util.GetWebContent("https://task-api.zowoyoo.com/api/thirdPaty/order/balance",
               postData, "application/json");
            string path = $"{Environment.CurrentDirectory}";
            
            string dateStr = DateTime.Now.Year.ToString() + DateTime.Now.Month.ToString().PadLeft(2, '0')
                + DateTime.Now.Day.ToString().PadLeft(2, '0');
            //string postJson = Newtonsoft.Json.JsonConvert.SerializeObject(postData);
            //path = path + "callback_" +  + ".txt";
            // 此文本只添加到文件一次。
            
            try
            {
                ZiwoyouQueryResult r = JsonConvert.DeserializeObject<ZiwoyouQueryResult>(ret);
                ZiwoyouAccountBalance b = JsonConvert.DeserializeObject<ZiwoyouAccountBalance>(r.data.ToString());
                return b.accountBalance;
            }
            catch
            {
                using (StreamWriter fw = new StreamWriter(path + "/booking_" + dateStr + ".txt", true))
                {
                    fw.WriteLine(DateTime.Now.ToString());
                    fw.WriteLine(postData);
                    fw.WriteLine(ret);
                    fw.WriteLine("");

                }
                return 0;
            }
        }

        [NonAction]
        public void SetParam(string source)
        {
            switch (source)
            {
                case "大好河山":
                    apiKey = dhhsApiKey;
                    custId = dhhsCustId;
                    break;
                default:
                    apiKey = wlApiKey;
                    custId = wlCustId;
                    break;
            }
        }

        




    }
}

