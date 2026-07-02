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

`seed_orders.sql` is the one exception: it's data seeding, not a schema
change, so it doesn't belong in EF migrations.
