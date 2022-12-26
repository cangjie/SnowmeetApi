using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web;

namespace SnowmeetApi
{
    public class Util
    {
        public static string workingPath = $"{Environment.CurrentDirectory}";

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
        public static string GetWebContent(string url, string postData)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
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

        public static string GetLongTimeStamp(DateTime currentDateTime)
        {
            TimeSpan ts = currentDateTime - new DateTime(1970, 1, 1, 0, 0, 0, 0);
            return Convert.ToInt64(ts.TotalMilliseconds).ToString();
        }

        public static bool IsAdmin(string staffSessionKey, Data.ApplicationDBContext db)
        {
            Models.Users.UnicUser._context = db;
            staffSessionKey = UrlDecode(staffSessionKey).Trim();
            Models.Users.UnicUser staffUser = Models.Users.UnicUser.GetUnicUser(staffSessionKey);
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
    }

}
