using System.Text.Json.Serialization;

namespace DocumentOCR.Application.Models;

/// <summary>
/// Structured field extraction as reported by an <see cref="Interfaces.ILlmExtractionClient"/> for
/// one Vietnamese VAT-invoice PDF text layer. Every field is an <see cref="LlmFieldValue"/> the
/// caller must re-verify against the source text before trusting — see
/// <see cref="LlmFieldValue"/>'s remarks. Property names match the JSON schema sent to the
/// provider (see <c>Infrastructure/Llm/GeminiExtractionClient</c>) via <see cref="JsonPropertyNameAttribute"/>.
/// </summary>
public sealed record LlmExtractionResult
{
    /// <summary>Mẫu số hoá đơn.</summary>
    [JsonPropertyName("mauSo")]
    public LlmFieldValue? MauSo { get; init; }

    /// <summary>Ký hiệu hoá đơn.</summary>
    [JsonPropertyName("kyHieu")]
    public LlmFieldValue? KyHieu { get; init; }

    /// <summary>Số hoá đơn.</summary>
    [JsonPropertyName("soHoaDon")]
    public LlmFieldValue? SoHoaDon { get; init; }

    /// <summary>Ngày lập hoá đơn.</summary>
    [JsonPropertyName("ngayLap")]
    public LlmFieldValue? NgayLap { get; init; }

    /// <summary>Mã của cơ quan thuế.</summary>
    [JsonPropertyName("maCqt")]
    public LlmFieldValue? MaCqt { get; init; }

    /// <summary>Mã tra cứu hoá đơn.</summary>
    [JsonPropertyName("maTraCuu")]
    public LlmFieldValue? MaTraCuu { get; init; }

    [JsonPropertyName("nguoiBanTen")]
    public LlmFieldValue? NguoiBanTen { get; init; }

    [JsonPropertyName("nguoiBanMst")]
    public LlmFieldValue? NguoiBanMst { get; init; }

    [JsonPropertyName("nguoiBanDiaChi")]
    public LlmFieldValue? NguoiBanDiaChi { get; init; }

    [JsonPropertyName("nguoiMuaTen")]
    public LlmFieldValue? NguoiMuaTen { get; init; }

    [JsonPropertyName("nguoiMuaMst")]
    public LlmFieldValue? NguoiMuaMst { get; init; }

    [JsonPropertyName("nguoiMuaDiaChi")]
    public LlmFieldValue? NguoiMuaDiaChi { get; init; }

    /// <summary>Cộng tiền hàng (chưa thuế).</summary>
    [JsonPropertyName("tienHang")]
    public LlmFieldValue? TienHang { get; init; }

    /// <summary>Tổng tiền thuế GTGT.</summary>
    [JsonPropertyName("tongTienThue")]
    public LlmFieldValue? TongTienThue { get; init; }

    /// <summary>Tổng tiền thanh toán.</summary>
    [JsonPropertyName("tongThanhToan")]
    public LlmFieldValue? TongThanhToan { get; init; }

    /// <summary>Số tiền bằng chữ.</summary>
    [JsonPropertyName("tienBangChu")]
    public LlmFieldValue? TienBangChu { get; init; }

    [JsonPropertyName("hinhThucThanhToan")]
    public LlmFieldValue? HinhThucThanhToan { get; init; }

    /// <summary>Đồng tiền (thường "VND").</summary>
    [JsonPropertyName("dongTien")]
    public LlmFieldValue? DongTien { get; init; }

    /// <summary>One line per distinct VAT rate on the invoice.</summary>
    [JsonPropertyName("chiTietThueSuat")]
    public IReadOnlyList<LlmTaxBreakdownLine> ChiTietThueSuat { get; init; } = [];

    /// <summary>Not part of the model's JSON schema — populated by the client from the provider's response metadata.</summary>
    [JsonIgnore]
    public LlmUsage Usage { get; init; } = new(0, 0, 0m);
}
