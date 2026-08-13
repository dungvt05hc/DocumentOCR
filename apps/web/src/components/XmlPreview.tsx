import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { CheckIcon, ChevronRightIcon, DownloadIcon, SearchIcon } from './icons';

interface ParsedXmlNode {
  tag: string;
  attrs: { name: string; value: string }[];
  children: ParsedXmlNode[];
  text: string | null;
}

function elementToNode(el: Element): ParsedXmlNode {
  const attrs = Array.from(el.attributes).map((attr) => ({ name: attr.name, value: attr.value }));
  const childElements = Array.from(el.children);

  if (childElements.length === 0) {
    const text = (el.textContent ?? '').trim();
    return { tag: el.tagName, attrs, children: [], text: text.length > 0 ? text : null };
  }

  return { tag: el.tagName, attrs, children: childElements.map(elementToNode), text: null };
}

function parseXml(xmlText: string): { root: ParsedXmlNode | null; error: string | null } {
  const doc = new DOMParser().parseFromString(xmlText, 'application/xml');
  if (doc.querySelector('parsererror')) return { root: null, error: 'Không đọc được cấu trúc XML.' };
  if (!doc.documentElement) return { root: null, error: 'Tệp XML rỗng.' };
  return { root: elementToNode(doc.documentElement), error: null };
}

function nodeSelfMatches(node: ParsedXmlNode, query: string): boolean {
  if (node.tag.toLowerCase().includes(query)) return true;
  if (node.attrs.some((a) => a.name.toLowerCase().includes(query) || a.value.toLowerCase().includes(query)))
    return true;
  if (node.text && node.text.toLowerCase().includes(query)) return true;
  return false;
}

function subtreeMatches(node: ParsedXmlNode, query: string): boolean {
  if (nodeSelfMatches(node, query)) return true;
  return node.children.some((child) => subtreeMatches(child, query));
}

function countMatches(node: ParsedXmlNode, query: string): number {
  let count = nodeSelfMatches(node, query) ? 1 : 0;
  for (const child of node.children) count += countMatches(child, query);
  return count;
}

function highlight(text: string, query: string): ReactNode {
  if (!query) return text;
  const lower = text.toLowerCase();
  const parts: ReactNode[] = [];
  let cursor = 0;
  let idx = lower.indexOf(query, cursor);
  while (idx !== -1) {
    if (idx > cursor) parts.push(text.slice(cursor, idx));
    parts.push(
      <mark className="xml-mark" key={idx}>
        {text.slice(idx, idx + query.length)}
      </mark>
    );
    cursor = idx + query.length;
    idx = lower.indexOf(query, cursor);
  }
  parts.push(text.slice(cursor));
  return parts;
}

const TEXT_TRUNCATE_AT = 200;

function LeafValue({ text, query }: { text: string; query: string }) {
  const selfMatches = query.length > 0 && text.toLowerCase().includes(query);
  const [expanded, setExpanded] = useState(false);
  const isLong = text.length > TEXT_TRUNCATE_AT;
  const showFull = expanded || selfMatches || !isLong;

  return (
    <span className="xml-text">
      {highlight(showFull ? text : `${text.slice(0, TEXT_TRUNCATE_AT)}…`, query)}
      {isLong && !selfMatches && (
        <button type="button" className="xml-truncate-toggle" onClick={() => setExpanded((v) => !v)}>
          {expanded ? 'thu gọn' : `xem đủ (${text.length.toLocaleString('vi-VN')} ký tự)`}
        </button>
      )}
    </span>
  );
}

interface NodeRowProps {
  node: ParsedXmlNode;
  depth: number;
  path: string;
  query: string;
  autoExpandDepth: number;
  overrides: Record<string, boolean>;
  onToggle: (path: string, nextOpen: boolean) => void;
}

function OpenTag({ node, query }: { node: ParsedXmlNode; query: string }) {
  return (
    <>
      <span className="xml-punct">&lt;</span>
      <span className="xml-tag">{highlight(node.tag, query)}</span>
      {node.attrs.map((attr) => (
        <span key={attr.name}>
          {' '}
          <span className="xml-attr-name">{highlight(attr.name, query)}</span>
          <span className="xml-punct">=&quot;</span>
          <span className="xml-attr-value">{highlight(attr.value, query)}</span>
          <span className="xml-punct">&quot;</span>
        </span>
      ))}
    </>
  );
}

