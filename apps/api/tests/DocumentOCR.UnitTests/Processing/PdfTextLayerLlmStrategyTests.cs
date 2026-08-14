using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Application.Processing;
using DocumentOCR.Domain.Enums;
using DocumentOCR.Infrastructure.Llm;
using DocumentOCR.Infrastructure.Ocr;
using DocumentOCR.Infrastructure.Processing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocumentOCR.UnitTests.Processing;

/// <summary>
/// Exercises <see cref="PdfTextLayerLlmStrategy"/>'s eligibility check, single-read caching, and —
/// the bulk of this file — the anti-hallucination verification and code-computed confidence in
/// <see cref="PdfTextLayerLlmStrategy.ExtractAsync"/>. Uses a hand-written stub
/// <see cref="ILlmExtractionClient"/> so no real LLM call happens (see .claude/rules/testing.md).
/// </summary>
public class PdfTextLayerLlmStrategyTests
{
    private const string SourceText =
        "HÓA ĐƠN GIÁ TRỊ GIA TĂNG Mẫu số 1 Ký hiệu 1C25TBP Số 00000947 Ngày lập 15/07/2026 " +
        "CÔNG TY TNHH ABC Cộng tiền hàng: 1.000.000 Tiền thuế GTGT: 100.000 Tổng thanh toán: 1.100.000 " +
        "MST người bán 0100109106 MST người mua 0109876543";

    private static DocumentInput MakeInput(string contentType = "application/pdf", string fileName = "invoice.pdf") => new()
    {
        Content = new MemoryStream([1, 2, 3, 4]),
        FileName = fileName,
        ContentType = contentType,
        FileSizeBytes = 4
    };

    private static PdfTextLayerLlmStrategy CreateSut(
        NormalizedOcrDocument textLayerResult,
        ILlmExtractionClient llmClient,
        LlmOptions? options = null,
        ILlmExtractionCache? cache = null,
        IDocumentStorageService? storage = null,
        OcrOptions? ocrOptions = null)
    {
        var provider = new RecordingOcrProvider(textLayerResult);
        return new PdfTextLayerLlmStrategy(
            provider,
            llmClient,
            new FieldNormalizationService(),
            cache ?? new InMemoryLlmExtractionCache(),
            storage ?? new RecordingDocumentStorageService(),
            Options.Create(options ?? new LlmOptions { Enabled = true, Model = "gemini-2.5-flash" }),
            Options.Create(ocrOptions ?? new OcrOptions()),
            NullLogger<PdfTextLayerLlmStrategy>.Instance);
    }

    private static NormalizedOcrDocument SuccessfulTextLayer(string? fullText = null) => new()
    {
        Success = true, ProviderName = "PdfTextLayer", FullText = fullText ?? SourceText, PageCount = 1
    };

    // ── CanHandleAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CanHandleAsync_LlmDisabled_ReturnsFalseAndNeverReadsTextLayer()
    {
        var provider = new RecordingOcrProvider(SuccessfulTextLayer());
        var sut = new PdfTextLayerLlmStrategy(
            provider, new StubLlmExtractionClient(_ => new LlmExtractionResult()), new FieldNormalizationService(),
            new InMemoryLlmExtractionCache(), new RecordingDocumentStorageService(),
            Options.Create(new LlmOptions { Enabled = false }), Options.Create(new OcrOptions()),
            NullLogger<PdfTextLayerLlmStrategy>.Instance);

        var canHandle = await sut.CanHandleAsync(MakeInput());

        Assert.False(canHandle);
        Assert.Equal(0, provider.CallCount);
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("text/xml")]
    public async Task CanHandleAsync_NonPdfContentType_ReturnsFalse(string contentType)
    {
        var sut = CreateSut(SuccessfulTextLayer(), new StubLlmExtractionClient(_ => new LlmExtractionResult()));

        Assert.False(await sut.CanHandleAsync(MakeInput(contentType)));
    }

