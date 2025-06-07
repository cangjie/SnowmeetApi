using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Models.Users;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using SnowmeetApi.Models;
using SnowmeetApi.Data;
using Newtonsoft.Json;
using System.Net.WebSockets;
using System.Configuration.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using SnowmeetApi.Controllers;
namespace SnowmeetApi
{
    public class Util
    {
        public static ApplicationDBContext? db = null;

        public static string workingPath = $"{Environment.CurrentDirectory}";

        public static Object GetValueFromResult(ActionResult value)
        {
            try
            {
                if (value.GetType().Name.Trim().Equals("OkObjectResult"))
                {
                    OkObjectResult result = (OkObjectResult)value;
                    return result.Value;
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

        public static long getTime13()
        {
            //ToUniversalTime()转换为标准时区的时间,去掉的话直接就用北京时间
            TimeSpan ts = DateTime.Now.ToUniversalTime() - new DateTime(1970, 1, 1);
            //得到精确到毫秒的时间戳（长度13位）
            long time = (long)ts.TotalMilliseconds;
            return time;
        }

        public static string GetMoneyStr(double amount)
        {
            return (int)(amount * 100) == ((int)amount) * 100 ?
                "¥" + ((int)amount).ToString() + ".00"
                : "¥" + Math.Round(amount, 2).ToString();
        }



        public static string GetRandomCode(int digit)
        {
            string code = "";
            for (int i = 0; i < digit; i++)
            {
                code = code + (new Random()).Next(0, 10).ToString();
            }
            return code;
        }
        public static string GetDbConStr(string fileName)
        {
            string conStr = "";

            string filePath = workingPath + "/" + fileName;

            using (StreamReader sr = new StreamReader(filePath, true))
            {
                conStr = sr.ReadToEnd();
                sr.Close();
            }
            return conStr;
        }

        public static string UrlEncode(string urlStr)
        {
            return HttpUtility.UrlEncode(urlStr.Trim().Replace(" ", "+").Replace("'", "\""));
        }

        public static string UrlDecode(string urlStr)
        {
            if (urlStr == null || urlStr.Trim().Equals(""))
            {
                return "";
            }
            try
            {
                return HttpUtility.UrlDecode(urlStr).Replace(" ", "+").Trim();
            }
            catch
            {
                return "";
            }

        }

        public static string GetWebContent(string url)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                HttpWebResponse res = (HttpWebResponse)req.GetResponse();
                Stream s = res.GetResponseStream();
                StreamReader sr = new StreamReader(s);
                string str = sr.ReadToEnd();
                sr.Close();
                s.Close();
                res.Close();
                req.Abort();
                return str;
            }
            catch
            {
                return "";
            }
        }
        public static string GetWebContent(string url, Encoding encoding)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                HttpWebResponse res = (HttpWebResponse)req.GetResponse();
                Stream s = res.GetResponseStream();
                StreamReader sr = new StreamReader(s, encoding);
                string str = sr.ReadToEnd();
                sr.Close();
                s.Close();
                res.Close();
                req.Abort();
                return str;
            }
            catch
            {
                return "";
            }
        }
        public static string GetWebContent(string url, string postData)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            //req.ContentType = "application/json";
            //req.ContentLength = postData.Length;
            Stream sPost = req.GetRequestStream();
            StreamWriter sw = new StreamWriter(sPost);
            sw.Write(postData);
            sw.Close();
            sPost.Close();
            HttpWebResponse res = (HttpWebResponse)req.GetResponse();
            Stream s = res.GetResponseStream();
            StreamReader sr = new StreamReader(s);
            string str = sr.ReadToEnd();
            sr.Close();
            s.Close();
            return str;
        }

        public static string GetWebContent(string url, string postData, string contentType)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = contentType;
            int len = System.Text.Encoding.UTF8.GetByteCount(postData);
            req.ContentLength = len;
            Stream sPost = req.GetRequestStream();
            StreamWriter sw = new StreamWriter(sPost);
            sw.Write(postData);
            sw.Close();
            sPost.Close();
            HttpWebResponse res = (HttpWebResponse)req.GetResponse();
            Stream s = res.GetResponseStream();
            StreamReader sr = new StreamReader(s);
            string str = sr.ReadToEnd();
            sr.Close();
            s.Close();
            return str;
        }

        public static async Task<string> GetWebContent(string url, string[] headers)
        {
            try
            {
                HttpClient client = new HttpClient();
                for (int i = 0; i < headers.Length; i++)
                {
                    string headerName = headers[i].Trim().Split(':')[0].Trim();
                    string headerValue = headers[i].Trim().Split(':')[1].Trim();
                    client.DefaultRequestHeaders.Add(headerName, headerValue);
                }
                var ret = await client.GetStringAsync(url);
                return ret;
            }
            catch (Exception err)
            {
                Console.WriteLine(err.ToString());
                return "";
            }

        }



        public static string GetLongTimeStamp(DateTime currentDateTime)
        {
            TimeSpan ts = currentDateTime - new DateTime(1970, 1, 1, 0, 0, 0, 0);
            return Convert.ToInt64(ts.TotalMilliseconds).ToString();
        }

        public async static Task<bool> IsAdmin(string staffSessionKey, Data.ApplicationDBContext db)
        {
            //Models.Users.UnicUser._context = db;
            staffSessionKey = UrlDecode(staffSessionKey).Trim();
            Models.Users.UnicUser staffUser = await Models.Users.UnicUser.GetUnicUserAsync(staffSessionKey, db);
            return staffUser.isAdmin;
        }

        public static double GetScoreRate(double realPayPrice, double marketPrice)
        {
            double disCountRate = realPayPrice / marketPrice;
            double rate = 0;
            if (disCountRate == 1)
                rate = 1;
            else if (disCountRate >= 0.95)
                rate = 0.925;
            else if (disCountRate >= 0.9)
                rate = 0.85;
            else if (disCountRate >= 0.85)
                rate = 0.775;
            else if (disCountRate >= 0.8)
                rate = 0.7;
            else if (disCountRate >= 0.75)
                rate = 0.625;
            else if (disCountRate >= 0.7)
                rate = 0.55;
            else if (disCountRate >= 0.65)
                rate = 0.475;
            else if (disCountRate >= 0.6)
                rate = 0.4;
            else if (disCountRate >= 0.55)
                rate = 0.325;
            else if (disCountRate >= 0.5)
                rate = 0.25;
            else if (disCountRate >= 0.45)
                rate = 0.175;
            else if (disCountRate >= 0.4)
                rate = 0.1;
            else
                rate = 0;
            return rate;
        }

        public static string AES_decrypt(string encryptedDataStr, string key, string iv)
        {
            //Aes aes = new Aes();
            Aes rijalg = Aes.Create();
            //-----------------    
            //设置 cipher 格式 AES-128-CBC    

            rijalg.KeySize = 128;

            rijalg.Padding = PaddingMode.PKCS7;
            rijalg.Mode = CipherMode.CBC;

            rijalg.Key = Convert.FromBase64String(key);
            rijalg.IV = Convert.FromBase64String(iv);


            byte[] encryptedData = Convert.FromBase64String(encryptedDataStr);
            //解密    
            ICryptoTransform decryptor = rijalg.CreateDecryptor(rijalg.Key, rijalg.IV);

            string result = "";

            using (MemoryStream msDecrypt = new MemoryStream(encryptedData))
            {
                using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                {
                    using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                    {

                        result += srDecrypt.ReadToEnd();
                    }
                }
            }

            return result;
        }

        public static async Task<UnicUser> GetUser(string sessionKey, Data.ApplicationDBContext db)
        {
            sessionKey = Util.UrlDecode(sessionKey).Trim();
            UnicUser user = await UnicUser.GetUnicUserAsync(sessionKey, db);
            return user;
        }

        public static string GetDayOfWeek(DateTime date)
        {
            string ret = "";
            switch (date.DayOfWeek)
            {
                case DayOfWeek.Sunday:
                    ret = "日";
                    break;
                case DayOfWeek.Monday:
                    ret = "一";
                    break;
                case DayOfWeek.Tuesday:
                    ret = "二";
                    break;
                case DayOfWeek.Wednesday:
                    ret = "三";
                    break;
                case DayOfWeek.Thursday:
                    ret = "四";
                    break;
                case DayOfWeek.Friday:
                    ret = "五";
                    break;
                case DayOfWeek.Saturday:
                    ret = "六";
                    break;
                default:
                    break;
            }
            return ret;
        }

        public static string GetSeason(DateTime date)
        {
            string season = "【雪季】";
            int year = date.Year;
            int month = date.Month;
            if (month <= 5)
            {
                season = (year - 1 - 2000).ToString() + "-" + (year - 2000).ToString() + season;
            }
            else if (month >= 10)
            {
                season = (year - 2000).ToString() + "-" + (year + 1 - 2000).ToString() + season;
            }
            else
            {
                season = year.ToString() + "【夏季】";

            }
            return season;
        }

        public static CoreDataModLog CreateCoreDataModLog(string table, string filed, int key, object? prev, object? curr, int? memberId, int? staffId, string scene, long? traceId = null)
        {
            if (traceId == null)
            {
                traceId = (DateTime.Now - DateTime.Parse("1970-1-1")).Ticks;
            }
            return new CoreDataModLog()
            {
                id = 0,
                table_name = table,
                field_name = filed,
                key_value = key,
                prev_value = prev == null ? "null" : prev.ToString(),
                current_value = curr == null ? "null" : curr.ToString(),
                member_id = memberId,
                staff_id = staffId,
                scene = scene,
                trace_id = (long)traceId,
                create_date = DateTime.Now
            };
        }

        public static string? GetSqlServerConnectionString()
        {
            string path = $"{Environment.CurrentDirectory}";

            if (path.StartsWith("/"))
            {
                path = path + "/";
            }
            else
            {
                path = path + "\\";
            }
            path = path + "config.sqlServer";

            string? conStr = null;

            using (StreamReader sr = new StreamReader(path, true))
            {
                conStr = sr.ReadToEnd().Trim();
                sr.Close();
            }
            return conStr;
        }
        public static ApplicationDBContext? GetDbContext()
        {
            string conStr = GetSqlServerConnectionString();
            if (conStr == null)
            {
                return null;
            }
            DbContextOptionsBuilder<ApplicationDBContext> dbo = new DbContextOptionsBuilder<ApplicationDBContext>();
            dbo.UseSqlServer(conStr);
            ApplicationDBContext db = new ApplicationDBContext(dbo.Options);
            return db;
        }
        public static async Task<string> DealWebSocketMessage(string message, IConfiguration config, IHttpContextAccessor http)
        {
            //string[] msgArr = message.Split('')
            /*
            if (db == null)
            {
                db = GetDbContext();
            }
            */
            ApplicationDBContext db = GetDbContext();
            try
            {
                ApiResult<object> result = new ApiResult<object>()
                {
                    code = 1,
                    message = "无任何结果。",
                    data = null
                };
                WebsocketPost<object> post = JsonConvert.DeserializeObject<WebsocketPost<object>>(message);
                switch (post.command.ToLower())
                {
                    case "queryqrscan":
                        QrCodeController _qH = new QrCodeController(db, config, http);
                        ScanQrCode sc = await _qH.QueryScan((int)post.id);
                        ApiResult<ScanQrCode> realResult = new ApiResult<ScanQrCode>()
                        {
                            code = 0,
                            message = "",
                            data = sc
                        };
                        return JsonConvert.SerializeObject(realResult);
                    case "stopqueryqrscan":
                        QrCodeController _qHStop = new QrCodeController(db, config, http);
                        ScanQrCode scStop = await _qHStop.StopQeryScan((int)post.id);
                        ApiResult<ScanQrCode> realResultStop = new ApiResult<ScanQrCode>()
                        {
                            code = 0,
                            message = "",
                            data = scStop
                        };
                        return JsonConvert.SerializeObject(realResultStop);
                    default:
                        break;
                }
                return JsonConvert.SerializeObject(result);
            }
            catch (Exception err)
            {
                ApiResult<object> result = new ApiResult<object>()
                {
                    code = 1,
                    message = err.ToString(),
                    data = null
                };
                return JsonConvert.SerializeObject(result);
            }
        }
      

        
    }

}
