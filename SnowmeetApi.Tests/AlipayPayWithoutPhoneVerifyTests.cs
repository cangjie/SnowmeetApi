using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Controllers.Order;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// 支付宝付款：订单会员有绑定手机号时免验证直接支付，付款账号只记在付款记录上（2026-11-27）。
/// 设计：snowmeet_ai_doc/docs/superpowers/specs/2026-11-27-alipay-pay-without-phone-verify-design.md
/// </summary>
[Collection("working-path")]
public class AlipayPayWithoutPhoneVerifyTests
{
    private const string Code = "wl_LS_261127_00001";
    private const string FirstNo = Code + "_ZF_01";
    private const string SecondNo = Code + "_ZF_02";
    private const int Owner = CoachFixture.StudentA;   // 订单会员，有手机号
    private const int Other = CoachFixture.StudentB;   // 另一位会员（支付宝会话 ali-b 认得出）
    private const int NoCell = 20;                     // 没有手机号的会员
    private const string Closed = "订单已支付或已取消";
    private const string Unrelated = "订单和当前用户无关";

    // ====== 身份判断 ======

    [Fact]
    public async Task Check_AlipayUnknownBuyer_MemberHasCell_IsDirect()
    {
        await using var f = await AlipayFixture.Create();
        Assert.Equal("direct", (await f.Check("alipay", "ali-new", "open-new")).status);
    }

    [Fact]
    public async Task Check_AlipayOtherMemberBuyer_MemberHasCell_IsDirect()
    {
        await using var f = await AlipayFixture.Create();
        var r = await f.Check("alipay", "ali-b", "open-b");
        Assert.Equal("direct", r.status);
        Assert.Equal(Other, r.scannerMemberId);
    }

    [Fact]
    public async Task Check_MemberWithoutCell_StillAsks()
    {
        await using var f = await AlipayFixture.Create();
        await f.EditOrder(o => o.member_id = NoCell);
        Assert.Equal("choose_identity", (await f.Check("alipay", "ali-new", "open-new")).status);
    }

    [Fact]
    public async Task Check_CellVoidedOrBlank_StillAsks()
    {
        await using var f = await AlipayFixture.Create();
        await using (var db = f.Base.NewContext())
        {
            var cell = await db.memberSocialAccount.AsTracking().SingleAsync(m => m.member_id == Owner && m.type == "cell");
            cell.valid = 0;
            db.memberSocialAccount.Add(new MemberSocialAccount { member_id = Owner, type = "cell", num = "  ", valid = 1 });
            await db.SaveChangesAsync();
        }
        Assert.Equal("choose_identity", (await f.Check("alipay", "ali-new", "open-new")).status);
    }

    [Fact]
    public async Task Check_WalkInOrder_Unchanged()
    {
        await using var f = await AlipayFixture.Create();
        await f.EditOrder(o => o.member_id = null);
        Assert.Equal("direct_to_scanner", (await f.Check("alipay", "ali-new", "open-new")).status);
    }

    [Fact]
    public async Task Check_Wechat_Unchanged()
    {
        await using var f = await AlipayFixture.Create();
        Assert.Equal("choose_identity", (await f.Check("wechat", "session-new", "mini-new")).status);
    }

    // ====== 下单 ======

    [Fact]
    public async Task Pay_SameBuyerAgain_KeepsOutTradeNoAndTrade()
    {
        await using var f = await AlipayFixture.Create();
        await f.SetTrade("open-new");

        var r = await f.Pay("ali-new");

        Assert.Equal(0, r.code);
        Assert.Equal(FirstNo, r.data!.out_trade_no);
        Assert.Equal("T1", r.data.ali_trade_no);
        var p = await f.ReadPayment();
        Assert.Equal(FirstNo, p.out_trade_no);
        Assert.Equal("T1", p.ali_trade_no);
    }

    [Fact]
    public async Task Pay_AnotherBuyer_DropsOldTradeAndTakesNewNumber()
    {
        await using var f = await AlipayFixture.Create();
        await f.SetTrade("open-new");

        AssertStoppedAtTradeCreate(await f.Pay("ali-b"));

        var p = await f.ReadPayment();
        Assert.Null(p.ali_trade_no);
        Assert.Equal(SecondNo, p.out_trade_no);
        Assert.Equal("open-b", p.ali_buyer_id);
    }

