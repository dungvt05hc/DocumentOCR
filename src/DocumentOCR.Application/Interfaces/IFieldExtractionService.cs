using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Entities;

namespace DocumentOCR.Application.Interfaces;

/// <summary>Maps raw OCR output fields to domain ExtractedField entities.</summary>
public interface IFieldExtractionService
{
    IReadOnlyList<ExtractedField> Extract(Guid documentId, OcrResult ocrResult);
}
