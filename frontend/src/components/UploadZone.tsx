import { useCallback, useRef, useState } from 'react';
import { uploadDocuments } from '../services/api';
import type { UploadDocumentResponse } from '../types';

interface Props {
  onUploaded: (docs: UploadDocumentResponse[]) => void;
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
      const invalid = fileList.find(
        (file) => !allowedTypes.includes(file.type) || file.size > maxFileSizeBytes
      );

      if (invalid) {
        setError('Use PDF, JPG, or PNG files up to 20 MB each.');
        return;
      }

      setUploading(true);
      setProgress(0);
      setError(null);

      try {
        const response = await uploadDocuments(fileList, setProgress);
        setProgress(100);
        onUploaded(response.data);
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
