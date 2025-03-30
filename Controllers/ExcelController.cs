using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using System.IO;
using NPOI.XSSF.UserModel;
using System;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.SS.Formula.Functions;
//using NPOI.SS.UserModel;

namespace SnowmeetApi.Controllers
{
    [Route("core/[controller]/[action]")]
    [ApiController]
    public class ExcelController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private IConfiguration _config;  
        public ExcelController(ApplicationDBContext context, IConfiguration config)
        {
            _db = context;
            _config = config;
        }

        [HttpGet]
        public void TestCreateExcel()
        {
            var workbook = new XSSFWorkbook();
            var sheet = workbook.CreateSheet("Sheet1");
            sheet.CreateRow(0).CreateCell(0).SetCellValue("This is a test");
            sheet.CreateRow(1).CreateCell(0).SetCellValue("test to merge");
            sheet.CreateRow(2).CreateCell(0).SetCellValue("get merge value");
            sheet.AddMergedRegion(new NPOI.SS.Util.CellRangeAddress(0, 1, 0,0));
            Console.Write(sheet.GetRow(0).GetCell(0).StringCellValue);
            Console.Write(sheet.GetRow(1).GetCell(0).StringCellValue);

            using(var file = System.IO.File.Create("sample.xlsx"))
            {
                workbook.Write(file);
            }
        }
        [HttpGet]
        public void TestReadExcel()
        {
            //NPOIMemoryStream stream = new NPOIMemoryStream();
            MemoryStream stream = new MemoryStream();
            using (var file = System.IO.File.OpenRead("sale.xls"))
            {
                file.CopyTo(stream);
                stream.Position = 0;
                using (var workbook = new HSSFWorkbook((Stream)stream))
                {
                    var sheet = workbook.GetSheetAt(0);
                    var cell = sheet.GetRow(0).GetCell(0);
                    Console.Write(cell.StringCellValue.Trim());
                }
                
            }
        }

    }
}