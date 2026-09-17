using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebGallery.Data;
using WebGallery.Models;
using WebGallery.Services;

namespace WebGallery.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore=true, Location=ResponseCacheLocation.None)]
public sealed class FilesController(GalleryDbContext db, UserManager<ApplicationUser> users, UploadSessions uploads,
    GalleryIndexService index, ThumbnailService thumbnails) : Controller
{
    public record FolderRequest(string Path, string Name);
    public record StartRequest(string Path, string RelativePath, long Size);
    public record MoveRequest(string Destination, string[] Paths);
    private async Task<UserRoot> Root() => await db.UserRoots.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == users.GetUserId(User))
        ?? throw new InvalidOperationException("No root folder is configured for this account.");
    private string Owner => users.GetUserId(User)!;
    private void Refresh(UserRoot root, string path) => index.Refresh(root.Id,
        path.Length == FileSystemService.RootMarker(root.Id).Length ? "" : path[(FileSystemService.RootMarker(root.Id).Length+1)..], true);
    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try { return Json(await action()); }
        catch (UnauthorizedAccessException) { return StatusCode(403, new { error="Access denied. Check the application's write permissions and root folder settings." }); }
        catch (KeyNotFoundException e) { return NotFound(new { error=e.Message }); }
        catch (DirectoryNotFoundException) { return NotFound(new { error="The folder no longer exists or is unavailable." }); }
        catch (IOException) { return Conflict(new { error="File operation failed: a name may already exist, the file may be in use, or storage may be unavailable/full." }); }
        catch (InvalidOperationException e) { return BadRequest(new { error=e.Message }); }
        catch (ArgumentException) { return BadRequest(new { error="Invalid file or folder path." }); }
    }
    [HttpGet]
    public Task<IActionResult> Folders(string? path) => Run(async () => {
        var root=await Root(); path=string.IsNullOrEmpty(path)?FileSystemService.RootMarker(root.Id):path;
        var physical=WritePaths.Resolve(root,path);
        var children=new DirectoryInfo(physical).EnumerateDirectories().Where(d=>!FileSystemService.IsIgnoredFileSystemEntry(d.FullName) && !FileSystemService.IsReparsePoint(d.FullName))
            .OrderBy(d=>d.Name).Select(d=>new { name=d.Name,path=path+"/"+d.Name }).ToArray();
        return new { root=FileSystemService.RootMarker(root.Id),rootName=root.Name,path,children };
    });
    [HttpPost]
    public Task<IActionResult> CreateFolder([FromBody] FolderRequest request) => Run(async () => {
        await uploads.Mutations.WaitAsync(HttpContext.RequestAborted);
        try {
            var root=await Root(); var parent=WritePaths.Resolve(root,request.Path);
            var path=Path.Combine(parent,WritePaths.Name(request.Name));
            if (Directory.Exists(path) || System.IO.File.Exists(path)) throw new IOException();
            Directory.CreateDirectory(path); Refresh(root,request.Path); return new { path=request.Path+"/"+request.Name };
        } finally { uploads.Mutations.Release(); }
    });
    [HttpPost]
    public Task<IActionResult> EnsureUploadFolder([FromBody] FolderRequest request) => Run(async () => {
        await uploads.Mutations.WaitAsync(HttpContext.RequestAborted);
        try {
            var root=await Root();WritePaths.Resolve(root,request.Path);
            var logical=request.Path+"/"+WritePaths.Name(request.Name);
            var physical=WritePaths.Resolve(root,logical,false);
            if(System.IO.File.Exists(physical))throw new IOException();
            Directory.CreateDirectory(physical);Refresh(root,request.Path);return new { path=logical };
        } finally { uploads.Mutations.Release(); }
    });
    [HttpPost]
    public Task<IActionResult> Start([FromBody] StartRequest request) => Run(async () => {
        var root=await Root();WritePaths.Resolve(root,request.Path);
        var relative=WritePaths.Relative(request.RelativePath);
        var target=WritePaths.Resolve(root,request.Path+"/"+relative,false);
        if (System.IO.File.Exists(target) || Directory.Exists(target)) throw new IOException();
        var session=uploads.Start(Owner,root,request.Path,relative,request.Size);
        return new { id=session.Id,offset=0,chunkSize=UploadSessions.ChunkSize };
    });
    [HttpGet]
    public Task<IActionResult> Status(string id) => Run(async () => {
        await Root();var s=uploads.Get(Owner,id);await s.Gate.WaitAsync(HttpContext.RequestAborted);
        try { return new { offset=s.Offset,complete=s.Complete,cancelled=s.Cancelled }; } finally { s.Gate.Release(); }
    });
    [HttpPost]
    [RequestSizeLimit(UploadSessions.ChunkSize)]
    public Task<IActionResult> Chunk(string id, long offset) => Run(async () => {
        var s=uploads.Get(Owner,id);await s.Gate.WaitAsync(HttpContext.RequestAborted);
        try {
            if (s.Cancelled || s.Complete) throw new InvalidOperationException("This upload is no longer accepting data.");
            if (offset!=s.Offset) return new { offset=s.Offset };
            if (Request.ContentLength is not long count || count<1 || count>UploadSessions.ChunkSize || offset+count>s.Size)
                throw new InvalidOperationException("Invalid upload segment size.");
            await using var file=new FileStream(s.Temp,FileMode.Open,FileAccess.Write,FileShare.None,65536,true);
            file.Position=offset;var buffer=new byte[65536];long received=0;
            try {
                while (received<count) {
                    var read=await Request.Body.ReadAsync(buffer.AsMemory(0,(int)Math.Min(buffer.Length,count-received)),HttpContext.RequestAborted);
                    if(read==0) throw new IOException();
                    await file.WriteAsync(buffer.AsMemory(0,read),HttpContext.RequestAborted);received+=read;
                }
                await file.FlushAsync(HttpContext.RequestAborted);s.Offset+=received;s.Updated=DateTime.UtcNow;
            } catch { file.SetLength(offset);throw; }
            return new { offset=s.Offset };
        } finally { s.Gate.Release(); }
    });
    [HttpPost]
    public Task<IActionResult> Complete(string id) => Run(async () => {
        var s=uploads.Get(Owner,id);await s.Gate.WaitAsync(HttpContext.RequestAborted);
        try {
            if (s.Complete) return new { complete=true,path=s.Destination+"/"+s.Relative };
            if (s.Cancelled || s.Offset!=s.Size) throw new InvalidOperationException("Upload is cancelled or incomplete.");
            var root=await Root();
            if(root.Id!=s.RootId || !FileSystemService.PathsEqual(root.PhysicalPath,s.RootPath)) throw new InvalidOperationException("Your root folder changed. Restart the upload.");
            var logical=s.Destination+"/"+s.Relative;string target;
            await uploads.Mutations.WaitAsync(HttpContext.RequestAborted);
            try {
                WritePaths.Resolve(root,s.Destination);
                target=WritePaths.Resolve(root,logical,false);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                WritePaths.Resolve(root,FileSystemService.GetParent(logical)!);
                // Stage on the destination filesystem; only the final rename exposes the file.
                var stage=Path.Combine(Path.GetDirectoryName(target)!,UploadSessions.PublishName(s.Id));
                await System.IO.File.WriteAllTextAsync(UploadSessions.JournalPath(s),System.Text.Json.JsonSerializer.Serialize(
                    new UploadSessions.PublishJournal(s.Id,Owner,root.Id,root.PhysicalPath,FileSystemService.GetParent(logical)!)),HttpContext.RequestAborted);
                try {
                    await using (var input=System.IO.File.OpenRead(s.Temp))
                    await using (var output=new FileStream(stage,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
                        await input.CopyToAsync(output,HttpContext.RequestAborted);
                    var assigned=await Root();
                    if(assigned.Id!=root.Id || !FileSystemService.PathsEqual(assigned.PhysicalPath,root.PhysicalPath))
                        throw new InvalidOperationException("Your root folder changed. Restart the upload.");
                    WritePaths.Resolve(assigned,FileSystemService.GetParent(logical)!);
                    System.IO.File.Move(stage,target,false);s.Complete=true;s.Updated=DateTime.UtcNow;
                } finally {
                    if(System.IO.File.Exists(stage)) System.IO.File.Delete(stage);
                    if(System.IO.File.Exists(UploadSessions.JournalPath(s)))System.IO.File.Delete(UploadSessions.JournalPath(s));
                }
                System.IO.File.Delete(s.Temp);
            } finally { uploads.Mutations.Release(); }
            Refresh(root,s.Destination);Refresh(root,FileSystemService.GetParent(logical)!);
            string? warning=null;
            if(FileSystemService.HasThumbnail(Path.GetExtension(target))) {
                try { await thumbnails.GetOrCreateAsync(Owner,target,ThumbnailPriority.Normal,CancellationToken.None); }
                catch { warning="Upload saved, but the thumbnail could not be created. It will retry when viewed."; }
            }
            return new { complete=true,path=logical,warning };
        } finally { s.Gate.Release(); }
    });
    [HttpPost]
    public Task<IActionResult> Cancel(string id) => Run(async () => {
        var s=uploads.Get(Owner,id);await s.Gate.WaitAsync(HttpContext.RequestAborted);
        try { if(!s.Complete) { s.Cancelled=true;if(System.IO.File.Exists(s.Temp))System.IO.File.Delete(s.Temp); }return new { cancelled=!s.Complete,complete=s.Complete }; }
        finally { s.Gate.Release(); }
    });
    [HttpPost]
    public Task<IActionResult> Move([FromBody] MoveRequest request) => Run(async () => {
        if(request.Paths is not { Length: >0 and <=1000 }) throw new InvalidOperationException("Select 1–1000 items.");
        await uploads.Mutations.WaitAsync(HttpContext.RequestAborted);
        try {
            var root=await Root();var target=WritePaths.Resolve(root,request.Destination);
            var operations=request.Paths.Distinct(FileSystemService.PathComparer).Select(path=>{
                var from=WritePaths.Resolve(root,path,false);
                var directory=Directory.Exists(from);
                if(!directory && !System.IO.File.Exists(from)) throw new InvalidOperationException("The source item no longer exists.");
                if(FileSystemService.PathsEqual(path,FileSystemService.RootMarker(root.Id)))
                    throw new InvalidOperationException("The configured root folder cannot be moved.");
                if(directory && FileSystemService.IsWithinShareScope(path,request.Destination))
                    throw new InvalidOperationException("A folder cannot be moved into itself or one of its descendants.");
                if(directory && uploads.HasActiveUploadInside(root.Id,path))
                    throw new InvalidOperationException("This folder has active uploads. Finish or cancel them before moving it.");
                var name=WritePaths.Name(Path.GetFileName(from));var to=Path.Combine(target,name);
                if(FileSystemService.PathsEqual(from,to) || System.IO.File.Exists(to) || Directory.Exists(to)) throw new IOException();
                return (path,from,to,directory);
            }).ToList();
            if(operations.Any(a=>a.directory && operations.Any(b=>a.path!=b.path && FileSystemService.IsWithinShareScope(a.path,b.path))))
                throw new InvalidOperationException("Do not move a folder and its contents in the same batch.");
            if(operations.Select(x=>x.to).Distinct(FileSystemService.PathComparer).Count()!=operations.Count) throw new IOException();
            var moved=new List<string>();string? error=null;
            foreach(var op in operations) {
                try {
                    WritePaths.Resolve(root,op.path,false);WritePaths.Resolve(root,request.Destination);
                    if(op.directory)Directory.Move(op.from,op.to);else System.IO.File.Move(op.from,op.to,false);
                    moved.Add(op.path);
                }
                catch(IOException) { error="Some files could not be moved (in use, name conflict or storage error). Completed moves were retained.";break; }
                catch(UnauthorizedAccessException) { error="Some files could not be moved: permission denied. Completed moves were retained.";break; }
            }
            foreach(var parent in moved.Select(x=>FileSystemService.GetParent(x)!).Append(request.Destination).Distinct())Refresh(root,parent);
            return new { moved,error };
        } finally { uploads.Mutations.Release(); }
    });
}
