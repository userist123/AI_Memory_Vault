# TypeORM Dialect Support

`@prosto/platform-adapter-typeorm` certifies the following engines in the named
jobs of `.github/workflows/typeorm-dialect-certification.yml`.

| Dialect | CI image / mode | Application peer dependency | Required test variables |
| --- | --- | --- | --- |
| PostgreSQL | `postgres:16` | `pg` | `POSTGRES_HOST`, `POSTGRES_PORT`, `POSTGRES_DATABASE`, `POSTGRES_USERNAME`, `POSTGRES_PASSWORD` |
| MySQL | `mysql:8.4` | `mysql2` | `MYSQL_HOST`, `MYSQL_PORT`, `MYSQL_DATABASE`, `MYSQL_USERNAME`, `MYSQL_PASSWORD` |
| MariaDB | `mariadb:11.4` | `mysql2` | `MARIADB_HOST`, `MARIADB_PORT`, `MARIADB_DATABASE`, `MARIADB_USERNAME`, `MARIADB_PASSWORD` |
| SQLite | temporary file | `sqlite3` | `PROSTO_TYPEORM_SQLITE_DIRECTORY` |
| SQL Server | `mcr.microsoft.com/mssql/server:2022-latest` | `mssql` | `MSSQL_HOST`, `MSSQL_PORT`, `MSSQL_DATABASE`, `MSSQL_USERNAME`, `MSSQL_PASSWORD` |

Run certification with `npm run test:integration --workspace=@prosto/platform-adapter-typeorm`.
Set `PROSTO_TYPEORM_INTEGRATION=1` and `PROSTO_TYPEORM_DIALECT` to the selected
dialect. A missing required environment variable is a setup failure, not a skip.

The adapter always sets `synchronize: false` and `migrationsRun: false`; schema
changes run only through the shared versioned migration set. PostgreSQL, MySQL,
MariaDB, and SQL Server use database-level migration locks. SQLite uses an
exclusive file lock and permits one startup writer per database file. SQLite
`:memory:` databases are process-local and cannot demonstrate lock contention.

Applications install only the peer driver for their selected dialect. A missing
selected driver raises `PersistenceDriverUnavailable` without exposing connection
details.
