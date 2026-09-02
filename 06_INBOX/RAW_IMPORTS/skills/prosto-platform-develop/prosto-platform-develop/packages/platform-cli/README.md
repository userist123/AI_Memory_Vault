# @prosto/platform-cli

Command-line package for platform operational utilities.

## Local Authentication Bootstrap

`prosto-platform auth bootstrap-local [--database <path>]` initializes an empty
SQLite local-auth database. It requires an interactive TTY and writes a
one-time password only to that terminal. The default database path is
`.prosto/local-auth.sqlite` relative to the current working directory.

Run the repository wrapper with an explicit path for the admin BFF example:

```bash
npm run auth:bootstrap-local -- --database examples/admin-bff-http-host/.prosto/local-auth.sqlite
```
