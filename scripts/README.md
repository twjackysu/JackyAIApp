# scripts/

**Schema changes go through EF Core migrations, not hand-written SQL.** Use
`dotnet ef migrations add <Name>` from `JackyAIApp.Server/`, and apply with
`dotnet ef database update` (or the generated idempotent script, for prod).

This folder previously accumulated DDL patches (`add_*_table.sql`,
`migrate_*.sql`) that were run by hand against Azure SQL outside the EF
migration history. That caused schema drift between `__EFMigrationsHistory`
and the actual database, which is exactly the kind of bug this rule exists
to prevent — see `archive/` for the old scripts (all since folded into EF
migrations; kept for reference only, do not re-run them).

Data seeding is the exception, since it isn't a schema change:

- `demo-data/` generates demo datasets for Data Analysis / Data Dashboard as
  CSV or SQL Server scripts. Its `sales_orders` dataset matches the live
  `dbo.Orders` and can replace it. See `demo-data/README.md`.
- `seed_orders.sql` is the original `dbo.Orders` seed. It no longer matches
  the live table (different categories, statuses and ID types), so prefer
  `demo-data/`.
