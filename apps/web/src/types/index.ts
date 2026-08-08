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
  | 'Notes'
  | 'BuyerName'
  | 'BuyerTaxCode'
  | 'BuyerAddress'
  | 'SupplierAddress'
  | 'InvoiceForm'
  | 'InvoiceSymbol'
  | 'TaxAuthorityCode'
  | 'InvoiceNature';

export type DocumentDirection = 'Unknown' | 'Purchase' | 'Sale';

export const DIRECTION_LABELS: Record<DocumentDirection, string> = {
  Unknown: 'Chưa xác định',
  Purchase: 'Mua vào',
  Sale: 'Bán ra',
};

export type WarningSeverity = 'Info' | 'Warning' | 'High' | 'Error';

export type ClientType = 'HouseholdBusiness' | 'Enterprise' | 'Individual';

export interface ClientProfileDto {
  id: string;
  name: string;
  taxCode: string | null;
  clientType: ClientType;
  address: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateClientProfileRequest {
  name: string;
  taxCode?: string | null;
  clientType: ClientType;
  address?: string | null;
}

export interface UpdateClientProfileRequest {
  name: string;
  taxCode?: string | null;
  clientType: ClientType;
  address?: string | null;
  isActive: boolean;
}

export interface DocumentDto {
  id: string;
  clientProfileId: string | null;
  clientProfileName: string | null;
  originalFileName: string;
  contentType: string;
  fileSizeBytes: number;
  pageCount: number;
  status: DocumentStatus;
  documentType: DocumentType;
  direction: DocumentDirection;
  errorMessage: string | null;
  warningCount: number;
  processingStartedAt: string | null;
  processingCompletedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface UploadFileResult {
  fileName: string;
  success: boolean;
  error: string | null;
  document: DocumentDto | null;
  jobId: string | null;
}

export interface ExtractedFieldDto {
  id: string;
  fieldName: FieldName;
  rawValue: string | null;
  normalizedValue: string | null;
  confidence: number | null;
  pageNumber: number | null;
  sourceType: string | null;
  providerFieldName: string | null;
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
  fieldName: string;
  normalizedValue: string | null;
  rawValue?: string | null;
}

export interface TableCellUpdateItem {
  columnKey: string | null;
  text: string;
  normalizedValue?: string | null;
}

export interface TableRowUpdateItem {
  rowIndex: number;
  cells: TableCellUpdateItem[];
}

export interface TableUpdateItem {
  tableId: string;
  rows: TableRowUpdateItem[];
}

/** Accepted by the save API for contract completeness but not persisted for MVP — see ReviewLineItem. */
export interface LineItemUpdateItem {
  lineNumber: number;
  description: string | null;
  quantity: number | null;
  unit: string | null;
  unitPrice: number | null;
  amount: number | null;
  currency: string | null;
}

export interface TaxBreakdownUpdateItem {
  id: string | null;
  vatRate: string | null;
  taxableAmount: number | null;
  taxAmount: number | null;
  sortOrder: number;
}

export interface UpdateFieldsRequest {
  fields: FieldUpdateItem[];
  tables?: TableUpdateItem[];
  lineItems?: LineItemUpdateItem[];
  /** The full, replacement set of tax-breakdown rows — omit to leave the stored breakdown untouched. */
  taxBreakdown?: TaxBreakdownUpdateItem[];
}

// ── Dynamic document review (document-category-driven profiles) ──────────────

export type DocumentCategory =
  | 'Unknown'
  | 'VatInvoice'
  | 'SalesReceipt'
  | 'PosReceipt'
  | 'RestaurantBill'
  | 'AppReceiptScreenshot'
  | 'InternationalInvoice'
  | 'CommercialInvoice';

export type ReviewFieldDataType =
  | 'Text'
  | 'Number'
  | 'Money'
  | 'Date'
  | 'Percentage'
  | 'Email'
  | 'Phone'
  | 'Url'
  | 'TaxCode'
  | 'Currency'
  | 'Enum'
  | 'MultilineText';

export interface ReviewField {
  fieldKey: string;
  label: string;
  value: string | null;
  rawValue: string | null;
  normalizedValue: string | null;
  dataType: ReviewFieldDataType;
  isRequired: boolean;
  isEditable: boolean;
  isMissing: boolean;
  confidence: number | null;
  displayOrder: number;
  sourceType: string | null;
  sourceText: string | null;
  sourcePageNumber: number | null;
  sourceBoundingBoxJson: string | null;
  extractionMethod: string | null;
  warningCodes: string[];
  options: string[] | null;
  isEditedByUser: boolean;
  editedAt: string | null;
}

export interface ReviewSection {
  sectionKey: string;
  title: string;
  description: string | null;
  displayOrder: number;
  fields: ReviewField[];
}

export interface ReviewWarningDto {
  severity: WarningSeverity;
  fieldKey: string | null;
  warningCode: string | null;
  message: string;
}

export interface ReviewTaxBreakdownRow {
  id: string;
  rawVatRate: string | null;
  vatRate: string | null;
  taxableAmount: number | null;
  taxAmount: number | null;
  confidence: number | null;
  sortOrder: number;
}

// ── Detected OCR tables + candidate line items ────────────────────────────────

export interface ReviewTableColumn {
  columnIndex: number;
  columnKey: string;
  label: string;
  normalizedKey: string | null;
  dataType: string | null;
  confidence: number | null;
}

export interface ReviewTableCell {
  rowIndex: number;
  columnIndex: number;
  columnKey: string | null;
  text: string;
  normalizedValue: string | null;
  confidence: number | null;
  sourceBoundingBoxJson: string | null;
  isHeader: boolean;
  isEditable: boolean;
}

export interface ReviewTableRow {
  rowIndex: number;
  rowType: 'Header' | 'Data' | string;
  cells: ReviewTableCell[];
}

export interface ReviewTable {
  tableId: string;
  title: string | null;
  pageNumber: number | null;
  rowCount: number;
  columnCount: number;
  confidence: number | null;
  tableType: string | null;
  columns: ReviewTableColumn[];
  rows: ReviewTableRow[];
  sourceBoundingBoxJson: string | null;
}

export interface ReviewLineItem {
  lineNumber: number;
  description: string | null;
  quantity: number | null;
  unit: string | null;
  unitPrice: number | null;
  amount: number | null;
  currency: string | null;
  confidence: number | null;
  sourceTableId: string;
  sourceRowIndex: number;
  isEditable: boolean;
  warnings: string[];
}

export interface OcrDebugKeyValuePair {
  key: string | null;
  value: string | null;
  confidence: number | null;
}

export interface OcrDebugData {
  fullText: string | null;
  lines: string[];
  paragraphs: string[];
  keyValuePairs: OcrDebugKeyValuePair[];
  rawProviderResponsePath: string | null;
  normalizedOcrResultPath: string | null;
  ocrSummary: string | null;
}

/** GET /api/documents/{id}/ocr-debug response — a superset of OcrDebugData with tables/fields/warnings. */
export interface OcrDebugResponse extends OcrDebugData {
  documentId: string;
  tables: ReviewTable[];
  fields: ExtractedFieldDto[];
  warnings: ValidationWarningDto[];
  rawProviderResponseJson: string | null;
}

export interface DocumentReviewResponse {
  documentId: string;
  fileName: string;
  contentType: string;
  status: DocumentStatus;
  documentCategory: DocumentCategory;
  direction: DocumentDirection;
  documentSubType: string | null;
  providerName: string | null;
  modelId: string | null;
  processedAt: string | null;
  overallConfidence: number | null;
  sections: ReviewSection[];
  warnings: ReviewWarningDto[];
  tables: ReviewTable[];
  lineItems: ReviewLineItem[];
  taxBreakdown: ReviewTaxBreakdownRow[];
  debugData: OcrDebugData | null;
  debugSummary: string | null;
}

export interface ExportRequest {
  documentIds: string[];
}
