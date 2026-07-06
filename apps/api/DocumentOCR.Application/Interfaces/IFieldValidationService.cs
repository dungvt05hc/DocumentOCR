using DocumentOCR.Domain.Entities;

namespace DocumentOCR.Application.Interfaces;

/// <summary>Validates extracted fields and produces ValidationWarning entities.</summary>
public interface IFieldValidationService
{
    IReadOnlyList<ValidationWarning> Validate(Guid documentId, IEnumerable<ExtractedField> fields);
}
