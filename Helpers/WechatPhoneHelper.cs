using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SnowmeetApi.Helpers
{
    // 微信小程序 getPhoneNumber 的 encryptedData + iv，用登录时的 session_key 解出手机号。
    // 与 PaymentIdentityController._extractPhone 的微信分支同一算法（AES-128-CBC）。
    public static class WechatPhoneHelper
    {
        public static string Decrypt(string encData, string iv, string sessionKey)
        {
            if (string.IsNullOrWhiteSpace(encData) || string.IsNullOrWhiteSpace(iv))
            {
                throw new Exception("encData / iv 缺失");
            }
            string json = Util.AES_decrypt(Util.UrlDecode(encData).Trim(), sessionKey.Trim(), Util.UrlDecode(iv).Trim());
            JToken jsonObj = (JToken)JsonConvert.DeserializeObject(json);
            if (jsonObj == null || jsonObj["phoneNumber"] == null)
            {
                throw new Exception("解密结果中无 phoneNumber");
            }
            return jsonObj["phoneNumber"].ToString().Trim();
        }
    }
}
