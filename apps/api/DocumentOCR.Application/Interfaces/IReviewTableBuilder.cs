using DocumentOCR.Application.DTOs;
using DocumentOCR.Application.Models;

namespace DocumentOCR.Application.Interfaces;

/// <summary>
/// Reshapes provider-neutral <see cref="OcrTable"/> data into review/export-friendly
/// <see cref="ReviewTable"/>s, and derives basic candidate <see cref="ReviewLineItem"/>s from
/// them. Shared by <c>DocumentReviewMappingService</c> and the Excel export service so the
/// column-normalization vocabulary (Description/Quantity/UnitPrice/Amount, English + Vietnamese)
/// lives in exactly one place.
/// </summary>
public interface IReviewTableBuilder
{
    List<ReviewTable> BuildTables(IReadOnlyList<OcrTable> tables);
    List<ReviewLineItem> BuildLineItems(IReadOnlyList<ReviewTable> tables);
}
