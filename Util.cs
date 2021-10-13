using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
    }
}
