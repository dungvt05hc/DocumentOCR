import { useCallback, useEffect, useMemo, useState } from 'react';
import './App.css';
import { Alert } from './components/Alert';
import { ClientsPanel } from './components/ClientsPanel';
import { DocumentTable } from './components/DocumentTable';
import { ExportPanel } from './components/ExportPanel';
import { FieldEditor } from './components/FieldEditor';
import { UploadZone } from './components/UploadZone';
import { DocumentsIcon, MoonIcon, SunIcon, UploadIcon, UsersIcon } from './components/icons';
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

type Page = 'upload' | 'documents' | 'clients';
type View = Page | 'review';
type StatusFilter = 'All' | DocumentStatus;
type Theme = 'light' | 'dark';
const ALL_CLIENTS = 'All';
const THEME_STORAGE_KEY = 'documentocr-theme';

const statuses: StatusFilter[] = [
  'All',
  'Uploaded',
  'Processing',
  'Processed',
  'Reviewed',
  'Failed',
  'Exported',
];

const navItems: { page: Page; label: string; icon: typeof UploadIcon }[] = [
  { page: 'upload', label: 'Upload', icon: UploadIcon },
  { page: 'documents', label: 'Documents', icon: DocumentsIcon },
  { page: 'clients', label: 'Clients', icon: UsersIcon },
];

function getStoredTheme(): Theme | null {
  const stored = window.localStorage.getItem(THEME_STORAGE_KEY);
  return stored === 'light' || stored === 'dark' ? stored : null;
}

export default function App() {
  const [view, setView] = useState<View>('upload');
  const [documents, setDocuments] = useState<DocumentDto[]>([]);
  const [isLoadingDocuments, setIsLoadingDocuments] = useState(true);
  const [clientProfiles, setClientProfiles] = useState<ClientProfileDto[]>([]);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [reviewDoc, setReviewDoc] = useState<DocumentReviewResponse | null>(null);
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('All');
  const [clientFilter, setClientFilter] = useState(ALL_CLIENTS);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [theme, setTheme] = useState<Theme | null>(() => getStoredTheme());

  useEffect(() => {
    if (theme) document.documentElement.dataset.theme = theme;
    else delete document.documentElement.dataset.theme;
  }, [theme]);

  const toggleTheme = () => {
    const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    const current = theme ?? (prefersDark ? 'dark' : 'light');
    const next: Theme = current === 'dark' ? 'light' : 'dark';
    setTheme(next);
    window.localStorage.setItem(THEME_STORAGE_KEY, next);
  };

  const loadDocuments = useCallback(async () => {
    try {
      const response = await getDocuments({
        clientProfileId: clientFilter === ALL_CLIENTS ? undefined : clientFilter,
      });
      setDocuments(response.data);
      setLoadError(null);
    } catch {
      setLoadError('Failed to load documents. Is the API running?');
    } finally {
      setIsLoadingDocuments(false);
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

  const activeTheme = theme ?? (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="sidebar-brand">
          <span className="sidebar-brand-mark">
            <DocumentsIcon size={17} />
          </span>
          <span className="sidebar-brand-text">
            <strong>DocumentOCR</strong>
            <span>Vietnamese invoice extraction</span>
          </span>
        </div>

        {view !== 'review' && (
          <nav className="sidebar-nav" aria-label="MVP pages">
            {navItems.map(({ page, label, icon: Icon }) => (
              <button
                key={page}
                type="button"
                className={view === page ? 'active' : ''}
                onClick={() => setView(page)}
              >
                <Icon size={17} />
                {label}
              </button>
            ))}
          </nav>
        )}

        <div className="sidebar-footer">
          <button type="button" className="theme-toggle" onClick={toggleTheme}>
            {activeTheme === 'dark' ? <SunIcon size={16} /> : <MoonIcon size={16} />}
            {activeTheme === 'dark' ? 'Light mode' : 'Dark mode'}
          </button>
        </div>
      </aside>

      <main className="app-main">
        <div className="app-main-inner">
          {view === 'upload' && (
            <div className="page-stack">
              <section className="panel">
                <div className="section-heading">
                  <h2>
                    <UploadIcon size={18} />
                    Upload Documents
                  </h2>
                  <span className="chip">PDF · JPG · PNG</span>
                </div>
                <UploadZone clientProfiles={clientProfiles} onUploaded={handleUploaded} />
              </section>
            </div>
          )}

          {view === 'documents' && (
            <div className="page-stack">
              <section className="panel">
                <div className="section-heading">
                  <h2>
                    <DocumentsIcon size={18} />
                    Documents List
                  </h2>
                  <button type="button" onClick={loadDocuments}>
                    Refresh
                  </button>
                </div>

                <div className="filters">
                  <label className="field">
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
                  <label className="field">
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

                {loadError && <Alert variant="error">{loadError}</Alert>}

                <ExportPanel
                  documents={documents}
                  selectedIds={selectedIds}
                  onClearSelection={() => setSelectedIds(new Set())}
                />

                <DocumentTable
                  documents={filteredDocuments}
                  isLoading={isLoadingDocuments}
                  selectedIds={selectedIds}
                  onToggleSelect={handleToggleSelect}
                  onViewDocument={handleViewDocument}
                  onTriggerProcess={handleTriggerProcess}
                />
              </section>
            </div>
          )}

          {view === 'clients' && (
            <div className="page-stack">
              <ClientsPanel clientProfiles={clientProfiles} onChanged={loadClientProfiles} />
            </div>
          )}

          {view === 'review' && reviewDoc && (
            <FieldEditor document={reviewDoc} onSaved={handleSaved} onBack={() => setView('documents')} />
          )}
        </div>
      </main>
    </div>
  );
}
