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
create table if not exists revisions(id uuid primary key default gen_random_uuid(), project_id uuid not null references projects(id) on delete cascade,
  number int not null, note text not null default '', created_at timestamptz not null default now(), snapshot jsonb not null, unique(project_id, number));
create table if not exists materials(id text primary key, category text not null, name text not null, supplier text not null, unit text not null, unit_price numeric(12,2) not null,
  pack jsonb, coverage numeric(8,2), consumption numeric(8,2), source_url text, verified_at date, verification_type text not null, confidence text not null, note text);
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
`;
