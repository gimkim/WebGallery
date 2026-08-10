using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WebGallery.Models;

namespace WebGallery.Data;

public sealed class GalleryDbContext(DbContextOptions<GalleryDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<FolderRule> FolderRules => Set<FolderRule>();
    public DbSet<ShareLink> ShareLinks => Set<ShareLink>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<GalleryCollection> Collections => Set<GalleryCollection>();
    public DbSet<GalleryCollectionFolder> CollectionFolders => Set<GalleryCollectionFolder>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<FolderRule>().HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ShareLink>().HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<GalleryCollection>().HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<GalleryCollectionFolder>().HasOne(x => x.Collection).WithMany(x => x.Folders).HasForeignKey(x => x.CollectionId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ShareLink>().HasOne(x => x.Collection).WithMany(x => x.ShareLinks).HasForeignKey(x => x.CollectionId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<FolderRule>().HasIndex(x => new { x.OwnerUserId, x.RelativePath }).IsUnique();
        builder.Entity<ShareLink>().HasIndex(x => x.Token).IsUnique();
        builder.Entity<GalleryCollection>().HasIndex(x => new { x.OwnerUserId, x.Name }).IsUnique();
        builder.Entity<GalleryCollectionFolder>().HasIndex(x => new { x.CollectionId, x.RelativePath }).IsUnique();
        builder.Entity<AppSetting>().HasKey(x => x.Key);
        builder.Entity<FolderRule>().Property(x => x.RelativePath).UseCollation("NOCASE");
        builder.Entity<GalleryCollection>().Property(x => x.Name).UseCollation("NOCASE");
        builder.Entity<GalleryCollectionFolder>().Property(x => x.RelativePath).UseCollation("NOCASE");
    }
}
