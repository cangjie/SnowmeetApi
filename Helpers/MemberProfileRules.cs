using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SnowmeetApi.Helpers
{
    /// <summary>
    /// 顾客昵称、头像（member.nick_name / avatar / nick_avatar_update_date）的纯规则：
    /// 要不要弹「完善资料」、昵称清洗、头像文件类型识别。无数据库、无 IO，好测。
    /// </summary>
    public static class MemberProfileRules
    {
        /// <summary>昵称最多存多少个字（按「看起来的一个字」算，一个表情也算一个）。库里列宽 64（UTF-16 单元），留够余量。</summary>
        public const int NickNameMaxLength = 32;
        private const int NickNameMaxUtf16Units = 64;

        /// <summary>头像文件最大 5 MB（微信 chooseAvatar 给的是压缩过的小图，正常只有几十 KB）。</summary>
        public const long AvatarMaxBytes = 5L * 1024 * 1024;

        /// <summary>
        /// 验证手机号之后要不要请顾客完善头像和昵称。现在：从没保存过（更新时间为空）就问，保存过就不再问。
        /// 2026-11-14 用户：更新时间将来超过一定时间要重新获取，但目前不必做——将来要做，只改这里。
        /// </summary>
        public static bool NeedPrompt(DateTime? updateDate, DateTime now)
        {
            return updateDate == null;
        }

        /// <summary>
        /// 同上，再加一条：昵称或头像缺任何一样（只存了头像、或只存了昵称）也要再问，让顾客补齐
        /// （2026-11-15 用户：存过头像没存昵称的会员，下次再问一次；「我的」页也可以点顶部头像自己补）。
        /// 顾客可以跳过，小程序里本次运行跳过过就不会再弹。
        /// </summary>
        public static bool NeedPrompt(DateTime? updateDate, DateTime now, string? nickName, string? avatar)
        {
            return NeedPrompt(updateDate, now) || string.IsNullOrWhiteSpace(nickName) || string.IsNullOrWhiteSpace(avatar);
        }

        /// <summary>
        /// 昵称清洗：去掉控制字符、两端空白，超过 32 个字截掉（不把表情等多字符的字切成两半）。清洗后什么都没有返回 null。
        /// </summary>
        public static string? CleanNickName(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string text = new string(raw.Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (text.Length == 0) return null;

            var kept = new StringBuilder();
            int elements = 0;
            TextElementEnumerator e = StringInfo.GetTextElementEnumerator(text);
            while (e.MoveNext())
            {
                string element = (string)e.Current;
                if (elements + 1 > NickNameMaxLength || kept.Length + element.Length > NickNameMaxUtf16Units) break;
                kept.Append(element);
                elements++;
            }
            string result = kept.ToString().Trim();
            return result.Length == 0 ? null : result;
        }

        /// <summary>
        /// 按文件开头的几个字节认头像格式，认得出返回带点的扩展名（.jpg / .png / .gif / .webp），否则返回 null。
        /// 不信客户端给的文件名和类型；头像放在公开的图片域名下，只收这四种位图（SVG、网页都不收，它们能带脚本）。
        /// </summary>
        public static string? DetectImageExtension(ReadOnlySpan<byte> head)
        {
            if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return ".jpg";
            if (head.Length >= 8 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47
                && head[4] == 0x0D && head[5] == 0x0A && head[6] == 0x1A && head[7] == 0x0A) return ".png";
            if (head.Length >= 6 && head[0] == (byte)'G' && head[1] == (byte)'I' && head[2] == (byte)'F' && head[3] == (byte)'8'
                && (head[4] == (byte)'7' || head[4] == (byte)'9') && head[5] == (byte)'a') return ".gif";
            if (head.Length >= 12 && head[0] == (byte)'R' && head[1] == (byte)'I' && head[2] == (byte)'F' && head[3] == (byte)'F'
                && head[8] == (byte)'W' && head[9] == (byte)'E' && head[10] == (byte)'B' && head[11] == (byte)'P') return ".webp";
            return null;
        }
    }
}
