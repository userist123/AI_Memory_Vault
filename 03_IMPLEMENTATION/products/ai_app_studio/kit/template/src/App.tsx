import { useCallback, useState } from "react";
import { Button, Card, EmptyState, Toast } from "./ui";

export default function App() {
  const [toast, setToast] = useState(false);
  const hide = useCallback(() => setToast(false), []);
  return (
    <div className="page">
      <header className="page__header">
        <img src="/icon.svg" alt="" width={48} height={48} />
        <h1>Hello from AI App Studio</h1>
        <p className="page__lead">Describe your app in the chat; it is built from the kit components and tokens.</p>
      </header>
      <main className="page__body">
        <Card
          title="Start here"
          subtitle="Blank template"
          actions={
            <Button block onClick={() => setToast(true)}>
              Say hello
            </Button>
          }
        >
          Replace this screen with your first one.
        </Card>
        <EmptyState icon={"\u2726"} title="Nothing here yet" description="Your app's content will appear here." />
      </main>
      <Toast open={toast} tone="success" message="Hello! Everything works." onDismiss={hide} />
    </div>
  );
}
