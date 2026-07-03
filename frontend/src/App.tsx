import { useCallback, useEffect, useState } from "react";
import { DocumentTable } from "./components/DocumentTable";
import { ExportPanel } from "./components/ExportPanel";
import { FieldEditor } from "./components/FieldEditor";
import { UploadZone } from "./components/UploadZone";
import { getDocumentById, getDocuments, triggerProcessing } from "./services/api";
import type { DocumentDetailDto, DocumentDto } from "./types";

type View = "list" | "review";

export default function App() {
  const [view, setView] = useState<View>("list");
  const [documents, setDocuments] = useState<DocumentDto[]>([]);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [reviewDoc, setReviewDoc] = useState<DocumentDetailDto | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  const loadDocuments = useCallback(async () => {
    try {
      const res = await getDocuments();
      setDocuments(res.data);
      setLoadError(null);
    } catch {
      setLoadError("Failed to load documents. Is the API running?");
    }
  }, []);

  useEffect(() => {
    loadDocuments();
    const interval = setInterval(loadDocuments, 5000);
    return () => clearInterval(interval);
  }, [loadDocuments]);

  const handleUploaded = (newDocs: DocumentDto[]) =>
    setDocuments((prev) => [...newDocs, ...prev]);

  const handleToggleSelect = (id: string) =>
    setSelectedIds((prev) => {
      const next = new Set(prev);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });

  const handleViewDocument = async (id: string) => {
    const res = await getDocumentById(id);
    setReviewDoc(res.data);
    setView("review");
  };

  const handleTriggerProcess = async (id: string) => {
    await triggerProcessing(id);
    await loadDocuments();
  };

  const handleSaved = async () => {
    setView("list");
    await loadDocuments();
  };

  return (
    <div style={{ maxWidth: 1100, margin: "0 auto", padding: "24px 16px", fontFamily: "system-ui, sans-serif" }}>
      <header style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0, color: "#2D6A9F" }}>DocumentOCR</h1>
        <p style={{ margin: "4px 0 0", color: "#888", fontSize: "0.9rem" }}>
          Vietnamese invoice and receipt extraction
        </p>
      </header>

      {view === "list" && (
        <>
          <section style={{ marginBottom: 24 }}>
            <h2 style={{ fontSize: "1rem", marginBottom: 8 }}>Upload Documents</h2>
            <UploadZone onUploaded={handleUploaded} />
          </section>

          <section>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 12 }}>
              <h2 style={{ fontSize: "1rem", margin: 0 }}>Documents</h2>
              <button onClick={loadDocuments} style={{ cursor: "pointer", fontSize: "0.85rem" }}>Refresh</button>
            </div>

            {loadError && <p style={{ color: "red" }}>{loadError}</p>}

            {selectedIds.size > 0 && (
              <div style={{ marginBottom: 12 }}>
                <ExportPanel selectedIds={selectedIds} onClearSelection={() => setSelectedIds(new Set())} />
              </div>
            )}

            <DocumentTable
              documents={documents}
              selectedIds={selectedIds}
              onToggleSelect={handleToggleSelect}
              onViewDocument={handleViewDocument}
              onTriggerProcess={handleTriggerProcess}
            />
          </section>
        </>
      )}

      {view === "review" && reviewDoc && (
        <FieldEditor document={reviewDoc} onSaved={handleSaved} onBack={() => setView("list")} />
      )}
    </div>
  );
}
