using System.Text.Json;
using DocumentOCR.Application.DTOs;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Application.Services;

/// <summary>
/// Builds the dynamic <see cref="DocumentReviewResponse"/> for a <see cref="Document"/> by
/// resolving its review category/profile (via <see cref="IDocumentProfileCatalog"/>) and mapping
/// whatever <see cref="ExtractedField"/> rows exist onto that profile's sections/fields. A
/// required field with no matching data is still included, marked <c>IsMissing = true</c> — it is
/// never silently dropped. Caller must have loaded <c>Document.Fields</c>/<c>ValidationWarnings</c>/
/// <c>OcrProviderLogs</c>/<c>Pages</c> (same include shape <see cref="DocumentService.GetByIdAsync"/> already uses).
/// </summary>
public class DocumentReviewMappingService
{
    private const string ExperimentalCategoryWarningCode = "EXPERIMENTAL_DOCUMENT_CATEGORY";
    private const string OtherFieldsSectionKey = "other";

    private readonly IDocumentProfileCatalog _profileCatalog;
    private readonly IReviewTableBuilder _tableBuilder;

    public DocumentReviewMappingService(IDocumentProfileCatalog profileCatalog, IReviewTableBuilder tableBuilder)
    {
        _profileCatalog = profileCatalog;
        _tableBuilder = tableBuilder;
    }

    /// <param name="includeDebugSummary">
    /// When true, populates <see cref="DocumentReviewResponse.DebugData"/> with a lightweight
    /// summary (full text + artifact paths). The richer view (lines/paragraphs/key-value pairs)
    /// is only available via the dedicated <c>GET /api/documents/{id}/ocr-debug</c> endpoint.
    /// </param>
    public DocumentReviewResponse Map(Document document, bool includeDebugSummary = false)
    {
        var fieldsByName = document.Fields
            .GroupBy(f => f.FieldName)
            .ToDictionary(g => g.Key, g => g.First());

        var category = ResolveCategory(fieldsByName, document.DocumentType);
        var profile = _profileCatalog.GetProfile(category);

        // The "DocumentType" pseudo-field is an internal category-resolution signal, not
        // user-facing data — treat it as already consumed so it never shows up as a mystery
        // "Other detected field" or dilutes the overall confidence average.
        var matchedFieldNames = new HashSet<string> { nameof(FieldName.DocumentType) };
        var sections = profile.Sections
            .OrderBy(s => s.DisplayOrder)
            .Select(section => MapSection(section, fieldsByName, document.ValidationWarnings, matchedFieldNames))
            .ToList();

        var otherFields = document.Fields
            .Where(f => !matchedFieldNames.Contains(f.FieldName))
            .OrderBy(f => f.FieldName)
            .Select(f => MapPopulatedField(
                new ProfileFieldDefinition { FieldKey = f.FieldName, Label = Humanize(f.FieldName), DisplayOrder = 0 },
                f,
                document.ValidationWarnings))
            .ToList();

        if (otherFields.Count > 0)
        {
            sections.Add(new ReviewSection
            {
                SectionKey = OtherFieldsSectionKey,
                Title = "Other detected fields",
                DisplayOrder = sections.Count,
                Fields = otherFields
            });
        }

        var warnings = document.ValidationWarnings
            .Select(w => new ReviewWarningDto
            {
                Severity = w.Severity,
                FieldKey = w.FieldName,
                WarningCode = w.WarningCode,
                Message = w.Message
            })
            .ToList();

        if (profile.IsExperimental)
        {
            warnings.Add(new ReviewWarningDto
            {
                Severity = ValidationSeverity.Info,
                WarningCode = ExperimentalCategoryWarningCode,
                Message = "This document category has lower extraction reliability (e.g. handwritten or mixed-format bills) — please review every field carefully."
            });
        }

        var latestLog = document.OcrProviderLogs.OrderByDescending(l => l.CreatedAt).FirstOrDefault();
        var confidences = sections.SelectMany(s => s.Fields).Where(f => f.Confidence.HasValue).Select(f => f.Confidence!.Value).ToList();

        var tables = _tableBuilder.BuildTables(DeserializeTables(document.TablesJson));
        var lineItems = _tableBuilder.BuildLineItems(tables);

        return new DocumentReviewResponse
        {
            DocumentId = document.Id,
            FileName = document.OriginalFileName,
            ContentType = document.ContentType,
            Status = document.Status,
            DocumentCategory = category,
            Direction = document.Direction,
            DocumentSubType = document.DocumentType.ToString(),
            ProviderName = latestLog?.ProviderName,
            ModelId = latestLog?.ModelId,
            ProcessedAt = document.ProcessingCompletedAt,
            OverallConfidence = confidences.Count > 0 ? confidences.Average() : null,
            Sections = sections,
            Warnings = warnings,
            Tables = tables,
            LineItems = lineItems,
            TaxBreakdown = document.TaxBreakdowns
                .OrderBy(t => t.SortOrder)
                .Select(t => new ReviewTaxBreakdownRow
                {
                    Id = t.Id,
                    RawVatRate = t.RawVatRate,
                    VatRate = t.VatRate,
                    TaxableAmount = t.TaxableAmount,
                    TaxAmount = t.TaxAmount,
                    Confidence = t.Confidence,
                    SortOrder = t.SortOrder
                })
                .ToList(),
            DebugData = includeDebugSummary ? BuildDebugSummary(document, latestLog, tables.Count) : null
        };
    }

