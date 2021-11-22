using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace SnowmeetApi
{
    public class Util
    {
        public static long getTime13()
        {
            //ToUniversalTime()转换为标准时区的时间,去掉的话直接就用北京时间
            TimeSpan ts = DateTime.Now.ToUniversalTime() - new DateTime(1970, 1, 1);
            //得到精确到毫秒的时间戳（长度13位）
            long time = (long)ts.TotalMilliseconds;
            return time;
        }

        public static string UrlDecode(string str)
        {
            return HttpUtility.UrlDecode(str).Replace(" ", "+");
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
    }
}