    [Fact]
    public async Task Pay_OtherMemberBuyer_IsMarkedProxy()
    {
        await using var f = await AlipayFixture.Create();

        AssertStoppedAtTradeCreate(await f.Pay("ali-b"));

        var p = await f.ReadPayment();
        Assert.Equal(Other, p.member_id);
        Assert.True(p.is_proxy_pay);
        Assert.Equal("open-b", p.ali_buyer_id);
        Assert.Equal(Owner, (await f.ReadOrder()).member_id);
    }

    [Fact]
    public async Task Pay_UnknownBuyerAfterOtherMember_ClearsPayer()
    {
        await using var f = await AlipayFixture.Create();
        AssertStoppedAtTradeCreate(await f.Pay("ali-b"));

        AssertStoppedAtTradeCreate(await f.Pay("ali-new"));

        var p = await f.ReadPayment();
        Assert.Null(p.member_id);
        Assert.False(p.is_proxy_pay);
        Assert.Equal("open-new", p.ali_buyer_id);
        Assert.Equal(SecondNo, p.out_trade_no);   // 上一次没建出交易：换新单号再建
    }

    [Fact]
    public async Task Pay_AfterIdentityConfirmByAnotherBuyer_StillDropsOldTrade()
    {
        await using var f = await AlipayFixture.Create();
        await f.EditOrder(o => o.member_id = NoCell);
        await f.SetTrade("open-new");

        var confirm = await f.ChooseProxy("ali-b", "open-b");

        Assert.Equal(0, confirm.code);
        Assert.Equal("open-new", (await f.ReadPayment()).ali_buyer_id);   // 交易还是 open-new 的
        AssertStoppedAtTradeCreate(await f.Pay("ali-b"));
        var p = await f.ReadPayment();
        Assert.Null(p.ali_trade_no);
        Assert.Equal(SecondNo, p.out_trade_no);
        Assert.Equal("open-b", p.ali_buyer_id);
    }

    [Fact]
    public async Task Pay_PaidPayment_Rejected()
    {
        await using var f = await AlipayFixture.Create();
        await f.SetTrade("open-new");
        await f.EditPayment(p => p.status = "支付成功");
        await AssertRejectedUntouched(f);
    }

    [Fact]
    public async Task Pay_VoidedPayment_Rejected()
    {
        await using var f = await AlipayFixture.Create();
        await f.SetTrade("open-new");
        await f.EditPayment(p => p.valid = 0);
        await AssertRejectedUntouched(f);
    }

    [Fact]
    public async Task Pay_PresetWechatPayment_BecomesAlipay()
    {
        await using var f = await AlipayFixture.Create();
        // 雪票下单预建的付款记录：写着微信支付、带单号、还没有支付宝交易
        await f.EditPayment(p => { p.pay_method = "微信支付"; p.out_trade_no = FirstNo; });

        AssertStoppedAtTradeCreate(await f.Pay("ali-new"));

        var p = await f.ReadPayment();
        Assert.Equal("支付宝", p.pay_method);
        Assert.Equal(SecondNo, p.out_trade_no);
    }

    [Fact]
    public async Task Pay_NeedsVerifyOrder_UnknownBuyerKeepsConfirmedPayer()
    {
        await using var f = await AlipayFixture.Create();
        await f.EditOrder(o => o.member_id = NoCell);
        // 身份确认留下的付款会员和代付标记：不是免验证订单时，下单认不出人也要保留
        await f.EditPayment(p => { p.member_id = Other; p.is_proxy_pay = true; });

        AssertStoppedAtTradeCreate(await f.Pay("ali-new"));

        var p = await f.ReadPayment();
        Assert.Equal(Other, p.member_id);
        Assert.True(p.is_proxy_pay);
    }

    // ====== 支付成功 ======

