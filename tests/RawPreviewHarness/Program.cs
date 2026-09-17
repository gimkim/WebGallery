using WebGallery.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Processing;

foreach(var raw in args.Skip(1)) {
    await using var preview=await RawPreview.OpenAsync(raw,args[0],default);
    using var image=await Image.LoadAsync(preview);
    Console.WriteLine($"Preview {Path.GetExtension(raw)}: {image.Width}x{image.Height}, {preview.Length} bytes");
    image.Mutate(x=>x.AutoOrient());
    foreach(var reduced in new[]{false,true}) {
        using var thumb=await ThumbnailService.DecodeAsync(raw,reduced,480,360,default,args[0]);
        thumb.Mutate(x=>x.AutoOrient().Resize(new ResizeOptions { Size=new Size(480,360),Mode=ResizeMode.Max }));
        if(thumb.Width>480 || thumb.Height>360)throw new Exception("Invalid bounds");
        if(Math.Abs((double)thumb.Width/thumb.Height-(double)image.Width/image.Height)>.01)throw new Exception("Orientation/aspect mismatch");
        Console.WriteLine($"PASS {(reduced?"fast":"full")} thumbnail {thumb.Width}x{thumb.Height}");
    }
}
