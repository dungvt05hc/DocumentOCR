namespace DocumentOCR.Application.Models;

/// <summary>
/// Normalized polygon bounding box (coordinates are in page unit space as returned
/// by the provider — callers may normalize to 0–1 using page width/height).
/// </summary>
public record BoundingBox(IReadOnlyList<BoundingPoint> Points)
{
    /// <summary>Creates a rectangular bounding box from top-left origin and dimensions.</summary>
    public static BoundingBox FromRect(double x, double y, double width, double height) =>
        new([
            new(x, y),
            new(x + width, y),
            new(x + width, y + height),
            new(x, y + height)
        ]);
}

/// <summary>A single point in 2-D page space.</summary>
public record BoundingPoint(double X, double Y);
