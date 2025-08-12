
namespace Smotrel.Messages
{
    public sealed class ResumeAvailableMessage
    {
        public Guid PartId { get; }
        public long PositionSeconds { get; }
        public string CourseRootPath { get; }

        public ResumeAvailableMessage(Guid partId, long positionSeconds, string courseRootPath)
        {
            PartId = partId;
            PositionSeconds = positionSeconds;
            CourseRootPath = courseRootPath;
        }
    }
}
