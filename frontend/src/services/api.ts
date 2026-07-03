import axios from 'axios';
import type {
  DocumentDetailDto,
  DocumentDto,
  ExportRequest,
  UpdateFieldsRequest,
} from '../types';

const api = axios.create({
  baseURL: 'http://localhost:5000/api',
});

// ── Documents ─────────────────────────────────────────────────────────────────

export const uploadDocuments = (files: File[]) => {
  const form = new FormData();
  files.forEach((f) => form.append('files', f));
  return api.post<DocumentDto[]>('/documents/upload', form, {
    headers: { 'Content-Type': 'multipart/form-data' },
  });
};

export const getDocuments = () =>
  api.get<DocumentDto[]>('/documents');

export const getDocumentById = (id: string) =>
  api.get<DocumentDetailDto>(`/documents/${id}`);

export const triggerProcessing = (id: string) =>
  api.post(`/documents/${id}/process`);

export const updateFields = (id: string, request: UpdateFieldsRequest) =>
  api.put(`/documents/${id}/fields`, request);

export const downloadOriginal = (id: string) =>
  api.get(`/documents/${id}/download-original`, { responseType: 'blob' });

// ── Exports ───────────────────────────────────────────────────────────────────

export const exportToExcel = (request: ExportRequest) =>
  api.post('/exports/excel', request, { responseType: 'blob' });
