// ── Enums (mirroring the backend) ─────────────────────────────────────────────

export type DocumentStatus =
  | 'Pending'
  | 'Processing'
  | 'ReviewRequired'
  | 'Reviewed'
  | 'Failed';

export type DocumentType = 'Unknown' | 'Invoice' | 'Receipt' | 'ExpenseDocument';

export type FieldName =
  | 'SupplierName'
  | 'SupplierTaxCode'
  | 'InvoiceNumber'
  | 'InvoiceDate'
  | 'SubtotalAmount'
  | 'VatAmount'
  | 'TotalAmount'
  | 'Currency'
  | 'DocumentType'
  | 'Notes';

export type WarningSeverity = 'Info' | 'Warning' | 'Error';

export type OcrProviderType =
  | 'None'
  | 'AzureDocumentIntelligence'
  | 'GoogleDocumentAI'
  | 'AwsTextract'
  | 'Tesseract';

// ── API DTOs ──────────────────────────────────────────────────────────────────

export interface DocumentDto {
  id: string;
  originalFileName: string;
  contentType: string;
  fileSizeBytes: number;
  status: DocumentStatus;
  detectedType: DocumentType;
  failureReason: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface ExtractedFieldDto {
  id: string;
  fieldName: FieldName;
  rawValue: string | null;
  normalizedValue: string | null;
  confidenceScore: number | null;
  isEditedByUser: boolean;
}

export interface ValidationWarningDto {
  id: string;
  relatedField: FieldName | null;
  severity: WarningSeverity;
  message: string;
}

export interface OcrProviderLogDto {
  provider: OcrProviderType;
  pageCount: number;
  processingTimeMs: number;
  estimatedCost: number;
  success: boolean;
  errorMessage: string | null;
}

export interface DocumentDetailDto extends DocumentDto {
  fields: ExtractedFieldDto[];
  warnings: ValidationWarningDto[];
  ocrLog: OcrProviderLogDto | null;
}

export interface FieldUpdateItem {
  fieldName: FieldName;
  normalizedValue: string | null;
}

export interface UpdateFieldsRequest {
  fields: FieldUpdateItem[];
}

export interface ExportRequest {
  documentIds: string[];
}
