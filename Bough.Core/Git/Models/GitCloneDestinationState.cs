namespace Bough.Core.Git.Models
{
    public enum GitCloneDestinationState
    {
        Absent,
        EmptyDirectory,
        ContainsContent,
        InspectionFailed
    }
}
