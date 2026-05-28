using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Controllers.Order
{
    /// <summary>
    /// 顾客扫码进入支付页后、调起付款前的账户匹配 + 代付识别 + 微信未验证标记。
    /// 配套需求文档：snowmeet_ai_doc/payment_identity_verification_requirements.md
    /// 配套实施方案：snowmeet_ai_doc/payment_identity_verification_plan.md
    /// </summary>
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class PaymentIdentityController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;
        private readonly MemberController _memberHelper;

        public PaymentIdentityController(ApplicationDBContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
            _memberHelper = new MemberController(db, config);
        }

        // ====== DTOs ======

        public class CheckPayerIdentityResult
        {
            public string status { get; set; } = "error";
            public int paymentId { get; set; }
            public int orderId { get; set; }
            public string payerType { get; set; }
            public int? orderMemberId { get; set; }
            public string orderMemberMaskedCell { get; set; }
            public string orderMemberName { get; set; }
            public int? scannerMemberId { get; set; }
            public bool scannerHasCell { get; set; }
            public string scannerMaskedCell { get; set; }
            public string errorCode { get; set; }
            public string errorMessage { get; set; }
        }

        public class ConfirmPayIdentityBody
        {
            public int paymentId { get; set; }
            public string payerType { get; set; }   // wechat | alipay
            public string scannerId { get; set; }   // openid (wechat) | payerid (alipay)
            public string action { get; set; }      // submit_phone | choose | confirm_direct
            public string choice { get; set; }      // self | proxy（仅 action=choose）
            public string encData { get; set; }     // 仅 wechat + submit_phone
            public string iv { get; set; }          // 仅 wechat + submit_phone
            public string phoneMock { get; set; }   // alipay + submit_phone 的 stub 入参
        }

        // ====== Public Endpoints ======

        [HttpGet]
        public async Task<ActionResult<ApiResult<CheckPayerIdentityResult>>> CheckPayerIdentity(
            int paymentId, string payerType, string scannerId, string sessionKey)
        {
            payerType = (payerType ?? "wechat").Trim().ToLower();
            scannerId = Util.UrlDecode(scannerId ?? "").Trim();
            var result = await _resolveStatus(paymentId, payerType, scannerId, sessionKey);
            return Ok(new ApiResult<CheckPayerIdentityResult>
            {
                code = result.status == "error" ? 1 : 0,
                message = result.errorMessage ?? "",
                data = result
            });
        }

        [HttpPost]
        public async Task<ActionResult<ApiResult<CheckPayerIdentityResult>>> ConfirmPayIdentity(
            [FromBody] ConfirmPayIdentityBody body, [FromQuery] string sessionKey)
        {
            if (body == null)
            {
                return Ok(_err("error", "请求体缺失"));
            }
            var payerType = (body.payerType ?? "wechat").Trim().ToLower();
            var scannerId = (body.scannerId ?? "").Trim();
            var action = (body.action ?? "").Trim();

            // op 守卫：必须存在且未支付。允许覆盖已有 op.member_id（用户改主意）—
            // 因为「订单归属」由 DealSuccessPaidOrder 在支付成功后才同步，付款方意图在 op.status='待支付' 期间始终可重写。
            var op0 = await _db.orderPayment
                .Where(p => p.id == body.paymentId && p.valid == 1)
                .AsNoTracking().FirstOrDefaultAsync();
            if (op0 == null)
            {
                return Ok(_err("payment_not_found", "支付记录不存在"));
            }
            if (op0.status != "待支付")
            {
                return Ok(_err("payment_closed", "订单已支付或已取消"));
            }

            if (action == "submit_phone")
            {
                return await _submitPhone(body, sessionKey, payerType, scannerId);
            }
            if (action == "choose")
            {
                return await _applyChoice(body, sessionKey, payerType, scannerId);
            }
            if (action == "confirm_direct")
            {
                return await _applyConfirmDirect(body, sessionKey, payerType, scannerId);
            }
            return Ok(_err("error", "未知 action: " + action));
        }

        // ====== Internal: Decision Tree ======

        private async Task<CheckPayerIdentityResult> _resolveStatus(int paymentId, string payerType, string scannerId, string sessionKey)
        {
            var result = new CheckPayerIdentityResult
            {
                status = "error",
                paymentId = paymentId,
                payerType = payerType
            };

            // 1) OrderPayment
            var op = await _db.orderPayment
                .Where(p => p.id == paymentId && p.valid == 1)
                .AsNoTracking().FirstOrDefaultAsync();
            if (op == null)
            {
                result.errorCode = "payment_not_found";
                result.errorMessage = "支付记录不存在";
                return result;
            }
            if (op.status != "待支付")
            {
                result.errorCode = "payment_closed";
                result.errorMessage = "订单已支付或已取消";
                return result;
            }

            // 2) Order
            var order = await _db.order
                .Where(o => o.id == op.order_id && o.valid == 1)
                .AsNoTracking().FirstOrDefaultAsync();
            if (order == null)
            {
                result.errorCode = "order_not_found";
                result.errorMessage = "订单不存在";
                return result;
            }
            result.orderId = order.id;
            result.orderMemberId = order.member_id;

            if (order.member_id != null)
            {
                var orderMember = await _memberHelper.GetWholeMemberById((int)order.member_id);
                if (orderMember != null)
                {
                    result.orderMemberName = string.IsNullOrEmpty(orderMember.real_name)
                        ? null : orderMember.real_name.Trim();
                    result.orderMemberMaskedCell = _maskCell(orderMember.cell);
                }
            }

            // 3) Scanner — 优先按 scannerId 反查；为空时退化为按 sessionKey 反查 mini_session
            string msaType = _msaTypeForPayer(payerType);
            if (msaType == null)
            {
                result.errorCode = "unsupported_payer_type";
                result.errorMessage = "不支持的支付通道: " + payerType;
                return result;
            }

            Member scanner = null;
            if (!string.IsNullOrEmpty(scannerId))
            {
                scanner = await _memberHelper.GetWholeMemberByNum(scannerId, msaType);
            }
            if (scanner == null && !string.IsNullOrEmpty(sessionKey))
            {
                // 兜底：用 sessionKey 反查 MiniSession → member_id（仅微信通道用 wechat_mini_openid sessionType）
                var sk = Util.UrlDecode(sessionKey).Trim();
                var sessionType = payerType == "alipay" ? "alipay_payerid" : "wechat_mini_openid";
                var sess = await _db.miniSession
                    .Where(s => s.session_key.Trim().Equals(sk)
                                && s.session_type.Equals(sessionType)
                                && s.valid == 1
                                && s.expire_date >= DateTime.Now
                                && s.member_id != null)
                    .OrderByDescending(s => s.expire_date)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();
                if (sess != null && sess.member_id != null)
                {
                    scanner = await _memberHelper.GetWholeMemberById((int)sess.member_id);
                }
            }

            if (scanner != null)
            {
                result.scannerMemberId = scanner.id;
                result.scannerHasCell = !string.IsNullOrEmpty(scanner.cell);
                result.scannerMaskedCell = _maskCell(scanner.cell);
            }
            else
            {
                result.scannerMemberId = null;
                result.scannerHasCell = false;
            }

            // 4) Status
            // 仅基于 order.member_id (订单归属) + scannerMemberId 决策。
            // op.member_id 是「付款方意图」,只在 _applyChoice/_applyConfirmDirect 当次返回时强制 direct,
            // 不在 _resolveStatus 里参与判断 —— 否则用户点错后刷新就无法重新选择。
            if (!result.scannerHasCell)
            {
                result.status = "phone_required";
                return result;
            }
            if (order.member_id == null)
            {
                result.status = "direct_to_scanner";
                return result;
            }
            if (result.scannerMemberId == order.member_id)
            {
                result.status = "direct";
                return result;
            }
            result.status = "choose_identity";
            return result;
        }

        // ====== Internal: ConfirmPayIdentity action handlers ======

        private async Task<ActionResult<ApiResult<CheckPayerIdentityResult>>> _submitPhone(
            ConfirmPayIdentityBody body, string sessionKey, string payerType, string scannerId)
        {
            string phone;
            try
            {
                phone = _extractPhone(body, sessionKey, payerType);
            }
            catch (NotSupportedException ex)
            {
                // alipay stub branch
                return Ok(_err("alipay_phone_pending", ex.Message));
            }
            catch (Exception ex)
            {
                return Ok(_err("phone_decrypt_failed", "手机号解析失败: " + ex.Message));
            }
            if (string.IsNullOrEmpty(phone) || phone.Length != 11)
            {
                return Ok(_err("phone_invalid", "手机号无效"));
            }

            string msaType = _msaTypeForPayer(payerType);
            if (msaType == null)
            {
                return Ok(_err("unsupported_payer_type", "不支持的支付通道: " + payerType));
            }

            // 已绑同手机号的会员（不限当前 scanner）
            var phoneOwner = await _memberHelper.GetWholeMemberByNum(phone, MemberSocialAccount.TYPE_CELL);
            // 当前 scanner 已绑的会员（按 openid/payerid 找）
            var scannerMember = await _memberHelper.GetWholeMemberByNum(scannerId, msaType);

            int finalMemberId;

            if (scannerMember == null)
            {
                // 扫码方还未绑任何会员
                if (phoneOwner != null)
                {
                    // 手机号已被另一会员认证
                    // 当前是 wechat/alipay → 看该会员是否已绑同类
                    bool alreadyBoundSameType = phoneOwner.memberSocialAccounts != null
                        && phoneOwner.memberSocialAccounts.Any(m => m.valid == 1 && m.type.Trim().Equals(msaType));
                    if (alreadyBoundSameType)
                    {
                        return Ok(_err(payerType == "alipay" ? "alipay_conflict" : "wechat_conflict",
                            "该手机号已绑其他账户，请换号或换" + (payerType == "alipay" ? "支付宝" : "微信")));
                    }
                    // 把当前 openid/payerid 链到该会员
                    await _addMsa(phoneOwner.id, scannerId, msaType);
                    finalMemberId = phoneOwner.id;
                }
                else
                {
                    // 全新顾客 → 注册新会员 + 两条 MSA
                    finalMemberId = await _createNewMember(phone, scannerId, msaType);
                }
            }
            else
            {
                // 扫码方已有会员
                if (string.IsNullOrEmpty(scannerMember.cell))
                {
                    if (phoneOwner == null)
                    {
                        // 手机号未被认证 → 绑到 scannerMember
                        await _memberHelper.BindMemberMainCellNum(scannerMember.id, phone, "支付前身份验证", null);
                        finalMemberId = scannerMember.id;
                    }
                    else if (phoneOwner.id == scannerMember.id)
                    {
                        // 手机号已绑同一会员（理论上 scannerMember.cell 应非空，这里兜底）
                        finalMemberId = scannerMember.id;
                    }
                    else
                    {
                        // 手机号已绑另一会员 — PRD 1.4.1 冲突规则：换号或换通道
                        bool alreadyBoundSameType = phoneOwner.memberSocialAccounts != null
                            && phoneOwner.memberSocialAccounts.Any(m => m.valid == 1 && m.type.Trim().Equals(msaType));
                        if (alreadyBoundSameType)
                        {
                            return Ok(_err(payerType == "alipay" ? "alipay_conflict" : "wechat_conflict",
                                "该手机号已绑其他账户，请换号或换" + (payerType == "alipay" ? "支付宝" : "微信")));
                        }
                        // 把当前 openid/payerid 链到该会员
                        await _addMsa(phoneOwner.id, scannerId, msaType);
                        finalMemberId = phoneOwner.id;
                    }
                }
                else
                {
                    // scannerMember 已有 cell → 走原 scanner
                    finalMemberId = scannerMember.id;
                }
            }

            // 完成绑定后重新评估 status；此时 scanner 应已有 cell
            var refreshed = await _resolveStatus(body.paymentId, payerType, scannerId, sessionKey);
            return Ok(new ApiResult<CheckPayerIdentityResult>
            {
                code = refreshed.status == "error" ? 1 : 0,
                message = refreshed.errorMessage ?? "",
                data = refreshed
            });
        }

        private async Task<ActionResult<ApiResult<CheckPayerIdentityResult>>> _applyChoice(
            ConfirmPayIdentityBody body, string sessionKey, string payerType, string scannerId)
        {
            var pre = await _resolveStatus(body.paymentId, payerType, scannerId, sessionKey);
            if (pre.status == "error")
            {
                return Ok(new ApiResult<CheckPayerIdentityResult> { code = 1, message = pre.errorMessage ?? "", data = pre });
            }
            if (pre.scannerMemberId == null)
            {
                return Ok(_err("scanner_not_registered", "扫码方尚未注册会员，请先验证手机号"));
            }
            if (pre.status != "choose_identity")
            {
                return Ok(_err("unexpected_state", "当前状态非 choose_identity: " + pre.status));
            }

            string choice = (body.choice ?? "").Trim().ToLower();
            if (choice != "self" && choice != "proxy")
            {
                return Ok(_err("invalid_choice", "choice 必须是 self 或 proxy"));
            }

            var order = await _db.order.Where(o => o.id == pre.orderId).FirstOrDefaultAsync();
            var op = await _db.orderPayment.Where(p => p.id == body.paymentId).FirstOrDefaultAsync();
            if (order == null || op == null)
            {
                return Ok(_err("order_not_found", "订单或支付记录消失"));
            }

            int scannerMemberId = (int)pre.scannerMemberId;
            // 决策时机迁回 notify：此处只在 OrderPayment 上写付款方意图，
            // Order.member_id / wechat_unverified 由 DealSuccessPaidOrder 在支付成功回调时同步
            if (choice == "self")
            {
                op.member_id = scannerMemberId;
                op.is_proxy_pay = false;
            }
            else // proxy
            {
                op.member_id = scannerMemberId;
                op.is_proxy_pay = true;
            }
            op.update_date = DateTime.Now;
            _db.orderPayment.Entry(op).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            // 本次点击的响应强制 direct，让前端 onIdentityRefreshed 自动调起支付。
            // 不污染 _resolveStatus：用户若取消支付后刷新，CheckPayerIdentity 仍会按 order.member_id 重新决策，可重选。
            var refreshed = await _resolveStatus(body.paymentId, payerType, scannerId, sessionKey);
            refreshed.status = "direct";
            return Ok(new ApiResult<CheckPayerIdentityResult> { code = 0, message = "", data = refreshed });
        }

        private async Task<ActionResult<ApiResult<CheckPayerIdentityResult>>> _applyConfirmDirect(
            ConfirmPayIdentityBody body, string sessionKey, string payerType, string scannerId)
        {
            var pre = await _resolveStatus(body.paymentId, payerType, scannerId, sessionKey);
            if (pre.status == "error")
            {
                return Ok(new ApiResult<CheckPayerIdentityResult> { code = 1, message = pre.errorMessage ?? "", data = pre });
            }
            if (pre.scannerMemberId == null)
            {
                return Ok(_err("scanner_not_registered", "扫码方尚未注册会员，请先验证手机号"));
            }
            if (pre.status != "direct" && pre.status != "direct_to_scanner")
            {
                return Ok(_err("unexpected_state", "当前状态非 direct/direct_to_scanner: " + pre.status));
            }

            var order = await _db.order.Where(o => o.id == pre.orderId).FirstOrDefaultAsync();
            var op = await _db.orderPayment.Where(p => p.id == body.paymentId).FirstOrDefaultAsync();
            if (order == null || op == null)
            {
                return Ok(_err("order_not_found", "订单或支付记录消失"));
            }

            int scannerMemberId = (int)pre.scannerMemberId;
            // 决策时机迁回 notify：此处只在 OrderPayment 上写付款方意图，
            // Order.member_id / wechat_unverified 由 DealSuccessPaidOrder 在支付成功回调时同步
            op.member_id = scannerMemberId;
            op.is_proxy_pay = false;
            op.update_date = DateTime.Now;
            _db.orderPayment.Entry(op).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            // 同 _applyChoice：本次点击响应强制 direct，刷新后 _resolveStatus 仍可重新决策
            var refreshed = await _resolveStatus(body.paymentId, payerType, scannerId, sessionKey);
            refreshed.status = "direct";
            return Ok(new ApiResult<CheckPayerIdentityResult> { code = 0, message = "", data = refreshed });
        }

        // ====== Helpers ======

        private string _extractPhone(ConfirmPayIdentityBody body, string sessionKey, string payerType)
        {
            if (payerType == "wechat")
            {
                if (string.IsNullOrEmpty(body.encData) || string.IsNullOrEmpty(body.iv))
                {
                    throw new Exception("encData / iv 缺失");
                }
                string sk = Util.UrlDecode(sessionKey ?? "");
                string enc = Util.UrlDecode(body.encData);
                string iv = Util.UrlDecode(body.iv);
                string json = Util.AES_decrypt(enc.Trim(), sk, iv);
                JToken jsonObj = (JToken)JsonConvert.DeserializeObject(json);
                if (jsonObj == null || jsonObj["phoneNumber"] == null)
                {
                    throw new Exception("解密结果中无 phoneNumber");
                }
                return jsonObj["phoneNumber"].ToString().Trim();
            }
            if (payerType == "alipay")
            {
                // TODO: 切换到支付宝小程序后接 alipay.system.oauth.token + alipay.user.info.share
                if (!string.IsNullOrEmpty(body.phoneMock))
                {
                    return body.phoneMock.Trim();
                }
                throw new NotSupportedException("支付宝手机号解密待支付宝小程序对接（可传 phoneMock 字段做开发期 mock）");
            }
            throw new Exception("不支持的支付通道: " + payerType);
        }

        private async Task _addMsa(int memberId, string num, string type)
        {
            var existing = await _db.memberSocialAccount
                .Where(m => m.member_id == memberId && m.type.Trim().Equals(type) && m.num.Trim().Equals(num.Trim()))
                .FirstOrDefaultAsync();
            if (existing != null)
            {
                if (existing.valid != 1)
                {
                    existing.valid = 1;
                    existing.update_date = DateTime.Now;
                    _db.memberSocialAccount.Entry(existing).State = EntityState.Modified;
                    await _db.SaveChangesAsync();
                }
                return;
            }
            var msa = new MemberSocialAccount
            {
                id = 0,
                member_id = memberId,
                type = type,
                num = num.Trim(),
                valid = 1
            };
            await _db.memberSocialAccount.AddAsync(msa);
            await _db.SaveChangesAsync();
        }

        private async Task<int> _createNewMember(string phone, string scannerId, string msaType)
        {
            var member = new Member
            {
                real_name = "",
                gender = "",
                source = "支付前身份验证"
            };
            await _db.member.AddAsync(member);
            await _db.SaveChangesAsync();

            await _addMsa(member.id, phone, MemberSocialAccount.TYPE_CELL);
            await _addMsa(member.id, scannerId, msaType);
            return member.id;
        }

        private string _msaTypeForPayer(string payerType)
        {
            if (payerType == "wechat") return MemberSocialAccount.TYPE_WECHAT_MINI_OPENID;
            if (payerType == "alipay") return MemberSocialAccount.TYPE_ALIPAY_PAYERID;
            return null;
        }

        private string _maskCell(string cell)
        {
            if (string.IsNullOrEmpty(cell)) return null;
            cell = cell.Trim();
            if (cell.Length != 11) return cell;
            return cell.Substring(0, 3) + "****" + cell.Substring(7);
        }

        private ApiResult<CheckPayerIdentityResult> _err(string errorCode, string message)
        {
            return new ApiResult<CheckPayerIdentityResult>
            {
                code = 1,
                message = message,
                data = new CheckPayerIdentityResult
                {
                    status = "error",
                    errorCode = errorCode,
                    errorMessage = message
                }
            };
        }
    }
}
