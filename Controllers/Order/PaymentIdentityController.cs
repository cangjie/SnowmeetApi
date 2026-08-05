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

        // 支付宝小程序 appId（与商户 appId 2021004143665722 区分）。
        // 用于 _extractPhone alipay 分支的 oauth.token + user.phone.get，证书放 AlipayCertificate/{appId}/
        public const string ALIPAY_MINI_APP_ID = "2021006157624571";

        public PaymentIdentityController(ApplicationDBContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
            _memberHelper = new MemberController(db, config);
        }

        // 读 alipay 小程序「接口加密方式」配置的 AES 密钥（base64，16/24/32 字节）。
        // 部署时把密钥写入 AlipayCertificate/{appId}/aes_key.txt（与 private_key_{appId}.txt 同目录）。
        // my.getPhoneNumber 返回的 response 字段就用这个密钥 AES-128-CBC + PKCS7 加密。
        private string _loadAlipayAesKey()
        {
            string keyPath = Util.workingPath + "/AlipayCertificate/" + ALIPAY_MINI_APP_ID + "/aes_key.txt";
            if (!System.IO.File.Exists(keyPath))
            {
                throw new Exception("支付宝 AES 密钥文件不存在：" + keyPath + "（在开放平台「接口加密方式」生成 AES 密钥后放此处）");
            }
            return System.IO.File.OpenText(keyPath).ReadToEnd().Trim();
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
            public string debugInfo { get; set; }
        }

        public class ConfirmPayIdentityBody
        {
            public int paymentId { get; set; }
            public string payerType { get; set; }   // wechat | alipay
            public string scannerId { get; set; }   // openid (wechat) | payerid (alipay)
            public string action { get; set; }      // submit_phone | choose | confirm_direct
            public string choice { get; set; }      // self | proxy（仅 action=choose）
            // wechat + submit_phone：encData(微信加密) + iv
            // alipay + submit_phone：encData = my.getPhoneNumber 返回的 response（AES-128-CBC + 全 0 IV 加密的 JSON），iv 不用
            public string encData { get; set; }
            public string iv { get; set; }
            public string phoneMock { get; set; }   // 开发期 fallback：跳过真授权流程直接传明文手机号
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

        // 顾客扫码进小程序、MemberLogin 后调用：纯身份核验（不涉及任何支付）。
        // 扫码人 == 订单会员 → order.wechat_unverified = true 持久化（注意：1 = 已核验为本人，命名与字面相反）。
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> VerifyWechatIdentity(int orderId, string sessionKey)
        {
            var (sessOpenid, sessUnionid, sess) = await _loadSessionContext(sessionKey);
            int? scannerMemberId = (sess != null) ? sess.member_id : null;
            if (scannerMemberId == null && sess != null && !string.IsNullOrEmpty(sess.wechat_openid))
            {
                var scannerByOpenId = await _memberHelper.GetWholeMemberByNum(sess.wechat_openid, MemberSocialAccount.TYPE_WECHAT_MINI_OPENID);
                if (scannerByOpenId != null)
                {
                    scannerMemberId = scannerByOpenId.id;
                    sess.member_id = scannerMemberId;
                    _db.miniSession.Entry(sess).State = EntityState.Modified;
                    await _db.SaveChangesAsync();
                }
            }
            if (scannerMemberId == null && sess != null && !string.IsNullOrEmpty(sess.cell))
            {
                var scannerByCell = await _memberHelper.GetWholeMemberByNum(sess.cell, MemberSocialAccount.TYPE_CELL);
                if (scannerByCell != null)
                {
                    scannerMemberId = scannerByCell.id;
                    sess.member_id = scannerMemberId;
                    _db.miniSession.Entry(sess).State = EntityState.Modified;
                    await _db.SaveChangesAsync();
                }
            }
            if (scannerMemberId == null)
            {
                return Ok(new ApiResult<object> { code = 1, message = "未识别扫码人身份，请重新登录", data = new { matched = false } });
            }
            var order = await _db.order.Where(o => o.id == orderId && o.valid == 1).FirstOrDefaultAsync();
            if (order == null)
            {
                return Ok(new ApiResult<object> { code = 1, message = "订单不存在", data = new { matched = false } });
            }
            if (order.member_id == null)
            {
                return Ok(new ApiResult<object> { code = 1, message = "订单无会员，无法核验", data = new { matched = false } });
            }
            bool matched = (order.member_id == scannerMemberId);
            if (matched)
            {
                if (!order.wechat_unverified)
                {
                    order.wechat_unverified = true;
                    // 全局 NoTracking：必须显式 State=Modified 否则不持久化
                    _db.order.Entry(order).State = EntityState.Modified;
                    CoreDataModLog log = CoreDataModLog.CreateManualLog("Order", "wechat_unverified", order.id,
                        "微信身份核验", scannerMemberId, null, "0", "1", "顾客扫码核验本人，置 wechat_unverified=1");
                    await _db.coreDataModLog.AddAsync(log);
                    await _db.SaveChangesAsync();
                }
                return Ok(new ApiResult<object> { code = 0, message = "", data = new { matched = true } });
            }
            string masked = null;
            var orderMember = await _memberHelper.GetWholeMemberById((int)order.member_id);
            if (orderMember != null) masked = _maskCell(orderMember.cell);
            return Ok(new ApiResult<object> { code = 0, message = "", data = new { matched = false, orderMemberMaskedCell = masked } });
        }

        // 店员端轮询：该订单是否已通过微信身份核验（wechat_unverified == true 即已核验本人）。
        [HttpGet]
        public async Task<ActionResult<ApiResult<object>>> GetWechatVerifyStatus(int orderId, string sessionKey,
            string sessionType = "wechat_mini_openid")
        {
            Staff staff = await Util.GetStaffBySessionKey(_db, sessionKey, sessionType);
            if (staff == null)
            {
                return Ok(new ApiResult<object> { code = 1, message = "没有权限", data = new { verified = false } });
            }
            var order = await _db.order.Where(o => o.id == orderId).AsNoTracking().FirstOrDefaultAsync();
            bool verified = (order != null && order.wechat_unverified);
            return Ok(new ApiResult<object> { code = 0, message = "", data = new { verified = verified } });
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

            // 养护微信支付：必须订单会员本人（当前会员绑定的微信）扫码支付，非本人拦截。
            // 散客养护单（无会员）不受此约束；仅养护 + 微信通道生效，不影响租赁/零售/雪票及支付宝。
            if (order.type != null && order.type.Trim() == "养护"
                && payerType == "wechat"
                && order.member_id != null
                && result.scannerMemberId != order.member_id)
            {
                result.status = "care_member_required";
                return result;
            }

            // 4) Status
            // 仅基于 order.member_id (订单归属) + scannerMemberId 决策。
            // op.member_id 是「付款方意图」,只在 _applyChoice/_applyConfirmDirect 当次返回时强制 direct,
            // 不在 _resolveStatus 里参与判断 —— 否则用户点错后刷新就无法重新选择。
            // 注意: scannerHasCell == false 不再硬阻断 (改为前端在「敬请支付」按钮上软提示弹窗,
            // 顾客可选择「授权手机号」或「跳过,直接支付」)。本字段仍写入响应供前端判定。
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
            string encMeta = payerType == "alipay" ? _summarizeAlipayEncData(body?.encData) : "";
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
                // 支付宝手机号授权是「软依赖」：解密失败不应阻断后续 confirm_direct/choose。
                // 前端 submit_phone 成功后才会继续第二个 action；这里返回 code=0 让流程能继续。
                if (payerType == "alipay")
                {
                    Console.WriteLine($"[_submitPhone:alipay] soft-fail phone decrypt, fallback to no-phone flow. appId={ALIPAY_MINI_APP_ID}, scannerId={(scannerId ?? "").Trim()}, encMeta={encMeta}, ex={ex.Message}");
                    var fallback = await _resolveStatus(body.paymentId, payerType, scannerId, sessionKey);
                    if (fallback.status == "error")
                    {
                        return Ok(_err("phone_decrypt_failed", "手机号解析失败: " + ex.Message));
                    }
                    // 软失败降噪：手机号授权失败不再污染前端状态字段，避免误判为流程阻断。
                    fallback.errorCode = null;
                    fallback.errorMessage = null;
                    fallback.debugInfo = $"phone_soft_fail|encMeta={encMeta}|ex={ex.Message}";
                    return Ok(new ApiResult<CheckPayerIdentityResult>
                    {
                        code = 0,
                        message = "",
                        data = fallback
                    });
                }
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

            // 2026-05-29: 从 mini_session 反查本次扫码方的 openid+unionid。
            // MemberLogin 不再自动建 stub 后,前端 globalData.member 可能为 null →
            // 前端传的 scannerId 可能是空字符串,得用 session 里的 wechat_openid 兜底。
            var (sessOpenid, sessUnionid, sess) = await _loadSessionContext(sessionKey);
            if (string.IsNullOrEmpty(scannerId) && !string.IsNullOrEmpty(sessOpenid))
            {
                scannerId = sessOpenid;
            }

            // 只要支付宝手机号解密成功,就把手机号写回 mini_session.cell,
            // 便于后续链路校验解密结果与支付成功回填时复用。
            if (payerType == "alipay" && sess != null)
            {
                bool needUpdateCell = string.IsNullOrEmpty(sess.cell) || !sess.cell.Trim().Equals(phone);
                if (needUpdateCell)
                {
                    sess.cell = phone;
                    sess.expire_date = DateTime.Now.AddHours(2);
                    _db.miniSession.Entry(sess).State = EntityState.Modified;
                    await _db.SaveChangesAsync();
                }
            }

            // 已绑同手机号的会员（不限当前 scanner）
            var phoneOwner = await _memberHelper.GetWholeMemberByNum(phone, MemberSocialAccount.TYPE_CELL);
            // 当前 scanner 已绑的会员（按 openid/payerid 找）
            var scannerMember = string.IsNullOrEmpty(scannerId)
                ? null
                : await _memberHelper.GetWholeMemberByNum(scannerId, msaType);

            int finalMemberId;

            // helper local: 若 owner 没有当前 unionid MSA 则补一条(unionid 是微信跨小程序/跨设备的稳定标识,
            // 长期价值高,本次建会员/绑会员都顺手补全)
            async Task EnsureUnionIdMsa(int ownerMemberId, Member ownerMember)
            {
                if (payerType != "wechat" || string.IsNullOrEmpty(sessUnionid)) return;
                bool hasUnionid = ownerMember != null && ownerMember.memberSocialAccounts != null
                    && ownerMember.memberSocialAccounts.Any(m => m.valid == 1
                        && m.type.Trim().Equals(MemberSocialAccount.TYPE_WECHAT_UNIONID)
                        && m.num.Trim().Equals(sessUnionid));
                if (!hasUnionid)
                {
                    await _addMsa(ownerMemberId, sessUnionid, MemberSocialAccount.TYPE_WECHAT_UNIONID);
                }
            }

            if (scannerMember == null)
            {
                // 扫码方未绑会员(MemberLogin 不再建 stub 后,新流程下散客都会走这里)
                if (phoneOwner != null)
                {
                    // 手机号已被另一会员认证 → 把当前 openid+unionid 链到 phoneOwner
                    // 2026-05-29: 删除 alreadyBoundSameType 拒绝逻辑 — 一人多设备共享会员是合理的
                    // (微信 getPhoneNumber 拿到的 cell 已被系统认证,user 能授权说明掌握该手机号)
                    await _addMsa(phoneOwner.id, scannerId, msaType);
                    await EnsureUnionIdMsa(phoneOwner.id, phoneOwner);
                    finalMemberId = phoneOwner.id;
                }
                else if (payerType == "alipay")
                {
                    // 2026-06-03 用户原则: 支付宝路径不在 PaymentIdentity 里建新会员,
                    // 会员推迟到支付宝 notify(AliController.CallBack)收到支付成功后再兜底建。
                    // 手机号已在上游统一回写到 mini_session.cell；这里继续保持 OP.member_id 不动。
                    var refreshedAli = await _resolveStatus(body.paymentId, payerType, scannerId, sessionKey);
                    return Ok(new ApiResult<CheckPayerIdentityResult>
                    {
                        code = refreshedAli.status == "error" ? 1 : 0,
                        message = refreshedAli.errorMessage ?? "",
                        data = refreshedAli
                    });
                }
                else
                {
                    // wechat 全新顾客 → 注册新会员 + cell + openid + (unionid)MSA (维持原行为)
                    finalMemberId = await _createNewMember(phone, scannerId, msaType, sessUnionid);
                }
            }
            else
            {
                // 扫码方已绑会员(过渡期场景:历史脏数据 stub 仍存在,或 user 早期注册过的真实会员)
                if (string.IsNullOrEmpty(scannerMember.cell))
                {
                    if (phoneOwner == null)
                    {
                        // 手机号未被绑过 → 绑给 scannerMember(补 cell)
                        await _memberHelper.BindMemberMainCellNum(scannerMember.id, phone, "支付前身份验证", null);
                        await EnsureUnionIdMsa(scannerMember.id, scannerMember);
                        finalMemberId = scannerMember.id;
                    }
                    else if (phoneOwner.id == scannerMember.id)
                    {
                        // 手机号已绑同一会员(理论上 scannerMember.cell 应非空,这里兜底)
                        finalMemberId = scannerMember.id;
                    }
                    else
                    {
                        // 手机号已绑别人 + scanner 当前 openid 已经关联了自己的 member →
                        // 2026-05-29 修: 优先用 scanner 本身的 member, 不动 phoneOwner, 不失效 scanner 的 openid/unionid MSA
                        //   (之前会把 scanner 的 openid/unionid MSA invalidate 然后迁移到 phoneOwner — 这个行为是错的:
                        //    user 反馈"应该是第二次刷新后就可以拿到会员 ID 了, 用这个会员 ID 支付呀")
                        // cell 仍归 phoneOwner, scanner 这次不绑 cell — 下次 user 进来用 scanner.id 直接支付即可
                        finalMemberId = scannerMember.id;
                    }
                }
                else
                {
                    // scannerMember 已有 cell → 走原 scanner(已是完整会员)
                    finalMemberId = scannerMember.id;
                }
            }

            // 把 mini_session 的 member_id 指向 finalMemberId,这样下次 MemberLogin 之前的 session 反查可以直接拿到
            if (sess != null && sess.member_id != finalMemberId)
            {
                sess.member_id = finalMemberId;
                _db.miniSession.Entry(sess).State = EntityState.Modified;
                await _db.SaveChangesAsync();
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
                if (payerType == "alipay")
                {
                    // 2026-06-03 用户原则: alipay 路径不在 PaymentIdentity 建会员,
                    // member 推迟到 AliController.CallBack 收到支付成功后兜底建。
                    // 此处 OP.member_id 留 null,仅写 is_proxy_pay 意图。下方逻辑统一处理。
                }
                else
                {
                    // wechat: 维持原行为, 游客拒绝授权时建无 cell 会员
                    var (sessOpenidAuto, sessUnionidAuto, sessAuto) = await _loadSessionContext(sessionKey);
                    if (sessAuto == null || string.IsNullOrEmpty(sessOpenidAuto))
                    {
                        return Ok(_err("session_not_found", "登录失效,请退出小程序重进"));
                    }
                    string msaTypeAuto = _msaTypeForPayer(payerType);
                    if (msaTypeAuto == null)
                    {
                        return Ok(_err("unsupported_payer_type", "不支持的支付通道: " + payerType));
                    }
                    var newMemberId = await _createNewMember(null, sessOpenidAuto, msaTypeAuto, sessUnionidAuto);
                    sessAuto.member_id = newMemberId;
                    _db.miniSession.Entry(sessAuto).State = EntityState.Modified;
                    await _db.SaveChangesAsync();
                    pre.scannerMemberId = newMemberId;
                    pre.scannerHasCell = false;
                    if (string.IsNullOrEmpty(scannerId)) scannerId = sessOpenidAuto;
                }
            }
            // 正常情况：choose_identity（订单归别人，二选一）。
            // 但扫码方在点选前刚授权手机号、被识别成订单本人时，状态会合法地翻成 direct
            // （扫码方 == 订单会员，"代付"已无意义）。此时不能报 unexpected_state 把人卡死，
            // 按直付处理即可，让前端照常发起支付。
            bool nowDirect = pre.status == "direct" || pre.status == "direct_to_scanner";
            if (pre.status != "choose_identity" && !nowDirect)
            {
                return Ok(_err("unexpected_state", "当前状态非 choose_identity: " + pre.status));
            }

            string choice = (body.choice ?? "").Trim().ToLower();
            if (choice != "self" && choice != "proxy")
            {
                return Ok(_err("invalid_choice", "choice 必须是 self 或 proxy"));
            }
            // 扫码方就是订单本人 → 代付无意义，强制按直付(self)落库，不写 is_proxy_pay / cell
            if (nowDirect)
            {
                choice = "self";
            }

            var order = await _db.order.Where(o => o.id == pre.orderId).FirstOrDefaultAsync();
            var op = await _db.orderPayment.Where(p => p.id == body.paymentId).FirstOrDefaultAsync();
            if (order == null || op == null)
            {
                return Ok(_err("order_not_found", "订单或支付记录消失"));
            }

            // 决策时机迁回 notify：此处只在 OrderPayment 上写付款方意图,
            // Order.member_id / wechat_unverified 由 DealSuccessPaidOrder 在支付成功回调时同步。
            // alipay 路径 scannerMemberId 可能仍为 null (用户原则: 推迟到支付成功后建会员) → OP.member_id 也留 null
            int? scannerMemberId = pre.scannerMemberId;
            op.member_id = scannerMemberId;
            op.is_proxy_pay = (choice == "proxy");
            // 落库扫码方第三方 openid（此前只写 member_id，openid 已解析却漏写表）
            await _persistPayerOpenId(op, payerType, scannerId, sessionKey);
            // 代付(is_proxy_pay=1)：把代付人手机号落到 order_payment.cell（软提示可跳过 → 拿不到就留空，不阻断）
            if (choice == "proxy")
            {
                string proxyCell = await _resolveProxyPayerCell(scannerMemberId, sessionKey);
                if (!string.IsNullOrEmpty(proxyCell)) op.cell = proxyCell;
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
                if (payerType == "alipay")
                {
                    // 2026-06-03 用户原则: alipay 路径不在 PaymentIdentity 建会员,
                    // member 推迟到 AliController.CallBack 收到支付成功后兜底建。
                    // OP.member_id 留 null, is_proxy_pay=false (确认直付意图)。
                }
                else
                {
                    // wechat: 维持原行为, 散客拒绝授权手机号要继续支付 → 反查 openid+unionid 自动建无 cell 会员
                    var (sessOpenidAuto, sessUnionidAuto, sessAuto) = await _loadSessionContext(sessionKey);
                    if (sessAuto == null || string.IsNullOrEmpty(sessOpenidAuto))
                    {
                        return Ok(_err("session_not_found", "登录失效,请退出小程序重进"));
                    }
                    string msaTypeAuto = _msaTypeForPayer(payerType);
                    if (msaTypeAuto == null)
                    {
                        return Ok(_err("unsupported_payer_type", "不支持的支付通道: " + payerType));
                    }
                    var newMemberId = await _createNewMember(null, sessOpenidAuto, msaTypeAuto, sessUnionidAuto);
                    sessAuto.member_id = newMemberId;
                    _db.miniSession.Entry(sessAuto).State = EntityState.Modified;
                    await _db.SaveChangesAsync();
                    pre.scannerMemberId = newMemberId;
                    pre.scannerHasCell = false;
                    if (string.IsNullOrEmpty(scannerId)) scannerId = sessOpenidAuto;
                }
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

            // 决策时机迁回 notify: 此处只在 OrderPayment 上写付款方意图,
            // Order.member_id / wechat_unverified 由 DealSuccessPaidOrder 在支付成功回调时同步。
            // alipay 路径 scannerMemberId 可能仍为 null (用户原则: 推迟到支付成功后建会员) → OP.member_id 也留 null
            int? scannerMemberId = pre.scannerMemberId;
            op.member_id = scannerMemberId;
            op.is_proxy_pay = false;
            // 落库扫码方第三方 openid（此前只写 member_id，openid 已解析却漏写表）
            await _persistPayerOpenId(op, payerType, scannerId, sessionKey);
            op.update_date = DateTime.Now;
            _db.orderPayment.Entry(op).State = EntityState.Modified;
            await _db.SaveChangesAsync();

            // 同 _applyChoice：本次点击响应强制 direct，刷新后 _resolveStatus 仍可重新决策
            var refreshed = await _resolveStatus(body.paymentId, payerType, scannerId, sessionKey);
            refreshed.status = "direct";
            return Ok(new ApiResult<CheckPayerIdentityResult> { code = 0, message = "", data = refreshed });
        }

        // ====== Helpers ======

        // 把扫码方（付款方）的第三方 openid 落到 order_payment：微信→open_id，支付宝→ali_buyer_id。
        // scannerId 为空时用 sessionKey 反查 mini_session 兜底（与建会员分支同源）。
        // 仅赋值，由调用方统一 SaveChanges。修复：身份确认只写 member_id、openid 漏写表的问题，
        // 保证每笔微信/支付宝 order_payment 自身份确认起就带 openid（不必等顾客真正发起支付才补）。
        private async Task _persistPayerOpenId(OrderPayment op, string payerType, string scannerId, string sessionKey)
        {
            string openId = (scannerId ?? "").Trim();
            if (string.IsNullOrEmpty(openId))
            {
                var (sessOpenid, _, _) = await _loadSessionContext(sessionKey);
                openId = (sessOpenid ?? "").Trim();
            }
            if (string.IsNullOrEmpty(openId))
            {
                return;
            }
            if (payerType == "alipay")
            {
                op.ali_buyer_id = openId;
            }
            else
            {
                op.open_id = openId;
            }
        }

        // 解析代付人(扫码付款方)手机号：微信取会员档案 cell；支付宝会员推迟到 notify 创建，取 mini_session.cell 兜底。
        // 仅返回手机号，由调用方决定是否写入 op.cell（与 _persistPayerOpenId 同风格）。
        private async Task<string> _resolveProxyPayerCell(int? scannerMemberId, string sessionKey)
        {
            if (scannerMemberId != null)
            {
                var m = await _memberHelper.GetWholeMemberById((int)scannerMemberId);
                if (m != null && !string.IsNullOrEmpty(m.cell)) return m.cell.Trim();
            }
            var (_, _, sess) = await _loadSessionContext(sessionKey);
            if (sess != null && !string.IsNullOrEmpty(sess.cell)) return sess.cell.Trim();
            return null;
        }

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
                // 2026-06-03: AES 解密路径 + JSON 解包 + base64 清洗 + key BOM/CRLF 清洗
                // 全部迁到 SnowmeetApi.Helpers.AlipayPhoneDecryptHelper 复用 (本 controller 与 MemberLogin 同源)
                if (!string.IsNullOrEmpty(body.encData))
                {
                    // 2026-06-03: AES 解密 + JSON 解包 + base64 清洗 + key BOM/CRLF 清洗 + 多路径 mobile 查找
                    // 全部内化在 SnowmeetApi.Helpers.AlipayPhoneDecryptHelper (与 MemberLogin 二次调用同源)
                    return SnowmeetApi.Helpers.AlipayPhoneDecryptHelper.Decrypt(body.encData, ALIPAY_MINI_APP_ID);
                }
                // 开发期 fallback：跑后端单测时不想真过支付宝授权流程，传 phoneMock 即可
                if (!string.IsNullOrEmpty(body.phoneMock))
                {
                    return body.phoneMock.Trim();
                }
                throw new NotSupportedException("支付宝手机号解密缺 encData（my.getPhoneNumber 的 response 字段）或 phoneMock（开发期 fallback）");
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

        // 2026-05-29: phone 可空(拒绝授权场景);unionId 可空(无微信 unionid 时)
        // MemberLogin 重构后此函数成为**唯一**建会员入口,scannerId+unionId 都从 mini_session 反查
        private async Task<int> _createNewMember(string? phone, string scannerId, string msaType, string? unionId = null)
        {
            var member = new Member
            {
                real_name = "",
                gender = "",
                source = "支付前身份验证",
                valid = 1   // 显式设值,不依赖 model 默认(某些 EF/DB schema 组合下默认值落库为 0)
            };
            await _db.member.AddAsync(member);
            await _db.SaveChangesAsync();

            if (!string.IsNullOrEmpty(phone))
            {
                await _addMsa(member.id, phone, MemberSocialAccount.TYPE_CELL);
            }
            if (!string.IsNullOrEmpty(scannerId))
            {
                await _addMsa(member.id, scannerId, msaType);
            }
            if (!string.IsNullOrEmpty(unionId))
            {
                await _addMsa(member.id, unionId, MemberSocialAccount.TYPE_WECHAT_UNIONID);
            }
            return member.id;
        }

        // 用 sessionKey 反查 mini_session,取出本次扫码方的 openid+unionid。
        // 2026-05-29 新加: MemberLogin 不再建 stub,未注册 user 的 openid+unionid 暂存在 mini_session,
        // PaymentIdentity 流程需要这俩字段建会员(_submitPhone 散客分支 / _applyConfirmDirect 散客分支)。
        // 2026-06-03: 支付宝 session 的 payerId 落到独立列 alipay_payerid(替代之前往 wechat_openid 列塞 hack)。
        // 返回 openid 字段按 session_type 分流:
        //   - session_type='alipay_payerid' → alipay_payerid 列(为空时 fallback wechat_openid 兼容历史 session)
        //   - 否则 → wechat_openid 列
        // unionid 字段仅 wechat 路径有意义, alipay 路径恒返 null。
        private async Task<(string? openid, string? unionid, MiniSession? session)> _loadSessionContext(string sessionKey)
        {
            if (string.IsNullOrEmpty(sessionKey)) return (null, null, null);
            var sk = Util.UrlDecode(sessionKey).Trim();
            var sess = await _db.miniSession
                .Where(s => s.session_key.Trim().Equals(sk)
                            && s.valid == 1
                            && s.expire_date >= DateTime.Now)
                .OrderByDescending(s => s.expire_date)
                .FirstOrDefaultAsync();
            if (sess == null) return (null, null, null);
            string sessTypeNorm = (sess.session_type ?? "").Trim();
            if (sessTypeNorm.Equals("alipay_payerid"))
            {
                string? alipayOpenid = !string.IsNullOrEmpty(sess.alipay_payerid) ? sess.alipay_payerid : sess.wechat_openid;
                return (alipayOpenid, null, sess);
            }
            return (sess.wechat_openid, sess.wechat_unionid, sess);
        }

        // 把 member 上指定 num+type 的 valid=1 MSA 全部 valid=0。
        // 用于 _submitPhone 「stub→phoneOwner 迁移」分支:scanner stub 上同 num+type MSA 失效,
        // 避免下次 MemberLogin(若有)再查回 stub。
        private async Task _invalidateMsa(int memberId, string num, string type)
        {
            if (string.IsNullOrEmpty(num)) return;
            var list = await _db.memberSocialAccount
                .Where(m => m.member_id == memberId
                            && m.num.Trim().Equals(num.Trim())
                            && m.type.Trim().Equals(type)
                            && m.valid == 1)
                .ToListAsync();
            foreach (var m in list)
            {
                m.valid = 0;
                m.update_date = DateTime.Now;
                _db.memberSocialAccount.Entry(m).State = EntityState.Modified;
            }
            if (list.Count > 0)
            {
                await _db.SaveChangesAsync();
            }
        }

        private string _msaTypeForPayer(string payerType)
        {
            if (payerType == "wechat") return MemberSocialAccount.TYPE_WECHAT_MINI_OPENID;
            if (payerType == "alipay") return MemberSocialAccount.TYPE_ALIPAY_PAYERID;
            return null;
        }

        private string _summarizeAlipayEncData(string rawEncData)
        {
            if (string.IsNullOrEmpty(rawEncData))
            {
                return "empty";
            }

            string raw = rawEncData.Trim();
            string decoded = Util.UrlDecode(raw);
            string normalized = (decoded ?? "").Trim();
            bool startsJson = normalized.StartsWith("{");

            if (!startsJson)
            {
                return $"shape=base64_like,rawLen={raw.Length},decodedLen={normalized.Length}";
            }

            try
            {
                JToken obj = (JToken)JsonConvert.DeserializeObject(normalized);
                string signType = obj?["signType"]?.ToString() ?? "";
                string subCode = obj?["subCode"]?.ToString() ?? obj?["sub_code"]?.ToString() ?? "";
                bool hasResponse = obj?["response"] != null;
                bool hasCode = obj?["code"] != null;
                bool hasSign = obj?["sign"] != null;
                return $"shape=json_wrap,rawLen={raw.Length},decodedLen={normalized.Length},hasResponse={hasResponse},hasCode={hasCode},hasSign={hasSign},signType={signType},subCode={subCode}";
            }
            catch (Exception ex)
            {
                return $"shape=json_parse_failed,rawLen={raw.Length},decodedLen={normalized.Length},err={ex.GetType().Name}";
            }
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
