using System;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using Xunit;

namespace SnowmeetApi.Tests
{
    public class CarePickRulesTests
    {
        static readonly DateTime Now = new DateTime(2026, 10, 22, 23, 40, 0);

        [Fact]
        public void 取板码带随机串和养护单号且能解析()
        {
            string code = CarePickRules.BuildCode("abcdefghij", 25693, 801);

            Assert.Equal("cp_abcdefghij_25693_801", code);
            Assert.True(CarePickRules.TryParseCareId(code, out int careId));
            Assert.Equal(25693, careId);
            Assert.Equal("https://mini.snowmeet.top/mapp/care_pick?code=" + code, CarePickRules.BuildQrText(code));
            Assert.False(CarePickRules.TryParseCareId("care_veri_25693_801", out _));
            Assert.False(CarePickRules.TryParseCareId(ReceptCheckinRules.BuildCode("abcdefghij", 801), out _));
            Assert.False(CarePickRules.TryParseCareId("cp_short_25693_801", out _));
            Assert.False(CarePickRules.TryParseCareId(null, out _));
        }

        [Fact]
        public void 扫码人就是下单会员才通过()
        {
            Assert.Equal(CarePickRules.MATCHED, CarePickRules.ScannedStage(7, 7));
            Assert.Equal(CarePickRules.MISMATCH, CarePickRules.ScannedStage(8, 7));
            // 散客单没有会员，谁扫都不通过
            Assert.Equal(CarePickRules.MISMATCH, CarePickRules.ScannedStage(8, null));
            // 新微信号还没会员身份，引导授权手机号后重试
            Assert.Equal(CarePickRules.NEED_MEMBER, CarePickRules.ScannedStage(null, 7));
        }

        [Fact]
        public void 店员侧阶段按扫码过期作废判定()
        {
            ScanQrCode fresh = new ScanQrCode { expire_time = Now.AddSeconds(60) };
            ScanQrCode expired = new ScanQrCode { expire_time = Now.AddSeconds(-1) };
            ScanQrCode stopped = new ScanQrCode { stoped = 1, expire_time = Now.AddSeconds(60) };
            ScanQrCode matched = new ScanQrCode { scaned = 1, scaner_member_id = 7, expire_time = Now.AddSeconds(-60) };
            ScanQrCode noMember = new ScanQrCode { scaned = 1, expire_time = Now.AddSeconds(-60) };

            Assert.Equal(CarePickRules.WAITING_SCAN, CarePickRules.StaffStage(fresh, 7, Now));
            Assert.Equal(CarePickRules.EXPIRED, CarePickRules.StaffStage(expired, 7, Now));
            Assert.Equal(CarePickRules.STOPPED, CarePickRules.StaffStage(stopped, 7, Now));
            // 已扫码的不受过期影响
            Assert.Equal(CarePickRules.MATCHED, CarePickRules.StaffStage(matched, 7, Now));
            Assert.Equal(CarePickRules.MISMATCH, CarePickRules.StaffStage(matched, 9, Now));
            Assert.Equal(CarePickRules.NEED_MEMBER, CarePickRules.StaffStage(noMember, 7, Now));
        }
    }
}
