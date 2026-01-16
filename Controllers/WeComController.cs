using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using SnowmeetApi;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using wechat_miniapp_base.Models;
namespace SnowmeetApi.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class WeComController : ControllerBase
    {
        public class CellValue
        {
            public string text { get; set; } = "";
        }
        public class CellFormat
        {
            public bool bold { get; set; } = false;
            public TextColor color { get; set; } = new TextColor();
        }
        public class TextColor
        {
            public int red { get; set; } = 0;
            public int green { get; set; } = 0;
            public int blue { get; set; } = 0;
            public int alpha { get; set; } = 255;
        }
        public class Cell
        {
            public CellValue cell_value { get; set; }
            public CellFormat cell_format { get; set; }
        }
        public class Row
        {
            public List<Cell> values { get; set; }
        }
        public class GridData
        {
            public int start_row { get; set; } = 0;
            public int start_column { get; set; } = 0;
            public List<Row> rows { get; set; }
        }
        public class UpdateRangeRequest
        {
            public string sheet_id { get; set; }
            public GridData grid_data { get; set; }
        }
        public class UpdateOperation
        {
            public UpdateRangeRequest? update_range_request { get; set; } = null;
        }
        public class BatchUpdateRequest
        {
            public string docid { get; set; }
            public List<UpdateOperation> requests { get; set; }
        }
        public class WeComApiResponse
        {
            public int errcode { get; set; }
            public string errmsg { get; set; }
            public string? access_token { get; set; } = null;
            public FileList? file_list { get; set; } = null;
            public string? docid { get; set; } = null;
            public string? url { get; set; } = null;
            public List<SheetProperties>? properties { get; set; } = null;
        }
        public class FileItem
        {
            public string? fileid { get; set; } = null;
            public string? file_name { get; set; } = null;
            public string? spaceid { get; set; } = null;
            public string? fatherid { get; set; } = null;
            public int? file_size { get; set; } = null;
            public int? ctime { get; set; } = null;
            public int? mtime { get; set; } = null;
            public int? file_type { get; set; } = null;
            public int? file_status { get; set; } = null;
            public string? url { get; set; } = null;


        }
        public class FileList
        {
            public List<FileItem> item { get; set; } = new List<FileItem>();
        }
        public class SheetProperties
        {
            public string? sheet_id { get; set; } = null;
            public string? title { get; set; } = null;
            public int row_count { get; set; } = 0;
            public int column_count { get; set; } = 0;
        }
        public ApplicationDBContext _db;
        public IConfiguration _config;
        public IHttpContextAccessor _http;
        public string cropid = "ww3a46c4555ae069f9";
        public string cropSecret = "vz9XOlLgtY63pYqqHix9HYcsSey4H3J7drQ4xUDa59M";
        public string spaceId = "s.ww3a46c4555ae069f9.767798597fsX";
        public string[] wepayBalanceFieldsArr = new string[] { "序号", "交易日期", "交易时间", "公众账号ID", "商户号", "特约商户号", "设备号", "微信订单号", "商户订单号", "用户标识", "交易类型", "付款银行", "货币种类", "应结订单金额", "代金券金额", "微信退款单号", "商户退款单号", "退款金额", "充值券退款金额", "退款状态", "商品名称", "商户数据包", "手续费", "费率", "订单金额", "申请退款金额", "费率备注", "导出批次", "导出日期", "导出时间" };
        public string[] wepayFundFieldsArr = new string[] { "序号", "记账日期", "记账时间", "商户号", "微信支付业务单号", "资金流水单号", "业务名称", "业务类型", "收支类型", "收支金额(元)", "账户结余(元)", "资金变更提交申请人", "备注", "业务凭证号", "导出批次", "导出日期", "导出时间" };
        public string wepayBalanceDirId = "s.ww3a46c4555ae069f9.767798597fsX_d.767972774CblA";
        public string wepayBalanceFileName = "【25-26】微信支付交易账单汇总";
        public string wepayDirId = "s.ww3a46c4555ae069f9.767798597fsX_d.767799479UX0M";
        public int[] mchIdArr = new int[] { 3, 5, 6, 7, 8, 9, 10, 11, 12, 15, 17 };
        //public int[] mchIdArr = new int[] { 12,15,17 };
        public string wepayFundDirId = "s.ww3a46c4555ae069f9.767798597fsX_d.7681092340bMJ";
        public string wepayFundFileName = "【25-26】微信支付资金账单汇总";
        public string aliFundDirId = "s.ww3a46c4555ae069f9.767798597fsX_d.767799545E4uh";
        public string aliFundFileName = "【25-26】支付宝资金账单";
        public string aliDocId = "dct3zTTcxB3z9Px3UE0Op0O3q1ubD24rifofvWnLlZ9m6mt7lxoQ5Vblxel0z1JV0huomN49EwwQsxnNJNkFEhVg";
        public string[] aliFields = new string[] { "序号", "账务流水号", "业务流水号", "商户订单号", "商品名称", "发生日期", "发生时间", "对方账号", "收入金额（+元）", "支出金额（-元）", "账户余额（元）", "交易渠道", "业务类型", "备注", "导出批次", "导出日期", "导出时间" };
        public MiniAppHelperController _mH;
        public WeComController(ApplicationDBContext context, IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            _db = context;
            _config = config;
            _http = httpContextAccessor;
            _mH = new MiniAppHelperController(_db, _config);
        }
        [HttpGet]
        public async Task<ActionResult<int>> ExportAliDataAuto()
        {
            string batchId = DateTime.Now.ToString("yyyyMMddHHmmss");
            string docId = aliDocId;
            string purpose = "支付宝";
            string token = await GetToken(batchId, purpose);
            List<SheetProperties> sheets = await GetSheetProperties(docId, token, batchId, purpose);
            await ExportAliData(sheets[0].sheet_id, docId, token, batchId, purpose, "更新数据");
            return Ok(0);
        }
        [HttpGet("{mchId}")]
        public async Task<ActionResult<int>> ExportWepayDataAuto(int mchId)
        {
            WepayKey key = await _db.wepayKeys.Where(k => k.id == mchId).AsNoTracking().FirstOrDefaultAsync();
            if (key == null)
            {
                return NotFound();
            }
            string batchId = DateTime.Now.ToString("yyyyMMddHHmmss");
            string docId = key.doc_id;
            string purpose = "微信支付";
            string token = await GetToken(batchId, purpose);
            List<SheetProperties> sheets = await GetSheetProperties(docId, token, batchId, purpose);
            string? fundSheetId = null;
            string? balanceSheetId = null;
            for(int i = 0; i < sheets.Count; i++)
            {
                switch(sheets[i].title.Trim())
                {
                    case "资金账单":
                        fundSheetId = sheets[i].sheet_id;
                        break;
                    case "交易账单":
                        balanceSheetId = sheets[i].sheet_id;
                        break;
                    default:
                        break;
                }
            }
            if (balanceSheetId == null || fundSheetId == null)
            {
                return BadRequest();
            }
            await ExportWepayFundData(fundSheetId, docId, token, batchId, purpose, "更新数据", key.mch_id);
            await ExportWepayTransData(balanceSheetId, docId, token, batchId, purpose, "更新数据", key.mch_id);
            return Ok(0);
        } 
        [HttpGet]
        public async Task<ActionResult<string>> CreateAliSheetFile(string? token = null, string? batchId = null, string purpose = "支付宝", string memo = "生成导出数据表格")
        {
            if (batchId == null)
            {
                batchId = DateTime.Now.ToString("yyyyMMddHHmmss");
            }
            if (token == null)
            {
                token = await GetToken(batchId, purpose);
            }
            string fileName = "【25-26】支付宝资金账单";
            string? fileId = await GetFileId(aliFundDirId, fileName, batchId, token, purpose, memo);
            for (; fileId != null;)
            {
                await DeleteFile(fileId, token, purpose, memo, batchId);
                fileId = await GetFileId(aliFundDirId, fileName, batchId, token, purpose, memo);
            }
            string docId = await CreateSheetDoc(aliFundDirId, fileName, token, purpose, memo, batchId);
            string sheetName = "交易账单";
            List<SheetProperties> sheetProperties = await CreateSheet(docId, sheetName, 29, 5000, token, purpose, memo, batchId);
            string? sheetId = null;
            for (int j = 0; j < sheetProperties.Count; j++)
            {
                SheetProperties sp = sheetProperties[j];
                if (sp.title == "Sheet1")
                {
                    await DeleteSheet(docId, sp.sheet_id, token, purpose, memo, batchId);
                }
                if (sp.title == sheetName)
                {
                    sheetId = sp.sheet_id;
                }
            }
            if (sheetId == null)
            {
                return BadRequest();
            }
            await CreateTransTableTitle(sheetId, docId, token, batchId, purpose, memo, "ali");
            await FillBlank(5000, docId, token, purpose, memo, batchId, sheetId);
            return Ok(docId);
        }
        [HttpGet("{mchId}")]
        public async Task<ActionResult<string>> CreateWepaySheetFile(int? mchId, string? token = null, string? batchId = null, string purpose = "微信支付", string memo = "分账户")
        {
            if (batchId == null)
            {
                batchId = DateTime.Now.ToString("yyyyMMddHHmmss");
            }
            if (token == null)
            {
                token = await GetToken(batchId, purpose);
            }
            WepayKey key = await _db.wepayKeys.Where(k => k.id == mchId).AsNoTracking().FirstOrDefaultAsync();
            string fileName = key.mch_id + "_" + key.mch_name;
            string? fileId = await GetFileId(wepayDirId, fileName, batchId, token, purpose, memo);
            for (; fileId != null;)
            {
                await DeleteFile(fileId, token, purpose, memo, batchId);
                fileId = await GetFileId(wepayDirId, fileName, batchId, token, purpose, memo);

            }
            string docId = await CreateSheetDoc(wepayDirId, fileName, token, purpose, memo, batchId);
            string? balanceSheetId = null;
            string? fundSheetId = null;
            await CreateSheet(docId, "交易账单", 29, 5000, token, purpose, memo, batchId);
            List<SheetProperties> sheetProperties = await GetSheetProperties(docId, token, batchId, purpose, memo);
            List<SheetProperties> sps = await CreateSheet(docId, "资金账单", 16, 5000, token, purpose, memo, batchId);
            for (int j = 0; j < sheetProperties.Count; j++)
            {
                SheetProperties sp = sheetProperties[j];
                if (sp.title == "Sheet1")
                {
                    await DeleteSheet(docId, sheetProperties[j].sheet_id, token, purpose, memo, batchId);
                }
            }

            for (int j = 0; j < sps.Count; j++)
            {
                switch (sps[j].title)
                {
                    case "资金账单":
                        fundSheetId = sps[j].sheet_id;
                        break;
                    case "交易账单":
                        balanceSheetId = sps[j].sheet_id;
                        break;
                    default:
                        break;
                }
            }
            if (balanceSheetId == null || fundSheetId == null)
            {
                return Ok(-2);
            }
            await CreateTransTableTitle(balanceSheetId, docId, token, batchId, purpose, memo);
            await CreateTransTableTitle(fundSheetId, docId, token, batchId, purpose, memo, "fund");
            await FillBlank(5000, docId, token, purpose, memo, batchId, balanceSheetId);
            await FillBlank(5000, docId, token, purpose, memo, batchId, fundSheetId);
            key.doc_id = docId;
            _db.wepayKeys.Entry(key).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return Ok(docId);
        }
        [HttpGet("{mchId}")]
        public async Task<ActionResult<int>> RefreshWepayByEachMchId(int mchId)
        {
            string purpose = "微信支付";
            string memo = "分账户";
            string batchId = DateTime.Now.ToString("yyyyMMddHHmmss");
            string token = await GetToken(batchId, "微信支付");
            WepayKey key = await _db.wepayKeys.Where(k => k.id == mchId).AsNoTracking().FirstOrDefaultAsync();
            if (key == null)
            {
                return Ok(-3);
            }
            string? balanceSheetId = null;
            string? fundSheetId = null;
            if (key.doc_id == null)
            {
                string fileName = key.mch_id + "_" + key.mch_name;
                string? fileId = await GetFileId(wepayDirId, fileName, batchId, token, purpose, memo);
                for (; fileId != null;)
                {
                    await DeleteFile(fileId, token, purpose, memo, batchId);
                    fileId = await GetFileId(wepayDirId, fileName, batchId, token, purpose, memo);

                }
                //continue;
                string docId = await CreateSheetDoc(wepayDirId, key.mch_id + "_" + key.mch_name, token, purpose, memo, batchId);
                key.doc_id = docId;
                _db.wepayKeys.Entry(key).State = EntityState.Modified;
                await _db.SaveChangesAsync();
                List<SheetProperties> sheetProperties = await GetSheetProperties(docId, token, batchId, purpose, memo);

                await CreateSheet(docId, "交易账单", 26, 5000, token, purpose, memo, batchId);
                List<SheetProperties> sps = await CreateSheet(docId, "资金账单", 13, 5000, token, purpose, memo, batchId);
                for (int j = 0; j < sheetProperties.Count; j++)
                {
                    SheetProperties sp = sheetProperties[j];
                    if (sp.title == "Sheet1")
                    {
                        await DeleteSheet(docId, sheetProperties[j].sheet_id, token, purpose, memo, batchId);
                    }
                }

                for (int j = 0; j < sps.Count; j++)
                {
                    switch (sps[j].title)
                    {
                        case "资金账单":
                            fundSheetId = sps[j].sheet_id;
                            break;
                        case "交易账单":
                            balanceSheetId = sps[j].sheet_id;
                            break;
                        default:
                            break;
                    }
                }
                if (balanceSheetId == null || fundSheetId == null)
                {
                    return Ok(-2);
                }
                await CreateTransTableTitle(balanceSheetId, docId, token, batchId, purpose, memo);
                await CreateTransTableTitle(fundSheetId, docId, token, batchId, purpose, memo, "fund");
            }
            if (fundSheetId == null || balanceSheetId == null)
            {
                List<SheetProperties> sps = await GetSheetProperties(key.doc_id, token, batchId, purpose, memo);
                for (int j = 0; j < sps.Count; j++)
                {
                    switch (sps[j].title)
                    {
                        case "资金账单":
                            fundSheetId = sps[j].sheet_id;
                            break;
                        case "交易账单":
                            balanceSheetId = sps[j].sheet_id;
                            break;
                        default:
                            break;
                    }
                }
            }
            if (fundSheetId == null || balanceSheetId == null)
            {
                return Ok(-1);
            }
            await InsertFundData(fundSheetId, key.doc_id, token, batchId, purpose, memo, key.mch_id);
            await FillBlank(5000, key.doc_id, token, purpose, memo, batchId, fundSheetId);
            await InsertTransData(balanceSheetId, key.doc_id, token, batchId, purpose, memo, key.mch_id);
            await FillBlank(5000, key.doc_id, token, purpose, memo, batchId, balanceSheetId);
            return Ok(0);
        }
        [HttpGet]
        public async Task RefreshWepayByMchId()
        {
            string purpose = "微信支付";
            string memo = "分账户";
            string batchId = DateTime.Now.ToString("yyyyMMddHHmmss");
            string token = await GetToken(batchId, "微信支付");

            List<WepayKey> keys = await _db.wepayKeys.ToListAsync();
            for (int i = 0; i < mchIdArr.Length; i++)
            {
                WepayKey key = await _db.wepayKeys.Where(k => k.id == mchIdArr[i]).AsNoTracking().FirstOrDefaultAsync();
                if (key == null)
                {
                    continue;
                }
                string? balanceSheetId = null;
                string? fundSheetId = null;
                if (key.doc_id == null)
                {
                    string fileName = key.mch_id + "_" + key.mch_name;
                    string? fileId = await GetFileId(wepayDirId, fileName, batchId, token, purpose, memo);
                    for (; fileId != null;)
                    {
                        await DeleteFile(fileId, token, purpose, memo, batchId);
                        fileId = await GetFileId(wepayDirId, fileName, batchId, token, purpose, memo);

                    }
                    //continue;
                    string docId = await CreateSheetDoc(wepayDirId, key.mch_id + "_" + key.mch_name, token, purpose, memo, batchId);
                    key.doc_id = docId;
                    _db.wepayKeys.Entry(key).State = EntityState.Modified;
                    await _db.SaveChangesAsync();
                    List<SheetProperties> sheetProperties = await GetSheetProperties(docId, token, batchId, purpose, memo);

                    await CreateSheet(docId, "交易账单", 26, 5000, token, purpose, memo, batchId);
                    List<SheetProperties> sps = await CreateSheet(docId, "资金账单", 13, 5000, token, purpose, memo, batchId);
                    for (int j = 0; j < sheetProperties.Count; j++)
                    {
                        SheetProperties sp = sheetProperties[j];
                        if (sp.title == "Sheet1")
                        {
                            await DeleteSheet(docId, sheetProperties[j].sheet_id, token, purpose, memo, batchId);
                        }
                    }

                    for (int j = 0; j < sps.Count; j++)
                    {
                        switch (sps[j].title)
                        {
                            case "资金账单":
                                fundSheetId = sps[j].sheet_id;
                                break;
                            case "交易账单":
                                balanceSheetId = sps[j].sheet_id;
                                break;
                            default:
                                break;
                        }
                    }
                    if (balanceSheetId == null || fundSheetId == null)
                    {
                        continue;
                    }
                    await CreateTransTableTitle(balanceSheetId, docId, token, batchId, purpose, memo);
                    await CreateTransTableTitle(fundSheetId, docId, token, batchId, purpose, memo, "fund");
                }
                if (fundSheetId == null || balanceSheetId == null)
                {
                    List<SheetProperties> sps = await GetSheetProperties(key.doc_id, token, batchId, purpose, memo);
                    for (int j = 0; j < sps.Count; j++)
                    {
                        switch (sps[j].title)
                        {
                            case "资金账单":
                                fundSheetId = sps[j].sheet_id;
                                break;
                            case "交易账单":
                                balanceSheetId = sps[j].sheet_id;
                                break;
                            default:
                                break;
                        }
                    }
                }
                if (fundSheetId == null || balanceSheetId == null)
                {
                    continue;
                }
                await InsertFundData(fundSheetId, key.doc_id, token, batchId, purpose, memo, key.mch_id);
                await FillBlank(5000, key.doc_id, token, purpose, memo, batchId, fundSheetId);
                await InsertTransData(balanceSheetId, key.doc_id, token, batchId, purpose, memo, key.mch_id);
                await FillBlank(5000, key.doc_id, token, purpose, memo, batchId, balanceSheetId);
            }
            return;
        }
        [NonAction]
        public async Task RecreateAliFundTable()
        {
            string purpose = "支付宝";
            bool needRefresh = await CheckNeedReCreateTransTable(purpose);
            if (!needRefresh)
            {
                return;
            }
            string batchId = DateTime.Now.ToString("yyyyMMddHHmmss");
            string token = await GetToken(batchId, purpose);
            if (token == null)
            {
                return;
            }
            string fileId = await GetFileId(aliFundDirId, aliFundFileName, batchId, token, purpose, "获取文件id");
            for (; fileId != null;)
            {
                await DeleteFile(fileId, token, purpose, "删除旧文件", batchId);
                fileId = await GetFileId(aliFundDirId, aliFundFileName, batchId, token, purpose, "获取文件id");
            }
            string docId = await CreateSheetDoc(aliFundDirId, aliFundFileName, token, purpose, "创建新表格", batchId);
            if (docId == null)
            {
                return;
            }
            string sheetId = await GetSheetId(docId, token, batchId, purpose, "获取sheetid");
            await CreateTransTableTitle(sheetId, docId, token, batchId, "资金账单", "更新数据", "ali");
            await InsertFundData(sheetId, docId, token, batchId, "支付宝", "更新数据");
        }
        [HttpGet]
        public async Task RefreshAli()
        {
            string docId = "dc8AjUHJof3cFWORO6kf-kcwY6AD-ARg9eHDyOonC22GWSKZE5AkCeRUp2tBGIcvHlupYo6YyhaeBV9sqnYxgIlA";
            string batchId = DateTime.Now.ToString("yyyyMMddHHmmss");
            string token = await GetToken(batchId, "支付宝");
            if (token == null)
            {
                return;
            }
            string sheetId = await GetSheetId(docId, token, batchId, "支付宝", "获取sheetid");
            await CreateTransTableTitle(sheetId, docId, token, batchId, "支付宝", "更新数据", "ali");
            await InsertAliData(sheetId, docId, token, batchId, "支付宝", "更新数据");
            await FillBlank(10000, docId, token, "支付宝", "更新数据", batchId);
        }
        [HttpGet]
        public async Task RefreshFundTable()
        {
            string docId = "dcVUQ5rOWF3z2uS4AKzylB1YxNcbW6kjUA411EO9H8R-V0mL7FDH8elbW36bRZj0242wPTkHb_V6UQPRcnjguEEw";
            string batchId = DateTime.Now.ToString("yyyyMMddHHmmss");
            string token = await GetToken(batchId, "资金账单");
            if (token == null)
            {
                return;
            }
            string sheetId = await GetSheetId(docId, token, batchId, "资金账单", "获取sheetid");
            await CreateTransTableTitle(sheetId, docId, token, batchId, "资金账单", "更新数据", "fund");
            await InsertFundData(sheetId, docId, token, batchId, "资金账单", "更新数据");
            await FillBlank(20000, docId, token, "资金账单", "更新数据", batchId);
        }
        [NonAction]
        public async Task RecreateFundTable()
        {
            string purpose = "资金账单";
            bool needRefresh = await CheckNeedReCreateTransTable(purpose);
            if (!needRefresh)
            {
                //return;
            }
            string batchId = DateTime.Now.ToString("yyyyMMddHHmmss");
            string token = await GetToken(batchId, purpose);
            if (token == null)
            {
                return;
            }
            string fileId = await GetFileId(wepayFundDirId, wepayFundFileName, batchId, token, purpose, "获取文件id");
            for (; fileId != null;)
            {
                await DeleteFile(fileId, token, purpose, "删除旧文件", batchId);
                fileId = await GetFileId(wepayFundDirId, wepayFundFileName, batchId, token, purpose, "获取文件id");
            }
            string docId = await CreateSheetDoc(wepayFundDirId, wepayFundFileName, token, purpose, "创建新表格", batchId);
            if (docId == null)
            {
                return;
            }
            string sheetId = await GetSheetId(docId, token, batchId, purpose, "获取sheetid");
            await CreateTransTableTitle(sheetId, docId, token, batchId, "资金账单", "更新数据", "fund");
            await InsertFundData(sheetId, docId, token, batchId, "资金账单", "更新数据");

        }
        [NonAction]
        public async Task FillBlank(int maxLineCount, string docId, string token, string purpose, string memo, string batchId, string? sheetId = null)
        {
            string url = "https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/get_sheet_properties?access_token=" + token;
            string payload = "{ \"docid\": \"" + docId + "\" }  ";
            WebApiLog getFileLog = await _mH.PerformRequest(url, "", payload, "POST", "企业微信", purpose, memo, batchId);
            WeComApiResponse res = JsonConvert.DeserializeObject<WeComApiResponse>(getFileLog.response);
            if (res.errcode != 0)
            {
                return;
            }
            int rowCount = res.properties[0].row_count;
            int colCount = res.properties[0].column_count;
            int willFilledLines = maxLineCount - rowCount;
            BatchUpdateRequest batchUpdateRequest = new BatchUpdateRequest()
            {
                docid = docId,
                requests = new List<UpdateOperation>()
            };
            sheetId = sheetId == null ? sheetId = await GetSheetId(docId, token, batchId, "交易账单", "获取sheetid") : sheetId;
            UpdateRangeRequest updateRange = new UpdateRangeRequest()
            {
                sheet_id = sheetId,
                grid_data = new GridData()
            };
            UpdateOperation updateOperation = new UpdateOperation();
            updateOperation.update_range_request = updateRange;
            batchUpdateRequest.requests.Add(updateOperation);
            GridData gData = updateRange.grid_data;
            gData.start_column = 0;
            gData.start_row = rowCount + 1;
            gData.rows = new List<Row>();
            int nextStart = gData.start_row;
            for (int i = 0; i < willFilledLines; i++)
            {
                if (i % 200 == 0 && i > 0)
                {
                    string payloadRow = JsonConvert.SerializeObject(batchUpdateRequest);
                    Console.WriteLine("");
                    Console.WriteLine(payloadRow);
                    WebApiLog log = await _mH.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", payloadRow, "POST", "企业微信", purpose, memo, batchId);
                    //logs.Add(log);
                    gData.rows.Clear();
                    gData.start_row = nextStart;
                }
                nextStart++;
                Row row = new Row();
                row.values = new List<Cell>();
                for (int j = 0; j < colCount; j++)
                {
                    Cell cell = new Cell();
                    cell.cell_format = new CellFormat();
                    cell.cell_format.bold = false;
                    cell.cell_value = new CellValue();
                    cell.cell_value.text = "";
                    row.values.Add(cell);
                }
                gData.rows.Add(row);
            }
            string payloadLast = JsonConvert.SerializeObject(batchUpdateRequest);
            WebApiLog logFinal = await _mH.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", payloadLast, "POST", "企业微信", purpose, memo, batchId);
            //logs.Add(logFinal);
        }
        [HttpGet]
        public async Task RefreshTransTable()
        {
            string docId = "dcrDFM9sIzIhyt7NC5FhjrBKJ6yC7Gz-z-xONcpuQxxX9nun1buvhIWGZW0pTfRt1KXUTkk4XGZcOSQjkUMv07AA";
            string batchId = DateTime.Now.ToString("yyyyMMddHHmmss");
            string token = await GetToken(batchId, "交易账单");
            if (token == null)
            {
                return;
            }
            string sheetId = await GetSheetId(docId, token, batchId, "交易账单", "获取sheetid");
            await CreateTransTableTitle(sheetId, docId, token, batchId, "交易账单", "更新数据");
            await InsertTransData(sheetId, docId, token, batchId, "交易账单", "更新数据");
            await FillBlank(15000, docId, token, "交易账单", "更新数据", batchId);
        }
        [NonAction]
        public async Task RecreateTransTable()
        {
            bool needRefresh = await CheckNeedReCreateTransTable();
            if (!needRefresh)
            {
                //return;
            }
            string batchId = DateTime.Now.ToString("yyyyMMddHHmmss");
            string token = await GetToken(batchId, "交易账单");
            if (token == null)
            {
                return;
            }
            string fileId = await GetFileId(wepayBalanceDirId, wepayBalanceFileName, batchId, token, "交易账单", "获取文件id");
            for (; fileId != null;)
            {
                await DeleteFile(fileId, token, "交易账单", "删除旧文件", batchId);
                fileId = await GetFileId(wepayBalanceDirId, wepayBalanceFileName, batchId, token, "交易账单", "获取文件id");
            }

            string docId = await CreateSheetDoc(wepayBalanceDirId, wepayBalanceFileName, token, "交易账单", "创建新表格", batchId);
            if (docId == null)
            {
                return;
            }
            string sheetId = await GetSheetId(docId, token, batchId, "交易账单", "获取sheetid");
            await CreateTransTableTitle(sheetId, docId, token, batchId, "交易账单", "更新数据");
            await InsertTransData(sheetId, docId, token, batchId, "交易账单", "更新数据");
        }
        [NonAction]
        public async Task DeleteSheet(string docId, string sheetId, string token, string purpose, string memo = "", string? batchId = null)
        {
            string json = " { \"docid\": \"" + docId + "\", \"requests\": [ { \"delete_sheet_request\": { \"sheet_id\" : \"" + sheetId + "\" } }  ] }";
            await _mH.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", json, "POST", "企业微信", purpose, memo, batchId);
        }
        [NonAction]
        public async Task<List<SheetProperties>> CreateSheet(string docId, string sheetName, int colCount, int rowCount, string token, string purpose, string memo = "", string? batchId = null)
        {
            string json = " { \"docid\": \"" + docId + "\", \"requests\": [ { \"add_sheet_request\": { \"title\" : \"" + sheetName + "\", \"row_couont\": "
                + rowCount.ToString() + ", \"column_count\": " + colCount.ToString() + " } }  ] }";
            await _mH.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", json, "POST", "企业微信", purpose, memo, batchId);
            return await GetSheetProperties(docId, token, batchId, purpose, memo);

        }
        [NonAction]
        public async Task<string?> CreateSheetDoc(string fatherId, string fileName, string token, string purpose, string memo = "", string? batchId = null)
        {
            string url = "https://qyapi.weixin.qq.com/cgi-bin/wedoc/create_doc?access_token=" + token;
            string payload = "{	\"spaceid\": \"" + spaceId + "\" , \"fatherid\": \"" + fatherId + "\", \"doc_type\": 4, \"doc_name\": \"" + fileName
                + "\", \"admin_users\": [\"Snowmeet-CangJie_1\",  \"CuiYang\", \"ZhangKai\"] }";
            WebApiLog tokenLog = await _mH.PerformRequest(url, "", payload, "POST", "企业微信", purpose, memo, batchId);
            try
            {
                WeComApiResponse res = JsonConvert.DeserializeObject<WeComApiResponse>(tokenLog.response);
                if (res.errcode == 0)
                {
                    return res.docid;
                }
                else
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }
        [NonAction]
        public async Task<string?> GetToken(string batchId, string purpose)
        {
            string getTokenUrl = "https://qyapi.weixin.qq.com/cgi-bin/gettoken?corpid=" + cropid + "&corpsecret=" + cropSecret;
            WebApiLog tokenLog = await _mH.PerformRequest(getTokenUrl, "", "", "GET", "企业微信", purpose, "获取Token", batchId);
            try
            {
                WeComApiResponse res = JsonConvert.DeserializeObject<WeComApiResponse>(tokenLog.response);
                if (res.errcode == 0)
                {
                    return res.access_token;
                }
                else
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }
        [NonAction]
        public async Task<List<SheetProperties>?> GetSheetProperties(string docId, string token, string batchId, string purpose = "", string memo = "")
        {
            string url = "https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/get_sheet_properties?access_token=" + token;
            string payload = "{ \"docid\": \"" + docId + "\" }";
            WebApiLog getFileLog = await _mH.PerformRequest(url, "", payload, "POST", "企业微信", purpose, memo, batchId);
            try
            {
                WeComApiResponse res = JsonConvert.DeserializeObject<WeComApiResponse>(getFileLog.response);
                return res.properties;
            }
            catch
            {
                return null;
            }
        }

        [NonAction]
        public async Task<string?> GetSheetId(string docId, string token, string batchId, string purpose = "", string memo = "")
        {
            string url = "https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/get_sheet_properties?access_token=" + token;
            string payload = "{ \"docid\": \"" + docId + "\" }";
            WebApiLog getFileLog = await _mH.PerformRequest(url, "", payload, "POST", "企业微信", purpose, memo, batchId);
            try
            {
                WeComApiResponse res = JsonConvert.DeserializeObject<WeComApiResponse>(getFileLog.response);
                return res.properties[0].sheet_id;
            }
            catch
            {
                return null;
            }
        }
        [NonAction]
        public async Task<string?> GetFileId(string fatherId, string fileName, string batchId, string token, string purpose = "", string memo = "")
        {
            string getUrl = "https://qyapi.weixin.qq.com/cgi-bin/wedrive/file_list?access_token=" + token;
            string payload = "{ \"spaceid\": \"" + spaceId + "\" , \"fatherid\": \"" + fatherId + "\" }";
            WebApiLog getFileLog = await _mH.PerformRequest(getUrl, "", payload, "POST", "企业微信", purpose, memo, batchId);
            WeComApiResponse res = JsonConvert.DeserializeObject<WeComApiResponse>(getFileLog.response);
            string fileId = null;
            for (int i = 0; res.file_list != null && res.file_list.item != null && fileId == null && i < res.file_list.item.Count; i++)
            {
                FileItem item = res.file_list.item[i];
                if (item.file_name == fileName)
                {
                    fileId = item.fileid;
                }
            }
            return fileId;
        }
        [NonAction]
        public async Task DeleteFile(string fileId, string token, string purpose, string memo = "", string? batchId = null)
        {
            string url = "https://qyapi.weixin.qq.com/cgi-bin/wedrive/file_delete?access_token=" + token;
            string payload = "{\"fileid\": [\"" + fileId + "\" ] }";
            await _mH.PerformRequest(url, "", payload, "POST", "企业微信", purpose, memo, batchId);
        }
        [NonAction]
        public async Task<bool> CheckNeedReCreateTransTable(string purpose = "交易账单")
        {
            //teTime nowDate = DateTime.Now.Date;
            string batchPrefix = DateTime.Now.ToString("yyyyMMdd");
            WebApiLog? lastLog = await _db.webApiLog.Where(l => l.source == "企业微信" && l.purpose == purpose && l.memo == "更新数据" && l.batch_id.StartsWith(batchPrefix))
                .OrderByDescending(l => l.create_date).AsNoTracking().FirstOrDefaultAsync();
            bool needRefresh = false;
            if (lastLog == null)
            {
                needRefresh = true;
            }
            else
            {
                string lastBatchId = lastLog.batch_id;
                List<WebApiLog> logs = await _db.webApiLog.Where(l => l.batch_id == lastBatchId).AsNoTracking().ToListAsync();
                bool withError = false;
                for (int i = 0; !withError && i < logs.Count; i++)
                {
                    try
                    {
                        string json = logs[i].response.Trim();
                        WeComApiResponse res = JsonConvert.DeserializeObject<WeComApiResponse>(json);
                        if (res.errcode != 0 || res.errmsg != "ok")
                        {
                            withError = true;
                        }
                    }
                    catch
                    {
                        withError = true;
                    }
                }
                if (withError)
                {
                    needRefresh = true;
                }
            }
            return needRefresh;
        }
        [NonAction]
        public async Task<WebApiLog> CreateTransTableTitle(string sheetId, string docId, string token, string batchId, string purpose, string memo, string type = "balance")
        {
            BatchUpdateRequest batchUpdateRequest = new BatchUpdateRequest()
            {
                docid = docId,
                requests = new List<UpdateOperation>()
            };
            UpdateRangeRequest updateRange = new UpdateRangeRequest()
            {
                sheet_id = sheetId,
                grid_data = new GridData()
            };
            UpdateOperation updateOperation = new UpdateOperation();
            updateOperation.update_range_request = updateRange;
            batchUpdateRequest.requests.Add(updateOperation);
            GridData gData = updateRange.grid_data;
            gData.start_column = 0;
            gData.start_row = 0;
            gData.rows = new List<Row>();
            Row row = new Row()
            {
                values = new List<Cell>()
            };
            string[] fields = wepayBalanceFieldsArr;
            switch (type)
            {
                case "fund":
                    fields = wepayFundFieldsArr;
                    break;
                case "ali":
                    fields = aliFields;
                    break;
                default:
                    break;
            }

            for (int i = 0; i < fields.Length; i++)
            {
                Cell cell = new Cell();
                CellFormat cf = new CellFormat()
                {
                    bold = true
                };
                CellValue cv = new CellValue()
                {
                    text = fields[i].Trim()
                };
                cell.cell_format = cf;
                cell.cell_value = cv;
                row.values.Add(cell);
            }
            gData.rows.Add(row);
            string json = JsonConvert.SerializeObject(batchUpdateRequest);
            Console.WriteLine(json);
            MiniAppHelperController _mHelper = new MiniAppHelperController(_db, _config);
            WebApiLog log = await _mHelper.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", json, "POST", "企业微信", purpose, memo, batchId);
            return log;
        }
        [NonAction]
        public async Task ExportAliData(string sheetId, string docId, string token, string batchId, string purpose, string memo)
        {
            DateTime startDate = DateTime.Parse("2025-10-15");
            List<AliDownloadFlowBill> bArr = await _db.aliDownloadFlowBill.Where(b => b.trans_date.Date >= startDate.Date && b.seq_id == null)
                .OrderBy(b => b.trans_date).ToListAsync();
            AliDownloadFlowBill last = await _db.aliDownloadFlowBill.Where(b => b.trans_date.Date >= startDate.Date  && b.seq_id != null)
                .OrderByDescending(b => b.seq_id).AsNoTracking().FirstOrDefaultAsync();
            int seq = 1;
            if (last != null)
            {
                seq = ((int)last.seq_id) + 1;
            }
            BatchUpdateRequest batchUpdateRequest = new BatchUpdateRequest()
            {
                docid = docId,
                requests = new List<UpdateOperation>()
            };
            UpdateRangeRequest updateRange = new UpdateRangeRequest()
            {
                sheet_id = sheetId,
                grid_data = new GridData()
            };
            UpdateOperation updateOperation = new UpdateOperation();
            updateOperation.update_range_request = updateRange;
            batchUpdateRequest.requests.Add(updateOperation);
            GridData gData = updateRange.grid_data;
            gData.start_column = 0;
            gData.start_row = 1;
            gData.rows = new List<Row>();
            //gData.start_row = 1;
            int nextStart = 1;
            List<WepayBalance> updateBatch = new List<WepayBalance>();
            for (int i = 0; i < bArr.Count; i++)
            {
                if (i % 200 == 0 && i > 0)
                {
                    string payload = JsonConvert.SerializeObject(batchUpdateRequest);
                    WebApiLog log = await _mH.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", payload, "POST", "企业微信", purpose, memo, batchId);
                    gData.rows.Clear();
                    gData.start_row = nextStart;
                    try
                    {
                        WeComApiResponse res = JsonConvert.DeserializeObject<WeComApiResponse>(log.response.Trim());
                        if (res.errcode == 0)
                        {
                            for(int j = 0; j < updateBatch.Count; j++)
                            {
                                updateBatch[j].wedoc_request_id = log.id;
                                _db.wepayBalance.Entry(updateBatch[j]).State = EntityState.Modified;
                            }
                            await _db.SaveChangesAsync();
                            updateBatch.Clear();
                            updateBatch = new List<WepayBalance>();
                        }
                    }
                    catch
                    {
                        updateBatch.Clear();
                        updateBatch = new List<WepayBalance>();
                    }
                }
                nextStart++;
                Row row = new Row();
                row.values = new List<Cell>();
                for (int j = 0; j < aliFields.Length; j++)
                {
                    Cell cell = new Cell();
                    cell.cell_format = new CellFormat();
                    cell.cell_format.bold = false;
                    cell.cell_value = new CellValue();
                    switch (aliFields[j].Trim())
                    {
                        case "序号":
                            cell.cell_value.text = seq.ToString();
                            bArr[i].seq_id = seq;
                            seq++;
                            break;
                        case "账务流水号":
                            cell.cell_value.text = bArr[i].id;
                            break;
                        case "发生日期":
                            DateTime transDate = DateTime.Parse(bArr[i].trans_date.ToString());
                            cell.cell_value.text = transDate.ToString("yyyy-MM-dd");
                            break;
                        case "发生时间":
                            DateTime transTime = DateTime.Parse(bArr[i].trans_date.ToString());
                            cell.cell_value.text = transTime.ToString("HH:mm:ss");
                            break;
                        case "业务流水号":
                            cell.cell_value.text = bArr[i].biz_num.ToString();
                            break;
                        case "商户订单号":
                            cell.cell_value.text = bArr[i].out_trade_num.ToString();

                            break;
                        case "商品名称":
                            cell.cell_value.text = bArr[i].prod_name.ToString();

                            break;
                        case "对方账号":
                            cell.cell_value.text = bArr[i].receiver_ali_account.ToString();

                            break;
                        case "收入金额（+元）":
                            cell.cell_value.text = bArr[i].income.ToString();

                            break;
                        case "支出金额（-元）":
                            cell.cell_value.text = bArr[i].outcome.ToString();

                            break;
                        case "账户余额（元）":
                            cell.cell_value.text = bArr[i].remainder.ToString();

                            break;
                        case "交易渠道":
                            cell.cell_value.text = bArr[i].trans_channel.ToString();

                            break;
                        case "业务类型":
                            cell.cell_value.text = bArr[i].biz_type.ToString();

                            break;
                        case "备注":
                            cell.cell_value.text = bArr[i].memo.ToString();

                            break;
                        case "导出批次":
                            cell.cell_value.text = batchId.Trim();
                            bArr[i].batch_id = batchId;
                            break;    
                        case "导出日期":
                            cell.cell_value.text = DateTime.Now.ToShortDateString();
                            break;                 
                        case "导出时间":
                            cell.cell_value.text = DateTime.Now.ToShortTimeString();
                            break;
                        default:
                            break;
                    }
                    row.values.Add(cell);
                }
                gData.rows.Add(row);
            }
            string payloadLast = JsonConvert.SerializeObject(batchUpdateRequest);
            WebApiLog logFinal = await _mH.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", payloadLast, "POST", "企业微信", purpose, memo, batchId);
            for(int i = 0; i < updateBatch.Count; i++)
            {
                updateBatch[i].wedoc_request_id = logFinal.id;
                _db.wepayBalance.Entry(updateBatch[i]).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();
        }
        [NonAction]
        public async Task ExportWepayTransData(string sheetId, string docId, string token, string batchId, string purpose, string memo, string mchId)
        {
            DateTime startDate = DateTime.Parse("2025-10-15");
            List<WepayBalance> bArr = await _db.wepayBalance.Where(b => b.trans_date.Date >= startDate.Date &&  b.mch_id == mchId && b.seq_id == null)
                .OrderBy(b => b.trans_date).ToListAsync();
            WepayBalance last = await _db.wepayBalance.Where(b => b.trans_date.Date >= startDate.Date &&  b.mch_id == mchId && b.seq_id != null)
                .OrderByDescending(b => b.seq_id).AsNoTracking().FirstOrDefaultAsync();
            int seq = 1;
            if (last != null)
            {
                seq = ((int)last.seq_id) + 1;
            }
            BatchUpdateRequest batchUpdateRequest = new BatchUpdateRequest()
            {
                docid = docId,
                requests = new List<UpdateOperation>()
            };
            UpdateRangeRequest updateRange = new UpdateRangeRequest()
            {
                sheet_id = sheetId,
                grid_data = new GridData()
            };
            UpdateOperation updateOperation = new UpdateOperation();
            updateOperation.update_range_request = updateRange;
            batchUpdateRequest.requests.Add(updateOperation);
            GridData gData = updateRange.grid_data;
            gData.start_column = 0;
            gData.start_row = 1;
            gData.rows = new List<Row>();
            //gData.start_row = 1;
            int nextStart = 1;
            List<WepayBalance> updateBatch = new List<WepayBalance>();
            for (int i = 0; i < bArr.Count; i++)
            {
                if (i % 200 == 0 && i > 0)
                {
                    string payload = JsonConvert.SerializeObject(batchUpdateRequest);
                    WebApiLog log = await _mH.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", payload, "POST", "企业微信", purpose, memo, batchId);
                    gData.rows.Clear();
                    gData.start_row = nextStart;
                    try
                    {
                        WeComApiResponse res = JsonConvert.DeserializeObject<WeComApiResponse>(log.response.Trim());
                        if (res.errcode == 0)
                        {
                            for(int j = 0; j < updateBatch.Count; j++)
                            {
                                updateBatch[j].wedoc_request_id = log.id;
                                _db.wepayBalance.Entry(updateBatch[j]).State = EntityState.Modified;
                            }
                            await _db.SaveChangesAsync();
                            updateBatch.Clear();
                            updateBatch = new List<WepayBalance>();
                        }
                    }
                    catch
                    {
                        updateBatch.Clear();
                        updateBatch = new List<WepayBalance>();
                    }
                }
                nextStart++;
                Row row = new Row();
                row.values = new List<Cell>();
                for (int j = 0; j < wepayBalanceFieldsArr.Length; j++)
                {
                    Cell cell = new Cell();
                    cell.cell_format = new CellFormat();
                    cell.cell_format.bold = false;
                    if (bArr[i].refund_amount != 0)
                    {
                        cell.cell_format.color.red = 255;
                    }
                    cell.cell_value = new CellValue();
                    switch (wepayBalanceFieldsArr[j].Trim())
                    {
                        case "序号":
                            cell.cell_value.text = seq.ToString();
                            bArr[i].seq_id = seq;
                            seq++;
                            break;
                        case "交易日期":
                            DateTime transDate = DateTime.Parse(bArr[i].trans_date.ToString());
                            cell.cell_value.text = transDate.ToString("yyyy-MM-dd");
                            break;
                        case "交易时间":
                            DateTime transTime = DateTime.Parse(bArr[i].trans_date.ToString());
                            cell.cell_value.text = transTime.ToString("HH:mm:ss");
                            break;
                        case "公众账号ID":
                            cell.cell_value.text = bArr[i].app_id.ToString();
                            break;
                        case "商户号":
                            cell.cell_value.text = bArr[i].mch_id.ToString();
                            break;
                        case "特约商户号":
                            cell.cell_value.text = bArr[i].spc_mch_id.ToString();
                            break;
                        case "设备号":
                            cell.cell_value.text = bArr[i].device_id.ToString();
                            break;
                        case "微信订单号":
                            cell.cell_value.text = bArr[i].wepay_order_num.ToString();
                            break;
                        case "商户订单号":
                            cell.cell_value.text = bArr[i].out_trade_no.ToString();
                            break;
                        case "用户标识":
                            cell.cell_value.text = bArr[i].open_id.ToString();
                            break;
                        case "交易类型":
                            cell.cell_value.text = bArr[i].trans_type.ToString();
                            break;
                        case "交易状态":
                            cell.cell_value.text = bArr[i].pay_status.ToString();
                            break;
                        case "付款银行":
                            cell.cell_value.text = bArr[i].bank.ToString();
                            break;
                        case "货币种类":
                            cell.cell_value.text = bArr[i].currency.ToString();
                            break;
                        case "应结订单金额":
                            cell.cell_value.text = bArr[i].settle_amount.ToString();
                            break;
                        case "代金券金额":
                            cell.cell_value.text = bArr[i].coupon_amount.ToString();
                            break;
                        case "微信退款单号":
                            cell.cell_value.text = bArr[i].refund_no.ToString();
                            break;
                        case "商户退款单号":
                            cell.cell_value.text = bArr[i].out_refund_no.ToString();
                            break;
                        case "退款金额":
                            cell.cell_value.text = bArr[i].refund_amount.ToString();
                            break;
                        case "充值券退款金额":
                            cell.cell_value.text = bArr[i].coupon_refund_amount.ToString();
                            break;
                        case "退款状态":
                            cell.cell_value.text = bArr[i].refund_status.ToString();
                            break;
                        case "商品名称":
                            cell.cell_value.text = bArr[i].product_name.ToString();
                            break;
                        case "商户数据包":
                            cell.cell_value.text = bArr[i].product_package.ToString();
                            break;
                        case "手续费":
                            cell.cell_value.text = bArr[i].fee.ToString();
                            break;
                        case "费率":
                            cell.cell_value.text = bArr[i].fee_rate.ToString();
                            break;
                        case "订单金额":
                            cell.cell_value.text = bArr[i].order_amount.ToString();
                            break;
                        case "申请退款金额":
                            cell.cell_value.text = bArr[i].request_refund_amount.ToString();
                            break;
                        case "费率备注":
                            cell.cell_value.text = bArr[i].fee_rate_memo.ToString();
                            break;
                        case "导出批次":
                            cell.cell_value.text = batchId.Trim();
                            bArr[i].batch_id = batchId;
                            break;    
                        case "导出日期":
                            cell.cell_value.text = DateTime.Now.ToShortDateString();
                            break;                 
                        case "导出时间":
                            cell.cell_value.text = DateTime.Now.ToShortTimeString();
                            break;
                        default:
                            break;
                    }
                    updateBatch.Add(bArr[i]);
                    row.values.Add(cell);
                }
                gData.rows.Add(row);
            }
            string payloadLast = JsonConvert.SerializeObject(batchUpdateRequest);
            WebApiLog logFinal = await _mH.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", payloadLast, "POST", "企业微信", purpose, memo, batchId);
            for(int i = 0; i < updateBatch.Count; i++)
            {
                updateBatch[i].wedoc_request_id = logFinal.id;
                _db.wepayBalance.Entry(updateBatch[i]).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();
        }
        [NonAction]
        public async Task<List<WebApiLog>> InsertTransData(string sheetId, string docId, string token, string batchId, string purpose, string memo, string? mchId = null)
        {
            MiniAppHelperController _mHelper = new MiniAppHelperController(_db, _config);
            List<WebApiLog> logs = new List<WebApiLog>();
            BatchUpdateRequest batchUpdateRequest = new BatchUpdateRequest()
            {
                docid = docId,
                requests = new List<UpdateOperation>()
            };
            UpdateRangeRequest updateRange = new UpdateRangeRequest()
            {
                sheet_id = sheetId,
                grid_data = new GridData()
            };
            UpdateOperation updateOperation = new UpdateOperation();
            updateOperation.update_range_request = updateRange;
            batchUpdateRequest.requests.Add(updateOperation);
            GridData gData = updateRange.grid_data;
            gData.start_column = 0;
            gData.start_row = 1;
            gData.rows = new List<Row>();
            //gData.start_row = 1;
            int nextStart = 1;
            DateTime startDate = DateTime.Parse("2025-10-15");
            List<WepayBalance> bArr = await _db.wepayBalance.Where(b => b.trans_date.Date >= startDate.Date
                && (mchId == null || b.mch_id == mchId)).OrderBy(b => b.mch_id).ThenBy(b => b.trans_date).AsNoTracking().ToListAsync();
            for (int i = 0; i < bArr.Count; i++)
            {
                if (i % 200 == 0 && i > 0)
                {
                    //gData.start_row = lastStart;
                    string payload = JsonConvert.SerializeObject(batchUpdateRequest);
                    WebApiLog log = await _mHelper.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", payload, "POST", "企业微信", purpose, memo, batchId);
                    logs.Add(log);
                    gData.rows.Clear();
                    gData.start_row = nextStart;
                }
                nextStart++;
                Row row = new Row();
                row.values = new List<Cell>();
                for (int j = 0; j < wepayBalanceFieldsArr.Length; j++)
                {
                    Cell cell = new Cell();
                    cell.cell_format = new CellFormat();
                    cell.cell_format.bold = false;
                    if (bArr[i].refund_amount != 0)
                    {
                        cell.cell_format.color.red = 255;
                    }
                    cell.cell_value = new CellValue();
                    switch (wepayBalanceFieldsArr[j].Trim())
                    {
                        case "交易日期":
                            DateTime transDate = DateTime.Parse(bArr[i].trans_date.ToString());
                            cell.cell_value.text = transDate.ToString("yyyy-MM-dd");
                            break;
                        case "交易时间":
                            DateTime transTime = DateTime.Parse(bArr[i].trans_date.ToString());
                            cell.cell_value.text = transTime.ToString("HH:mm:ss");
                            break;
                        case "公众账号ID":
                            cell.cell_value.text = bArr[i].app_id.ToString();
                            break;
                        case "商户号":
                            cell.cell_value.text = bArr[i].mch_id.ToString();

                            break;
                        case "特约商户号":
                            cell.cell_value.text = bArr[i].spc_mch_id.ToString();

                            break;
                        case "设备号":
                            cell.cell_value.text = bArr[i].device_id.ToString();

                            break;
                        case "微信订单号":
                            cell.cell_value.text = bArr[i].wepay_order_num.ToString();

                            break;
                        case "商户订单号":
                            cell.cell_value.text = bArr[i].out_trade_no.ToString();

                            break;
                        case "用户标识":
                            cell.cell_value.text = bArr[i].open_id.ToString();

                            break;
                        case "交易类型":
                            cell.cell_value.text = bArr[i].trans_type.ToString();

                            break;
                        case "交易状态":
                            cell.cell_value.text = bArr[i].pay_status.ToString();

                            break;
                        case "付款银行":
                            cell.cell_value.text = bArr[i].bank.ToString();

                            break;
                        case "货币种类":
                            cell.cell_value.text = bArr[i].currency.ToString();

                            break;
                        case "应结订单金额":
                            cell.cell_value.text = bArr[i].settle_amount.ToString();

                            break;
                        case "代金券金额":
                            cell.cell_value.text = bArr[i].coupon_amount.ToString();

                            break;
                        case "微信退款单号":
                            cell.cell_value.text = bArr[i].refund_no.ToString();

                            break;
                        case "商户退款单号":
                            cell.cell_value.text = bArr[i].out_refund_no.ToString();

                            break;
                        case "退款金额":
                            cell.cell_value.text = bArr[i].refund_amount.ToString();

                            break;
                        case "充值券退款金额":
                            cell.cell_value.text = bArr[i].coupon_refund_amount.ToString();

                            break;
                        case "退款状态":
                            cell.cell_value.text = bArr[i].refund_status.ToString();

                            break;
                        case "商品名称":
                            cell.cell_value.text = bArr[i].product_name.ToString();

                            break;
                        case "商户数据包":
                            cell.cell_value.text = bArr[i].product_package.ToString();

                            break;
                        case "手续费":
                            cell.cell_value.text = bArr[i].fee.ToString();

                            break;
                        case "费率":
                            cell.cell_value.text = bArr[i].fee_rate.ToString();

                            break;
                        case "订单金额":
                            cell.cell_value.text = bArr[i].order_amount.ToString();

                            break;
                        case "申请退款金额":
                            cell.cell_value.text = bArr[i].request_refund_amount.ToString();

                            break;
                        case "费率备注":
                            cell.cell_value.text = bArr[i].fee_rate_memo.ToString();

                            break;

                        default:
                            break;
                    }
                    row.values.Add(cell);
                }
                gData.rows.Add(row);
            }
            string payloadLast = JsonConvert.SerializeObject(batchUpdateRequest);
            WebApiLog logFinal = await _mHelper.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", payloadLast, "POST", "企业微信", purpose, memo, batchId);
            logs.Add(logFinal);
            return logs;
        }
        [NonAction]
        public async Task ExportWepayFundData(string sheetId, string docId, string token, string batchId, string purpose, string memo, string mchId)
        {
            DateTime startDate = DateTime.Parse("2025-10-15");
            List<WepayFlowBill> bArr = await _db.wepayFlowBill.Where(b => b.bill_date_time.Date >= startDate.Date &&  b.mch_id == mchId && b.seq_id == null)
                .OrderBy(b => b.bill_date_time).ToListAsync();
            WepayFlowBill last = await _db.wepayFlowBill.Where(b => b.bill_date_time.Date >= startDate.Date &&  b.mch_id == mchId && b.seq_id != null)
                .OrderByDescending(b => b.seq_id).AsNoTracking().FirstOrDefaultAsync();
            int seq = 1;
            if (last != null)
            {
                seq = ((int)last.seq_id) + 1;
            }
            BatchUpdateRequest batchUpdateRequest = new BatchUpdateRequest()
            {
                docid = docId,
                requests = new List<UpdateOperation>()
            };
            UpdateRangeRequest updateRange = new UpdateRangeRequest()
            {
                sheet_id = sheetId,
                grid_data = new GridData()
            };
            UpdateOperation updateOperation = new UpdateOperation();
            updateOperation.update_range_request = updateRange;
            batchUpdateRequest.requests.Add(updateOperation);
            GridData gData = updateRange.grid_data;
            gData.start_column = 0;
            gData.start_row = 1;
            gData.rows = new List<Row>();
            //gData.start_row = 1;
            int nextStart = 1;
            List<WepayFlowBill> updateBatch = new List<WepayFlowBill>();
            for (int i = 0; i < bArr.Count; i++)
            {
                if (i % 200 == 0 && i > 0)
                {
                    string payload = JsonConvert.SerializeObject(batchUpdateRequest);
                    WebApiLog log = await _mH.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", payload, "POST", "企业微信", purpose, memo, batchId);
                    gData.rows.Clear();
                    gData.start_row = nextStart;
                    try
                    {
                        WeComApiResponse res = JsonConvert.DeserializeObject<WeComApiResponse>(log.response.Trim());
                        if (res.errcode == 0)
                        {
                            for(int j = 0; j < updateBatch.Count; j++)
                            {
                                updateBatch[j].wedoc_request_id = log.id;
                                _db.wepayFlowBill.Entry(updateBatch[j]).State = EntityState.Modified;
                            }
                            await _db.SaveChangesAsync();
                            updateBatch.Clear();
                            updateBatch = new List<WepayFlowBill>();
                        }
                    }
                    catch
                    {
                        updateBatch.Clear();
                        updateBatch = new List<WepayFlowBill>();
                    }
                }
                nextStart++;
                Row row = new Row();
                row.values = new List<Cell>();
                for (int j = 0; j < wepayFundFieldsArr.Length; j++)
                {
                    Cell cell = new Cell();
                    cell.cell_format = new CellFormat();
                    cell.cell_format.bold = false;
                    cell.cell_value = new CellValue();
                    switch (wepayFundFieldsArr[j].Trim())
                    {
                        case "序号":
                            cell.cell_value.text = seq.ToString();
                            bArr[i].seq_id = seq;
                            seq++;
                            break;
                        case "商户号":
                            cell.cell_value.text = bArr[i].mch_id;
                            break;
                        case "记账日期":
                            DateTime transDate = DateTime.Parse(bArr[i].bill_date_time.ToString());
                            cell.cell_value.text = transDate.ToString("yyyy-MM-dd");
                            break;
                        case "记账时间":
                            DateTime transTime = DateTime.Parse(bArr[i].bill_date_time.ToString());
                            cell.cell_value.text = transTime.ToString("HH:mm:ss");
                            break;
                        case "微信支付业务单号":
                            cell.cell_value.text = bArr[i].biz_no.ToString();
                            break;
                        case "资金流水单号":
                            cell.cell_value.text = bArr[i].flow_no.ToString();
                            break;
                        case "业务名称":
                            cell.cell_value.text = bArr[i].biz_name.ToString();
                            break;
                        case "业务类型":
                            cell.cell_value.text = bArr[i].biz_type.ToString();
                            break;
                        case "收支类型":
                            cell.cell_value.text = bArr[i].bill_type.ToString();
                            break;
                        case "收支金额(元)":
                            cell.cell_value.text = bArr[i].amount.ToString();
                            break;
                        case "账户结余(元)":
                            cell.cell_value.text = bArr[i].surplus.ToString();
                            break;
                        case "资金变更提交申请人":
                            cell.cell_value.text = bArr[i].oper.ToString();
                            break;
                        case "备注":
                            cell.cell_value.text = bArr[i].memo.ToString();
                            break;
                        case "业务凭证号":
                            cell.cell_value.text = bArr[i].invoice_id.ToString();
                            break;
                        case "导出批次":
                            cell.cell_value.text = batchId.Trim();
                            bArr[i].batch_id = batchId;
                            break;    
                        case "导出日期":
                            cell.cell_value.text = DateTime.Now.ToShortDateString();
                            break;                 
                        case "导出时间":
                            cell.cell_value.text = DateTime.Now.ToShortTimeString();
                            break;
                        default:
                            break;
                    }
                    row.values.Add(cell);
                }
                updateBatch.Add(bArr[i]);
                gData.rows.Add(row);
            }
            string payloadLast = JsonConvert.SerializeObject(batchUpdateRequest);
            WebApiLog logFinal = await _mH.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", payloadLast, "POST", "企业微信", purpose, memo, batchId);
            for(int i = 0; i < updateBatch.Count; i++)
            {
                updateBatch[i].wedoc_request_id = logFinal.id;
                _db.wepayFlowBill.Entry(updateBatch[i]).State = EntityState.Modified;
            }
            await _db.SaveChangesAsync();
        }
        [NonAction]
        public async Task<List<WebApiLog>> InsertFundData(string sheetId, string docId, string token, string batchId, string purpose, string memo, string? mchId = null)
        {
            MiniAppHelperController _mHelper = new MiniAppHelperController(_db, _config);
            List<WebApiLog> logs = new List<WebApiLog>();
            BatchUpdateRequest batchUpdateRequest = new BatchUpdateRequest()
            {
                docid = docId,
                requests = new List<UpdateOperation>()
            };
            UpdateRangeRequest updateRange = new UpdateRangeRequest()
            {
                sheet_id = sheetId,
                grid_data = new GridData()
            };
            UpdateOperation updateOperation = new UpdateOperation();
            updateOperation.update_range_request = updateRange;
            batchUpdateRequest.requests.Add(updateOperation);
            GridData gData = updateRange.grid_data;
            gData.start_column = 0;
            gData.start_row = 1;
            gData.rows = new List<Row>();
            //gData.start_row = 1;
            int nextStart = 1;
            DateTime startDate = DateTime.Parse("2025-10-15");
            List<WepayFlowBill> bArr = await _db.wepayFlowBill.Where(b => b.bill_date_time.Date >= startDate.Date && (mchId == null || b.mch_id == mchId)).OrderBy(b => b.mch_id).ThenBy(b => b.bill_date_time).AsNoTracking().ToListAsync();
            for (int i = 0; i < bArr.Count; i++)
            {
                if (i % 200 == 0 && i > 0)
                {
                    //gData.start_row = lastStart;
                    string payload = JsonConvert.SerializeObject(batchUpdateRequest);
                    WebApiLog log = await _mHelper.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", payload, "POST", "企业微信", purpose, memo, batchId);
                    logs.Add(log);
                    gData.rows.Clear();
                    gData.start_row = nextStart;
                }
                nextStart++;
                Row row = new Row();
                row.values = new List<Cell>();
                for (int j = 0; j < wepayFundFieldsArr.Length; j++)
                {
                    Cell cell = new Cell();
                    cell.cell_format = new CellFormat();
                    cell.cell_format.bold = false;
                    cell.cell_value = new CellValue();
                    switch (wepayFundFieldsArr[j].Trim())
                    {
                        case "商户号":
                            cell.cell_value.text = bArr[i].mch_id;
                            break;
                        case "记账日期":
                            DateTime transDate = DateTime.Parse(bArr[i].bill_date_time.ToString());
                            cell.cell_value.text = transDate.ToString("yyyy-MM-dd");
                            break;
                        case "记账时间":
                            DateTime transTime = DateTime.Parse(bArr[i].bill_date_time.ToString());
                            cell.cell_value.text = transTime.ToString("HH:mm:ss");
                            break;
                        case "微信支付业务单号":
                            cell.cell_value.text = bArr[i].biz_no.ToString();
                            break;
                        case "资金流水单号":
                            cell.cell_value.text = bArr[i].flow_no.ToString();

                            break;
                        case "业务名称":
                            cell.cell_value.text = bArr[i].biz_name.ToString();

                            break;
                        case "业务类型":
                            cell.cell_value.text = bArr[i].biz_type.ToString();

                            break;
                        case "收支类型":
                            cell.cell_value.text = bArr[i].bill_type.ToString();

                            break;
                        case "收支金额(元)":
                            cell.cell_value.text = bArr[i].amount.ToString();

                            break;
                        case "账户结余(元)":
                            cell.cell_value.text = bArr[i].surplus.ToString();

                            break;
                        case "资金变更提交申请人":
                            cell.cell_value.text = bArr[i].oper.ToString();

                            break;
                        case "备注":
                            cell.cell_value.text = bArr[i].memo.ToString();

                            break;
                        case "业务凭证号":
                            cell.cell_value.text = bArr[i].invoice_id.ToString();

                            break;

                        default:
                            break;
                    }
                    row.values.Add(cell);
                }
                gData.rows.Add(row);
            }
            string payloadLast = JsonConvert.SerializeObject(batchUpdateRequest);
            WebApiLog logFinal = await _mHelper.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", payloadLast, "POST", "企业微信", purpose, memo, batchId);
            logs.Add(logFinal);
            return logs;
        }
        [NonAction]
        public async Task<List<WebApiLog>> InsertAliData(string sheetId, string docId, string token, string batchId, string purpose, string memo)
        {
            MiniAppHelperController _mHelper = new MiniAppHelperController(_db, _config);
            List<WebApiLog> logs = new List<WebApiLog>();
            BatchUpdateRequest batchUpdateRequest = new BatchUpdateRequest()
            {
                docid = docId,
                requests = new List<UpdateOperation>()
            };
            UpdateRangeRequest updateRange = new UpdateRangeRequest()
            {
                sheet_id = sheetId,
                grid_data = new GridData()
            };
            UpdateOperation updateOperation = new UpdateOperation();
            updateOperation.update_range_request = updateRange;
            batchUpdateRequest.requests.Add(updateOperation);
            GridData gData = updateRange.grid_data;
            gData.start_column = 0;
            gData.start_row = 1;
            gData.rows = new List<Row>();
            gData.start_row = 1;
            int nextStart = 1;
            DateTime startDate = DateTime.Parse("2025-10-15");
            List<AliDownloadFlowBill> bArr = await _db.aliDownloadFlowBill.Where(a => a.trans_date.Date >= startDate.Date).OrderBy(a => a.trans_date).AsNoTracking().ToListAsync();
            for (int i = 0; i < bArr.Count; i++)
            {
                if (i % 200 == 0 && i > 0)
                {
                    //gData.start_row = lastStart;
                    string payload = JsonConvert.SerializeObject(batchUpdateRequest);
                    WebApiLog log = await _mHelper.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", payload, "POST", "企业微信", purpose, memo, batchId);
                    logs.Add(log);
                    gData.rows.Clear();
                    gData.start_row = nextStart;
                }
                nextStart++;
                Row row = new Row();
                row.values = new List<Cell>();
                for (int j = 0; j < aliFields.Length; j++)
                {
                    Cell cell = new Cell();
                    cell.cell_format = new CellFormat();
                    cell.cell_format.bold = false;
                    /*
                    if (bArr[i].refund_amount != 0)
                    {
                        cell.cell_format.color.red = 255;
                    }
                    */
                    cell.cell_value = new CellValue();
                    switch (aliFields[j].Trim())
                    {
                        case "账务流水号":
                            cell.cell_value.text = bArr[i].id;
                            break;
                        case "发生日期":
                            DateTime transDate = DateTime.Parse(bArr[i].trans_date.ToString());
                            cell.cell_value.text = transDate.ToString("yyyy-MM-dd");
                            break;
                        case "发生时间":
                            DateTime transTime = DateTime.Parse(bArr[i].trans_date.ToString());
                            cell.cell_value.text = transTime.ToString("HH:mm:ss");
                            break;
                        case "业务流水号":
                            cell.cell_value.text = bArr[i].biz_num.ToString();
                            break;
                        case "商户订单号":
                            cell.cell_value.text = bArr[i].out_trade_num.ToString();

                            break;
                        case "商品名称":
                            cell.cell_value.text = bArr[i].prod_name.ToString();

                            break;
                        case "对方账号":
                            cell.cell_value.text = bArr[i].receiver_ali_account.ToString();

                            break;
                        case "收入金额（+元）":
                            cell.cell_value.text = bArr[i].income.ToString();

                            break;
                        case "支出金额（-元）":
                            cell.cell_value.text = bArr[i].outcome.ToString();

                            break;
                        case "账户余额（元）":
                            cell.cell_value.text = bArr[i].remainder.ToString();

                            break;
                        case "交易渠道":
                            cell.cell_value.text = bArr[i].trans_channel.ToString();

                            break;
                        case "业务类型":
                            cell.cell_value.text = bArr[i].biz_type.ToString();

                            break;
                        case "备注":
                            cell.cell_value.text = bArr[i].memo.ToString();

                            break;

                        default:
                            break;
                    }
                    row.values.Add(cell);
                }
                gData.rows.Add(row);
            }
            string payloadLast = JsonConvert.SerializeObject(batchUpdateRequest);
            WebApiLog logFinal = await _mHelper.PerformRequest("https://qyapi.weixin.qq.com/cgi-bin/wedoc/spreadsheet/batch_update?access_token=" + token, "", payloadLast, "POST", "企业微信", purpose, memo, batchId);
            logs.Add(logFinal);
            return logs;
        }
        /*
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
        */
    }
}