namespace Bitcraft.ResourceFinder.Web.Models
{
    public class ReportForm
    {
        public ReportTargetType Target { get; set; } = ReportTargetType.Resource;
        public ReportReason Reason { get; set; } = ReportReason.Incorrect;
        public string? Notes { get; set; }
    }
}