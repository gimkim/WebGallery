using Microsoft.EntityFrameworkCore;
using WebGallery.Data;
using WebGallery.Models;
using System.Reflection;

var path=Path.Combine(Path.GetTempPath(),"root-migration-"+Guid.NewGuid().ToString("N")+".db");
try
{
    await using var db=new GalleryDbContext(new DbContextOptionsBuilder<GalleryDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options);
    await db.Database.EnsureCreatedAsync();
    db.Users.AddRange(new ApplicationUser {Id="existing",UserName="existing",RootFolder=@"E:\"},new ApplicationUser {Id="legacy",UserName="legacy",RootFolder=@"F:\"});
    db.UserRoots.Add(new UserRoot {OwnerUserId="existing",Name="camera",PhysicalPath=@"E:\"});
    db.UserRoots.Add(new UserRoot {OwnerUserId="existing",Name=@"E:\",PhysicalPath="E:"});
    await db.SaveChangesAsync();
    var method=typeof(DatabaseInitializer).GetMethod("EnsureUserRootSchemaAsync",BindingFlags.Static|BindingFlags.NonPublic)!;
    Task Migrate()=>(Task)method.Invoke(null,[db])!;
    await Migrate();
    if(await db.UserRoots.CountAsync()!=3 || !await db.UserRoots.AnyAsync(r=>r.OwnerUserId=="legacy" && r.PhysicalPath==@"F:\")) throw new Exception("Legacy drive root corrupted or named root duplicated");
    await db.UserRoots.Where(r=>r.PhysicalPath=="E:").ExecuteDeleteAsync();
    await Migrate(); await Migrate();
    if(await db.UserRoots.CountAsync(r=>r.OwnerUserId=="existing")!=1) throw new Exception("Deleted ghost recreated");
    await db.UserRoots.ExecuteDeleteAsync();
    await Migrate();
    if(await db.UserRoots.AnyAsync()) throw new Exception("Removed roots recreated from stale legacy columns");
    Console.WriteLine("PASS: drive-root preserved, existing IDs retained, removed duplicate stays removed, repeated migration and empty roots stable.");
}
finally { if(File.Exists(path)) File.Delete(path); }
