export const SCHEMA = `
create table if not exists suppliers(id text primary key, name text not null, country text not null, website text not null);
create table if not exists products(id text primary key, grp text not null, name text not null, brand text not null, category text not null, model3d text not null);
create table if not exists variants(id text primary key, product_id text not null references products(id), name text not null, legacy_index int not null,
  dims jsonb, dims_confidence text not null, style jsonb not null default '{}'::jsonb, chairs int, included_with text);
create table if not exists offers(id text primary key, variant_id text not null references variants(id), supplier_id text not null references suppliers(id),
  price numeric(12,2) not null, currency text not null, availability text not null default 'UNKNOWN', affiliate_url text,
  source text not null, source_url text, verified_at date, verification_type text not null, confidence text not null);
create table if not exists projects(id uuid primary key default gen_random_uuid(), owner text not null, name text not null, currency text not null default 'RON',
  country text not null default 'RO', draft jsonb not null, created_at timestamptz not null default now(), updated_at timestamptz not null default now(),
  current_revision int not null default 0);
create index if not exists projects_owner on projects(owner, updated_at desc);
alter table projects add column if not exists meta jsonb not null default '{}'::jsonb;
alter table projects add column if not exists snag_seq int not null default 0;
create table if not exists revisions(id uuid primary key default gen_random_uuid(), project_id uuid not null references projects(id) on delete cascade,
  number int not null, note text not null default '', created_at timestamptz not null default now(), snapshot jsonb not null, unique(project_id, number));
create table if not exists materials(id text primary key, category text not null, name text not null, supplier text not null, unit text not null, unit_price numeric(12,2) not null,
  pack jsonb, coverage numeric(8,2), consumption numeric(8,2), source_url text, verified_at date, verification_type text not null, confidence text not null, note text);
alter table materials add column if not exists specs jsonb;
create table if not exists labor_rates(id text primary key, label text not null, unit text not null, low numeric(10,2) not null, expected numeric(10,2) not null, high numeric(10,2) not null,
  sources jsonb not null, confidence text not null, verified_at date);
create table if not exists services(id text primary key, label text not null, supplier text not null, price numeric(10,2), price_per_meter numeric(10,2), source_url text,
  verification_type text not null, confidence text not null, note text, verified_at date);
create table if not exists proposals(id uuid primary key default gen_random_uuid(), project_id uuid not null references projects(id) on delete cascade,
  source text not null, model text, brief jsonb not null, raw jsonb not null, notes jsonb not null default '[]'::jsonb, decisions jsonb not null default '{}'::jsonb, created_at timestamptz not null default now());
create table if not exists retailers(slug text primary key, name text not null, website text not null, affiliate_status text not null default 'none', network text,
  default_utm jsonb not null default '{}'::jsonb, disclosure_required boolean not null default false);
create table if not exists offer_links(id bigserial primary key, target_kind text not null, target_id text not null, retailer text not null references retailers(slug), type text not null,
  network text, url text not null, active_from timestamptz not null default now(), active_to timestamptz, last_status int, last_checked_at timestamptz, created_at timestamptz not null default now());
create index if not exists offer_links_target on offer_links(target_kind, target_id);
create table if not exists clicks(id bigserial primary key, target_kind text not null, target_id text not null, retailer text not null, link_type text not null,
  hour timestamptz not null, device text not null, from_page text);
create index if not exists clicks_target on clicks(target_kind, target_id);
create table if not exists price_history(id bigserial primary key, target_kind text not null, target_id text not null, price numeric(12,2) not null, verified_at date not null, recorded_at timestamptz not null default now());
create table if not exists design_proposals(id uuid primary key default gen_random_uuid(), project_id uuid not null references projects(id) on delete cascade,
  base_fingerprint text not null, brief jsonb not null, alternatives jsonb not null, decisions jsonb not null default '{}'::jsonb, created_at timestamptz not null default now());
alter table design_proposals add column if not exists applied_index int;
create index if not exists design_proposals_project on design_proposals(project_id, created_at desc);
create table if not exists shares(token text primary key, project_id uuid not null references projects(id) on delete cascade, revision_number int not null,
  created_at timestamptz not null default now(), revoked_at timestamptz);
create index if not exists shares_project on shares(project_id);
create table if not exists share_feedback(id uuid primary key default gen_random_uuid(), token text not null references shares(token) on delete cascade,
  project_id uuid not null references projects(id) on delete cascade, room_id text, verdict text not null check (verdict in ('like','change','question')),
  author text not null, body text not null, created_at timestamptz not null default now(), resolved_at timestamptz);
create index if not exists share_feedback_project on share_feedback(project_id, created_at desc);
create index if not exists share_feedback_token on share_feedback(token);
alter table share_feedback add column if not exists item jsonb;
create table if not exists site_photos(id uuid primary key default gen_random_uuid(), project_id uuid not null references projects(id) on delete cascade,
  mime text not null, data bytea not null, bytes int not null, created_at timestamptz not null default now());
create index if not exists site_photos_project on site_photos(project_id);
create table if not exists snags(id uuid primary key default gen_random_uuid(), project_id uuid not null references projects(id) on delete cascade, number int not null,
  title text not null, body text not null default '', room_id text, trade text not null, status text not null default 'open', photos jsonb not null default '[]'::jsonb,
  created_at timestamptz not null default now(), updated_at timestamptz not null default now(), resolved_at timestamptz, unique(project_id, number));
create index if not exists snags_project on snags(project_id, number);
create table if not exists tasks(id uuid primary key default gen_random_uuid(), owner text not null, project_id uuid references projects(id) on delete cascade,
  title text not null, due date, done_at timestamptz, created_at timestamptz not null default now());
create index if not exists tasks_owner on tasks(owner, done_at, due);
create table if not exists procurement(project_id uuid not null references projects(id) on delete cascade, key text not null, state jsonb not null,
  updated_at timestamptz not null default now(), primary key(project_id, key));
create table if not exists custom_items(id uuid primary key default gen_random_uuid(), project_id uuid not null references projects(id) on delete cascade,
  name text not null, supplier text not null, url text, image text, sku text, price numeric(12,2), currency text, qty numeric(12,2) not null default 1, unit text not null default 'buc',
  room_id text, created_at timestamptz not null default now());
create index if not exists custom_items_project on custom_items(project_id, created_at);
create table if not exists change_orders(id uuid primary key default gen_random_uuid(), project_id uuid not null references projects(id) on delete cascade, number int not null,
  title text not null, body text not null default '', amount numeric(12,2) not null, days int not null default 0, status text not null default 'draft',
  created_at timestamptz not null default now(), sent_at timestamptz, decided_at timestamptz, decided_by text, client_note text, unique(project_id, number));
create index if not exists change_orders_project on change_orders(project_id, number);
create table if not exists surveys(project_id uuid not null references projects(id) on delete cascade, room_id text not null, data jsonb not null,
  measured_at timestamptz not null default now(), primary key(project_id, room_id));
create table if not exists time_entries(id uuid primary key default gen_random_uuid(), project_id uuid not null references projects(id) on delete cascade,
  date date not null, minutes int not null, kind text not null, note text not null default '', created_at timestamptz not null default now());
create index if not exists time_entries_project on time_entries(project_id, date desc);
alter table projects add column if not exists deleted_at timestamptz;
create index if not exists projects_trash on projects(owner, deleted_at) where deleted_at is not null;
create table if not exists studio_profile(owner text primary key, data jsonb not null, updated_at timestamptz not null default now());
alter table projects add column if not exists draft_rev int not null default 0;
create table if not exists room_choices(token text not null references shares(token) on delete cascade, project_id uuid not null references projects(id) on delete cascade, room_id text not null,
  option_id text not null, label text not null, author text not null, note text not null default '', chosen_at timestamptz not null default now(), primary key(token, room_id));
create index if not exists room_choices_project on room_choices(project_id, chosen_at desc);
create table if not exists project_contract(project_id uuid primary key references projects(id) on delete cascade, data jsonb not null, updated_at timestamptz not null default now());
alter table custom_items add column if not exists cost_price numeric(12,2);
alter table custom_items add column if not exists lead_days int;
alter table custom_items add column if not exists dimension text;
create table if not exists project_commercial(project_id uuid primary key references projects(id) on delete cascade, data jsonb not null, updated_at timestamptz not null default now());
`;
