using Azure.AI.DocumentIntelligence;
using DocumentOCR.Infrastructure.Ocr;
using Xunit;

namespace DocumentOCR.UnitTests.Ocr;

/// <summary>
/// Covers the new Tables/Paragraphs/KeyValuePairs mapping added for prebuilt-layout support.
/// Uses <see cref="DocumentIntelligenceModelFactory"/> to construct SDK model instances directly —
/// no network call, consistent with the repo rule against calling real Azure in tests.
/// </summary>
public class AzureDocumentIntelligenceProviderLayoutMappingTests
{
    [Fact]
    public void BuildTableResult_MapsRowsColumnsAndCells()
    {
        var table = DocumentIntelligenceModelFactory.DocumentTable(
            rowCount: 2,
            columnCount: 2,
            cells:
            [
                DocumentIntelligenceModelFactory.DocumentTableCell(
                    kind: DocumentTableCellKind.ColumnHeader,
                    rowIndex: 0,
                    columnIndex: 0,
                    content: "Mặt hàng",
                    boundingRegions: [DocumentIntelligenceModelFactory.BoundingRegion(1, [0f, 0f, 1f, 0f, 1f, 1f, 0f, 1f])]),
                DocumentIntelligenceModelFactory.DocumentTableCell(
                    kind: DocumentTableCellKind.Content,
                    rowIndex: 1,
                    columnIndex: 0,
                    content: "Trà sữa")
            ],
            boundingRegions: [DocumentIntelligenceModelFactory.BoundingRegion(1, [0f, 0f, 1f, 0f, 1f, 1f, 0f, 1f])]);

        var result = AzureDocumentIntelligenceProvider.BuildTableResult(table);

        Assert.Equal(2, result.RowCount);
        Assert.Equal(2, result.ColumnCount);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(2, result.Cells.Count);
        Assert.Contains(result.Cells, c => c.Text == "Mặt hàng" && c.RowIndex == 0 && c.ColumnIndex == 0);
        Assert.Contains(result.Cells, c => c.Text == "Trà sữa" && c.RowIndex == 1 && c.ColumnIndex == 0);
    }

    [Fact]
    public void BuildParagraphResult_MapsContentRoleAndPage()
    {
        var paragraph = DocumentIntelligenceModelFactory.DocumentParagraph(
            role: ParagraphRole.Title,
            content: "MOTA CAFE",
            boundingRegions: [DocumentIntelligenceModelFactory.BoundingRegion(1, [0f, 0f, 1f, 0f, 1f, 1f, 0f, 1f])]);

        var result = AzureDocumentIntelligenceProvider.BuildParagraphResult(paragraph);

        Assert.Equal("MOTA CAFE", result.Text);
        Assert.Equal(1, result.PageNumber);
        Assert.NotNull(result.BoundingBox);
    }

    [Fact]
    public void BuildKeyValuePairResult_MapsKeyValueAndConfidence()
    {
        var kvp = DocumentIntelligenceModelFactory.DocumentKeyValuePair(
            key: DocumentIntelligenceModelFactory.DocumentKeyValueElement(
                content: "Tổng",
                boundingRegions: [DocumentIntelligenceModelFactory.BoundingRegion(1, [0f, 0f, 1f, 0f, 1f, 1f, 0f, 1f])]),
            value: DocumentIntelligenceModelFactory.DocumentKeyValueElement(content: "85.000"),
            confidence: 0.91f);

        var result = AzureDocumentIntelligenceProvider.BuildKeyValuePairResult(kvp);

        Assert.Equal("Tổng", result.KeyText);
        Assert.Equal("85.000", result.ValueText);
        Assert.Equal(0.91, result.Confidence!.Value, 2);
        Assert.Equal(1, result.PageNumber);
    }

    [Fact]
    public void BuildKeyValuePairResult_MissingValue_ReturnsNullValueAndBoundingBox()
    {
        var kvp = DocumentIntelligenceModelFactory.DocumentKeyValuePair(
            key: DocumentIntelligenceModelFactory.DocumentKeyValueElement(content: "Ghi chú"),
            value: null,
            confidence: 0.5f);

        var result = AzureDocumentIntelligenceProvider.BuildKeyValuePairResult(kvp);

        Assert.Null(result.ValueText);
        Assert.Null(result.ValueBoundingBox);
    }
}