    private static IReadOnlyList<OcrTable> DeserializeTables(string? tablesJson)
    {
        if (string.IsNullOrWhiteSpace(tablesJson)) return [];
        return JsonSerializer.Deserialize<List<OcrTable>>(tablesJson) ?? [];
    }

    private static OcrDebugData BuildDebugSummary(Document document, OcrProviderLog? latestLog, int tableCount)
    {
        var fullText = string.Join(
            "\n\n",
            document.Pages.OrderBy(p => p.PageNumber).Select(p => p.RawText));

        return new OcrDebugData
        {
            FullText = fullText,
            RawProviderResponsePath = latestLog?.RawResponsePath,
            NormalizedOcrResultPath = latestLog?.NormalizedResultPath,
            OcrSummary = $"{document.PageCount} page(s), {tableCount} table(s), {document.Fields.Count} field(s), {document.ValidationWarnings.Count} warning(s)"
        };
    }

    /// <summary>
    /// Resolves the review category from the same "DocumentType" pseudo-field
    /// <see cref="Infrastructure.Processing.FieldValidationService"/> reads (raw value first,
    /// falling back to the legacy <see cref="Document.DocumentType"/> when absent/unparseable).
    /// </summary>
    private DocumentCategory ResolveCategory(
        IReadOnlyDictionary<string, ExtractedField> fieldsByName,
        DocumentType fallbackDocumentType)
    {
        var documentTypeField = fieldsByName.GetValueOrDefault(nameof(FieldName.DocumentType));
        var rawValue = documentTypeField is null
            ? null
            : !string.IsNullOrWhiteSpace(documentTypeField.NormalizedValue) ? documentTypeField.NormalizedValue : documentTypeField.RawValue;

        return _profileCatalog.ResolveCategory(rawValue, fallbackDocumentType);
    }

    private ReviewSection MapSection(
        ProfileSection section,
        IReadOnlyDictionary<string, ExtractedField> fieldsByName,
        ICollection<ValidationWarning> warnings,
        HashSet<string> matchedFieldNames)
    {
        var fields = section.Fields
            .OrderBy(f => f.DisplayOrder)
            .Select(f => MapField(f, fieldsByName, warnings, matchedFieldNames))
            .ToList();

        return new ReviewSection
        {
            SectionKey = section.SectionKey,
            Title = section.Title,
            Description = section.Description,
            DisplayOrder = section.DisplayOrder,
            Fields = fields
        };
    }

    private static ReviewField MapField(
        ProfileFieldDefinition definition,
        IReadOnlyDictionary<string, ExtractedField> fieldsByName,
        ICollection<ValidationWarning> warnings,
        HashSet<string> matchedFieldNames)
    {
        var candidateKeys = new[] { definition.FieldKey }.Concat(definition.AliasFieldNames).ToList();
        var matched = candidateKeys
            .Select(key => fieldsByName.GetValueOrDefault(key))
            .FirstOrDefault(f => f is not null && !string.IsNullOrWhiteSpace(f.NormalizedValue ?? f.RawValue));

        if (matched is not null)
        {
            matchedFieldNames.Add(matched.FieldName);
            return MapPopulatedField(definition, matched, warnings, candidateKeys);
        }

        return MapMissingField(definition, warnings, candidateKeys);
    }

    private static ReviewField MapPopulatedField(
        ProfileFieldDefinition definition,
        ExtractedField field,
        ICollection<ValidationWarning> warnings,
        IReadOnlyCollection<string>? candidateKeys = null)
    {
        var keys = candidateKeys ?? [definition.FieldKey];

        return new ReviewField
        {
            FieldKey = definition.FieldKey,
            StorageFieldName = field.FieldName,
            Label = definition.Label,
            Value = field.NormalizedValue ?? field.RawValue,
            RawValue = field.RawValue,
            NormalizedValue = field.NormalizedValue,
            DataType = definition.DataType,
            IsRequired = definition.IsRequired,
            IsMissing = false,
            Confidence = field.Confidence,
            DisplayOrder = definition.DisplayOrder,
            SourceType = field.SourceType,
            SourceText = field.SourceText,
            SourcePageNumber = field.PageNumber,
            SourceBoundingBoxJson = field.BoundingBoxJson,
            ExtractionMethod = field.ExtractionMethod,
            WarningCodes = warnings.Where(w => keys.Contains(w.FieldName)).Select(w => w.WarningCode ?? string.Empty).ToList(),
            Options = definition.Options?.ToList(),
            IsEditedByUser = field.IsEditedByUser,
            EditedAt = field.EditedAt
        };
    }

    private static ReviewField MapMissingField(
        ProfileFieldDefinition definition,
        ICollection<ValidationWarning> warnings,
        IReadOnlyCollection<string> candidateKeys)
    {
        return new ReviewField
        {
            FieldKey = definition.FieldKey,
            Label = definition.Label,
            DataType = definition.DataType,
            IsRequired = definition.IsRequired,
            IsMissing = true,
            DisplayOrder = definition.DisplayOrder,
            WarningCodes = warnings.Where(w => candidateKeys.Contains(w.FieldName)).Select(w => w.WarningCode ?? string.Empty).ToList(),
            Options = definition.Options?.ToList()
        };
    }

    private static string Humanize(string fieldName)
    {
        // Simple PascalCase → "Pascal Case" for the fallback "Other detected fields" label —
        // these are fields with no profile definition at all, so there's no curated Label.
        var result = new System.Text.StringBuilder();
        foreach (var c in fieldName)
        {
            if (char.IsUpper(c) && result.Length > 0) result.Append(' ');
            result.Append(c);
        }

        return result.ToString();
    }
}
