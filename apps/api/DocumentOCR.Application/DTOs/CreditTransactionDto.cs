using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Application.DTOs;

public class CreditTransactionDto
{
    public Guid Id { get; set; }
    public CreditTransactionType Type { get; set; }
    public int Amount { get; set; }
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreditTransactionPageDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public List<CreditTransactionDto> Items { get; set; } = [];
}
