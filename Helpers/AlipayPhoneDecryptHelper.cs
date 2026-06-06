using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SnowmeetApi.Helpers
{
    // 2026-06-03: 把 my.getPhoneNumber 返回的加密 response 解出 mobile。
    //
    // 历史 bug(6-3 续 真机 toast): `The input is not a valid Base-64 string ...`
    // 根因怀疑组合:
    //   (a) 部分 alipay SDK 把 res.response 包成 JSON {"response":"<base64>","sign":"...","signType":"RSA2"},
    //       前端整段当 base64 直传给后端 → Convert.FromBase64String 直接炸
    //   (b) aes_key.txt 通过剪贴板粘贴落地, 残留 UTF-8 BOM(EF BB BF) 或 CRLF, .Trim() 不去 BOM
    //   (c) encData URL 传输丢 padding `==` 或 `+` 被空格替换
    // 本 helper 一次性把三类清洗都做掉, 让 _extractPhone / _alipayMemberLogin 复用。
    public static class AlipayPhoneDecryptHelper
    {
        // 全 0 IV: base64 编码后是 22 字符末尾两个 = padding, 解码后 16 个 \x00
        private const string ZERO_IV = "AAAAAAAAAAAAAAAAAAAAAA==";

        // appId → cert 目录里 aes_key.txt 路径
        public static string Decrypt(string rawEncData, string appId)
        {
            if (string.IsNullOrEmpty(rawEncData))
            {
                throw new ArgumentException("encData 为空");
            }
            if (string.IsNullOrEmpty(appId))
            {
                throw new ArgumentException("appId 为空");
            }

            string aesKey = LoadAesKey(appId);
            string encDataCleaned = CleanEncData(rawEncData);

            Console.WriteLine($"[AlipayPhoneDecrypt] appId={appId}, aesKeyLen={aesKey?.Length}, encDataLen={encDataCleaned?.Length}, encDataHead={Head(encDataCleaned, 40)}");

            string json;
            try
            {
                json = Util.AES_decrypt(encDataCleaned, aesKey, ZERO_IV);
            }
            catch (FormatException fex)
            {
                throw new Exception($"AES base64 解码失败 (aesKeyLen={aesKey?.Length}, encDataLen={encDataCleaned?.Length}): {fex.Message}");
            }
            catch (Exception ex)
            {
                throw new Exception($"AES 解密失败: {ex.Message}");
            }

            Console.WriteLine($"[AlipayPhoneDecrypt] decrypted jsonLen={json?.Length}, head={Head(json, 80)}");

            JToken jsonObj;
            try
            {
                jsonObj = (JToken)JsonConvert.DeserializeObject(json);
            }
            catch (Exception ex)
            {
                throw new Exception($"解密结果 JSON 解析失败: {ex.Message}");
            }

            // 多路径查 mobile: 根/response.*/data.* 都尝试一遍
            // (alipay 不同 SDK 版本返回结构差异: 早期是 {mobile}, 新的可能是 {response: {mobile}})
            JToken mobileToken = jsonObj?["mobile"]
                ?? jsonObj?["phoneNumber"]
                ?? jsonObj?.SelectToken("response.mobile")
                ?? jsonObj?.SelectToken("response.phoneNumber")
                ?? jsonObj?.SelectToken("data.mobile")
                ?? jsonObj?.SelectToken("data.phoneNumber");

            // response 字段是字符串化的 JSON → 再解一层
            if ((mobileToken == null || string.IsNullOrWhiteSpace(mobileToken.ToString()))
                && jsonObj?["response"] != null
                && jsonObj["response"].Type == JTokenType.String)
            {
                try
                {
                    var responseObj = (JToken)JsonConvert.DeserializeObject(jsonObj["response"].ToString());
                    mobileToken = responseObj?["mobile"]
                        ?? responseObj?["phoneNumber"]
                        ?? responseObj?.SelectToken("data.mobile")
                        ?? responseObj?.SelectToken("data.phoneNumber");
                }
                catch
                {
                    // ignore, fall through to error path
                }
            }

            if (mobileToken == null || string.IsNullOrWhiteSpace(mobileToken.ToString()))
            {
                // 解密成功但 alipay 返回失败码(如未授权、密钥失效) → 抛 alipay 的 code/sub_code/msg
                string aliCode = jsonObj?["code"]?.ToString();
                string aliSubCode = jsonObj?["sub_code"]?.ToString() ?? jsonObj?["subCode"]?.ToString();
                string aliMsg = jsonObj?["msg"]?.ToString()
                    ?? jsonObj?["sub_msg"]?.ToString()
                    ?? jsonObj?["subMsg"]?.ToString()
                    ?? "解密结果中无 mobile 字段";

                if (!string.IsNullOrEmpty(aliSubCode)
                    && aliSubCode.Trim().Equals("isv.missing-default-signature-type", StringComparison.OrdinalIgnoreCase))
                {
                    aliMsg += "；请在支付宝开放平台为该小程序应用配置默认签名方式（通常为 RSA2）并确认公私钥已生效";
                }

                if (!string.IsNullOrEmpty(aliCode) || !string.IsNullOrEmpty(aliSubCode))
                {
                    aliMsg = $"{aliMsg} (code={aliCode ?? ""}, subCode={aliSubCode ?? ""})";
                }
                throw new Exception("支付宝 getPhoneNumber 解密失败: " + aliMsg);
            }
            return mobileToken.ToString().Trim();
        }

        // 客户端可能整段 URL 编码、可能 JSON 包装、可能 padding 丢失。三层清洗。
        private static string CleanEncData(string raw)
        {
            string s = Util.UrlDecode(raw ?? "").Trim();

            // JSON 包装尝试: my.getPhoneNumber 新 SDK 形如 {"response":"<base64>","sign":"...","signType":"RSA2"}
            if (s.StartsWith("{"))
            {
                try
                {
                    JToken wrap = (JToken)JsonConvert.DeserializeObject(s);
                    if (wrap != null && wrap["response"] != null)
                    {
                        s = wrap["response"].ToString().Trim();
                        Console.WriteLine($"[AlipayPhoneDecrypt] unwrapped JSON response field, innerLen={s.Length}");
                    }
                }
                catch
                {
                    // 不是合法 JSON, 当 raw base64 继续走
                }
            }

            // 字符级清洗: CRLF / LF / 空白都去掉
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\r' || c == '\n' || c == ' ' || c == '\t')
                {
                    continue;
                }
                sb.Append(c);
            }
            s = sb.ToString();

            // padding 兜底: URL 传输丢 `==`, 按 % 4 补
            int mod = s.Length % 4;
            if (mod == 2)
            {
                s += "==";
                Console.WriteLine("[AlipayPhoneDecrypt] padding fixed: +==");
            }
            else if (mod == 3)
            {
                s += "=";
                Console.WriteLine("[AlipayPhoneDecrypt] padding fixed: +=");
            }
            else if (mod == 1)
            {
                // 1 不是合法 base64 余数; 报错前留诊断 (Util.AES_decrypt 会再抛)
                Console.WriteLine("[AlipayPhoneDecrypt] WARN: encData length % 4 == 1, likely truncated");
            }

            return s;
        }

        private static string LoadAesKey(string appId)
        {
            string contentRoot = Directory.GetCurrentDirectory();
            string keyPath = Path.Combine(contentRoot, "AlipayCertificate", appId, "aes_key.txt");
            if (!File.Exists(keyPath))
            {
                throw new Exception("支付宝 AES 密钥文件不存在: " + keyPath);
            }
            string content = File.ReadAllText(keyPath);
            // BOM (EF BB BF -> U+FEFF) 去掉; 多种字节顺序兜底
            if (!string.IsNullOrEmpty(content) && content[0] == '﻿')
            {
                content = content.Substring(1);
                Console.WriteLine("[AlipayPhoneDecrypt] aes_key.txt BOM stripped");
            }
            // CRLF / LF / 空白全去
            content = content.Replace("\r", "").Replace("\n", "").Trim();
            if (string.IsNullOrEmpty(content))
            {
                throw new Exception("支付宝 AES 密钥文件内容为空: " + keyPath);
            }
            return content;
        }

        private static string Head(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n);
        }
    }
}
