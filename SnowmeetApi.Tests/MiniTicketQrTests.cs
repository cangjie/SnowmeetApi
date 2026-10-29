using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SnowmeetApi.Services.Storage;
using SnowmeetApi.Services.Tickets;

namespace SnowmeetApi.Tests;

public sealed class MiniTicketQrTests
{
    internal static byte[] MakePng(int width=2,int height=2,bool white=false)
    {
        using var image=new Image<Rgba32>(width,height,white?new Rgba32(255,255,255,255):new Rgba32(0,0,0,255));
        using var stream=new MemoryStream();image.SaveAsPng(stream);return stream.ToArray();
    }
    internal static readonly byte[] Png=MakePng();
    internal sealed class Storage : IFileStorage
    {
        public Dictionary<string,byte[]> Files=new(); public int Saves; public bool Fail;
        public async Task<string> SaveAsync(Stream content,string fileName,bool isWeb=true,string namePrefix="")
        {if(Fail)throw new IOException("storage unavailable"); using var ms=new MemoryStream(); await content.CopyToAsync(ms); string path="/upload/"+(++Saves)+".png";Files[path]=ms.ToArray();return path;}
        public Task<byte[]?> ReadAsync(string path,bool isWeb=true)=>Task.FromResult(Files.TryGetValue(path,out var b)?b:null);
        public string PublicUrl(string path)=>"https://images.test"+path;
    }
    internal sealed class Tokens : IMiniTicketTokenProvider
    {
        public int Calls; public Task<string> GetTokenAsync(bool forceRefresh=false){Calls++; return Task.FromResult(forceRefresh?"refreshed":"token");}
    }
    internal sealed class Handler : HttpMessageHandler
    {
        public int Calls; public List<string> Requests=new();public string? Error;public bool ExpireOnce; public byte[]? ImageBytes;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Calls++;Requests.Add(await request.Content!.ReadAsStringAsync(ct));
            if(ExpireOnce && Calls==1)return new(HttpStatusCode.OK){Content=new StringContent("{\"errcode\":40001,\"errmsg\":\"invalid token\"}")};
            return new(HttpStatusCode.OK){Content=Error==null?new ByteArrayContent(ImageBytes ?? Png):new StringContent(Error)};
        }
    }
    [Fact] public async Task CacheReusesPersistedBytesAcrossInstancesAndEnvironmentsAreIsolated()
    {
        await using var f=await TicketMigrationTests.Fixture.Create("mini_q");var storage=new Storage();var token=new Tokens();var handler=new Handler();var http=new HttpClient(handler);
        var qr=new MiniTicketQrService(f.Db,f.Config,storage,token,http);
        Assert.Equal(Png,await qr.GetAsync(1));
        Assert.Equal(Png,await new MiniTicketQrService(f.Db,f.Config,storage,token,http).GetAsync(1));
        Assert.Equal(1,handler.Calls);Assert.Equal(1,storage.Saves);Assert.Equal(1,await f.Db.UploadFile.CountAsync());
        await qr.GetAsync(1,"trial");Assert.Equal(2,handler.Calls);
        var release=JsonDocument.Parse(handler.Requests[0]).RootElement;var trial=JsonDocument.Parse(handler.Requests[1]).RootElement;
        Assert.Equal(MiniTicketRules.ClaimPage,release.GetProperty("page").GetString());Assert.Equal("batch=1",release.GetProperty("scene").GetString());
        Assert.True(release.GetProperty("check_path").GetBoolean());Assert.Equal("release",release.GetProperty("env_version").GetString());
        Assert.False(trial.GetProperty("check_path").GetBoolean());Assert.Equal("trial",trial.GetProperty("env_version").GetString());
    }
    [Fact] public async Task WechatJpegIsConvertedToPngAndCached()
    {
        await using var f=await TicketMigrationTests.Fixture.Create("mini_q");
        using var source=new Image<Rgba32>(40,40,new Rgba32(255,255,255,255));
        using var jpegStream=new MemoryStream(); source.SaveAsJpeg(jpegStream);
        var handler=new Handler{ImageBytes=jpegStream.ToArray()};var storage=new Storage();
        var service=new MiniTicketQrService(f.Db,f.Config,storage,new Tokens(),new HttpClient(handler));
        var result=await service.GetAsync(1,"develop");
        Assert.Equal(new byte[]{137,80,78,71,13,10,26,10},result.Take(8).ToArray());
        using var decoded=Image.Load(result);Assert.Equal(40,decoded.Width);Assert.Equal(40,decoded.Height);
        Assert.Equal(result,await service.GetAsync(1,"develop"));Assert.Equal(1,handler.Calls);Assert.Equal(1,storage.Saves);
    }
    [Fact] public async Task InvalidWechatImageIsRejectedWithoutCaching()
    {
        await using var f=await TicketMigrationTests.Fixture.Create("mini_q");
        var storage=new Storage();var handler=new Handler{ImageBytes=new byte[]{255,216,1,2,3}};
        var qr=new MiniTicketQrService(f.Db,f.Config,storage,new Tokens(),new HttpClient(handler));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>qr.GetAsync(1));
        Assert.Equal(0,storage.Saves);Assert.Equal(0,await f.Db.UploadFile.CountAsync());
    }
    [Fact] public async Task ConcurrentRequestsGenerateOnce()
    {
        await using var f=await TicketMigrationTests.Fixture.Create("mini_q");var storage=new Storage();var handler=new Handler();var qr=new MiniTicketQrService(f.Db,f.Config,storage,new Tokens(),new HttpClient(handler));
        var results=await Task.WhenAll(qr.GetAsync(1,"develop"),qr.GetAsync(1,"develop"));Assert.All(results,r=>Assert.Equal(Png,r));Assert.Equal(1,handler.Calls);
    }
    [Fact] public async Task WechatFailureDoesNotWriteCacheAndIncludesUpstreamError()
    {
        await using var f=await TicketMigrationTests.Fixture.Create("mini_q");var storage=new Storage();var handler=new Handler{Error="{\"errcode\":41030,\"errmsg\":\"invalid page\"}"};
        var qr=new MiniTicketQrService(f.Db,f.Config,storage,new Tokens(),new HttpClient(handler));
        var e=await Assert.ThrowsAsync<InvalidOperationException>(()=>qr.GetAsync(1));Assert.Contains("41030",e.Message);Assert.Equal(0,storage.Saves);Assert.Equal(0,await f.Db.UploadFile.CountAsync());
    }
    [Fact] public async Task StorageFailureDoesNotWriteSuccessfulMetadata()
    {
        await using var f=await TicketMigrationTests.Fixture.Create("mini_q");var qr=new MiniTicketQrService(f.Db,f.Config,new Storage{Fail=true},new Tokens(),new HttpClient(new Handler()));
        await Assert.ThrowsAsync<IOException>(()=>qr.GetAsync(1));Assert.Equal(0,await f.Db.UploadFile.CountAsync());
    }
    [Fact] public async Task InvalidEnvironmentAndOldBatchAreRejectedBeforeHttp()
    {
        await using var f=await TicketMigrationTests.Fixture.Create("qrcode");var handler=new Handler();var qr=new MiniTicketQrService(f.Db,f.Config,new Storage(),new Tokens(),new HttpClient(handler));
        await Assert.ThrowsAsync<ArgumentException>(()=>qr.GetAsync(1,"production"));await Assert.ThrowsAsync<InvalidOperationException>(()=>qr.GetAsync(1));Assert.Equal(0,handler.Calls);
    }
    [Fact] public async Task ExpiredTokenIsRefreshedOnlyOnce()
    {
        await using var f=await TicketMigrationTests.Fixture.Create("mini_q");var handler=new Handler{ExpireOnce=true};var token=new Tokens();var qr=new MiniTicketQrService(f.Db,f.Config,new Storage(),token,new HttpClient(handler));
        Assert.Equal(Png,await qr.GetAsync(1));Assert.Equal(2,handler.Calls);Assert.Equal(2,token.Calls);
    }

    [Fact] public async Task CacheMetadataFitsProductionColumnsAtLargestBatchIdAndSeparatesApps()
    {
        await using var f=await TicketMigrationTests.Fixture.Create("mini_q");
        await f.Db.ticketShareBatch.Where(b=>b.id==1).ExecuteDeleteAsync();f.Db.ticketShareBatch.Add(new TicketShareBatch{id=int.MaxValue,template_id=12,staff_id=9,share_type="mini_q"});await f.Db.SaveChangesAsync();f.Db.ChangeTracker.Clear();
        var storage=new Storage();var handler=new Handler();var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Settings:AppId"]="wx1234567890123456"}).Build();
        await new MiniTicketQrService(f.Db,config,storage,new Tokens(),new HttpClient(handler)).GetAsync(int.MaxValue,"develop");
        var metadata=await f.Db.UploadFile.SingleAsync();Assert.True(metadata.purpose.Length<=50,metadata.purpose);Assert.True(metadata.file_path_name.Length<=100);Assert.True(metadata.file_type.Length<=50);
        var other=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Settings:AppId"]="wx6543210987654321"}).Build();
        await new MiniTicketQrService(f.Db,other,storage,new Tokens(),new HttpClient(handler)).GetAsync(int.MaxValue,"develop");Assert.Equal(2,handler.Calls);
    }
    internal sealed class Qr : IMiniTicketQrService
    {
        public int Calls;public Task<byte[]> GetAsync(int batchId,string envVersion="release"){Calls++;return Task.FromResult(Png);}
    }
    [Fact] public async Task ImageEndpointsRequireActualStaffSessionAndReturnPng()
    {
        await using var f=await TicketMigrationTests.Fixture.Create("mini_q");var qr=new Qr();var poster=new TicketPosterController(f.Db,f.Config);
        Assert.IsType<BadRequestObjectResult>(await poster.GetMiniQrCode(1,"session-2",qr));Assert.Equal(0,qr.Calls);
        await f.AddStaff();var response=Assert.IsType<FileContentResult>(await poster.GetMiniQrCode(1,"session-1",qr));Assert.Equal("image/png",response.ContentType);Assert.Equal(Png,response.FileContents);
    }
    [Fact] public async Task NewPosterPreservesCoverSizeAndScaledQrPlacement()
    {
        await using var f=await TicketMigrationTests.Fixture.Create("mini_g");await f.AddStaff();var storage=new Storage();storage.Files["/upload/cover.png"]=MakePng(108,144,true);
        f.Db.UploadFile.Add(new UploadFile{id=10,file_path_name="/upload/cover.png",purpose="cover",file_type="image/png"});await f.Db.SaveChangesAsync();
        await f.Db.ticketTemplate.Where(t=>t.id==12).ExecuteUpdateAsync(s=>s.SetProperty(t=>t.cover_upload_id,10).SetProperty(t=>t.qr_x,100).SetProperty(t=>t.qr_y,200));
        var response=Assert.IsType<OkObjectResult>(await new TicketPosterController(f.Db,f.Config).GenerateMini(1,"session-1",storage,new Qr(),envVersion:"trial"));
        Assert.Equal(0,Assert.IsType<ApiResult<object>>(response.Value).code);using var image=Image.Load<Rgba32>(storage.Files["/upload/1.png"]);
        Assert.Equal(108,image.Width);Assert.Equal(144,image.Height);Assert.Equal(new Rgba32(0,0,0,255),image[10,20]);Assert.Equal(new Rgba32(0,0,0,255),image[33,43]);Assert.Equal(new Rgba32(255,255,255,255),image[34,44]);
    }
}