function XmlNodeRow({ node, depth, path, query, autoExpandDepth, overrides, onToggle }: NodeRowProps) {
  const hasChildren = node.children.length > 0;
  const forceOpenBySearch = query.length > 0 && hasChildren && subtreeMatches(node, query);
  const defaultOpen = depth < autoExpandDepth;
  const isOpen = forceOpenBySearch || (overrides[path] ?? defaultOpen);
  const indent = { paddingLeft: depth * 16 };

  if (!hasChildren) {
    return (
      <div className="xml-row" style={indent}>
        <span className="xml-row-spacer" />
        <OpenTag node={node} query={query} />
        {node.text === null ? (
          <span className="xml-punct"> /&gt;</span>
        ) : (
          <>
            <span className="xml-punct">&gt;</span>
            <LeafValue text={node.text} query={query} />
            <span className="xml-punct">&lt;/</span>
            <span className="xml-tag">{node.tag}</span>
            <span className="xml-punct">&gt;</span>
          </>
        )}
      </div>
    );
  }

  return (
    <div className="xml-node">
      <div className="xml-row" style={indent}>
        <button
          type="button"
          className={`xml-toggle${isOpen ? ' is-open' : ''}`}
          onClick={() => onToggle(path, !isOpen)}
          aria-label={isOpen ? 'Thu gọn' : 'Mở rộng'}
        >
          <ChevronRightIcon size={13} />
        </button>
        <OpenTag node={node} query={query} />
        <span className="xml-punct">&gt;</span>
        {!isOpen && (
          <>
            <button type="button" className="xml-collapsed-summary" onClick={() => onToggle(path, true)}>
              … {node.children.length} phần tử
            </button>
            <span className="xml-punct">&lt;/</span>
            <span className="xml-tag">{node.tag}</span>
            <span className="xml-punct">&gt;</span>
          </>
        )}
      </div>

      {isOpen && (
        <>
          {node.children.map((child, index) => (
            <XmlNodeRow
              key={index}
              node={child}
              depth={depth + 1}
              path={`${path}.${index}`}
              query={query}
              autoExpandDepth={autoExpandDepth}
              overrides={overrides}
              onToggle={onToggle}
            />
          ))}
          <div className="xml-row" style={indent}>
            <span className="xml-row-spacer" />
            <span className="xml-punct">&lt;/</span>
            <span className="xml-tag">{node.tag}</span>
            <span className="xml-punct">&gt;</span>
          </div>
        </>
      )}
    </div>
  );
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

const DEFAULT_EXPAND_DEPTH = 2;

interface Props {
  xml: string;
  fileName: string;
  downloadHref: string | null;
}

export function XmlPreview({ xml, fileName, downloadHref }: Props) {
  const { root, error } = useMemo(() => parseXml(xml), [xml]);
  const [rawQuery, setRawQuery] = useState('');
  const query = rawQuery.trim().toLowerCase();
  const [autoExpandDepth, setAutoExpandDepth] = useState(DEFAULT_EXPAND_DEPTH);
  const [overrides, setOverrides] = useState<Record<string, boolean>>({});
  const [copied, setCopied] = useState(false);
  const bodyRef = useRef<HTMLDivElement>(null);

  const matchCount = useMemo(
    () => (root && query ? countMatches(root, query) : 0),
    [root, query]
  );

  useEffect(() => {
    if (!query) return;
    const timer = window.setTimeout(() => {
      bodyRef.current?.querySelector('mark')?.scrollIntoView({ block: 'center', behavior: 'smooth' });
    }, 30);
    return () => window.clearTimeout(timer);
  }, [query]);

  const handleToggle = (path: string, nextOpen: boolean) =>
    setOverrides((current) => ({ ...current, [path]: nextOpen }));

  const handleExpandAll = () => {
    setAutoExpandDepth(Number.POSITIVE_INFINITY);
    setOverrides({});
  };

  const handleCollapseAll = () => {
    setAutoExpandDepth(0);
    setOverrides({});
  };

  const handleCopy = async () => {
    await navigator.clipboard.writeText(xml);
    setCopied(true);
    window.setTimeout(() => setCopied(false), 1500);
  };

  const byteSize = useMemo(() => new TextEncoder().encode(xml).length, [xml]);

  return (
    <div className="xml-viewer">
      <div className="xml-viewer-toolbar">
        <div className="xml-search">
          <SearchIcon size={14} />
          <input
            type="search"
            placeholder="Tìm thẻ, thuộc tính, giá trị..."
            value={rawQuery}
            onChange={(event) => setRawQuery(event.target.value)}
          />
          {query && <span className="xml-search-count">{matchCount} kết quả</span>}
        </div>

        <div className="xml-viewer-actions">
          <button type="button" onClick={handleExpandAll}>
            Mở rộng tất cả
          </button>
          <button type="button" onClick={handleCollapseAll}>
            Thu gọn tất cả
          </button>
          <button type="button" onClick={handleCopy}>
            {copied ? <CheckIcon size={13} /> : null}
            {copied ? 'Đã sao chép' : 'Sao chép XML'}
          </button>
          {downloadHref && (
            <a className="btn-icon-text" href={downloadHref} download={fileName}>
              <DownloadIcon size={13} />
              Tải xuống
            </a>
          )}
        </div>
      </div>

      <div className="xml-viewer-meta">
        <span>{fileName}</span>
        <span>{formatBytes(byteSize)}</span>
      </div>

      <div className="xml-viewer-body" ref={bodyRef}>
        {root ? (
          <XmlNodeRow
            node={root}
            depth={0}
            path="0"
            query={query}
            autoExpandDepth={autoExpandDepth}
            overrides={overrides}
            onToggle={handleToggle}
          />
        ) : (
          <>
            {error && <div className="xml-parse-error">{error} Hiển thị nội dung thô bên dưới.</div>}
            <pre className="xml-raw-fallback">{xml}</pre>
          </>
        )}
      </div>
    </div>
  );
}
