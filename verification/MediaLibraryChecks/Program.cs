using System.Security.Claims;
using Lensora.Api.Controllers;
using Lensora.Api.Contracts;
using Lensora.Api.Domain;
using Lensora.Api.Infrastructure;
using Lensora.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
var config=new ConfigurationBuilder().AddJsonFile(Path.GetFullPath("appsettings.json")).AddUserSecrets(typeof(Lensora.Api.Services.JwtTokenService).Assembly, optional: true).AddEnvironmentVariables().Build();
var options=new DbContextOptionsBuilder<LensoraDbContext>().UseSqlServer(config.GetConnectionString("Lensora") ?? config.GetConnectionString("DefaultConnection")).Options;
await using var setup=new LensoraDbContext(options);
var user=new User {Email=$"media-check-{Guid.NewGuid():N}@example.invalid",PasswordHash="not-a-login",Role="Photographer"};
var profile=new Photographer {User=user,Name="Temporary media check",Slug=$"media-check-{Guid.NewGuid():N}",StorageLimitBytes=100};
setup.Photographers.Add(profile); await setup.SaveChangesAsync();
var provider=new FakeStore();
AdminMediaController Controller(LensoraDbContext db,int owner) {
    var context=new DefaultHttpContext(); context.Request.Path="/api/admin/media/files";
    context.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,owner.ToString())},"test"));
    return new(provider,db,NullLogger<AdminMediaController>.Instance){ControllerContext=new(){HttpContext=context}};
}
IFormFile Image(byte fill,string name="sample.png") {
    var bytes=Enumerable.Repeat(fill,64).Select(x=>(byte)x).ToArray();
    Convert.FromHexString("89504E470D0A1A0A").CopyTo(bytes,0);
    return new FormFile(new MemoryStream(bytes),0,bytes.Length,"file",name){Headers=new HeaderDictionary(),ContentType="image/png"};
}
try {
    await using var db1=new LensoraDbContext(options);
    await using var db2=new LensoraDbContext(options);
    var c1=Controller(db1,user.Id); var c2=Controller(db2,user.Id);
    Check(await Controller(db2,0).List(default) is ForbidResult,"Unknown owner must be denied");
    var first=c1.Upload(new(){File=Image(1)},default);
    await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(20));
    Check(await setup.MediaAssets.CountAsync(x=>x.PhotographerId==profile.Id && x.Status=="Pending")==1,"Quota reservation must be durable before remote upload");
    var duplicate=await c2.Upload(new(){File=Image(1,"renamed.png")},default);
    Check(duplicate is ConflictObjectResult,"Concurrent duplicate must not reupload");
    var full=await c2.Upload(new(){File=Image(2)},default);
    Check(full is ObjectResult{StatusCode:413},"Concurrent different upload must not exceed reserved quota");
    provider.Release.TrySetResult();
    Check(await first is OkObjectResult,"Upload completes");
    await using var reuseDb=new LensoraDbContext(options);
    var reuse=await Controller(reuseDb,user.Id).Upload(new(){File=Image(1,"renamed.png")},default);
    Check(reuse is OkObjectResult && provider.Uploads==1,"Exact duplicate with another name must reuse the existing provider asset");
    Check(await setup.MediaAssets.CountAsync(x=>x.PhotographerId==profile.Id)==1,"No duplicate database media asset");
    // A new request/db context must read only this user's library.
    await using var db3=new LensoraDbContext(options);
    var listing=await Controller(db3,user.Id).List(default) as OkObjectResult;
    var json=System.Text.Json.JsonSerializer.Serialize(listing!.Value);
    Check(json.Contains("\"usedBytes\":64") && json.Contains("\"limitBytes\":100"),"Usage and limit must be exact");
    var invalid=new FormFile(new MemoryStream(new byte[16]),0,16,"file","script.png"){Headers=new HeaderDictionary(),ContentType="image/png"};
    Check(await Controller(db3,user.Id).Upload(new(){File=invalid},default) is BadRequestObjectResult,"Renamed/non-image bytes must be rejected");
    var legacy=new MediaAsset{PhotographerId=profile.Id,FileName="old.png",Url="https://example.invalid/old",PublicId="old",Kind="image",ContentType="image/png",Status="Ready",CreatedUtc=DateTime.UtcNow};
    setup.MediaAssets.Add(legacy);await setup.SaveChangesAsync();
    Check(await Controller(db3,user.Id).Upload(new(){File=Image(3)},default) is ConflictObjectResult,"Unmeasured legacy assets must block quota bypass");
    Check(provider.Uploads==1,"Rejected uploads must not reach storage provider");
    setup.MediaAssets.Remove(legacy);await setup.SaveChangesAsync();
    await using var recoveryDb=new LensoraDbContext(options);
    var reserved=await recoveryDb.MediaAssets.SingleAsync(x=>x.PhotographerId==profile.Id);
    reserved.Status="Pending";reserved.CreatedUtc=DateTime.UtcNow.AddMinutes(-11);await recoveryDb.SaveChangesAsync();
    Check(await Controller(recoveryDb,user.Id).Sync(default) is OkObjectResult,"Old pending upload can reconcile");
    Check(reserved.Status=="Retryable","Provider-missing upload keeps its reservation and becomes retryable");
    await using var retryDb=new LensoraDbContext(options);
    Check(await Controller(retryDb,user.Id).Upload(new(){File=Image(1)},default) is OkObjectResult,"Retry uses the existing reserved asset even with only 36 bytes remaining");
    Check(await setup.MediaAssets.CountAsync(x=>x.PhotographerId==profile.Id)==1,"Retry must not create another reservation");
    Console.WriteLine("PASS: owner checks; durable reservations; concurrent quota enforcement; concurrent duplicate rejection; renamed-file reuse; exact byte totals; signature validation; unknown-size protection. No real cloud uploads.");
} finally {
    provider.Release.TrySetResult();
    setup.MediaAssets.RemoveRange(await setup.MediaAssets.Where(x=>x.PhotographerId==profile.Id).ToListAsync());
    setup.Photographers.Remove(profile);setup.Users.Remove(user);await setup.SaveChangesAsync();
}

sealed class FakeStore() : CloudinaryMediaService(new ConfigurationBuilder().Build()) {
    public TaskCompletionSource Started {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Uploads;
    public override void CheckConfiguration() {}
    public override Task<(bool Missing,long Bytes,string Url)> InspectAsset(string publicId,string kind,CancellationToken ct) => Task.FromResult((true,0L,""));
    public override async Task<string> UploadAsset(IFormFile file,string publicId,string kind,CancellationToken ct) {
        Interlocked.Increment(ref Uploads);Started.TrySetResult();await Release.Task.WaitAsync(ct);return "https://example.invalid/asset.png";
    }
}
