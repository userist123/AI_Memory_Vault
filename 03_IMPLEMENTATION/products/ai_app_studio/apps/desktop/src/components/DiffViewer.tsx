import type { FileView } from "../lib/store";

function lineClass(l: string): string {
  if (l.startsWith("@@")) return "hunk";
  if (l.startsWith("+++") || l.startsWith("---")) return "meta";
  if (l.startsWith("+")) return "add";
  if (l.startsWith("-")) return "del";
  return "";
}

/** Read-only live diff of the file being written. Every line is a text node. */
export function DiffViewer(props: {
  files: Record<string, FileView>;
  order: string[];
  active: string | null;
  onSelect: (path: string) => void;
}) {
  const file = props.active ? props.files[props.active] : undefined;
  const tabs = props.order.slice(-8).reverse();
  return (
    <div className="diff-viewer" data-testid="diff-viewer">
      <nav className="tabs" aria-label="Files">
        {tabs.length === 0 && <span className="muted">No file written yet</span>}
        {tabs.map((p) => (
          <button
            key={p}
            className={`tab${p === props.active ? " active" : ""}`}
            onClick={() => props.onSelect(p)}
            title={p}
          >
            {p.split("/").pop()}
            {props.files[p]?.typing ? <span className="dot" aria-label="being written" /> : null}
          </button>
        ))}
      </nav>
      {file ? (
        <>
          <div className="file-head">
            <span className="path" data-testid="diff-path">
              {file.path}
            </span>
            {file.revision !== null && <span className="tag">rev {file.revision}</span>}
            <span className="pills">
              <span className="pill add">+{file.added}</span>
              <span className="pill del">-{file.removed}</span>
            </span>
          </div>
          <pre className="code" data-testid="diff-body">
            {file.typing
              ? file.typing.split("\n").map((l, i) => (
                  <div key={i} className="add typing">
                    <span className="gutter">{i + 1}</span>
                    {l}
                  </div>
                ))
              : file.diff.split("\n").map((l, i) => (
                  <div key={i} className={lineClass(l)}>
                    {l}
                  </div>
                ))}
            {file.typing ? <span className="caret" aria-hidden="true" /> : null}
          </pre>
        </>
      ) : (
        <p className="empty">The file the AI is writing appears here, live.</p>
      )}
    </div>
  );
}
