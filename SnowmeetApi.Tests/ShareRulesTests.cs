using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.StaffAccounts;

namespace SnowmeetApi.Tests;

public class ShareRulesTests
{
    private static OrderPayment Pay(string method, double amount, double refunded = 0)
    {
        var p = new OrderPayment { id = 1, amount = amount, pay_method = method, status = "支付成功", valid = 1 };
        if (refunded > 0) p.refunds.Add(new OrderPaymentRefund { amount = refunded, refund_id = "R1" });
        return p;
    }

    [Fact]
    public void Introducer_rules()
    {
        Assert.Equal(40, IntroducerRules.SuggestedRentShare(400));
        Assert.Equal(33.33, IntroducerRules.SuggestedRentShare(333.33));
        Assert.True(IntroducerRules.IsValidType("教练"));
        Assert.True(IntroducerRules.IsValidType("KOL"));
        Assert.True(IntroducerRules.IsValidType("机构"));
        Assert.False(IntroducerRules.IsValidType("分销"));
        Assert.False(IntroducerRules.IsValidType(null));
        Assert.Equal("公司", IntroducerRules.RelationType("机构"));
        Assert.Equal("个人", IntroducerRules.RelationType("KOL"));
    }

    [Fact]
    public void Alipay_login_name_is_a_phone_number_or_an_email()
    {
        foreach (var ok in new[] { "13691457006", "zhang.san@example.com", "a_b-c+d@mail.example.cn" })
            Assert.True(IntroducerRules.IsValidAlipayLoginName(ok), ok);
        foreach (var bad in new[] { "2088000000000001", "1369145700", "136914570061", "abc", "a@b", "@example.com", "a b@example.com", "", null })
            Assert.False(IntroducerRules.IsValidAlipayLoginName(bad), bad ?? "null");
    }

    [Fact]
    public void Cap_uses_net_for_wechat_and_gross_for_alipay()
    {
        var wechat = Pay("微信支付", 300, 250);               // 基数 50，门槛 15
        Assert.True(ShareCapRules.IsOverCap(25, new[] { wechat }));
        Assert.False(ShareCapRules.IsOverCap(14.99, new[] { wechat }));

        var alipay = Pay("支付宝", 6000, 5140);                // 基数 6000，门槛 1800
        Assert.False(ShareCapRules.IsOverCap(516, new[] { alipay }));
        Assert.True(ShareCapRules.IsOverCap(1800, new[] { alipay }));

        // 两笔付款：500 + 1000 = 1500，门槛 450
        Assert.False(ShareCapRules.IsOverCap(150, new[] { Pay("微信支付", 1000, 500), Pay("支付宝", 1000) }));
    }

    [Fact]
    public void Capacity_is_29_percent_of_the_base_minus_what_is_already_shared()
    {
        Assert.Equal(1310, ShareCapRules.Capacity(Pay("支付宝", 6000, 5140), 430));
        Assert.Equal(0, ShareCapRules.Capacity(Pay("微信支付", 1000, 500), 200));
        Assert.Equal(145, ShareCapRules.Capacity(Pay("微信支付", 1000, 500), 0));
    }

    [Fact]
    public void Over_cap_memo_names_the_platform_base()
    {
        Assert.Equal("分账金额超过实收（扣除退款）的30%，不自动分账", ShareCapRules.OverCapMemo(Pay("微信支付", 300, 250)));
        Assert.Equal("分账金额超过付款总额的30%，不自动分账", ShareCapRules.OverCapMemo(Pay("支付宝", 300, 250)));
    }

    [Fact]
    public void Wechat_cap_base_defaults_to_net()
    {
        Assert.True(WanlongShareRules.WechatCapOnNet);
        Assert.Equal(50, WanlongShareRules.ShareBase(Pay("微信支付", 300, 250)));
        Assert.Equal(300, WanlongShareRules.ShareBase(Pay("支付宝", 300, 250)));
    }

    [Fact]
    public void Receiver_problem_per_channel()
    {
        var wechatOk = new OrderShareRelation { wepay_account_type = "PERSONAL_OPENID", wepay_account_num = "oX" };
        Assert.Null(ShareReceiverRules.Problem(wechatOk, new ShareRelationBind[0], "微信支付"));
        Assert.Equal("介绍人没有微信收款账号", ShareReceiverRules.Problem(new OrderShareRelation(), new ShareRelationBind[0], "微信支付"));

        var ali = new OrderShareRelation { ali_account_type = "loginName", ali_account_num = "13691457006" };
        Assert.Equal("未绑定分账关系", ShareReceiverRules.Problem(ali, new ShareRelationBind[0], "支付宝"));
        var bound = new[] { new ShareRelationBind { pay_method = "支付宝", valid = true } };
        Assert.Null(ShareReceiverRules.Problem(ali, bound, "支付宝"));
        var unbound = new[] { new ShareRelationBind { pay_method = "支付宝", valid = false }, new ShareRelationBind { pay_method = "微信支付", valid = true } };
        Assert.Equal("未绑定分账关系", ShareReceiverRules.Problem(ali, unbound, "支付宝"));
        Assert.Equal("介绍人没有支付宝收款账号", ShareReceiverRules.Problem(new OrderShareRelation(), bound, "支付宝"));
    }

    [Fact]
    public void With_auto_replaces_only_the_automatic_memos()
    {
        const string over = "分账金额超过实收（扣除退款）的30%，不自动分账";
        const string note = "应分由 215 改为 430：第二日租金后改为租金类型";
        Assert.Equal(note, ShareMemoRules.WithAuto(note + "；" + over, null));
        Assert.Equal(over, ShareMemoRules.WithAuto(null, over));
        Assert.Equal("人工备注；" + over, ShareMemoRules.WithAuto("人工备注", over));
        Assert.Equal("人工备注；" + over, ShareMemoRules.WithAuto(ShareMemoRules.WithAuto("人工备注", over), over));
        Assert.Equal("A；未绑定分账关系", ShareMemoRules.WithAuto("A；分账金额超过付款总额的30%，不自动分账", "未绑定分账关系"));
        Assert.Null(ShareMemoRules.WithAuto(over, null));
    }

    [Fact]
    public void Manager_is_valid_staff_at_level_200_or_more()
    {
        Assert.True(StaffAccountRules.IsManager(new Staff { valid = 1, title_level = 200 }));
        Assert.True(StaffAccountRules.IsManager(new Staff { valid = 1, title_level = 300 }));
        Assert.False(StaffAccountRules.IsManager(new Staff { valid = 1, title_level = 100 }));
        Assert.False(StaffAccountRules.IsManager(new Staff { valid = 0, title_level = 200 }));
        Assert.False(StaffAccountRules.IsManager(null));
    }
}
