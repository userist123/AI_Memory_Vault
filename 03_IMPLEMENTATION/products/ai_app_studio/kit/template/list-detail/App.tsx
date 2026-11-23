import { useCallback, useEffect, useRef, useState } from "react";
import { Button, Dialog, EmptyState, Input, List, ListItem, Sheet, Toast } from "./ui";

type Item = { id: string; title: string; summary: string; body: string };

const START: Item[] = [
  { id: "1", title: "First item", summary: "A short summary", body: "Details of the first item go here." },
  { id: "2", title: "Second item", summary: "Another summary", body: "Details of the second item go here." },
  { id: "3", title: "Third item", summary: "One more", body: "Details of the third item go here." },
];

export default function App() {
  const [items, setItems] = useState<Item[]>(START);
  const [selected, setSelected] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);
  const [draft, setDraft] = useState("");
  const [draftError, setDraftError] = useState("");
  const [confirm, setConfirm] = useState(false);
  const [toast, setToast] = useState("");
  const heading = useRef<HTMLHeadingElement>(null);
  const item = items.find((i) => i.id === selected) ?? null;

  useEffect(() => {
    if (item) heading.current?.focus(); // move focus to the detail for keyboard and screen-reader users
  }, [item]);

  const closeAdd = useCallback(() => setAdding(false), []);
  const closeConfirm = useCallback(() => setConfirm(false), []);
  const hideToast = useCallback(() => setToast(""), []);

  const add = () => {
    const title = draft.trim();
    if (!title) {
      setDraftError("Give the item a name.");
      return;
    }
    const id = String(Date.now());
    setItems((xs) => [...xs, { id, title, summary: "New", body: "Write the details here." }]);
    setDraft("");
    setDraftError("");
    setAdding(false);
    setToast("Item added");
  };

  const remove = () => {
    setItems((xs) => xs.filter((x) => x.id !== selected));
    setSelected(null);
    setConfirm(false);
    setToast("Item deleted");
  };

  return (
    <div className={item ? "layout has-detail" : "layout"}>
      <nav className="list" aria-label="Items">
        <div className="list__header">
          <h1>Items</h1>
          <Button variant="secondary" onClick={() => setAdding(true)}>
            Add
          </Button>
        </div>
        {items.length ? (
          <List aria-label="All items">
            {items.map((i) => (
              <ListItem key={i.id} title={i.title} subtitle={i.summary} leading={i.title[0]} chevron
                aria-current={i.id === selected ? "true" : undefined} onClick={() => setSelected(i.id)} />
            ))}
          </List>
        ) : (
          <EmptyState icon={"\u2630"} title="No items yet" description="Add your first item to get started."
            action={<Button onClick={() => setAdding(true)}>Add item</Button>} />
        )}
      </nav>
      <main className="detail" aria-label="Details">
        {item ? (
          <article className="detail__article">
            <Button variant="ghost" className="back" onClick={() => setSelected(null)}>
              {"\u2039"} Back
            </Button>
            <h2 ref={heading} tabIndex={-1}>
              {item.title}
            </h2>
            <p>{item.body}</p>
            <Button variant="danger" onClick={() => setConfirm(true)}>
              Delete
            </Button>
          </article>
        ) : (
          <EmptyState title="Nothing selected" description="Choose an item to see its details." />
        )}
      </main>
      <Sheet open={adding} title="New item" onClose={closeAdd}>
        <Input label="Name" value={draft} error={draftError || undefined} onChange={(e) => setDraft(e.target.value)} />
        <Button block onClick={add}>
          Add item
        </Button>
      </Sheet>
      <Dialog open={confirm} title="Delete this item?" description="This cannot be undone." onClose={closeConfirm}
        actions={
          <>
            <Button variant="ghost" onClick={closeConfirm}>Cancel</Button>
            <Button variant="danger" onClick={remove}>Delete</Button>
          </>
        } />
      <Toast open={toast !== ""} tone="success" message={toast} onDismiss={hideToast} />
    </div>
  );
}
