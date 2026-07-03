import { useCallback, useRef, useState } from 'react';
import { uploadDocuments } from '../services/api';
import type { DocumentDto } from '../types';

interface Props {
  onUploaded: (docs: DocumentDto[]) => void;
}

export function UploadZone({ onUploaded }: Props) {
  const [isDragging, setIsDragging] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  const handleFiles = useCallback(
    async (files: FileList | null) => {
      if (!files || files.length === 0) return;

      const allowed = ['application/pdf', 'image/jpeg', 'image/png'];
      const fileArr = Array.from(files).filter((f) => allowed.includes(f.type));
      if (fileArr.length === 0) {
        setError('Only PDF, JPEG, and PNG files are supported.');
        return;
      }

      setUploading(true);
      setError(null);
      try {
        const res = await uploadDocuments(fileArr);
        onUploaded(res.data);
      } catch {
        setError('Upload failed. Please try again.');
      } finally {
        setUploading(false);
      }
    },
    [onUploaded]
  );

  return (
    <div
      onDragOver={(e) => { e.preventDefault(); setIsDragging(true); }}
      onDragLeave={() => setIsDragging(false)}
      onDrop={(e) => { e.preventDefault(); setIsDragging(false); handleFiles(e.dataTransfer.files); }}
      onClick={() => inputRef.current?.click()}
      style={{
        border: `2px dashed ${isDragging ? '#2D6A9F' : '#aaa'}`,
        borderRadius: 8,
        padding: '3rem',
        textAlign: 'center',
        cursor: 'pointer',
        background: isDragging ? '#e8f0fe' : '#fafafa',
        transition: 'all 0.2s',
      }}
    >
      <input
        ref={inputRef}
        type="file"
        multiple
        accept=".pdf,.jpg,.jpeg,.png"
        style={{ display: 'none' }}
        onChange={(e) => handleFiles(e.target.files)}
      />
      {uploading ? (
        <p>Uploading…</p>
      ) : (
        <>
          <p style={{ fontSize: '1.1rem', margin: 0 }}>
            Drop files here or <strong>click to browse</strong>
          </p>
          <p style={{ color: '#888', fontSize: '0.85rem', marginTop: 4 }}>
            Supports PDF, JPEG, PNG — up to 20 MB each
          </p>
        </>
      )}
      {error && <p style={{ color: 'red', marginTop: 8 }}>{error}</p>}
    </div>
  );
}
