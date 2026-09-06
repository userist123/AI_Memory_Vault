-- Schema bază de date - Registru Transferuri Media
-- Conform HG 585/2002

-- Tabela principală: Transferuri
CREATE TABLE IF NOT EXISTS transfers (
    id TEXT PRIMARY KEY,
    nr TEXT UNIQUE NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    operator TEXT NOT NULL,
    
    -- Sursă
    src_institutie TEXT NOT NULL,
    src_pc_nume TEXT NOT NULL,
    src_medium TEXT NOT NULL,
    src_sn TEXT,
    src_path TEXT,
    
    -- Persoană primitor
    pers_nume TEXT NOT NULL,
    pers_functie TEXT,
    pers_legitimatie TEXT,
    pers_autorizatie TEXT NOT NULL,
    
    -- Mediu de transfer
    transfer_medium TEXT NOT NULL,
    transfer_sn TEXT,
    transfer_label TEXT,
    transfer_cap_gb REAL,
    transfer_free_gb REAL,
    
    -- Destinație
    dst_institutie TEXT NOT NULL,
    dst_pc_nume TEXT,
    dst_medium TEXT,
    dst_sn TEXT,
    dst_path TEXT,
    
    -- Arhivă
    arhiva_nume TEXT,
    arhiva_tip TEXT,
    arhiva_dim_gb REAL,
    arhiva_fisiere INTEGER,
    arhiva_hash TEXT,
    arhiva_descriere TEXT,
    
    -- Conformitate HG 585/2002
    clasificare TEXT NOT NULL,
    restrictii TEXT,
    aprobare_mult TEXT,
    baza_legala TEXT,
    observatii TEXT,
    
    -- Status
    status TEXT NOT NULL DEFAULT 'active',
    deleted_at TEXT,
    deleted_by TEXT
);

-- Index pentru căutări rapide
CREATE INDEX IF NOT EXISTS idx_nr ON transfers(nr);
CREATE INDEX IF NOT EXISTS idx_created_at ON transfers(created_at);
CREATE INDEX IF NOT EXISTS idx_operator ON transfers(operator);
CREATE INDEX IF NOT EXISTS idx_status ON transfers(status);
CREATE INDEX IF NOT EXISTS idx_src_institutie ON transfers(src_institutie);
CREATE INDEX IF NOT EXISTS idx_dst_institutie ON transfers(dst_institutie);
CREATE INDEX IF NOT EXISTS idx_pers_nume ON transfers(pers_nume);
CREATE INDEX IF NOT EXISTS idx_clasificare ON transfers(clasificare);
CREATE INDEX IF NOT EXISTS idx_arhiva_hash ON transfers(arhiva_hash);

-- Audit Log
CREATE TABLE IF NOT EXISTS audit_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp TEXT NOT NULL,
    operator TEXT NOT NULL,
    operation TEXT NOT NULL,
    record_id TEXT NOT NULL,
    nr_registru TEXT,
    field_name TEXT,
    old_value TEXT,
    new_value TEXT
);

CREATE INDEX IF NOT EXISTS idx_audit_timestamp ON audit_log(timestamp);
CREATE INDEX IF NOT EXISTS idx_audit_record_id ON audit_log(record_id);

-- Operatori
CREATE TABLE IF NOT EXISTS operators (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT UNIQUE NOT NULL,
    rank TEXT,
    unit TEXT,
    created_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_operator_name ON operators(name);

-- Cache autocomplete
CREATE TABLE IF NOT EXISTS autocomplete_cache (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    category TEXT NOT NULL,
    value TEXT NOT NULL,
    frequency INTEGER DEFAULT 1,
    last_used TEXT NOT NULL,
    UNIQUE(category, value)
);

CREATE INDEX IF NOT EXISTS idx_autocomplete_category ON autocomplete_cache(category);
CREATE INDEX IF NOT EXISTS idx_autocomplete_freq ON autocomplete_cache(category, frequency DESC);
