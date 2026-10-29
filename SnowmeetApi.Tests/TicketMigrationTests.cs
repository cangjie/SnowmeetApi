using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using System.Text.Json;

namespace SnowmeetApi.Tests;

public sealed class TicketMigrationTests
{
    internal sealed class TestTicketController(ApplicationDBContext db,IConfiguration config) : TicketController(db,config)
    {
        protected override Task NotifyMiniTransferSender(string openId,Ticket reward)=>Task.CompletedTask;
    }
    internal sealed class Fixture : IAsyncDisposable
    {
        public SqliteConnection Connection = new("Data Source=mini-ticket-"+Guid.NewGuid().ToString("N")+";Mode=Memory;Cache=Shared;Foreign Keys=False");
        public ApplicationDBContext Db = null!;
        public IConfiguration Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        { ["Settings:AppId"]="test-app", ["Settings:AppSecret"]="test-secret" }).Build();
        public static async Task<Fixture> Create(string type="mini_g", int? limit=null)
        {
            var f=new Fixture(); await f.Connection.OpenAsync();
            f.Db=new(new DbContextOptionsBuilder<ApplicationDBContext>().UseSqlite(f.Connection).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);
            await f.Db.Database.EnsureCreatedAsync();
            for (int id=1;id<=3;id++)
            {
                f.Db.member.Add(new Member {id=id, following_wechat=0});
                f.Db.memberSocialAccount.Add(new MemberSocialAccount {member_id=id,type="wechat_mini_openid",num="mini-"+id,valid=1});
                f.Db.miniSession.Add(new MiniSession {session_key="session-"+id,session_type="wechat_mini_openid",member_id=id,wechat_openid="mini-"+id, valid=1,expire_date=DateTime.Now.AddHours(1)});
            }
            f.Db.ticketTemplate.Add(new TicketTemplate {id=12,name="测试券",memo="说明",type="养护券",miniapp_recept_path="",sharable=1,valid=1});
            f.Db.ticketShareBatch.Add(new TicketShareBatch {id=1,template_id=12,staff_id=9,share_type=type,max_claims=limit,channel="门店"});
            await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear(); return f;
        }
        public ApplicationDBContext NewContext()=>new(new DbContextOptionsBuilder<ApplicationDBContext>().UseSqlite(Connection.ConnectionString).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);
        public async Task AddStaff(int memberId=1,int staffId=9)
        {
            Db.staff.Add(new Staff{id=staffId,name="店员",title_level=200,valid=1});
            Db.socialAccountForJob.Add(new SocialAccountForJob{id=staffId,member_id=memberId,wechat_mini_openid="mini-"+memberId});
            Db.staffSocialAccount.Add(new StaffSocialAccount{staff_id=staffId,social_account_id=staffId,valid=1,start_date=DateTime.Now.AddDays(-2)});
            await Db.SaveChangesAsync();Db.ChangeTracker.Clear();
        }
        public TicketShareController Share(){ Db.ChangeTracker.Clear(); return new(Db,Config); }
        public TicketController Transfer(){ Db.ChangeTracker.Clear(); return new TestTicketController(Db,Config); }
        public Ticket Ticket(string code="111111111",int member=1)=>new() {code=code,name="测试券",open_id="mini-"+member,member_id=member,template_id=12,valid=1,used=0,shared=1,shared_time=DateTime.Now,create_date=DateTime.Now.AddDays(-1),expire_date=DateTime.Now.AddDays(30)};
        public async ValueTask DisposeAsync(){await Db.DisposeAsync(); await Connection.DisposeAsync();}
    }
    internal static ApiResult<T> Result<T>(ActionResult<ApiResult<T>> action)=>Assert.IsType<ApiResult<T>>(Assert.IsType<OkObjectResult>(action.Result).Value);
    internal static JsonElement Data(ApiResult<object> result)=>JsonSerializer.SerializeToElement(result.data);

    static async Task<ActionResult<ApiResult<T>>> Invoke<T>(object controller,string method,params object[] args)
    {
        var m=controller.GetType().GetMethod(method); Assert.NotNull(m);
        var parameters=m!.GetParameters(); var values=parameters.Select((p,i)=>i<args.Length?args[i]:p.DefaultValue).ToArray();
        return await (Task<ActionResult<ApiResult<T>>>)m.Invoke(controller,values)!;
    }
    static async Task<ApiResult<object>> Mini(object controller,string method,params object[] args)=>Result(await Invoke<object>(controller,method,args));
    static async Task<ApiResult<Ticket>> MiniTicket(object controller,string method,params object[] args)=>Result(await Invoke<Ticket>(controller,method,args));

    [Fact] public async Task DirectGroupClaimNeedsNoOaAccountOrEvent()
    {
        await using var f=await Fixture.Create();
        var r=await Mini(f.Share(),"ClaimMiniSharedTicket",1,"session-2");
        Assert.Equal(0,r.code); Assert.Single(await f.Db.ticket.ToListAsync());
        Assert.Single(await f.Db.ticketShareClaim.ToListAsync()); Assert.Equal(1,(await f.Db.ticketShareBatch.FirstAsync()).claim_count);
    }
    [Fact] public async Task BatchRequiresLogin()
    {
        await using var f=await Fixture.Create(); Assert.Equal(1,(await Mini(f.Share(),"GetMiniShareBatch",1,"bad")).code);
    }
    [Fact] public async Task BatchRejectsInactiveTemplate()
    {
        await using var f=await Fixture.Create(); await f.Db.ticketTemplate.Where(t=>t.id==12).ExecuteUpdateAsync(s=>s.SetProperty(t=>t.valid,0));
        Assert.Equal(1,(await Mini(f.Share(),"GetMiniShareBatch",1,"session-2")).code);
    }
    [Theory]
    [InlineData("mini_p")]
    [InlineData("mini_g")]
    [InlineData("mini_q")]
    public async Task ClosingTemplateSharingStopsNewQueriesAndClaims(string type)
    {
        await using var f = await Fixture.Create(type, type == "mini_p" ? 1 : null);
        await f.Db.ticketTemplate.Where(t => t.id == 12).ExecuteUpdateAsync(s => s.SetProperty(t => t.sharable, 0));
        Assert.Equal(1, (await Mini(f.Share(), "GetMiniShareBatch", 1, "session-2")).code);
        string endpoint = type == "mini_q" ? "ClaimMiniQrCodeTicket" : "ClaimMiniSharedTicket";
        Assert.Equal(1, (await Mini(f.Share(), endpoint, 1, "session-2")).code);
        Assert.Empty(await f.Db.ticket.ToListAsync());
        Assert.Empty(await f.Db.ticketShareClaim.ToListAsync());
    }
    [Fact] public async Task ClosingTemplateSharingStopsNewTransfersWithoutChangingTheCoupon()
    {
        await using var f = await Fixture.Create();
        f.Db.ticket.Add(f.Ticket()); await f.Db.SaveChangesAsync();
        Assert.Equal(0, (await MiniTicket(f.Transfer(), "SetTicketToMiniShare", "111111111", "session-1")).code);
        await f.Db.ticketTemplate.Where(t => t.id == 12).ExecuteUpdateAsync(s => s.SetProperty(t => t.sharable, 0));
        Assert.Equal(1, (await MiniTicket(f.Transfer(), "AcceptMiniTicket", "111111111", "", "session-2")).code);
        Assert.Equal(1, (await MiniTicket(f.Transfer(), "SetTicketToMiniShare", "111111111", "session-1")).code);
        Assert.Equal(1, (await f.Db.ticket.SingleAsync()).member_id);
        Assert.Empty(await f.Db.ticketLog.Where(l => l.accepter_open_id != "").ToListAsync());
    }
    [Fact] public async Task QrBatchCannotUseOrdinaryClaimEvenIfFollowing()
    {
        await using var f=await Fixture.Create("mini_q"); await f.Db.member.Where(m=>m.id==2).ExecuteUpdateAsync(s=>s.SetProperty(m=>m.following_wechat,1));
        Assert.Equal(1,(await Mini(f.Share(),"ClaimMiniSharedTicket",1,"session-2")).code); Assert.Empty(await f.Db.ticket.ToListAsync());
    }
    [Fact] public async Task QrClaimContractExists()
    {
        Assert.NotNull(typeof(TicketShareController).GetMethod("ClaimMiniQrCodeTicket")); await Task.CompletedTask;
    }
    [Fact] public async Task QrBatchShowsAnyValidTicketIssuedTodayAcrossSources()
    {
        await using var f=await Fixture.Create("mini_q"); var t=f.Ticket(member:2);t.create_date=DateTime.Now;t.shared=0;f.Db.ticket.Add(t); await f.Db.SaveChangesAsync();
        var r=await Mini(f.Share(),"GetMiniShareBatch",1,"session-2"); Assert.True(Data(r).GetProperty("claimed").GetBoolean()); Assert.Equal(t.code,Data(r).GetProperty("claimedCode").GetString());
    }
    [Fact] public async Task DirectTransferNeedsNoOaAccountOrEvent()
    {
        await using var f=await Fixture.Create(); f.Db.ticket.Add(f.Ticket()); await f.Db.SaveChangesAsync();f.Db.ChangeTracker.Clear();
        Assert.Equal(0,(await MiniTicket(f.Transfer(),"SetTicketToMiniShare","111111111","session-1")).code);
        Assert.Equal(0,(await MiniTicket(f.Transfer(),"AcceptMiniTicket","111111111","","session-2")).code);
        Assert.Single(await f.Db.ticketLog.Where(SnowmeetApi.Helpers.TicketTransferRules.TransferLogFilter()).ToListAsync());Assert.Equal(2,(await f.Db.ticket.FirstAsync(t=>t.code=="111111111")).member_id);
        Assert.Empty(await f.Db.ticket.Where(t=>t.create_memo=="转赠被领取回赠").ToListAsync());
    }
    [Theory][InlineData("expired")][InlineData("used")][InlineData("revoked")][InlineData("unshareable")]
    public async Task TransferRejectsInvalidCouponEvenIfFollowing(string state)
    {
        await using var f=await Fixture.Create();var t=f.Ticket();
        f.Db.ticket.Add(t);await f.Db.SaveChangesAsync();
        Assert.Equal(0,(await MiniTicket(f.Transfer(),"SetTicketToMiniShare",t.code,"session-1")).code);
        if(state=="expired")await f.Db.ticket.Where(x=>x.code==t.code).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.expire_date,DateTime.Now.AddDays(-1)));
        if(state=="used")await f.Db.ticket.Where(x=>x.code==t.code).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.used,1));
        if(state=="revoked")await f.Db.ticket.Where(x=>x.code==t.code).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.valid,0));
        if(state=="unshareable")await f.Db.ticket.Where(x=>x.code==t.code).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.template_id,99));
        Assert.Equal(1,(await MiniTicket(f.Transfer(),"AcceptMiniTicket",t.code,"","session-2")).code);
        Assert.Empty(await f.Db.ticketLog.Where(l=>l.accepter_open_id!="").ToListAsync());
    }
    [Fact] public async Task CannotShareExpiredTicket()
    {
        await using var f=await Fixture.Create();var t=f.Ticket();t.expire_date=DateTime.Now.AddDays(-1);f.Db.ticket.Add(t);await f.Db.SaveChangesAsync();f.Db.ChangeTracker.Clear();
        Assert.Equal(1,(await MiniTicket(f.Transfer(),"SetTicketToMiniShare",t.code,"session-1")).code);
    }
    [Fact] public void MiniQrEndpointContractExists()=>Assert.NotNull(typeof(TicketPosterController).GetMethod("GetMiniQrCode"));

    [Fact] public async Task NewBatchCreationEndpointsExist()
    {
        Assert.NotNull(typeof(TicketTemplateAdminController).GetMethod("ShareMiniTicketTemplateByStaff"));
        Assert.NotNull(typeof(TicketTemplateAdminController).GetMethod("CreateMiniQrCodeBatchByStaff"));
        await Task.CompletedTask;
    }
    [Theory][InlineData("personal")][InlineData("group")][InlineData("qrcode")]
    public async Task NewEndpointsRejectOldBatchTypesAndLegacyKeepsOaGate(string type)
    {
        await using var f=await Fixture.Create(type,type=="personal"?1:null);
        Assert.Equal(1,(await Mini(f.Share(),"GetMiniShareBatch",1,"session-2")).code);
        Assert.Equal(1,(await Mini(f.Share(),"ClaimMiniSharedTicket",1,"session-2")).code);
        Assert.Equal(1,(await Mini(f.Share(),"ClaimMiniQrCodeTicket",1,"session-2")).code);
        Assert.Equal(1,Result(await f.Share().ClaimSharedTicket(1,"session-2")).code);
        Assert.Empty(await f.Db.ticket.ToListAsync());
    }
    [Theory][InlineData("mini_p")][InlineData("mini_g")][InlineData("mini_q")]
    public async Task LegacyClaimCannotIssueNewBatch(string type)
    {
        await using var f=await Fixture.Create(type);await f.Db.member.Where(m=>m.id==2).ExecuteUpdateAsync(s=>s.SetProperty(m=>m.following_wechat,1));
        f.Db.memberSocialAccount.Add(new MemberSocialAccount{member_id=2,type="wechat_oa_openid",num="oa-2",valid=1});await f.Db.SaveChangesAsync();
        Assert.Equal(1,Result(await f.Share().ClaimSharedTicket(1,"session-2")).code);
        Assert.Equal(1,Result(await f.Share().ClaimSharedTicketByOaFollow(1,"oa-2")).code);
        Assert.Equal(1,Result(await f.Share().ClaimByOaScan(1,"oa-2")).code);
        Assert.Empty(await f.Db.ticket.ToListAsync());
    }
    [Fact] public async Task OldTransferStillRequiresOaAndNewEndpointRequiresCurrentMarker()
    {
        await using var f=await Fixture.Create();f.Db.ticket.Add(f.Ticket());await f.Db.SaveChangesAsync();f.Db.ChangeTracker.Clear();
        Assert.Equal(1,Result(await f.Transfer().AcceptTicket("111111111","","session-2")).code);
        Assert.Equal(1,(await MiniTicket(f.Transfer(),"AcceptMiniTicket","111111111","","session-2")).code);
        Assert.Empty(await f.Db.ticketLog.ToListAsync());
    }

    [Theory][InlineData("mini_g",true)][InlineData("mini_p",false)]
    public async Task GroupIsDailyAndPersonalIsLifetime(string type,bool allowed)
    {
        await using var f=await Fixture.Create(type,type=="mini_p"?10:null);
        f.Db.ticketShareClaim.Add(new TicketShareClaim{batch_id=1,member_id=2,ticket_code="old",claim_date=DateTime.Now.Date.AddDays(-1)});await f.Db.SaveChangesAsync();
        var result=await Mini(f.Share(),"ClaimMiniSharedTicket",1,"session-2");Assert.Equal(allowed?0:1,result.code);
        Assert.Equal(1,(await Mini(f.Share(),"ClaimMiniSharedTicket",1,"session-2")).code);
    }
    [Fact] public async Task FixedCodeChecksAllSourcesIncludingUsedTicketsButNotInvalidOrYesterday()
    {
        await using var f=await Fixture.Create("mini_q");var t=f.Ticket(member:2);t.create_date=DateTime.Now;t.used=1;f.Db.ticket.Add(t);await f.Db.SaveChangesAsync();
        Assert.Equal(1,(await Mini(f.Share(),"ClaimMiniQrCodeTicket",1,"session-2")).code);
        await f.Db.ticket.Where(x=>x.code==t.code).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.valid,0));
        Assert.Equal(0,(await Mini(f.Share(),"ClaimMiniQrCodeTicket",1,"session-2")).code);
    }
    [Fact] public async Task CapIsThreeAndActiveStaffRemainExempt()
    {
        await using var f=await Fixture.Create();for(int i=0;i<3;i++)f.Db.ticket.Add(f.Ticket("cap"+i,2));await f.Db.SaveChangesAsync();
        Assert.Equal(1,(await Mini(f.Share(),"ClaimMiniSharedTicket",1,"session-2")).code);
        await f.AddStaff(2,19);Assert.Equal(0,(await Mini(f.Share(),"ClaimMiniSharedTicket",1,"session-2")).code);
    }
    [Fact] public async Task PersonalBatchHasOneWinnerAcrossDifferentMembers()
    {
        await using var f=await Fixture.Create("mini_p",1);
        async Task<ApiResult<object>> Claim(int member) { await using var db=f.NewContext(); return Result(await new TicketShareController(db,f.Config).ClaimMiniSharedTicket(1,"session-"+member)); }
        var results=await Task.WhenAll(Claim(2),Claim(3));Assert.Single(results.Where(r=>r.code==0));Assert.Single(await f.Db.ticket.ToListAsync());
        Assert.Single(await f.Db.ticketShareClaim.ToListAsync());Assert.Equal(1,(await f.Db.ticketShareBatch.FirstAsync()).claim_count);
    }
    [Fact] public async Task SimultaneousFixedClaimsAcrossBatchesHaveOneWinner()
    {
        await using var f=await Fixture.Create("mini_q");f.Db.ticketShareBatch.Add(new TicketShareBatch{id=2,template_id=12,staff_id=19,share_type="mini_q",channel="another"});await f.Db.SaveChangesAsync();
        async Task<ApiResult<object>> Claim(int batch) { await using var db=f.NewContext(); return Result(await new TicketShareController(db,f.Config).ClaimMiniQrCodeTicket(batch,"session-2")); }
        var results=await Task.WhenAll(Claim(1),Claim(2));Assert.Single(results.Where(r=>r.code==0));Assert.Single(await f.Db.ticket.ToListAsync());
        Assert.Equal(1,await f.Db.ticketShareBatch.SumAsync(b=>b.claim_count));
    }
    [Fact] public async Task ConcurrentTransferHasOneCompletedLogAndOneReward()
    {
        await using var f=await Fixture.Create();
        f.Db.ticketTemplate.Add(new TicketTemplate {id=16,name="老顾客优惠券",memo="说明",sharable=1,valid=1});
        var ticket=f.Ticket();ticket.template_id=16;f.Db.ticket.Add(ticket);await f.Db.SaveChangesAsync();
        Assert.Equal(0,(await MiniTicket(f.Transfer(),"SetTicketToMiniShare","111111111","session-1")).code);
        async Task<ApiResult<Ticket>> Accept(int member) { await using var db=f.NewContext(); return Result(await new TestTicketController(db,f.Config).AcceptMiniTicket("111111111","直接领取","session-"+member)); }
        var results=await Task.WhenAll(Accept(2),Accept(3));Assert.Single(results.Where(r=>r.code==0));
        Assert.Single(await f.Db.ticketLog.Where(l=>l.accepter_open_id!="").ToListAsync());Assert.Single(await f.Db.ticket.Where(t=>t.create_memo=="转赠被领取回赠").ToListAsync());
    }
    [Fact] public async Task CapIsSharedAcrossConcurrentTransferAndBatchClaims()
    {
        await using var f=await Fixture.Create();f.Db.ticket.Add(f.Ticket());f.Db.ticket.Add(f.Ticket("cap1",2));f.Db.ticket.Add(f.Ticket("cap2",2));await f.Db.SaveChangesAsync();
        Assert.Equal(0,(await MiniTicket(f.Transfer(),"SetTicketToMiniShare","111111111","session-1")).code);
        await using var db1=f.NewContext();await using var db2=f.NewContext();
        var transfer=new TestTicketController(db1,f.Config).AcceptMiniTicket("111111111","直接领取","session-2");
        var batch=new TicketShareController(db2,f.Config).ClaimMiniSharedTicket(1,"session-2");
        await Task.WhenAll(transfer,batch);Assert.Equal(1,new[]{Result(await transfer).code,Result(await batch).code}.Count(c=>c==0));
        Assert.Equal(3,await f.Db.ticket.Where(SnowmeetApi.Helpers.TicketTransferRules.UsableTicketFilter(2,12,DateTime.Now)).CountAsync());
    }
    [Fact] public async Task ClaimLogFailureRollsBackTicketAndCounter()
    {
        await using var f=await Fixture.Create();await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_claim BEFORE INSERT ON ticket_share_claim BEGIN SELECT RAISE(ABORT,'test failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(()=>f.Share().ClaimMiniSharedTicket(1,"session-2"));
        Assert.Empty(await f.Db.ticket.ToListAsync());Assert.Equal(0,(await f.Db.ticketShareBatch.FirstAsync()).claim_count);
    }
    [Fact] public async Task MarkerIsExcludedFromCompletedTransferReportsAndCannotBeReusedAfterOldShare()
    {
        await using var f=await Fixture.Create();f.Db.ticket.Add(f.Ticket());await f.Db.SaveChangesAsync();
        Assert.Equal(0,(await MiniTicket(f.Transfer(),"SetTicketToMiniShare","111111111","session-1")).code);
        var t=await f.Db.ticket.FirstAsync();var marker=await f.Db.ticketLog.SingleAsync();Assert.Equal(t.shared_time,marker.transact_time);
        Assert.Empty(await f.Db.ticketLog.Where(SnowmeetApi.Helpers.TicketTransferRules.TransferLogFilter()).ToListAsync());
        Assert.Equal(1,Result(await f.Transfer().AcceptTicket(t.code,"","session-2")).code);
        Assert.Equal(0,Result(await f.Transfer().SetTicketToShare(t.code,"session-1")).code);
        var preserved = await f.Db.ticketLog.SingleAsync(l => l.id == marker.id);
        Assert.Equal(marker.memo, preserved.memo);
        Assert.Equal(marker.transact_time, preserved.transact_time);
        Assert.Equal(2, await f.Db.ticketLog.CountAsync());
        Assert.Equal(1,(await MiniTicket(f.Transfer(),"AcceptMiniTicket",t.code,"","session-2")).code);
    }
    [Fact] public async Task CancelledMiniShareAndSelfOrMalformedAcceptAreRejected()
    {
        await using var f=await Fixture.Create();f.Db.ticket.Add(f.Ticket());await f.Db.SaveChangesAsync();
        Assert.Equal(0,(await MiniTicket(f.Transfer(),"SetTicketToMiniShare","111111111","session-1")).code);
        Assert.Equal(1,(await MiniTicket(f.Transfer(),"AcceptMiniTicket","111111111","","session-1")).code);
        Assert.Equal(1,(await MiniTicket(f.Transfer(),"AcceptMiniTicket","garbage","","session-2")).code);
        Assert.Equal(0,Result(await f.Transfer().CancelShare("111111111","session-1")).code);
        Assert.Equal(1,(await MiniTicket(f.Transfer(),"AcceptMiniTicket","111111111","","session-2")).code);
    }
    [Fact] public async Task NewStaffBatchCreationPreservesIdempotencyAndDoesNotReuseOldFixedCode()
    {
        await using var f=await Fixture.Create("qrcode");await f.AddStaff();
        var admin=new TicketTemplateAdminController(f.Db,f.Config);
        var one=await Mini(admin,"CreateMiniQrCodeBatchByStaff",12,"门店","session-1");Assert.Equal(0,one.code);var id=Data(one).GetProperty("batchId").GetInt32();Assert.NotEqual(1,id);
        f.Db.ChangeTracker.Clear();var two=await Mini(admin,"CreateMiniQrCodeBatchByStaff",12,"门店","session-1");Assert.Equal(id,Data(two).GetProperty("batchId").GetInt32());
        Assert.Equal("qrcode",(await f.Db.ticketShareBatch.FirstAsync(b=>b.id==1)).share_type);
        Assert.Equal("mini_q",(await f.Db.ticketShareBatch.FirstAsync(b=>b.id==id)).share_type);
        Assert.Equal(1,(await Mini(admin,"ShareMiniTicketTemplateByStaff",12,"personal","session-2")).code);
    }
}
