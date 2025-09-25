using System.ComponentModel.DataAnnotations;

namespace Bitcraft.ResourceFinder.Web.Models;

public enum ResourceStatus { Unconfirmed = 0, Confirmed = 1 }
public enum ReportReason { Incorrect = 0, ViolatesPolicy = 1 }
public enum ReportTargetType { Resource = 0, AcceptedImage = 1 }
public enum ReportStatus { Open = 0, Closed = 1 }


public class TypeItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required, MaxLength(64)] public string Name { get; set; } = "";
    [Required, MaxLength(64)] public string Slug { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public List<Resource> Resources { get; set; } = new();
}

public class Biome
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required, MaxLength(64)] public string Name { get; set; } = "";
    [Required, MaxLength(64)] public string Slug { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public List<Resource> Resources { get; set; } = new();
}

public class Resource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Range(1,10)] public int Tier { get; set; }
    [Required, MaxLength(80)] public string Name { get; set; } = "";
    [Required, MaxLength(80)] public string CanonicalName { get; set; } = "";
    public ResourceStatus Status { get; set; } = ResourceStatus.Unconfirmed;

    public Guid TypeId { get; set; }
    public TypeItem? Type { get; set; }

    public Guid BiomeId { get; set; }
    public Biome? Biome { get; set; }

    public string? Img256Url { get; set; }
    public string? Img512Url { get; set; }
    public string? ImagePhash { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string? SubmitterIp { get; set; }
    public string? SubmitterUserAgent { get; set; }

    public Guid? CreatedById { get; set; }
    public Guid? UpdatedById { get; set; }

    public List<ResourceAlias> Aliases { get; set; } = new();
}

public class ResourceAlias
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required, MaxLength(80)] public string Alias { get; set; } = "";
    [Required, MaxLength(80)] public string CanonicalAlias { get; set; } = "";
    public Guid ResourceId { get; set; }
    public Resource? Resource { get; set; }
}

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? ActorId { get; set; } // Identity UserId (string GUID)
    public string? ActorLabel { get; set; } // e.g., "Public@IP"
    [Required] public string Action { get; set; } = "";
    [Required] public string SubjectTable { get; set; } = "";
    public string? SubjectId { get; set; }
    public string? DiffJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class PendingImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ResourceId { get; set; }
    public Resource? Resource { get; set; }

    // store processed, webp thumbnails like accepted images
    public string Img256Url { get; set; } = "";
    public string Img512Url { get; set; } = "";
    public string? ImagePhash { get; set; }

    public string? UploadedByUserId { get; set; }   // IdentityUser Id (string GUID)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Report
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public ReportTargetType Target { get; set; } = ReportTargetType.Resource;
    public ReportReason Reason { get; set; } = ReportReason.Incorrect;

    public Guid ResourceId { get; set; }
    public Resource? Resource { get; set; }

    // If Target == AcceptedImage, this refers to the resource's current official image.
    // We don't have a row for accepted image; it lives on Resource itself.
    public string? Notes { get; set; }

    public ReportStatus Status { get; set; } = ReportStatus.Open;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? CreatedByUserId { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedByUserId { get; set; }
}
