using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using System.IO;
using NPOI.XSSF.UserModel;
using System;
using NPOI.HSSF.UserModel;

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
            using (var file = System.IO.File.OpenRead("mi7.csv"))
            {
                file.CopyTo(stream);
                stream.Position = 0;
                using (var workbook = new XSSFWorkbook((Stream)stream))
                {
                    Console.Write(workbook.ToString());
                }
            }
        }

    }
}