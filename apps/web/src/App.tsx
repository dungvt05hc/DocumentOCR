import { useCallback, useEffect, useMemo, useState } from 'react';
import './App.css';
import { ClientsPanel } from './components/ClientsPanel';
import { DocumentTable } from './components/DocumentTable';
import { ExportPanel } from './components/ExportPanel';
import { FieldEditor } from './components/FieldEditor';
import { UploadZone } from './components/UploadZone';
import {
  getClientProfiles,
  getDocumentReview,
  getDocuments,
  triggerProcessing,
} from './services/api';
import type {
  ClientProfileDto,
  DocumentDto,
  DocumentReviewResponse,
  DocumentStatus,
  UploadFileResult,
} from './types';

type Page = 'upload' | 'documents' | 'clients' | 'export';
type View = Page | 'review';
type StatusFilter = 'All' | DocumentStatus;
const ALL_CLIENTS = 'All';

const statuses: StatusFilter[] = [
  'All',
  'Uploaded',
  'Processing',
  'Processed',
  'Reviewed',
  'Failed',
  'Exported',
];

export default function App() {
  const [view, setView] = useState<View>('upload');
  const [documents, setDocuments] = useState<DocumentDto[]>([]);
  const [clientProfiles, setClientProfiles] = useState<ClientProfileDto[]>([]);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [reviewDoc, setReviewDoc] = useState<DocumentReviewResponse | null>(null);
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('All');
  const [clientFilter, setClientFilter] = useState(ALL_CLIENTS);
  const [loadError, setLoadError] = useState<string | null>(null);

  const loadDocuments = useCallback(async () => {
    try {
      const response = await getDocuments({
        clientProfileId: clientFilter === ALL_CLIENTS ? undefined : clientFilter,
      });
      setDocuments(response.data);
      setLoadError(null);
    } catch {
      setLoadError('Failed to load documents. Is the API running?');
    }
  }, [clientFilter]);

  const loadClientProfiles = useCallback(async () => {
    const response = await getClientProfiles();
    setClientProfiles(response.data);
  }, []);

  useEffect(() => {
    loadDocuments();
    const interval = window.setInterval(loadDocuments, 5000);
    return () => window.clearInterval(interval);
  }, [loadDocuments]);

  useEffect(() => {
    loadClientProfiles();
  }, [loadClientProfiles]);

  const filteredDocuments = useMemo(
    () =>
      statusFilter === 'All'
        ? documents
        : documents.filter((document) => document.status === statusFilter),
    [documents, statusFilter]
  );

  const handleUploaded = async (_uploaded: UploadFileResult[]) => {
    await loadDocuments();
    setView('documents');
  };

  const handleToggleSelect = (id: string) =>
    setSelectedIds((current) => {
      const next = new Set(current);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });

  const handleViewDocument = async (id: string) => {
    const response = await getDocumentReview(id);
    setReviewDoc(response.data);
    setView('review');
  };

  const handleTriggerProcess = async (id: string) => {
    await triggerProcessing(id);
    await loadDocuments();
  };

  const handleSaved = async () => {
    setView('documents');
    setReviewDoc(null);
    await loadDocuments();
  };

  return (
    <div className="app-shell">
      <header className="app-header">
        <div>
          <h1>DocumentOCR</h1>
          <p>Vietnamese invoice and receipt extraction</p>
        </div>
        {view !== 'review' && (
          <nav className="tabs" aria-label="MVP pages">
            <button
              type="button"
              className={view === 'upload' ? 'active' : ''}
              onClick={() => setView('upload')}
            >
              Upload
            </button>
            <button
              type="button"
              className={view === 'documents' ? 'active' : ''}
              onClick={() => setView('documents')}
            >
              Documents
            </button>
            <button
              type="button"
              className={view === 'clients' ? 'active' : ''}
              onClick={() => setView('clients')}
            >
              Clients
            </button>
            <button
              type="button"
              className={view === 'export' ? 'active' : ''}
              onClick={() => setView('export')}
            >
              Export
            </button>
          </nav>
        )}
      </header>

      {view === 'upload' && (
        <main className="page-stack">
          <section className="panel">
            <div className="section-heading">
              <h2>Upload Documents</h2>
              <span className="muted">PDF, JPG, PNG</span>
            </div>
            <UploadZone clientProfiles={clientProfiles} onUploaded={handleUploaded} />
          </section>
        </main>
      )}

      {view === 'documents' && (
        <main className="page-stack">
          <section className="panel">
            <div className="section-heading">
              <h2>Documents List</h2>
              <button type="button" onClick={loadDocuments}>
                Refresh
              </button>
            </div>

            <div className="filters">
              <label>
                Status
                <select
                  value={statusFilter}
                  onChange={(event) => setStatusFilter(event.target.value as StatusFilter)}
                >
                  {statuses.map((status) => (
                    <option key={status} value={status}>
                      {status}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Client
                <select
                  value={clientFilter}
                  onChange={(event) => setClientFilter(event.target.value)}
                >
                  <option value={ALL_CLIENTS}>All</option>
                  {clientProfiles.map((client) => (
                    <option key={client.id} value={client.id}>
                      {client.name}
                    </option>
                  ))}
                </select>
              </label>
            </div>

            {loadError && <p className="message error">{loadError}</p>}

            <DocumentTable
              documents={filteredDocuments}
              selectedIds={selectedIds}
              onToggleSelect={handleToggleSelect}
              onViewDocument={handleViewDocument}
              onTriggerProcess={handleTriggerProcess}
            />
          </section>
        </main>
      )}

      {view === 'clients' && (
        <main className="page-stack">
          <ClientsPanel clientProfiles={clientProfiles} onChanged={loadClientProfiles} />
        </main>
      )}

      {view === 'export' && (
        <main className="page-stack">
          <ExportPanel
            documents={documents}
            selectedIds={selectedIds}
            onClearSelection={() => setSelectedIds(new Set())}
          />
        </main>
      )}

      {view === 'review' && reviewDoc && (
        <FieldEditor document={reviewDoc} onSaved={handleSaved} onBack={() => setView('documents')} />
      )}
    </div>
  );
}
