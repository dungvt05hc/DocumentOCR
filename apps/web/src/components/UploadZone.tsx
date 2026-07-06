import { useCallback, useRef, useState } from 'react';
import { uploadDocuments } from '../services/api';
import type { UploadFileResult } from '../types';

interface Props {
  onUploaded: (results: UploadFileResult[]) => void;
}

const allowedTypes = ['application/pdf', 'image/jpeg', 'image/png'];
const maxFileSizeBytes = 20 * 1024 * 1024;

export function UploadZone({ onUploaded }: Props) {
  const [isDragging, setIsDragging] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [progress, setProgress] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  const handleFiles = useCallback(
    async (files: FileList | null) => {
      if (!files || files.length === 0) return;

      const fileList = Array.from(files);
      // Files that obviously can't succeed (wrong type/too large) are skipped
      // client-side, but one bad file must not block the valid ones in the
      // same batch from being uploaded.
      const skipped = fileList.filter(
        (file) => !allowedTypes.includes(file.type) || file.size > maxFileSizeBytes
      );
      const toUpload = fileList.filter((file) => !skipped.includes(file));

      if (toUpload.length === 0) {
        setError('Use PDF, JPG, or PNG files up to 20 MB each.');
        return;
      }

      setUploading(true);
      setProgress(0);
      setError(
        skipped.length > 0
          ? `Skipped ${skipped.length} file(s): must be PDF, JPG, or PNG up to 20 MB.`
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
                ? `Skipped ${skipped.length} file(s): must be PDF, JPG, or PNG up to 20 MB.`
                : null,
              `${failed.length} of ${response.data.length} file(s) failed: ` +
                failed.map((result) => `${result.fileName} (${result.error})`).join('; '),
            ]
              .filter(Boolean)
              .join(' ')
          );
        }

        if (response.data.some((result) => result.success)) {
          onUploaded(response.data);
        }
      } catch {
        setError('Upload failed. Please try again.');
      } finally {
        setUploading(false);
        if (inputRef.current) inputRef.current.value = '';
      }
    },
    [onUploaded]
  );

  return (
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
        accept=".pdf,.jpg,.jpeg,.png"
        onChange={(event) => handleFiles(event.target.files)}
      />

      <strong>Drop documents here or click to choose files</strong>
      <span>PDF, JPG, PNG. Maximum 20 MB per file.</span>

      {uploading && (
        <div className="progress">
          <div className="progress-bar" style={{ width: `${progress}%` }} />
          <span>{progress}%</span>
        </div>
      )}

      {error && <p className="message error">{error}</p>}
    </div>
  );
}
