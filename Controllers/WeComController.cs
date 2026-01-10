using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
namespace SnowmeetApi.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class WeComController : ControllerBase
    {
        public ApplicationDBContext _db;
        public IConfiguration _config;
        public IHttpContextAccessor _http;

        public string[] wepayBalanceFieldsArr = new string[] {"交易时间", "公众账号ID", "商户号", "特约商户号", "设备号", "微信订单号", "商户订单号", "用户标识", "交易类型", "付款银行", "货币种类", "应结订单金额", "代金券金额", "微信退款单号", "商户退款单号", "退款金额", "充值券退款金额", "退款类型", "退款状态", "商品名称", "商户数据包", "手续费", "费率", "订单金额", "申请退款金额", "费率备注"};
        public WeComController (ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = context;
            _config = config;
            _http = httpContextAccessor;
        }
        [HttpGet]
        public async Task CreateTransTableTitle(string sheetId, string docId, string token)
        {
            string values = "";
            for(int i = 0; i < wepayBalanceFieldsArr.Length; i++)
            {
                string value = "{\"cell_value\":{\"text\": \"" + wepayBalanceFieldsArr[i].Trim() + "\"},  \"cell_format\":{\"bold\":true}    }";
                values += ((i == 0? "": ",") + value); 
            }
            string json = "{"
                + " \"docid\": \"" + docId + "\", "
                + " \"requests\": "
                +       "[" 
                +           " { \"update_range_request\" : " 
                +               " { "
                +                   " \"sheet_id\" : \"" + sheetId + "\", " 
                +                   " \"grid_data\": { \"start_row\": 0,  \"start_column\": 0, \"rows\": [ {\"values\": [" + values + "]}]}"
                +           "} "
                +       "}] "
                + "}";
            MiniAppHelperController _mHelper = new MiniAppHelperController(_db, _config);
            WebApiLog log = await _mHelper.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", json, "POST", "企业微信文档", "创建表头", "");
        }
        [HttpGet]
        public async Task InserTransData(string sheetId, string docId, string token)
        {
            MiniAppHelperController _mHelper = new MiniAppHelperController(_db, _config);
            
            DateTime startDate = DateTime.Parse("2025-10-15");
            List<WepayBalance> bArr = await _db.wepayBalance.Where(b => b.trans_date.Date >= startDate.Date).OrderBy(b => b.mch_id).ThenBy(b => b.trans_date).AsNoTracking().ToListAsync();
            string json =  "{"
                + " \"docid\": \"" + docId + "\", "
                + " \"requests\": "
                +       "[" 
                +           " { \"update_range_request\" : " 
                +               " { "
                +                   " \"sheet_id\" : \"" + sheetId + "\", " 
                +                   " \"grid_data\": { \"start_row\": @startRow,  \"start_column\": 0, \"rows\": [@rows]}"
                +           "} "
                +       "}] "
                + "}";
            string rows = "";
            for(int i = 0; i < bArr.Count; i++)
            {
                string row = "";
                if (i % 100 == 0 && i > 0)
                {
                    //rows = "{ \"values\" : [" + rows + "]} ";
                    string payLoad = json.Replace("@startRow", (i - 100 + 1).ToString())
                        .Replace("@rows", rows);
                    Console.WriteLine(payLoad);
                    WebApiLog log = await _mHelper.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", payLoad, "POST", "企业微信文档", "创建表头", "");
                    rows = "";    
                }

                for(int j = 0; j < wepayBalanceFieldsArr.Length; j++)
                {
                    string cell = "{ \"cell_value\": {\"text\": \"@text\" }, \"cell_format\" : {\"bold\": false} }";
                    switch(wepayBalanceFieldsArr[j].Trim())
                    {
                        case "交易时间":
                            cell = cell.Replace("@text", bArr[i].trans_date.ToString());
                            break;
                        case "公众账号ID":
                            cell = cell.Replace("@text", bArr[i].app_id.ToString());
                            break;
                        case "商户号":
                            cell = cell.Replace("@text", bArr[i].mch_id.ToString());
                            break;
                        case "特约商户号":
                            cell = cell.Replace("@text", bArr[i].spc_mch_id.ToString());
                            break;
                        case "设备号":
                            cell = cell.Replace("@text", bArr[i].device_id.ToString());
                            break;
                        case "微信订单号":
                            cell = cell.Replace("@text", bArr[i].wepay_order_num.ToString());
                            break;
                        case "商户订单号":
                            cell = cell.Replace("@text", bArr[i].out_trade_no.ToString());
                            break;
                        case "用户标识":
                            cell = cell.Replace("@text", bArr[i].open_id.ToString());
                            break;
                        case "交易类型":
                            cell = cell.Replace("@text", bArr[i].trans_type.ToString());
                            break;
                        case "交易状态":
                            cell = cell.Replace("@text", bArr[i].pay_status.ToString());
                            break;
                        case "付款银行":
                            cell = cell.Replace("@text", bArr[i].bank.ToString());
                            break;
                        case "货币种类":
                            cell = cell.Replace("@text", bArr[i].currency.ToString());
                            break;
                        case "应结订单金额":
                            cell = cell.Replace("@text", bArr[i].settle_amount.ToString());
                            break;
                        case "代金券金额":
                            cell = cell.Replace("@text", bArr[i].coupon_amount.ToString());
                            break;
                        case "微信退款单号":
                            cell = cell.Replace("@text", bArr[i].refund_no.ToString());
                            break;
                        case "商户退款单号":
                            cell = cell.Replace("@text", bArr[i].out_refund_no.ToString());
                            break;
                        case "退款金额":
                            cell = cell.Replace("@text", bArr[i].refund_amount.ToString());
                            break;
                        case "充值券退款金额":
                            cell = cell.Replace("@text", bArr[i].coupon_refund_amount.ToString());
                            break;
                        case "退款状态":
                            cell = cell.Replace("@text", bArr[i].refund_status.ToString());
                            break;
                        case "商品名称":
                            cell = cell.Replace("@text", bArr[i].product_name.ToString());
                            break;
                        case "商户数据包":
                            cell = cell.Replace("@text", bArr[i].product_package.ToString());
                            break;
                        case "手续费":
                            cell = cell.Replace("@text", bArr[i].fee.ToString());
                            break;
                        case "费率":
                            cell = cell.Replace("@text", bArr[i].fee_rate.ToString());
                            break;
                        case "订单金额":
                            cell = cell.Replace("@text", bArr[i].order_amount.ToString());
                            break;
                        case "申请退款金额":
                            cell = cell.Replace("@text", bArr[i].request_refund_amount.ToString());
                            break;
                        case "费率备注":
                            cell = cell.Replace("@text", bArr[i].fee_rate_memo.ToString());
                            break; 
                        default:
                            break;
                    }
                    row = row + ((row == "" ? "": ",")  + cell );
                }
                
                rows = rows + ((rows == "" ? "": ",") + "{ \"values\": [" + row + "] }" );
            }
            
        }
    }
}