    [Fact]
    public async Task CanHandleAsync_PdfDetectedAsScan_ReturnsFalse()
    {
        var sut = CreateSut(
            new NormalizedOcrDocument { Success = false, ProviderName = "PdfTextLayer", ErrorMessage = "scan" },
            new StubLlmExtractionClient(_ => new LlmExtractionResult()));

        Assert.False(await sut.CanHandleAsync(MakeInput()));
    }

    [Fact]
    public async Task CanHandleAsync_CalledTwice_OnlyReadsTextLayerOnce()
    {
        var provider = new RecordingOcrProvider(SuccessfulTextLayer());
        var sut = new PdfTextLayerLlmStrategy(
            provider, new StubLlmExtractionClient(_ => new LlmExtractionResult()), new FieldNormalizationService(),
            new InMemoryLlmExtractionCache(), new RecordingDocumentStorageService(),
            Options.Create(new LlmOptions { Enabled = true }), Options.Create(new OcrOptions()),
            NullLogger<PdfTextLayerLlmStrategy>.Instance);
        var input = MakeInput();

        await sut.CanHandleAsync(input);
        await sut.CanHandleAsync(input);

        Assert.Equal(1, provider.CallCount);
    }

    // ── ExtractAsync: happy path ─────────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_AllFieldsVerified_MapsFieldsAtHighConfidence()
    {
        var llmResult = new LlmExtractionResult
        {
            SoHoaDon = new LlmFieldValue("00000947", "Số 00000947"),
            NguoiBanTen = new LlmFieldValue("CÔNG TY TNHH ABC", "CÔNG TY TNHH ABC"),
            NguoiBanMst = new LlmFieldValue("0100109106", "MST người bán 0100109106"),
            TienHang = new LlmFieldValue("1.000.000", "Cộng tiền hàng: 1.000.000"),
            TongTienThue = new LlmFieldValue("100.000", "Tiền thuế GTGT: 100.000"),
            TongThanhToan = new LlmFieldValue("1.100.000", "Tổng thanh toán: 1.100.000"),
            NgayLap = new LlmFieldValue("15/07/2026", "Ngày lập 15/07/2026")
        };
        var sut = CreateSut(SuccessfulTextLayer(), new StubLlmExtractionClient(_ => llmResult));

        var result = await sut.ExtractAsync(MakeInput());

        Assert.True(result.Success);
        Assert.Equal("PdfTextLayerLlm", result.ProviderName);

        var invoiceNumber = Assert.Single(result.Fields, f => f.FieldName == nameof(FieldName.InvoiceNumber));
        Assert.Equal("00000947", invoiceNumber.RawValue);
        Assert.Equal(0.9, invoiceNumber.Confidence);
        Assert.Equal("Số 00000947", invoiceNumber.SourceText);

        var supplierTaxCode = Assert.Single(result.Fields, f => f.FieldName == nameof(FieldName.SupplierTaxCode));
        Assert.Equal("0100109106", supplierTaxCode.NormalizedValue);
        Assert.Equal(0.9, supplierTaxCode.Confidence);

        var subtotal = Assert.Single(result.Fields, f => f.FieldName == nameof(FieldName.SubtotalAmount));
        Assert.Equal("1000000", subtotal.NormalizedValue);
        Assert.Equal(0.9, subtotal.Confidence);

        var invoiceDate = Assert.Single(result.Fields, f => f.FieldName == nameof(FieldName.InvoiceDate));
        Assert.Equal("2026-07-15", invoiceDate.NormalizedValue);
        Assert.Equal(0.9, invoiceDate.Confidence);

        var documentType = Assert.Single(result.Fields, f => f.FieldName == nameof(FieldName.DocumentType));
        Assert.Equal(nameof(DocumentType.VatInvoice), documentType.NormalizedValue);
        Assert.Equal(1.0, documentType.Confidence);
    }

    [Fact]
    public async Task ExtractAsync_NullLlmField_IsNotAddedAsExtractedField()
    {
        var llmResult = new LlmExtractionResult { SoHoaDon = null };
        var sut = CreateSut(SuccessfulTextLayer(), new StubLlmExtractionClient(_ => llmResult));

        var result = await sut.ExtractAsync(MakeInput());

        Assert.DoesNotContain(result.Fields, f => f.FieldName == nameof(FieldName.InvoiceNumber));
    }

