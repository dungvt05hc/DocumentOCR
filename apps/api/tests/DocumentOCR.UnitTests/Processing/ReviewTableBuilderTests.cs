using DocumentOCR.Application.Models;
using DocumentOCR.Application.Processing;
using Xunit;

namespace DocumentOCR.UnitTests.Processing;

public class ReviewTableBuilderTests
{
    private readonly ReviewTableBuilder _sut = new();

    [Fact]
    public void BuildTables_EnglishHeaders_NormalizesToCanonicalColumnKeys()
    {
        var table = TableFromRows(
            ["ITEMS", "QUANTITY", "PRICE"],
            ["Widget", "2", "10.00"]);

        var result = _sut.BuildTables([table]);

        var reviewTable = Assert.Single(result);
        Assert.Equal(["Description", "Quantity", "UnitPrice"], reviewTable.Columns.Select(c => c.NormalizedKey));
        Assert.DoesNotContain(reviewTable.Columns, c => c.NormalizedKey == "Amount");
    }

    [Fact]
    public void BuildTables_VietnameseHeaders_NormalizesToCanonicalColumnKeys()
    {
        var table = TableFromRows(
            ["Tên hàng", "SL", "Đơn giá", "Thành tiền"],
            ["Cà phê", "2", "25.000", "50.000"]);

        var result = _sut.BuildTables([table]);

        var reviewTable = Assert.Single(result);
        Assert.Equal(
            ["Description", "Quantity", "UnitPrice", "Amount"],
            reviewTable.Columns.Select(c => c.NormalizedKey));
    }

    [Fact]
    public void BuildTables_PosReceiptVietnameseHeaders_DetectsTable()
    {
        var table = TableFromRows(
            ["Tên", "Sl", "Giá", "Tổng"],
            ["Trà sữa", "1", "35.000", "35.000"]);

        var result = _sut.BuildTables([table]);

        var reviewTable = Assert.Single(result);
        Assert.Equal(2, reviewTable.RowCount);
        Assert.Contains(reviewTable.Columns, c => c.NormalizedKey == "Description");
        Assert.Contains(reviewTable.Columns, c => c.NormalizedKey == "Amount");
    }

    [Fact]
    public void BuildTables_RaggedRowMissingCells_DoesNotThrowAndLeavesGapsBlank()
    {
        var table = new OcrTable
        {
            RowCount = 2,
            ColumnCount = 3,
            Cells =
            [
                new() { RowIndex = 0, ColumnIndex = 0, Text = "ITEMS", Kind = "columnHeader" },
                new() { RowIndex = 0, ColumnIndex = 1, Text = "QUANTITY", Kind = "columnHeader" },
                new() { RowIndex = 0, ColumnIndex = 2, Text = "PRICE", Kind = "columnHeader" },
                new() { RowIndex = 1, ColumnIndex = 0, Text = "Widget" }
                // Row 1 has no Quantity/Price cell at all — must not throw.
            ]
        };

        var result = _sut.BuildTables([table]);

        var reviewTable = Assert.Single(result);
        var dataRow = Assert.Single(reviewTable.Rows, r => r.RowType != "Header");
        Assert.Single(dataRow.Cells);
    }

    [Fact]
    public void BuildTables_EmptyTable_ReturnsEmptyShapeWithoutThrowing()
    {
        var table = new OcrTable { RowCount = 0, ColumnCount = 0, Cells = [] };

        var result = _sut.BuildTables([table]);

        var reviewTable = Assert.Single(result);
        Assert.Empty(reviewTable.Columns);
        Assert.Empty(reviewTable.Rows);
    }

    [Fact]
    public void BuildLineItems_DescriptionWithQuantityAndPrice_CreatesCandidates()
    {
        var table = TableFromRows(
            ["ITEMS", "QUANTITY", "PRICE"],
            ["Widget", "2", "10.00"],
            ["Gadget", "1", "5.50"]);

        var tables = _sut.BuildTables([table]);
        var lineItems = _sut.BuildLineItems(tables);

        Assert.Equal(2, lineItems.Count);
        Assert.Equal("Widget", lineItems[0].Description);
        Assert.Equal(2m, lineItems[0].Quantity);
        Assert.Equal(10.00m, lineItems[0].UnitPrice);
        Assert.Equal("table-0", lineItems[0].SourceTableId);
    }

    [Fact]
    public void BuildLineItems_FooterTotalRow_IsExcludedFromCandidates()
    {
        var table = TableFromRows(
            ["ITEMS", "QUANTITY", "AMOUNT"],
            ["Widget", "2", "20.00"],
            ["Total", "", "20.00"]);

        var tables = _sut.BuildTables([table]);
        var lineItems = _sut.BuildLineItems(tables);

        Assert.Single(lineItems);
        Assert.Equal("Widget", lineItems[0].Description);
    }

    [Fact]
    public void BuildLineItems_UnparsableNumericCell_KeepsRowWithNullValueInsteadOfFailing()
    {
        var table = TableFromRows(
            ["ITEMS", "QUANTITY", "PRICE"],
            ["Widget", "two", "10.00"]);

        var tables = _sut.BuildTables([table]);
        var lineItems = _sut.BuildLineItems(tables);

        var lineItem = Assert.Single(lineItems);
        Assert.Null(lineItem.Quantity);
        Assert.NotEmpty(lineItem.Warnings);
        Assert.True(lineItem.Confidence < 0.6);
    }

    [Fact]
    public void BuildLineItems_TableWithoutDescriptionColumn_ProducesNoCandidates()
    {
        var table = TableFromRows(
            ["QUANTITY", "PRICE"],
            ["2", "10.00"]);

        var tables = _sut.BuildTables([table]);
        var lineItems = _sut.BuildLineItems(tables);

        Assert.Empty(lineItems);
    }

    private static OcrTable TableFromRows(params string[][] rows)
    {
        var cells = new List<OcrTableCell>();
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            for (var columnIndex = 0; columnIndex < rows[rowIndex].Length; columnIndex++)
            {
                cells.Add(new OcrTableCell
                {
                    RowIndex = rowIndex,
                    ColumnIndex = columnIndex,
                    Text = rows[rowIndex][columnIndex],
                    Kind = rowIndex == 0 ? "columnHeader" : "content"
                });
            }
        }

        return new OcrTable
        {
            RowCount = rows.Length,
            ColumnCount = rows.Max(r => r.Length),
            Cells = cells
        };
    }
}