    [Fact]
    public async Task PayAgainThenCallback_OrderBecomesPaid()
    {
        await using var f = await AlipayFixture.Create();
        await f.SetTrade("open-new");            // 第一次点支付已用 _ZF_01 建了交易
        await f.Pay("ali-new");                  // 取消后同一账号再点一次

        await f.Callback(FirstNo, "open-new");   // 支付宝带的是建交易时的单号

        Assert.Equal("支付成功", (await f.ReadPayment()).status);
        Assert.Equal(1, (await f.ReadOrder()).dealed);
    }

    [Fact]
    public async Task Callback_UnknownBuyer_RecordsOnPaymentOnly()
    {
        await using var f = await AlipayFixture.Create();
        AssertStoppedAtTradeCreate(await f.Pay("ali-new"));
        await f.SetTrade("open-new");
        var before = await f.Counts();

        // 支付宝会重复送回调：第二次什么都不能改
        for (int delivery = 0; delivery < 2; delivery++)
        {
            await f.Callback(FirstNo, "open-new");

            var p = await f.ReadPayment();
            Assert.Equal("支付成功", p.status);
            Assert.Equal("open-new", p.open_id);
            Assert.Equal("alipay_openid", p.open_id_type);
            Assert.Equal("open-new", p.ali_buyer_id);
            Assert.Null(p.member_id);
            Assert.Equal(before, await f.Counts());
            var order = await f.ReadOrder();
            Assert.Equal(Owner, order.member_id);
            Assert.Equal(1, order.dealed);
            Assert.Null((await f.ReadSession("ali-new")).member_id);
        }
    }

    [Fact]
    public async Task Callback_OtherMemberBuyer_KeepsOrderMemberAndBindsNothing()
    {
        await using var f = await AlipayFixture.Create();
        await f.SetTrade("open-b");
        // 身份确认可能留下「付款会员是别人、没标代付」
        await f.EditPayment(p => { p.member_id = Other; p.is_proxy_pay = false; });
        var before = await f.Counts();

        await f.Callback(FirstNo, "open-b");

        Assert.Equal("支付成功", (await f.ReadPayment()).status);
        Assert.Equal(Owner, (await f.ReadOrder()).member_id);
        Assert.Equal(before, await f.Counts());
    }

    [Fact]
    public async Task Callback_WalkInOrder_StillCreatesMember()
    {
        await using var f = await AlipayFixture.Create();
        await f.EditOrder(o => o.member_id = null);
        await f.SetTrade("open-new");
        var before = await f.Counts();

        await f.Callback(FirstNo, "open-new");

        var after = await f.Counts();
        Assert.Equal(before.members + 1, after.members);
        Assert.Equal(before.alipayAccounts + 1, after.alipayAccounts);
        var p = await f.ReadPayment();
        Assert.NotNull(p.member_id);
        Assert.Equal(p.member_id, (await f.ReadOrder()).member_id);
    }

    [Fact]
    public async Task Paid_WechatPayer_StillTakesOrder()
    {
        await using var f = await AlipayFixture.Create();
        await f.EditPayment(p => { p.pay_method = "微信支付"; p.member_id = Other; p.is_proxy_pay = false; p.status = "支付成功"; });

        await f.Orders().DealSuccessPaidOrder(f.OrderId, f.PaymentId);

        Assert.Equal(Other, (await f.ReadOrder()).member_id);
    }

    // ====== 付款后看订单 ======

    [Fact]
    public async Task PaidOrder_VisibleToAlipayBuyer()
    {
        await using var f = await AlipayFixture.Create();
        await f.EditPayment(p => { p.status = "支付成功"; p.ali_buyer_id = "open-new"; });

        var r = await f.View("ali-new");

        Assert.Equal(0, r.code);
        Assert.Equal(f.OrderId, r.data!.id);
    }

    [Fact]
    public async Task PaidOrder_HiddenFromOtherAlipayAccount()
    {
        await using var f = await AlipayFixture.Create();
        await f.EditPayment(p => { p.status = "支付成功"; p.ali_buyer_id = "open-new"; });
        AssertHidden(await f.View("ali-b"));
    }

    [Fact]
    public async Task PaidOrder_HiddenWhenSessionExpired()
    {
        await using var f = await AlipayFixture.Create();
        await f.EditPayment(p => { p.status = "支付成功"; p.ali_buyer_id = "open-new"; });
        await f.EditSession("ali-new", s => s.expire_date = DateTime.Now.AddMinutes(-1));
        AssertHidden(await f.View("ali-new"));
    }

