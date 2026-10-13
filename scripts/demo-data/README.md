# Demo data generator

`generate_demo_data.py` produces flat, analysis-ready datasets for **Data Analysis** and
**Data Dashboard**. Each dataset is one denormalized table, so no joins are needed: every row already
carries its dimensions, dates and measures. The values are fictional, but they follow realistic
patterns: seasonality with a Q4 peak, about 14% yearly growth, and correlated attributes. Charts and
AI questions therefore give believable answers.

It needs only Python 3.9+ and the standard library.

```bash
cd scripts/demo-data
python generate_demo_data.py                    # every dataset as CSV into ./demo-data-out
python generate_demo_data.py --format both      # CSV and SQL Server scripts
python generate_demo_data.py --help             # all options
```

The same `--seed` and `--end` always produce the same data. `--end` defaults to today, so pass a
fixed date when you need to regenerate identical files later.

## Datasets

| Dataset | Grain | Default rows | Good for |
|---|---|---|---|
| `sales_orders` | one order | 20,000 | Revenue by region/segment/channel, deal-size tiers, margin by category, rep performance |
| `hr_employees` | one employee (leavers included) | 1,500 | Headcount, attrition vs engagement and overtime, salary by level and office |
| `marketing_campaigns` | campaign × day × device | ~19,500 | Funnel (impressions → clicks → leads → conversions), CTR, CPC, ROAS by channel |
| `support_tickets` | one ticket | 15,000 | SLA, CSAT, escalation paths (`EntryTeam` → `ResolvedByTeam` for a sankey), response times |
| `pnl_monthly` | month × business unit × region × account | ~9,800 | Waterfall (`Actual` is signed: income +, expenses −), actual vs budget |
| `inventory_weekly` | SKU × warehouse × week | ~14,900 | Stock levels, stockouts (peaking in December), reorder points, inventory value |
| `saas_subscriptions` | account × month | ~14,000 | MRR/ARR, new/expansion/contraction/churn movements, health score vs churn |

`pnl_monthly` and `inventory_weekly` are sized by the date range: three calendar years by default
(`--years`, counted from January 1). The other datasets take `--scale` or `--rows NAME=N`.

All datasets share the same vocabulary, so filters carry across tables:
- regions `AMER` / `APAC` / `EMEA` / `LATAM`
- country names such as `USA` and `UK`
- quarters as `2024Q2`
- product categories `Software` / `Hardware` / `Support` / `Consulting` / `Training`

### `sales_orders` and dbo.Orders

The first 19 columns of `sales_orders` repeat the live `dbo.Orders` table that the default Data
Analysis source reads:
- the same names, column order and types (integer `OrderId` / `CustomerId` / `SalesRepId`, a
  `DATETIME2` `OrderDate` with time of day);
- the same values (its 10 products, `Completed` / `Pending` / `Cancelled`, `CreditCard` / `Invoice` /
  `Wire`, ...), plus a few new ones: extra products and countries, and the `Government` and
  `Education` segments.

So loading it over `dbo.Orders` keeps the app's example questions and any saved dashboard filter
working. Fifteen columns follow:
- `FiscalMonth`, `CustomerName`, `Industry`, `CompanySize`, `City`
- `SalesTeam`, `SalesRepName`, `LeadSource`
- `IsRefunded`, `ContractTermMonths`, `SalesCycleDays`
- `UnitCost`, `Cost`, `GrossProfit`, `GrossMarginPct`

`scripts/seed_orders.sql` does **not** match the live table: it has different categories, statuses
and IDs. Use this generator instead.

## Loading the data

### 1. Upload a CSV (quickest, temporary)

In Data Analysis, choose **上傳資料** and pick a generated `.csv`. Uploads:
- are kept for 2 hours and never stored in a database;
- are limited to 50,000 rows, which every default size stays under;
- are written as UTF-8 with a BOM, so Excel shows them correctly (`--no-bom` turns that off).

Booleans are written as `true` / `false`, which the upload detects as Boolean. `1` / `0` would be
read as integers.

### 2. Load into SQL Server as new tables

```bash
python generate_demo_data.py --format sql
```

Each `.sql` script creates its own table and inserts the rows in one transaction:
`dbo.DemoSalesOrders`, `dbo.DemoHrEmployees`, `dbo.DemoMarketingCampaigns`, ... Use
`--table NAME=SCHEMA.TABLE` to choose another name. Run the scripts in SSMS (or `sqlcmd -i`).

- **Existing tables are safe.** A script stops with an error if its table already exists. Generate
  it with `--replace` to drop and recreate the table instead.
- **The default source only reads dbo.Orders.** To query the `Demo*` tables, add a **SQL Server**
  source under **管理來源** that points at the database, then type the table name in **資料表**.

### 3. Replace dbo.Orders behind the default source

```bash
python generate_demo_data.py --datasets sales_orders --format sql --table sales_orders=dbo.Orders --replace --end 2026-10-13
```

This drops `dbo.Orders` and recreates it with the richer data. Keep a copy first, in case you want the
old rows back:

```sql
SELECT * INTO dbo.Orders_backup FROM dbo.Orders;
```

## Options

| Option | Default | Meaning |
|---|---|---|
| `--datasets` | `all` | Comma-separated dataset names |
| `--out` | `demo-data-out` | Output folder |
| `--format` | `csv` | `csv`, `sql` or `both` |
| `--scale` | `1` | Multiplies the default row counts |
| `--rows NAME=N` | | Exact row count for one dataset; repeatable |
| `--years` | `3` | Calendar years of history, from January 1 up to `--end` |
| `--end` | today | Last date in the data (`YYYY-MM-DD`) |
| `--seed` | `42` | Random seed |
| `--table NAME=SCHEMA.TABLE` | `dbo.Demo<Name>` | Target table of a SQL script; repeatable |
| `--replace` | off | SQL scripts drop an existing table first |
| `--no-bom` | off | CSV without the UTF-8 BOM |

Each dataset draws from its own random stream. Resizing or adding one dataset never changes the
others.
