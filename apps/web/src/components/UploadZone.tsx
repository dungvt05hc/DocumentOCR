import { useCallback, useRef, useState } from 'react';
import { assignDocumentClient, uploadDocuments } from '../services/api';
import { Alert } from './Alert';
import { UploadIcon } from './icons';
import type { ClientProfileDto, UploadFileResult } from '../types';

interface Props {
  clientProfiles: ClientProfileDto[];
  onUploaded: (results: UploadFileResult[]) => void;
}

const allowedTypes = ['application/pdf', 'image/jpeg', 'image/png', 'text/xml', 'application/xml'];
const maxFileSizeBytes = 20 * 1024 * 1024;
const maxXmlFileSizeBytes = 5 * 1024 * 1024;

const hasXmlExtension = (file: File) => file.name.toLowerCase().endsWith('.xml');

// Browsers/OSes report inconsistent (or empty) MIME types for .xml, so an empty type is
// accepted based on extension alone — the server re-validates the actual file content anyway.
const isAllowedType = (file: File) =>
  allowedTypes.includes(file.type) || (file.type === '' && hasXmlExtension(file));

const maxSizeFor = (file: File) => (hasXmlExtension(file) ? maxXmlFileSizeBytes : maxFileSizeBytes);

export function UploadZone({ clientProfiles, onUploaded }: Props) {
  const [isDragging, setIsDragging] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [progress, setProgress] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [clientProfileId, setClientProfileId] = useState('');
  const inputRef = useRef<HTMLInputElement>(null);

  const handleFiles = useCallback(
    async (files: FileList | null) => {
      if (!files || files.length === 0) return;

      const fileList = Array.from(files);
      // Files that obviously can't succeed (wrong type/too large) are skipped
      // client-side, but one bad file must not block the valid ones in the
      // same batch from being uploaded.
      const skipped = fileList.filter(
        (file) => !isAllowedType(file) || file.size > maxSizeFor(file)
      );
      const toUpload = fileList.filter((file) => !skipped.includes(file));

      if (toUpload.length === 0) {
        setError('Use XML, PDF, JPG, or PNG files (XML up to 5 MB, others up to 20 MB each).');
        return;
      }

      setUploading(true);
      setProgress(0);
      setError(
        skipped.length > 0
          ? `Skipped ${skipped.length} file(s): must be XML, PDF, JPG, or PNG (XML up to 5 MB, others up to 20 MB).`
          : null
      );

      try {
        const response = await uploadDocuments(toUpload, setProgress);
        setProgress(100);

        const failed = response.data.filter((result) => !result.success);
        if (failed.length > 0) {
          setError(
            [
              skipped.length > 0
                ? `Skipped ${skipped.length} file(s): must be XML, PDF, JPG, or PNG (XML up to 5 MB, others up to 20 MB).`
                : null,
              `${failed.length} of ${response.data.length} file(s) failed: ` +
                failed.map((result) => `${result.fileName} (${result.error})`).join('; '),
            ]
              .filter(Boolean)
              .join(' ')
          );
        }

        const succeeded = response.data.filter((result) => result.success && result.document);
        if (clientProfileId && succeeded.length > 0) {
          await Promise.all(
            succeeded.map((result) => assignDocumentClient(result.document!.id, clientProfileId))
          );
        }

        if (succeeded.length > 0) {
          onUploaded(response.data);
        }
      } catch {
        setError('Upload failed. Please try again.');
      } finally {
        setUploading(false);
        if (inputRef.current) inputRef.current.value = '';
      }
    },
    [onUploaded, clientProfileId]
  );

  return (
    <div className="upload-zone-wrap">
      <label className="field">
        Khách hàng
        <select
          value={clientProfileId}
          onClick={(event) => event.stopPropagation()}
          onChange={(event) => setClientProfileId(event.target.value)}
        >
          <option value="">(Chưa gán — sẽ tự nhận diện nếu khớp MST)</option>
          {clientProfiles
            .filter((client) => client.isActive)
            .map((client) => (
              <option key={client.id} value={client.id}>
                {client.name}
              </option>
            ))}
        </select>
      </label>

      <div
        className={`upload-zone${isDragging ? ' is-dragging' : ''}`}
        onClick={() => inputRef.current?.click()}
        onDragOver={(event) => {
          event.preventDefault();
          setIsDragging(true);
        }}
        onDragLeave={() => setIsDragging(false)}
        onDrop={(event) => {
          event.preventDefault();
          setIsDragging(false);
          handleFiles(event.dataTransfer.files);
        }}
      >
        <input
          ref={inputRef}
          type="file"
          multiple
          accept=".pdf,.jpg,.jpeg,.png,.xml"
          onChange={(event) => handleFiles(event.target.files)}
        />

        <span className="upload-zone-icon">
          <UploadIcon size={22} />
        </span>
        <strong>Drop documents here or click to choose files</strong>
        <span>XML up to 5 MB, other formats up to 20 MB per file.</span>
        <span className="chip-row">
          <span className="chip">XML</span>
          <span className="chip">PDF</span>
          <span className="chip">JPG</span>
          <span className="chip">PNG</span>
        </span>
        <span className="upload-zone-hint">
          Ưu tiên tải file XML hóa đơn điện tử — chính xác 100% và miễn phí
        </span>

        {uploading && (
          <div className="progress">
            <div className="progress-bar" style={{ width: `${progress}%` }} />
            <span>{progress}%</span>
          </div>
        )}

        {error && (
          <div onClick={(event) => event.stopPropagation()}>
            <Alert variant="error">{error}</Alert>
          </div>
        )}
      </div>
    </div>
  );
}