    // ── ExtractAsync: anti-hallucination ─────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_SourceTextNotInDocument_FieldIsDropped()
    {
        var llmResult = new LlmExtractionResult
        {
            SoHoaDon = new LlmFieldValue("99999999", "this text does not appear in the source")
        };
        var sut = CreateSut(SuccessfulTextLayer(), new StubLlmExtractionClient(_ => llmResult));

        var result = await sut.ExtractAsync(MakeInput());

        Assert.DoesNotContain(result.Fields, f => f.FieldName == nameof(FieldName.InvoiceNumber));
    }

    [Fact]
    public async Task ExtractAsync_SourceTextNotInDocument_IncrementsRejectedFieldCount()
    {
        var llmResult = new LlmExtractionResult
        {
            SoHoaDon = new LlmFieldValue("99999999", "this text does not appear in the source"),
            NguoiBanTen = new LlmFieldValue("SOME COMPANY", "also not in the source")
        };
        var sut = CreateSut(SuccessfulTextLayer(), new StubLlmExtractionClient(_ => llmResult));

        var result = await sut.ExtractAsync(MakeInput());

        Assert.Equal(2, result.RejectedFieldCount);
    }

    [Fact]
    public async Task ExtractAsync_MissingLlmValue_DoesNotCountAsRejected()
    {
        // A field the LLM legitimately couldn't find (null value) is not "rejected" -- rejection is
        // reserved for values the LLM did provide but whose sourceText failed verification.
        var llmResult = new LlmExtractionResult { SoHoaDon = null };
        var sut = CreateSut(SuccessfulTextLayer(), new StubLlmExtractionClient(_ => llmResult));

        var result = await sut.ExtractAsync(MakeInput());

        Assert.Equal(0, result.RejectedFieldCount);
    }

    [Fact]
    public async Task ExtractAsync_SourceTextMatchesAfterWhitespaceNormalization_FieldIsKept()
    {
        var llmResult = new LlmExtractionResult
        {
            SoHoaDon = new LlmFieldValue("00000947", "Số    00000947")
        };
        var sut = CreateSut(SuccessfulTextLayer(), new StubLlmExtractionClient(_ => llmResult));

        var result = await sut.ExtractAsync(MakeInput());

        Assert.Contains(result.Fields, f => f.FieldName == nameof(FieldName.InvoiceNumber));
    }

    // ── ExtractAsync: format-check confidence downgrades ─────────────────────────

    [Fact]
    public async Task ExtractAsync_DateDoesNotParse_ConfidenceIsHalf()
    {
        const string text = "Ngày lập KHONG-PHAI-NGAY";
        var llmResult = new LlmExtractionResult
        {
            NgayLap = new LlmFieldValue("KHONG-PHAI-NGAY", "Ngày lập KHONG-PHAI-NGAY")
        };
        var sut = CreateSut(SuccessfulTextLayer(text), new StubLlmExtractionClient(_ => llmResult));

        var result = await sut.ExtractAsync(MakeInput());

        var field = Assert.Single(result.Fields, f => f.FieldName == nameof(FieldName.InvoiceDate));
        Assert.Equal(0.5, field.Confidence);
        Assert.Null(field.NormalizedValue);
    }

    [Fact]
    public async Task ExtractAsync_TaxCodeWrongDigitCount_ConfidenceIsHalf()
    {
        const string text = "MST người bán 12345";
        var llmResult = new LlmExtractionResult
        {
            NguoiBanMst = new LlmFieldValue("12345", "MST người bán 12345")
        };
        var sut = CreateSut(SuccessfulTextLayer(text), new StubLlmExtractionClient(_ => llmResult));

        var result = await sut.ExtractAsync(MakeInput());

        var field = Assert.Single(result.Fields, f => f.FieldName == nameof(FieldName.SupplierTaxCode));
        Assert.Equal(0.5, field.Confidence);
    }

