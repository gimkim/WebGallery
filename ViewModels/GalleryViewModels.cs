using WebGallery.Models;
using WebGallery.Services;

namespace WebGallery.ViewModels;

public sealed record GalleryItemViewModel(
    string Name,
    string RelativePath,
    bool IsDirectory,
    bool IsImage,
    long Size,
    DateTimeOffset ModifiedUtc,
    string Extension,
    IReadOnlyList<ThumbnailSourceViewModel> CoverImages);

public sealed record ThumbnailSourceViewModel(string RelativePath, string CacheStamp);

public sealed class GalleryViewModel
{
    public required string Title { get; init; }
    public required string OwnerUserName { get; init; }
    public string Path { get; init; } = "";
    public string? ParentPath { get; init; }
    public string Sort { get; init; } = "name";
    public string Direction { get; init; } = "asc";
    public string BrowseMode { get; init; } = "private";
    public string? ShareToken { get; init; }
    public string ShareRootPath { get; init; } = "";
    public bool CanManage { get; init; }
    public string FocusPath { get; init; } = "";
    public int DefaultItemsPerRow { get; init; } = 8;
    public int? InitialItemsPerRow { get; init; }
    public string? InitialViewMode { get; init; }
    public required IReadOnlyList<GalleryItemViewModel> Items { get; init; }
    public IReadOnlyList<ShareLinkManagementViewModel> ShareLinks { get; init; } = [];
    public bool IsCollectionShare { get; init; }
    public bool IsCollectionRoot { get; init; }
    public string CollectionName { get; init; } = "";
}

public sealed class CollectionsIndexViewModel
{
    public required string OwnerUserName { get; init; }
    public required IReadOnlyList<CollectionManagementViewModel> Collections { get; init; }
    public int DefaultItemsPerRow { get; init; } = 8;
}

public sealed class CollectionManagementViewModel
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<CollectionFolderCardViewModel> Folders { get; init; }
    public required IReadOnlyList<ShareLinkManagementViewModel> ShareLinks { get; init; }
}

public sealed class ShareLinkManagementViewModel
{
    public required ShareLink Link { get; init; }
    public int AccessCount { get; init; }
    public int ViewCount { get; init; }
    public int DownloadCount { get; init; }
    public int UniqueVisitorCount { get; init; }
    public DateTimeOffset? LastActivityUtc { get; init; }
}

public sealed class ShareActivityViewModel
{
    public required ShareLink Link { get; init; }
    public required string ShareLabel { get; init; }
    public required ShareLinkManagementViewModel Summary { get; init; }
    public required IReadOnlyList<ShareAuditEvent> Events { get; init; }
    public int Page { get; init; }
    public int TotalPages { get; init; }
}

public sealed class CollectionFolderCardViewModel
{
    public required int MembershipId { get; init; }
    public required string RelativePath { get; init; }
    public GalleryItemViewModel? Item { get; init; }
}

public sealed class CollectionFolderPickerViewModel
{
    public required int CollectionId { get; init; }
    public required string CollectionName { get; init; }
    public required string OwnerUserName { get; init; }
    public required string Path { get; init; }
    public string? ParentPath { get; init; }
    public string Sort { get; init; } = "name";
    public string Direction { get; init; } = "asc";
    public int ExistingFolderCount { get; init; }
    public int DefaultItemsPerRow { get; init; } = 8;
    public required IReadOnlyList<CollectionFolderPickerItemViewModel> Folders { get; init; }
}

public sealed class CollectionFolderPickerItemViewModel
{
    public required GalleryItemViewModel Item { get; init; }
    public int? DirectMembershipId { get; init; }
    public string? IncludedByParentPath { get; init; }
    public bool IsAlreadyIncluded => DirectMembershipId.HasValue || IncludedByParentPath is not null;
    public bool ContainsIncludedFolder { get; init; }
}

public sealed class LoginViewModel
{
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
    public int? RetryAfterSeconds { get; set; }
}

public sealed class CooldownViewModel
{
    public required string Title { get; init; }
    public required string Message { get; init; }
    public required int RetryAfterSeconds { get; init; }
}

public sealed class AdminUserViewModel
{
    public required string Id { get; init; }
    public required string UserName { get; init; }
    public string DisplayName { get; init; } = "";
    public string RootFolder { get; init; } = "";
    public bool IsAdmin { get; init; }
}

public sealed class AdminIndexViewModel
{
    public required IReadOnlyList<AdminUserViewModel> Users { get; init; }
    public string AppTitle { get; init; } = "Gim Gallery";
    public string Theme { get; init; } = "retro";
    public int ThumbnailConcurrency { get; init; } = ThumbnailQueueSettings.DefaultConcurrency;
}
