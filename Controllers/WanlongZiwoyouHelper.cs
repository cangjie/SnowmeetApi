﻿using System;
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
using static SKIT.FlurlHttpClient.Wechat.TenpayV3.Models.AddHKSubMerchantRequest.Types;

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

        public WanlongZiwoyouHelper(ApplicationDBContext context, IConfiguration config)
		{
            _context = context;
            _config = config.GetSection("Settings");
            _appId = _config.GetSection("AppId").Value.Trim();
            apiKey = dhhsApiKey;
            custId = dhhsCustId;
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
            if (keyword.IndexOf("万龙") >= 0)
            {
                custId = wlCustId;
                apiKey = wlApiKey;            
            }
            string postJson = "{\"apikey\": \"" + apiKey + "\",\t\"catIds\": \"\",\t\"cityId\": \"\",\t\"cityName\": \"\",\t\"custId\": " + custId + " ,\t\"isConfirm\": \"0\",\t\"isExpress\": \"0\",\t\"isMulti\": \"\",\t\"isPackage\": \"\",\t\"isPay\": \"\",\t\"keyWord\": \"" + keyword.Trim() + "\",\t\"orderBy\": \"\",\t\"page\": " + pageNum.ToString() + ",\t\"productNos\": \"\",\t\"resultNum\": " + pageSize.ToString() + ",\t\"tagIds\": \"\",\t\"treeId\": \"\",\t\"viewId\": \"\"}";
            string ret = Util.GetWebContent("https://task-api.zowoyoo.com/api/thirdPaty/prod/list", postJson, "application/json");
            Console.WriteLine(postJson);
            ProductQueryResult r = JsonConvert.DeserializeObject<ProductQueryResult>(ret);
            return r;
        }

        [HttpGet]
        public ActionResult<ZiwoyouPlaceOrderResult> PlaceOrder(string productNo, string name, string cell, int count, DateTime date, string memo, int orderId)
        {

            string postData = "{\n\t\"apikey\": \"" + apiKey
                + "\",\n\t\"custId\": " + custId + " ,\n\t\"infoId\": " + productNo
                + ",\n\t\"isSend\": \"1\",\n\t\"linkMan\": \"" + name
                + "\",\n\t\"linkPhone\": \"" + cell + "\",\n\t\"num\": " + count.ToString()
                + ",\n\t\"orderMemo\": \"" + memo + "\",\n\t\"orderSourceId\": \"" + orderId.ToString()
                + "\",\n\t\"travelDate\": \"" + date.ToString("yyyy-MM-dd") + "\"\n}";
            string ret = Util.GetWebContent("https://task-api-stag.zowoyoo.com/api/thirdPaty/order/add",
                postData, "application/json");
            ZiwoyouPlaceOrderResult r = JsonConvert.DeserializeObject<ZiwoyouPlaceOrderResult>(ret);
            return Ok(r);

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

        [HttpGet("{orderId}")]
        public ActionResult<PayResult> Pay(int orderId)
        {
            string postData = "{\"apikey\": \"" + apiKey + "\",\"custId\": " + custId.Trim() + ",\"orderId\": " + orderId.ToString() + "}";
            string ret = Util.GetWebContent("https://task-api-stag.zowoyoo.com/api/thirdPaty/order/pay",
                postData, "application/json");
            PayResult p = JsonConvert.DeserializeObject<PayResult>(ret);
            return Ok(p);

        }

        [HttpGet("{orderId}")]
        public ActionResult<string> GetOrder(int orderId)
        {
            string postData = "{\"apikey\": \"" + apiKey + "\",\"custId\": " + custId.Trim()
                + ",\"orderId\": " + orderId.ToString() + "}";
            string ret = Util.GetWebContent("https://task-api-stag.zowoyoo.com/api/thirdPaty/order/detail",
                postData, "application/json");
            return Ok(ret);

        }

        [HttpGet("{orderId}")]
        public ActionResult<string> CancelOrder(int orderId)
        {
            string postData = "{\"apikey\": \"" + apiKey + "\",\"custId\": " + custId.Trim()
                + ",\"orderId\": " + orderId.ToString() + "}";
            string ret = Util.GetWebContent("https://task-api-stag.zowoyoo.com/api/thirdPaty/order/cancel",
                postData, "application/json");
            return Ok(ret);

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

        [HttpPost]
        public ActionResult CallBack()
        {
            return Ok();
        }

        




    }
}