    [Fact]
    public async Task ExtractAsync_MoneyDoesNotParse_ConfidenceIsHalf()
    {
        const string text = "Cộng tiền hàng: khong-phai-so";
        var llmResult = new LlmExtractionResult
        {
            TienHang = new LlmFieldValue("khong-phai-so", "Cộng tiền hàng: khong-phai-so")
        };
        var sut = CreateSut(SuccessfulTextLayer(text), new StubLlmExtractionClient(_ => llmResult));

        var result = await sut.ExtractAsync(MakeInput());

        var field = Assert.Single(result.Fields, f => f.FieldName == nameof(FieldName.SubtotalAmount));
        Assert.Equal(0.5, field.Confidence);
    }

    // ── ExtractAsync: amount consistency ─────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_AmountsDoNotReconcile_DowngradesAllThreeMoneyFields()
    {
        const string text =
            "Cộng tiền hàng: 1.000.000 Tiền thuế GTGT: 100.000 Tổng thanh toán: 5.000.000";
        var llmResult = new LlmExtractionResult
        {
            TienHang = new LlmFieldValue("1.000.000", "Cộng tiền hàng: 1.000.000"),
            TongTienThue = new LlmFieldValue("100.000", "Tiền thuế GTGT: 100.000"),
            TongThanhToan = new LlmFieldValue("5.000.000", "Tổng thanh toán: 5.000.000")
        };
        var sut = CreateSut(SuccessfulTextLayer(text), new StubLlmExtractionClient(_ => llmResult));

        var result = await sut.ExtractAsync(MakeInput());

        Assert.All(
            result.Fields.Where(f => f.FieldName is nameof(FieldName.SubtotalAmount) or nameof(FieldName.VatAmount) or nameof(FieldName.TotalAmount)),
            f => Assert.Equal(0.5, f.Confidence));
    }

    [Fact]
    public async Task ExtractAsync_AmountsReconcileWithinOneDong_ConfidenceStaysHigh()
    {
        const string text =
            "Cộng tiền hàng: 1.000.000 Tiền thuế GTGT: 100.000 Tổng thanh toán: 1.100.000";
        var llmResult = new LlmExtractionResult
        {
            TienHang = new LlmFieldValue("1.000.000", "Cộng tiền hàng: 1.000.000"),
            TongTienThue = new LlmFieldValue("100.000", "Tiền thuế GTGT: 100.000"),
            TongThanhToan = new LlmFieldValue("1.100.000", "Tổng thanh toán: 1.100.000")
        };
        var sut = CreateSut(SuccessfulTextLayer(text), new StubLlmExtractionClient(_ => llmResult));

        var result = await sut.ExtractAsync(MakeInput());

        Assert.All(
            result.Fields.Where(f => f.FieldName is nameof(FieldName.SubtotalAmount) or nameof(FieldName.VatAmount) or nameof(FieldName.TotalAmount)),
            f => Assert.Equal(0.9, f.Confidence));
    }

    // ── ExtractAsync: tax breakdown ──────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_TaxBreakdownLineFullyVerified_IsAdded()
    {
        const string text = "Thuế suất 10% Tiền chưa thuế 1.000.000 Tiền thuế 100.000";
        var llmResult = new LlmExtractionResult
        {
            ChiTietThueSuat =
            [
                new LlmTaxBreakdownLine(
                    new LlmFieldValue("10%", "Thuế suất 10%"),
                    new LlmFieldValue("1.000.000", "Tiền chưa thuế 1.000.000"),
                    new LlmFieldValue("100.000", "Tiền thuế 100.000"))
            ]
        };
        var sut = CreateSut(SuccessfulTextLayer(text), new StubLlmExtractionClient(_ => llmResult));

        var result = await sut.ExtractAsync(MakeInput());

        var row = Assert.Single(result.TaxBreakdown);
        Assert.Equal("10%", row.VatRate);
        Assert.Equal(1000000m, row.TaxableAmount);
        Assert.Equal(100000m, row.TaxAmount);
        Assert.Equal(0.9, row.Confidence);
    }

