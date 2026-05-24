-- ============================================================
-- seed_orders.sql
-- Creates and seeds dbo.Orders with 1,000 realistic mock rows.
-- Target database: JackyAI (jackyai.database.windows.net)
-- ============================================================

USE [JackyAI];
GO

-- ── 1. Create table ──────────────────────────────────────────
IF OBJECT_ID('dbo.Orders', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Orders (
        OrderId         UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
        CustomerId      NVARCHAR(20)     NOT NULL,
        Region          NVARCHAR(20)     NOT NULL,
        Country         NVARCHAR(50)     NOT NULL,
        ProductCategory NVARCHAR(50)     NOT NULL,
        ProductName     NVARCHAR(100)    NOT NULL,
        CustomerSegment NVARCHAR(30)     NOT NULL,
        SalesChannel    NVARCHAR(20)     NOT NULL,
        SalesRepId      NVARCHAR(10)     NOT NULL,
        OrderStatus     NVARCHAR(20)     NOT NULL,
        PaymentMethod   NVARCHAR(30)     NOT NULL,
        IsRenewal       BIT              NOT NULL,
        Quantity        INT              NOT NULL,
        UnitPrice       DECIMAL(12,2)    NOT NULL,
        DiscountPct     DECIMAL(5,4)     NOT NULL,  -- e.g. 0.1500 = 15%
        Revenue         DECIMAL(14,2)    NOT NULL,  -- Qty * UnitPrice * (1 - Disc)
        OrderDate       DATE             NOT NULL,
        FiscalYear      INT              NOT NULL,
        FiscalQuarter   NVARCHAR(2)      NOT NULL   -- 'Q1' … 'Q4'
    );
    PRINT 'Table dbo.Orders created.';
END
ELSE
BEGIN
    PRINT 'Table dbo.Orders already exists – skipping CREATE.';
END
GO

-- ── 2. Seed 1,000 rows ───────────────────────────────────────
-- Each NEWID() in a SELECT INTO is evaluated per-row, so the
-- random seeds are genuinely independent for every row.

IF EXISTS (SELECT 1 FROM dbo.Orders)
BEGIN
    PRINT 'dbo.Orders already has rows – skipping seed.';
    RETURN;
END

-- Step A: materialise per-row random seeds into a temp table.
SELECT TOP 1000
    ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS rn,
    ABS(CHECKSUM(NEWID()))  AS r1,
    ABS(CHECKSUM(NEWID()))  AS r2,
    ABS(CHECKSUM(NEWID()))  AS r3,
    ABS(CHECKSUM(NEWID()))  AS r4,
    ABS(CHECKSUM(NEWID()))  AS r5,
    ABS(CHECKSUM(NEWID()))  AS r6,
    ABS(CHECKSUM(NEWID()))  AS r7,
    ABS(CHECKSUM(NEWID()))  AS r8,
    ABS(CHECKSUM(NEWID()))  AS r9,
    ABS(CHECKSUM(NEWID()))  AS r10
INTO #Rnd
FROM sys.all_columns AS a
CROSS JOIN sys.all_columns AS b;

-- Step B: derive business columns from seeds, then compute Revenue.
WITH base AS (
    SELECT
        rn, r1, r2, r3, r4, r5, r6, r7, r8, r9, r10,

        -- Region (4 options)
        CASE r1 % 4
            WHEN 0 THEN 'APAC'
            WHEN 1 THEN 'EMEA'
            WHEN 2 THEN 'Americas'
            ELSE        'NAMER'
        END AS region,

        -- Country (correlated with region)
        CASE r1 % 4
            WHEN 0 THEN -- APAC
                CASE r2 % 5
                    WHEN 0 THEN 'Japan'
                    WHEN 1 THEN 'Australia'
                    WHEN 2 THEN 'Singapore'
                    WHEN 3 THEN 'India'
                    ELSE        'South Korea'
                END
            WHEN 1 THEN -- EMEA
                CASE r2 % 5
                    WHEN 0 THEN 'Germany'
                    WHEN 1 THEN 'United Kingdom'
                    WHEN 2 THEN 'France'
                    WHEN 3 THEN 'Netherlands'
                    ELSE        'Sweden'
                END
            WHEN 2 THEN -- Americas
                CASE r2 % 4
                    WHEN 0 THEN 'Brazil'
                    WHEN 1 THEN 'Mexico'
                    WHEN 2 THEN 'Canada'
                    ELSE        'Argentina'
                END
            ELSE -- NAMER
                CASE r2 % 3
                    WHEN 0 THEN 'United States'
                    WHEN 1 THEN 'United States'
                    ELSE        'Canada'
                END
        END AS country,

        -- ProductCategory (5 options)
        CASE r3 % 5
            WHEN 0 THEN 'Cybersecurity'
            WHEN 1 THEN 'Cloud Security'
            WHEN 2 THEN 'Endpoint Protection'
            WHEN 3 THEN 'Network Security'
            ELSE        'Consulting'
        END AS product_category,

        -- ProductName (correlated with category)
        CASE r3 % 5
            WHEN 0 THEN  -- Cybersecurity
                CASE r4 % 4
                    WHEN 0 THEN 'Apex One'
                    WHEN 1 THEN 'Deep Security'
                    WHEN 2 THEN 'Vision One XDR'
                    ELSE        'TXOne OT Security'
                END
            WHEN 1 THEN  -- Cloud Security
                CASE r4 % 3
                    WHEN 0 THEN 'Cloud One'
                    WHEN 1 THEN 'Container Security'
                    ELSE        'Cloud Conformity'
                END
            WHEN 2 THEN  -- Endpoint Protection
                CASE r4 % 3
                    WHEN 0 THEN 'Apex One SaaS'
                    WHEN 1 THEN 'Worry-Free Services'
                    ELSE        'Endpoint Sensor'
                END
            WHEN 3 THEN  -- Network Security
                CASE r4 % 3
                    WHEN 0 THEN 'TippingPoint IPS'
                    WHEN 1 THEN 'Network Sensor'
                    ELSE        'Zero Trust NAC'
                END
            ELSE         -- Consulting
                CASE r4 % 3
                    WHEN 0 THEN 'Implementation Service'
                    WHEN 1 THEN 'Training Package'
                    ELSE        'Managed Detection & Response'
                END
        END AS product_name,

        -- CustomerSegment (4 options)
        CASE r5 % 4
            WHEN 0 THEN 'Enterprise'
            WHEN 1 THEN 'SMB'
            WHEN 2 THEN 'Government'
            ELSE        'Education'
        END AS customer_segment,

        -- SalesChannel (4 options)
        CASE r6 % 4
            WHEN 0 THEN 'Direct'
            WHEN 1 THEN 'Partner'
            WHEN 2 THEN 'Online'
            ELSE        'Distributor'
        END AS sales_channel,

        -- SalesRepId (50 reps)
        'REP-' + RIGHT('000' + CAST((r7 % 50) + 1 AS NVARCHAR), 3) AS sales_rep_id,

        -- OrderStatus (Closed ~70%, rest spread)
        CASE r8 % 10
            WHEN 0 THEN 'Open'
            WHEN 1 THEN 'Pending'
            WHEN 2 THEN 'Cancelled'
            ELSE        'Closed'
        END AS order_status,

        -- PaymentMethod (4 options)
        CASE r9 % 4
            WHEN 0 THEN 'Credit Card'
            WHEN 1 THEN 'Purchase Order'
            WHEN 2 THEN 'Wire Transfer'
            ELSE        'ACH'
        END AS payment_method,

        -- IsRenewal (~33%)
        CASE WHEN r10 % 3 = 0 THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS is_renewal,

        -- Quantity (1–20)
        (r1 % 20) + 1 AS qty,

        -- UnitPrice (varies by category)
        CAST(
            CASE r3 % 5
                WHEN 0 THEN  500 + (r2 % 9500)   -- Cybersecurity: $500–$10000
                WHEN 1 THEN  800 + (r3 % 14200)  -- Cloud Security: $800–$15000
                WHEN 2 THEN  200 + (r4 % 2800)   -- Endpoint:       $200–$3000
                WHEN 3 THEN 1000 + (r5 % 14000)  -- Network:        $1000–$15000
                ELSE        3000 + (r6 % 47000)  -- Consulting:     $3000–$50000
            END
        AS DECIMAL(12,2)) AS unit_price,

        -- DiscountPct (0–25 %, step 1 %)
        CAST((r7 % 26) * 0.01 AS DECIMAL(5,4)) AS disc_pct,

        -- OrderDate (2022-01-01 → 2026-12-31, ~5 years)
        DATEADD(DAY,
            r8 % DATEDIFF(DAY, '2022-01-01', '2027-01-01'),
            '2022-01-01'
        ) AS order_date
    FROM #Rnd
)
INSERT INTO dbo.Orders (
    OrderId, CustomerId,
    Region, Country,
    ProductCategory, ProductName,
    CustomerSegment, SalesChannel,
    SalesRepId, OrderStatus, PaymentMethod,
    IsRenewal,
    Quantity, UnitPrice, DiscountPct, Revenue,
    OrderDate, FiscalYear, FiscalQuarter
)
SELECT
    NEWID(),
    'CUST-' + RIGHT('00000' + CAST(rn AS NVARCHAR), 5),
    region, country,
    product_category, product_name,
    customer_segment, sales_channel,
    sales_rep_id, order_status, payment_method,
    is_renewal,
    qty,
    unit_price,
    disc_pct,
    ROUND(CAST(qty AS DECIMAL(14,2)) * unit_price * (1 - disc_pct), 2),
    order_date,
    YEAR(order_date),
    'Q' + CAST(DATEPART(QUARTER, order_date) AS NVARCHAR)
FROM base;

DROP TABLE #Rnd;

PRINT 'Inserted ' + CAST(@@ROWCOUNT AS NVARCHAR) + ' rows into dbo.Orders.';
GO

-- ── 3. Quick sanity check ─────────────────────────────────────
SELECT
    COUNT(*)                              AS TotalRows,
    COUNT(DISTINCT Region)                AS Regions,
    COUNT(DISTINCT Country)               AS Countries,
    COUNT(DISTINCT ProductCategory)       AS Categories,
    COUNT(DISTINCT ProductName)           AS Products,
    MIN(OrderDate)                        AS EarliestDate,
    MAX(OrderDate)                        AS LatestDate,
    ROUND(SUM(Revenue), 0)                AS TotalRevenue
FROM dbo.Orders;
GO
