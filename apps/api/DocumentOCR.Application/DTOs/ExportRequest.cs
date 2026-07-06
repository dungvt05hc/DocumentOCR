namespace DocumentOCR.Application.DTOs;

public class ExportRequest
{
    public List<Guid> DocumentIds { get; set; } = new();
}