    [Fact]
    public async Task ExtractAsync_TaxBreakdownLineWithUnverifiedSourceText_IsDropped()
    {
        var llmResult = new LlmExtractionResult
        {
            ChiTietThueSuat =
            [
                new LlmTaxBreakdownLine(
                    new LlmFieldValue("10%", "not in the document"),
                    new LlmFieldValue("1.000.000", "Cộng tiền hàng: 1.000.000"),
                    new LlmFieldValue("100.000", "Tiền thuế GTGT: 100.000"))
            ]
        };
        var sut = CreateSut(SuccessfulTextLayer(), new StubLlmExtractionClient(_ => llmResult));

        var result = await sut.ExtractAsync(MakeInput());

        Assert.Empty(result.TaxBreakdown);
    }

    [Fact]
    public async Task ExtractAsync_TaxBreakdownLineMissingOnePiece_IsDropped()
    {
        var llmResult = new LlmExtractionResult
        {
            ChiTietThueSuat =
            [
                new LlmTaxBreakdownLine(
                    new LlmFieldValue("10%", "Thuế suất 10%"),
                    null,
                    new LlmFieldValue("100.000", "Tiền thuế GTGT: 100.000"))
            ]
        };
        var sut = CreateSut(SuccessfulTextLayer("Thuế suất 10% Tiền thuế GTGT: 100.000"), new StubLlmExtractionClient(_ => llmResult));

        var result = await sut.ExtractAsync(MakeInput());

        Assert.Empty(result.TaxBreakdown);
    }

    // ── ExtractAsync: failure modes fall through (Success = false, never throws) ─

    [Fact]
    public async Task ExtractAsync_LlmClientThrows_ReturnsUnsuccessfulResultWithoutThrowing()
    {
        var sut = CreateSut(
            SuccessfulTextLayer(),
            new StubLlmExtractionClient(_ => throw new HttpRequestException("Gemini is unreachable")));

        var result = await sut.ExtractAsync(MakeInput());

        Assert.False(result.Success);
        Assert.Equal("PdfTextLayerLlm", result.ProviderName);
        Assert.Contains("Gemini is unreachable", result.ErrorMessage);
    }

    [Fact]
    public async Task ExtractAsync_TextLayerUnreadable_ReturnsUnsuccessfulResultWithoutCallingLlm()
    {
        var llmClient = new StubLlmExtractionClient(_ => throw new InvalidOperationException("should not be called"));
        var sut = CreateSut(
            new NormalizedOcrDocument { Success = false, ProviderName = "PdfTextLayer", ErrorMessage = "scan" },
            llmClient);

        var result = await sut.ExtractAsync(MakeInput());

        Assert.False(result.Success);
    }

    // ── ExtractAsync: raw response persistence ───────────────────────────────────

    [Fact]
    public async Task ExtractAsync_StoreRawProviderResponseEnabled_PersistsGeminiRawResponseAndSetsPath()
    {
        var llmResult = new LlmExtractionResult
        {
            SoHoaDon = new LlmFieldValue("00000947", "Số 00000947"),
            RawResponseJson = """{"candidates":[{"content":{"parts":[{"text":"..."}]}}]}"""
        };
        var storage = new RecordingDocumentStorageService();
        var sut = CreateSut(
            SuccessfulTextLayer(), new StubLlmExtractionClient(_ => llmResult),
            storage: storage, ocrOptions: new OcrOptions { StoreRawProviderResponse = true });

        var result = await sut.ExtractAsync(MakeInput());

        Assert.Equal(llmResult.RawResponseJson, result.RawResponseJson);
        Assert.NotNull(result.RawResponsePath);
        var saved = Assert.Single(storage.SavedFiles);
        Assert.Equal(llmResult.RawResponseJson, saved.Content);
        Assert.Equal("application/json", saved.ContentType);
    }

