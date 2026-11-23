import { useCallback, useState } from "react";
import { useAasState, type AasState } from "./aas-states";
import { Button, Card, Dialog, EmptyState, Input, List, ListItem, Sheet, Skeleton, TabBar, Toast, panelId, tabId, type TabItem } from "./ui";

const TABS: TabItem[] = [
  { id: "home", label: "Home", icon: "\u2302" },
  { id: "search", label: "Search", icon: "\u2315" },
  { id: "profile", label: "Profile", icon: "\u263a" },
];

const PLACES = ["Botanical garden", "City library", "Riverside park", "Old town square", "Science museum"];

/** Loading and error views shared by the screens (the studio forces them through ?aas-state=); null = show the screen. */
function stateView(forced: AasState | null, what: string, retry: () => void) {
  if (forced === "loading") {
    return (
      <div aria-busy="true" aria-label="Loading">
        <Skeleton lines={4} />
      </div>
    );
  }
  if (forced === "error") {
    return (
      <EmptyState tone="error" icon={"!"} title={`Could not load ${what}`} description="Check your connection and try again."
        action={<Button onClick={retry}>Try again</Button>} />
    );
  }
  return null;
}

function Home(props: { onDone: () => void; forced: AasState | null }) {
  const other = stateView(props.forced, "your home screen", props.onDone);
  if (other) return other;
  if (props.forced === "empty") {
    return <EmptyState icon={"\u2302"} title="Nothing here yet" description="Your home screen will fill up as you use the app." />;
  }
  return (
    <>
      <h1>Welcome</h1>
      <p className="lead">Start here. Replace this text with your app's home screen.</p>
      <Card title="Today" subtitle="3 things to try" actions={<Button onClick={props.onDone}>Mark done</Button>}>
        Cards group related content; keep one primary action per card.
      </Card>
      <Card title="Tip" subtitle="Design system">
        Every colour comes from a token, so dark mode and the style preset just work.
      </Card>
    </>
  );
}

function Search(props: { forced: AasState | null }) {
  const [q, setQ] = useState("");
  const hits = props.forced === "empty" ? [] : PLACES.filter((p) => p.toLowerCase().includes(q.trim().toLowerCase()));
  const other = stateView(props.forced, "places", () => setQ(""));
  if (other) return other;
  return (
    <>
      <h1>Search</h1>
      <Input label="Search places" type="search" value={q} placeholder="e.g. park" onChange={(e) => setQ(e.target.value)} />
      {hits.length ? (
        <List aria-label="Results">
          {hits.map((p) => (
            <ListItem key={p} title={p} subtitle="Open now" leading={p[0]} chevron onClick={() => undefined} />
          ))}
        </List>
      ) : (
        <EmptyState icon={"\u2315"} title="No results" description={`Nothing matches \u201c${q}\u201d. Try another word.`}
          action={<Button variant="secondary" onClick={() => setQ("")}>Clear search</Button>} />
      )}
    </>
  );
}

function Profile(props: { onSaved: () => void; forced: AasState | null }) {
  const [sheet, setSheet] = useState(false);
  const [confirm, setConfirm] = useState(false);
  const [name, setName] = useState("Alex");
  const closeSheet = useCallback(() => setSheet(false), []);
  const closeConfirm = useCallback(() => setConfirm(false), []);
  const other = stateView(props.forced, "your profile", props.onSaved);
  if (other) return other;
  if (props.forced === "empty") {
    return <EmptyState icon={"\u263a"} title="No profile yet" description="Add your details to see them here." />;
  }
  return (
    <>
      <h1>Profile</h1>
      <List aria-label="Account">
        <ListItem title={name} subtitle="Tap to edit your name" leading={name[0] ?? "?"} chevron onClick={() => setSheet(true)} />
        <ListItem title="Notifications" trailing="On" />
        <ListItem title="Version" trailing="1.0" />
      </List>
      <Button variant="danger" block onClick={() => setConfirm(true)}>
        Sign out
      </Button>
      <Sheet open={sheet} title="Edit name" onClose={closeSheet}>
        <Input label="Name" value={name} onChange={(e) => setName(e.target.value)} />
        <Button block onClick={() => { setSheet(false); props.onSaved(); }}>
          Save
        </Button>
      </Sheet>
      <Dialog open={confirm} title="Sign out?" description="You can sign in again at any time." onClose={closeConfirm}
        actions={
          <>
            <Button variant="ghost" onClick={closeConfirm}>Cancel</Button>
            <Button variant="danger" onClick={closeConfirm}>Sign out</Button>
          </>
        } />
    </>
  );
}

export default function App() {
  const [active, setActive] = useState("home");
  const [toast, setToast] = useState("");
  const hide = useCallback(() => setToast(""), []);
  const forced = useAasState(); // preview state switch (?aas-state=), null in normal use
  return (
    <div className="shell">
      <main className="panel" role="tabpanel" id={panelId(active)} aria-labelledby={tabId(active)} tabIndex={0}>
        {active === "home" ? <Home forced={forced} onDone={() => setToast("Nice, marked as done")} /> : null}
        {active === "search" ? <Search forced={forced} /> : null}
        {active === "profile" ? <Profile forced={forced} onSaved={() => setToast("Name saved")} /> : null}
      </main>
      <TabBar tabs={TABS} active={active} onChange={setActive} />
      <Toast open={toast !== ""} tone="success" message={toast} onDismiss={hide} />
    </div>
  );
}
