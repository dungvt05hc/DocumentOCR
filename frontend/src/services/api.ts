import axios from 'axios';
import type {
  DocumentDetailDto,
  DocumentDto,
  ExportRequest,
  UpdateFieldsRequest,
  UploadDocumentResponse,
} from '../types';

export const api = axios.create({
  baseURL: 'http://localhost:5000/api',
});

export const uploadDocuments = (
  files: File[],
  onProgress?: (percent: number) => void
) => {
  const form = new FormData();
  files.forEach((file) => form.append('files', file));

  return api.post<UploadDocumentResponse[]>('/documents/upload', form, {
    headers: { 'Content-Type': 'multipart/form-data' },
    onUploadProgress: (event) => {
      if (!event.total || !onProgress) return;
      onProgress(Math.round((event.loaded * 100) / event.total));
    },
  });
};

export const getDocuments = () => api.get<DocumentDto[]>('/documents');

export const getDocumentById = (id: string) =>
  api.get<DocumentDetailDto>(`/documents/${id}`);

export const triggerProcessing = (id: string) =>
  api.post(`/documents/${id}/process`);

export const updateFields = (id: string, request: UpdateFieldsRequest) =>
  api.put(`/documents/${id}/fields`, request);

export const downloadOriginal = (id: string) =>
  api.get(`/documents/${id}/download-original`, { responseType: 'blob' });

export const exportToExcel = (request: ExportRequest) =>
  api.post('/exports/excel', request, { responseType: 'blob' });