    [Fact]
    public async Task PaidOrder_BlankBuyerNeverMatches()
    {
        await using var f = await AlipayFixture.Create();
        await f.EditPayment(p => { p.status = "支付成功"; p.ali_buyer_id = null; });
        await f.EditSession("ali-new", s => s.alipay_openid = "");
        AssertHidden(await f.View("ali-new"));
    }

    // ====== 断言 ======

    /// <summary>需要建新交易的下单在测试里停在读证书这一步（没有证书，绝不会连到支付宝）。</summary>
    private static void AssertStoppedAtTradeCreate(ApiResult<OrderPayment?> r)
    {
        Assert.Equal(1, r.code);
        Assert.StartsWith("支付宝证书加载失败", r.message);
    }

    private static async Task AssertRejectedUntouched(AlipayFixture f)
    {
        var r = await f.Pay("ali-b");

        Assert.Equal(1, r.code);
        Assert.Equal(Closed, r.message);
        var p = await f.ReadPayment();
        Assert.Null(p.member_id);
        Assert.Equal("open-new", p.ali_buyer_id);
        Assert.Equal(FirstNo, p.out_trade_no);
        Assert.Equal("T1", p.ali_trade_no);
    }

    private static void AssertHidden(ApiResult<SnowmeetApi.Models.Order?> r)
    {
        Assert.Equal(1, r.code);
        Assert.Equal(Unrelated, r.message);
        Assert.Null(r.data);
    }

    // ====== 夹具 ======

