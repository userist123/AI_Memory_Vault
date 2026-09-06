"""
Script Inițializare Bază de Date
Creează/Recrează baza de date transferuri.db cu schema completă
"""
import sqlite3
import os
from pathlib import Path

def init_database():
    """Inițializează baza de date cu schema SQL"""
    
    db_path = "transferuri.db"
    schema_path = "database/schema.sql"
    
    # Verifică dacă schema.sql există
    if not os.path.exists(schema_path):
        print(f"[EROARE] Nu gasesc {schema_path}")
        print("Verifica ca fisierul database/schema.sql existe!")
        return False
    
    # Șterge baza de date veche dacă există și e goală/coruptă
    if os.path.exists(db_path):
        print(f"[INFO] Gasit {db_path} existent")
        try:
            conn = sqlite3.connect(db_path)
            cursor = conn.cursor()
            # Verifică dacă tabelul transferuri există
            cursor.execute("""
                SELECT name FROM sqlite_master 
                WHERE type='table' AND name='transferuri'
            """)
            if cursor.fetchone():
                print("[INFO] Tabelul transferuri deja exista")
                conn.close()
                print("[SUCCESS] Baza de date este deja initializata!")
                return True
            conn.close()
        except Exception as e:
            print(f"[ATENTIE] Eroare la verificare DB: {e}")
        
        # Backup și ștergere
        backup_path = f"{db_path}.backup"
        if os.path.exists(backup_path):
            os.remove(backup_path)
        os.rename(db_path, backup_path)
        print(f"[INFO] Backup vechi: {backup_path}")
    
    # Citește schema SQL
    print(f"[INFO] Citire {schema_path}...")
    with open(schema_path, 'r', encoding='utf-8') as f:
        schema_sql = f.read()
    
    # Creează baza de date nouă
    print(f"[INFO] Creare {db_path}...")
    conn = sqlite3.connect(db_path)
    cursor = conn.cursor()
    
    # Execută schema SQL
    print("[INFO] Executare schema SQL...")
    try:
        # Split pe statement-uri (separat prin ;)
        statements = [s.strip() for s in schema_sql.split(';') if s.strip()]
        for i, statement in enumerate(statements, 1):
            cursor.execute(statement)
            print(f"  [{i}/{len(statements)}] Statement executat")
        
        conn.commit()
        print("[SUCCESS] Schema SQL executata cu succes!")
        
        # Verificare finală
        cursor.execute("""
            SELECT name FROM sqlite_master 
            WHERE type='table'
            ORDER BY name
        """)
        tables = [row[0] for row in cursor.fetchall()]
        print(f"\n[INFO] Tabele create ({len(tables)}):")
        for table in tables:
            cursor.execute(f"SELECT COUNT(*) FROM {table}")
            count = cursor.fetchone()[0]
            print(f"  - {table} ({count} randuri)")
        
        conn.close()
        print(f"\n[SUCCESS] Baza de date {db_path} initializata complet!")
        return True
        
    except Exception as e:
        print(f"\n[EROARE] Esec la executare schema: {e}")
        conn.rollback()
        conn.close()
        return False

if __name__ == "__main__":
    print("=" * 60)
    print("  INITIALIZARE BAZA DE DATE - Registru Transferuri")
    print("=" * 60)
    print()
    
    success = init_database()
    
    print()
    print("=" * 60)
    if success:
        print("  GATA! Acum ruleaza: python main.py")
    else:
        print("  EROARE! Verifica mesajele de mai sus.")
    print("=" * 60)
    
    input("\nApasa ENTER pentru a inchide...")