    [Fact]
    public async Task ExtractAsync_StoreRawProviderResponseDisabled_DoesNotPersistRawResponse()
    {
        var llmResult = new LlmExtractionResult
        {
            RawResponseJson = """{"candidates":[]}"""
        };
        var storage = new RecordingDocumentStorageService();
        var sut = CreateSut(
            SuccessfulTextLayer(), new StubLlmExtractionClient(_ => llmResult),
            storage: storage, ocrOptions: new OcrOptions { StoreRawProviderResponse = false });

        var result = await sut.ExtractAsync(MakeInput());

        Assert.Null(result.RawResponseJson);
        Assert.Null(result.RawResponsePath);
        Assert.Empty(storage.SavedFiles);
    }

    [Fact]
    public async Task ExtractAsync_CacheHit_HasNoRawResponseToPersist()
    {
        // A cache hit means Gemini was never called on this run -- there is no raw response body
        // for this attempt, so nothing gets written to storage even though StoreRawProviderResponse
        // is on.
        var cache = new InMemoryLlmExtractionCache();
        var llmClient = new StubLlmExtractionClient(_ => new LlmExtractionResult
        {
            SoHoaDon = new LlmFieldValue("00000947", "Số 00000947"),
            RawResponseJson = """{"candidates":[]}"""
        });
        var options = new LlmOptions { Enabled = true, Model = "gemini-2.5-flash" };
        var storage = new RecordingDocumentStorageService();

        await CreateSut(SuccessfulTextLayer(), llmClient, options, cache).ExtractAsync(MakeInput());

        var result = await CreateSut(
            SuccessfulTextLayer(), llmClient, options, cache, storage,
            new OcrOptions { StoreRawProviderResponse = true }).ExtractAsync(MakeInput());

        Assert.Null(result.RawResponseJson);
        Assert.Null(result.RawResponsePath);
        Assert.Empty(storage.SavedFiles);
    }

    [Fact]
    public async Task ExtractAsync_LlmCallTakesMeasurableTime_ReportsProcessingTime()
    {
        var llmClient = new DelayingLlmExtractionClient(TimeSpan.FromMilliseconds(20), new LlmExtractionResult());
        var sut = CreateSut(SuccessfulTextLayer(), llmClient);

        var result = await sut.ExtractAsync(MakeInput());

        Assert.True(result.ProcessingTimeMs >= 20, $"Expected ProcessingTimeMs >= 20, was {result.ProcessingTimeMs}");
    }

    // ── ExtractAsync: response cache ─────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_CacheHit_NeverCallsLlmClientButStillVerifiesFields()
    {
        var llmClient = new StubLlmExtractionClient(_ => new LlmExtractionResult
        {
            SoHoaDon = new LlmFieldValue("00000947", "Số 00000947")
        });
        var cache = new InMemoryLlmExtractionCache();
        var options = new LlmOptions { Enabled = true, Model = "gemini-2.5-flash" };

        // First call: cache miss -- calls the LLM and populates the cache.
        var firstSut = CreateSut(SuccessfulTextLayer(), llmClient, options, cache);
        await firstSut.ExtractAsync(MakeInput());
        Assert.Equal(1, llmClient.CallCount);

        // Second call (fresh strategy instance, same text/model): cache hit -- LLM must not be called again.
        var secondSut = CreateSut(SuccessfulTextLayer(), llmClient, options, cache);
        var result = await secondSut.ExtractAsync(MakeInput());

        Assert.Equal(1, llmClient.CallCount);
        Assert.True(result.Success);
        Assert.Contains(result.Fields, f => f.FieldName == nameof(FieldName.InvoiceNumber) && f.RawValue == "00000947");
    }

