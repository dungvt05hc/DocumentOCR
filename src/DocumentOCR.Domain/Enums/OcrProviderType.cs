namespace DocumentOCR.Domain.Enums;

public enum OcrProviderType
{
    None = 0,
    AzureDocumentIntelligence = 1,
    GoogleDocumentAI = 2,
    AwsTextract = 3,
    Tesseract = 4
}
