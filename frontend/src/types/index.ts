export type DocumentStatus =
  | 'Uploaded'
  | 'Processing'
  | 'Processed'
  | 'Failed'
  | 'Reviewed'
  | 'Exported';

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

export type WarningSeverity = 'Info' | 'Warning' | 'High' | 'Error';

export interface DocumentDto {
  id: string;
  originalFileName: string;
  contentType: string;
  fileSizeBytes: number;
  pageCount: number;
  status: DocumentStatus;
  documentType: DocumentType;
  errorMessage: string | null;
  warningCount: number;
  processingStartedAt: string | null;
  processingCompletedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface UploadDocumentResponse extends DocumentDto {
  documentId: string;
  jobId: string;
  message: string;
}

export interface ExtractedFieldDto {
  id: string;
  fieldName: FieldName;
  rawValue: string | null;
  normalizedValue: string | null;
  confidence: number | null;
  pageNumber: number | null;
  isRequired: boolean;
  isEditedByUser: boolean;
  editedAt: string | null;
}

export interface ValidationWarningDto {
  id: string;
  fieldName: FieldName | null;
  warningCode: string | null;
  severity: WarningSeverity;
  message: string;
}

export interface OcrProviderLogDto {
  providerName: string;
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