    [Fact]
    public async Task ExtractAsync_CacheHitWithUnverifiableSourceText_StillRejectsField()
    {
        // The cache doesn't bypass anti-hallucination verification -- even a cached response is
        // re-checked against whatever text this particular document actually contains.
        var cache = new InMemoryLlmExtractionCache();
        await cache.SetAsync(
            ComputeExpectedHash(SourceText),
            "gemini-2.5-flash",
            System.Text.Json.JsonSerializer.Serialize(new LlmExtractionResult
            {
                SoHoaDon = new LlmFieldValue("00000947", "this text is not in the source")
            }));
        var llmClient = new StubLlmExtractionClient(_ => throw new InvalidOperationException("should not be called on cache hit"));
        var sut = CreateSut(SuccessfulTextLayer(), llmClient, new LlmOptions { Enabled = true, Model = "gemini-2.5-flash" }, cache);

        var result = await sut.ExtractAsync(MakeInput());

        Assert.DoesNotContain(result.Fields, f => f.FieldName == nameof(FieldName.InvoiceNumber));
    }

    [Fact]
    public async Task ExtractAsync_DifferentModel_DoesNotReuseOtherModelsCacheEntry()
    {
        var cache = new InMemoryLlmExtractionCache();
        var llmClient = new StubLlmExtractionClient(_ => new LlmExtractionResult
        {
            SoHoaDon = new LlmFieldValue("00000947", "Số 00000947")
        });

        var firstSut = CreateSut(SuccessfulTextLayer(), llmClient, new LlmOptions { Enabled = true, Model = "model-a" }, cache);
        await firstSut.ExtractAsync(MakeInput());
        Assert.Equal(1, llmClient.CallCount);

        var secondSut = CreateSut(SuccessfulTextLayer(), llmClient, new LlmOptions { Enabled = true, Model = "model-b" }, cache);
        await secondSut.ExtractAsync(MakeInput());

        Assert.Equal(2, llmClient.CallCount);
    }

    private static string ComputeExpectedHash(string fullText)
    {
        var normalized = System.Text.RegularExpressions.Regex.Replace(fullText, @"\s+", " ").Trim();
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized)));
    }

    // ── Test doubles ─────────────────────────────────────────────────────────────

    private sealed class RecordingOcrProvider(NormalizedOcrDocument result) : IDocumentOcrProvider
    {
        public int CallCount { get; private set; }

        public string ProviderName => result.ProviderName;

        public Task<NormalizedOcrDocument> AnalyzeAsync(DocumentInput input, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class StubLlmExtractionClient(Func<string, LlmExtractionResult> handle) : ILlmExtractionClient
    {
        public int CallCount { get; private set; }

        public Task<LlmExtractionResult> ExtractAsync(string documentText, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(handle(documentText));
        }
    }

    private sealed class DelayingLlmExtractionClient(TimeSpan delay, LlmExtractionResult result) : ILlmExtractionClient
    {
        public async Task<LlmExtractionResult> ExtractAsync(string documentText, CancellationToken ct = default)
        {
            await Task.Delay(delay, ct);
            return result;
        }
    }

    private sealed class RecordingDocumentStorageService : IDocumentStorageService
    {
        public List<(string FileName, string ContentType, string Content)> SavedFiles { get; } = [];

        public Task<string> SaveAsync(Stream fileStream, string originalFileName, string contentType, CancellationToken ct = default)
        {
            using var reader = new StreamReader(fileStream);
            var content = reader.ReadToEnd();
            SavedFiles.Add((originalFileName, contentType, content));
            return Task.FromResult($"fake/{originalFileName}");
        }

        public Task<Stream> GetStreamAsync(string storedPath, CancellationToken ct = default) =>
            throw new NotSupportedException("Not used by PdfTextLayerLlmStrategy.");

        public Task DeleteAsync(string storedPath, CancellationToken ct = default) =>
            throw new NotSupportedException("Not used by PdfTextLayerLlmStrategy.");
    }

    private sealed class InMemoryLlmExtractionCache : ILlmExtractionCache
    {
        private readonly Dictionary<(string TextHash, string Model), string> _entries = [];

        public Task<string?> TryGetResponseJsonAsync(string textHash, string model, CancellationToken ct = default) =>
            Task.FromResult(_entries.GetValueOrDefault((textHash, model)));

        public Task SetAsync(string textHash, string model, string responseJson, CancellationToken ct = default)
        {
            _entries[(textHash, model)] = responseJson;
            return Task.CompletedTask;
        }
    }
}