    private sealed class Http : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } = new DefaultHttpContext(); }

    private sealed class AlipayFixture : IAsyncDisposable
    {
        private const string MiniAppId = "2021006157624571";
        private const string MerchantAppId = "2021004143665722";

        public CoachFixture Base = null!;
        public int OrderId, PaymentId;

        public static async Task<AlipayFixture> Create()
        {
            var f = new AlipayFixture { Base = await CoachFixture.Create(withPricing: false) };
            await using var db = f.Base.NewContext();
            db.member.Add(new Member { id = NoCell, real_name = "没手机号", following_wechat = 0 });
            db.miniSession.Add(AliSession("ali-new", "open-new", null));
            db.miniSession.Add(AliSession("ali-b", "open-b", Other));
            var order = new SnowmeetApi.Models.Order { type = "零售", shop = "万龙服务中心", member_id = Owner, valid = 1, is_test = 0, code = Code };
            db.order.Add(order);
            await db.SaveChangesAsync();
            var payment = new OrderPayment { order_id = order.id, pay_method = "支付宝", status = "待支付", valid = 1, amount = 100 };
            db.orderPayment.Add(payment);
            await db.SaveChangesAsync();
            f.OrderId = order.id;
            f.PaymentId = payment.id;
            return f;
        }

        private static MiniSession AliSession(string key, string openId, int? memberId) => new()
        { session_key = key, session_type = "alipay_payerid", alipay_openid = openId, member_id = memberId, valid = 1, expire_date = DateTime.Now.AddHours(2) };

        public OrderController Orders() => new(Base.NewContext(), Base.Config, new Http());

        private PaymentIdentityController Identity() => new(Base.NewContext(), Base.Config);

        public async Task<PaymentIdentityController.CheckPayerIdentityResult> Check(string payerType, string sessionKey, string scannerId)
            => Unwrap(await Identity().CheckPayerIdentity(PaymentId, payerType, scannerId, sessionKey)).data!;

        public async Task<ApiResult<PaymentIdentityController.CheckPayerIdentityResult>> ChooseProxy(string sessionKey, string scannerId)
            => Unwrap(await Identity().ConfirmPayIdentity(new PaymentIdentityController.ConfirmPayIdentityBody
            { paymentId = PaymentId, payerType = "alipay", scannerId = scannerId, action = "choose", choice = "proxy" }, sessionKey));

        public async Task<ApiResult<OrderPayment?>> Pay(string sessionKey)
        {
            // 小程序证书绝不能出现在测试目录里：有它就会真的去支付宝建交易
            Assert.False(File.Exists(Path.Combine(Util.workingPath, "AlipayCertificate", MiniAppId, $"private_key_{MiniAppId}.txt")));
            return Unwrap(await Orders().AlipayPayByOrderPayment(PaymentId, sessionKey));
        }

        public async Task<ApiResult<SnowmeetApi.Models.Order?>> View(string sessionKey)
            => Unwrap(await Orders().GetOrderFromPaymentByCustomer(PaymentId, sessionKey));

        public async Task Callback(string outTradeNo, string buyerOpenId)
        {
            EnsureFakeMerchantCerts();
            var controller = new AliController(Base.NewContext(), Base.Config, new Http());
            var context = new DefaultHttpContext();
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
                $"notify_type=trade_status_sync&trade_status=TRADE_SUCCESS&app_id={MerchantAppId}&out_trade_no={outTradeNo}&trade_no=T1&buyer_open_id={buyerOpenId}&notify_id=n1"));
            controller.ControllerContext = new ControllerContext { HttpContext = context };
            await controller.CallBack();
        }

        /// <summary>回调接口构造时要读商户证书：在测试目录里放一套临时自签名的假证书。</summary>
        private static void EnsureFakeMerchantCerts()
        {
            string dir = Path.Combine(Util.workingPath, "AlipayCertificate", MerchantAppId);
            Directory.CreateDirectory(dir);
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=snowmeet-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            string pem = new(PemEncoding.Write("CERTIFICATE", cert.RawData));
            void WriteIfMissing(string name, string content)
            {
                string path = Path.Combine(dir, name);
                if (!File.Exists(path)) File.WriteAllText(path, content);
            }
            WriteIfMissing($"private_key_{MerchantAppId}.txt", Convert.ToBase64String(rsa.ExportRSAPrivateKey()));
            WriteIfMissing($"appCertPublicKey_{MerchantAppId}.crt", pem);
            WriteIfMissing("alipayCertPublicKey_RSA2.crt", pem);
            WriteIfMissing("alipayRootCert.crt", pem);
        }

        /// <summary>付款记录改成「已经用 buyer 的账号建过交易 T1、单号 _ZF_01」。</summary>
        public Task SetTrade(string buyer) => EditPayment(p => { p.ali_buyer_id = buyer; p.out_trade_no = FirstNo; p.ali_trade_no = "T1"; });

        public async Task EditPayment(Action<OrderPayment> edit)
        {
            await using var db = Base.NewContext();
            var row = await db.orderPayment.AsTracking().SingleAsync(p => p.id == PaymentId);
            edit(row);
            await db.SaveChangesAsync();
        }

        public async Task EditOrder(Action<SnowmeetApi.Models.Order> edit)
        {
            await using var db = Base.NewContext();
            var row = await db.order.AsTracking().SingleAsync(o => o.id == OrderId);
            edit(row);
            await db.SaveChangesAsync();
        }

        public async Task EditSession(string key, Action<MiniSession> edit)
        {
            await using var db = Base.NewContext();
            var row = await db.miniSession.AsTracking().SingleAsync(s => s.session_key == key);
            edit(row);
            await db.SaveChangesAsync();
        }

        public async Task<OrderPayment> ReadPayment()
        {
            await using var db = Base.NewContext();
            return await db.orderPayment.SingleAsync(p => p.id == PaymentId);
        }

        public async Task<SnowmeetApi.Models.Order> ReadOrder()
        {
            await using var db = Base.NewContext();
            return await db.order.SingleAsync(o => o.id == OrderId);
        }

        public async Task<MiniSession> ReadSession(string key)
        {
            await using var db = Base.NewContext();
            return await db.miniSession.SingleAsync(s => s.session_key == key);
        }

        public async Task<(int members, int alipayAccounts)> Counts()
        {
            await using var db = Base.NewContext();
            return (await db.member.CountAsync(), await db.memberSocialAccount.CountAsync(m => m.type == "alipay_payerid"));
        }

        private static T Unwrap<T>(ActionResult<T> result) => (T)((OkObjectResult)result.Result!).Value!;

        public ValueTask DisposeAsync() => Base.DisposeAsync();
    }
}
