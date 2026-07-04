namespace DocumentOCR.Application.Exceptions;

/// <summary>
/// Thrown when a resolved storage path would escape the configured storage root.
/// Distinct from <see cref="UnauthorizedAccessException"/> so storage path validation
/// failures are never confused with real user authorization failures.
/// </summary>
public class PathTraversalException : Exception
{
    public PathTraversalException(string message) : base(message)
    {
    }
}
