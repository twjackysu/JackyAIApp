#!/usr/bin/env python3
"""
Generate flat, analysis-ready demo datasets for Data Analysis / Data Dashboard.

Every dataset is one denormalized table (no joins needed): each row already carries its
dimensions (region, segment, product, team, ...), its dates, and its measures. Values are
fictional but follow realistic patterns (seasonality, year-on-year growth, correlated
attributes), so charts and AI questions return believable answers.

Standard library only. Examples:

    # every dataset as CSV (Excel-friendly UTF-8 with BOM) into ./demo-data-out
    python generate_demo_data.py

    # two datasets, CSV and SQL Server scripts, twice the default size
    python generate_demo_data.py --datasets hr_employees,support_tickets --format both --scale 2

    # SQL script that replaces dbo.Orders with the richer sales dataset
    python generate_demo_data.py --datasets sales_orders --format sql --table sales_orders=dbo.Orders --replace

Same --seed and --end always produce the same data. See README.md for the datasets.
"""

from __future__ import annotations

import argparse
import csv
import json
import datetime as dt
import math
import random
import zlib
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Iterable

# ─────────────────────────────────────────────────────────────────────────────
# Shared building blocks
# ─────────────────────────────────────────────────────────────────────────────


@dataclass(frozen=True)
class Column:
    name: str
    sql_type: str  # SQL Server type used in the generated CREATE TABLE


@dataclass(frozen=True)
class Dataset:
    name: str
    description: str
    columns: list[Column]
    default_rows: int
    generate: Callable[["Context", int], Iterable[dict]]


@dataclass
class Context:
    seed: int
    start: dt.date
    end: dt.date

    def rng(self, dataset: str) -> random.Random:
        # Each dataset gets its own stream, so adding or resizing one never changes another.
        return random.Random(self.seed * 1_000_003 + zlib.crc32(dataset.encode()))


# Busier towards year end (Q4 budget flush), quieter in Q1 and summer.
MONTH_SEASONALITY = [0.82, 0.80, 0.98, 0.95, 0.97, 1.05, 0.90, 0.88, 1.00, 1.06, 1.16, 1.40]
ANNUAL_GROWTH = 0.14


def pick(rng: random.Random, options: dict) -> str:
    """Weighted choice from {value: weight}."""
    return rng.choices(list(options), weights=list(options.values()))[0]


def clamp(value: float, low: float, high: float) -> float:
    return max(low, min(high, value))


def money(value: float) -> float:
    return round(value, 2)


def fiscal(d: dt.date) -> tuple[int, str, int]:
    # "2024Q2", the format the live dbo.Orders uses: unique across years and sorts by time.
    return d.year, f"{d.year}Q{(d.month - 1) // 3 + 1}", d.month


def month_starts(start: dt.date, end: dt.date) -> list[dt.date]:
    months, d = [], start.replace(day=1)
    while d <= end:
        months.append(d)
        d = (d.replace(day=28) + dt.timedelta(days=4)).replace(day=1)
    return months


def demand_weight(d: dt.date, start: dt.date) -> float:
    """Seasonality times compound growth since the start of the range."""
    years = (d - start).days / 365.25
    return MONTH_SEASONALITY[d.month - 1] * (1 + ANNUAL_GROWTH) ** years


def weighted_days(ctx: Context, rng: random.Random, k: int, weekday_only: bool = False) -> list[dt.date]:
    days, weights = [], []
    d = ctx.start
    while d <= ctx.end:
        if not (weekday_only and d.weekday() >= 5):
            days.append(d)
            weights.append(demand_weight(d, ctx.start) * (0.45 if d.weekday() >= 5 else 1.0))
        d += dt.timedelta(days=1)
    return rng.choices(days, weights=weights, k=k)


FIRST_NAMES = [
    "Alex", "Amy", "Andy", "Angela", "Ben", "Brian", "Cathy", "Chris", "Daniel", "David",
    "Emily", "Eric", "Grace", "Hannah", "Henry", "Ivy", "Jack", "Jason", "Jenny", "Jessica",
    "Kevin", "Linda", "Lucas", "Mandy", "Mark", "Michelle", "Nina", "Oscar", "Peggy", "Peter",
    "Rachel", "Ryan", "Sam", "Sandy", "Sophia", "Steven", "Tina", "Tom", "Vicky", "Wendy",
    "Yuki", "Kenji", "Haruka", "Min-jun", "Ji-woo", "Priya", "Arjun", "Lukas", "Emma", "Liam",
]
LAST_NAMES = [
    "Chen", "Lin", "Huang", "Chang", "Lee", "Wang", "Wu", "Liu", "Tsai", "Yang",
    "Hsu", "Cheng", "Hsieh", "Kuo", "Hung", "Tanaka", "Sato", "Suzuki", "Kim", "Park",
    "Sharma", "Patel", "Smith", "Johnson", "Brown", "Miller", "Garcia", "Martin", "Muller", "Schmidt",
]
COMPANY_PREFIXES = [
    "Apex", "Blue", "Bright", "Cedar", "Crystal", "Delta", "Eagle", "Evergreen", "Falcon", "Golden",
    "Granite", "Harbor", "Horizon", "Jade", "Lotus", "Maple", "Meridian", "Nova", "Orion", "Pacific",
    "Phoenix", "Pine", "Prime", "Quantum", "River", "Sakura", "Silver", "Summit", "Titan", "Vertex",
]
COMPANY_SUFFIXES = [
    "Manufacturing", "Logistics", "Bank", "Insurance", "Health", "Retail", "Foods", "Energy",
    "Telecom", "Electronics", "Motors", "Pharma", "Media", "Holdings", "Systems", "Solutions",
]


def person(rng: random.Random) -> str:
    return f"{rng.choice(FIRST_NAMES)} {rng.choice(LAST_NAMES)}"


# Region → country → (weight within the region, cities). Region and country names are a superset of
# the live dbo.Orders values (AMER/APAC/EMEA/LATAM; "USA", "UK"), so every dataset filters the same way.
GEOGRAPHY = {
    "AMER": {
        "USA": (80, ["New York", "San Francisco", "Chicago", "Dallas", "Seattle", "Boston"]),
        "Canada": (20, ["Toronto", "Vancouver", "Montreal"]),
    },
    "APAC": {
        "Japan": (24, ["Tokyo", "Osaka", "Nagoya"]),
        "Australia": (20, ["Sydney", "Melbourne", "Brisbane"]),
        "Taiwan": (20, ["Taipei", "Hsinchu", "Taichung"]),
        "Singapore": (12, ["Singapore"]),
        "South Korea": (12, ["Seoul", "Busan"]),
        "India": (12, ["Bengaluru", "Mumbai", "New Delhi"]),
    },
    "EMEA": {
        "Germany": (30, ["Berlin", "Munich", "Frankfurt"]),
        "UK": (30, ["London", "Manchester", "Edinburgh"]),
        "France": (22, ["Paris", "Lyon"]),
        "Netherlands": (18, ["Amsterdam", "Rotterdam"]),
    },
    "LATAM": {
        "Brazil": (45, ["Sao Paulo", "Rio de Janeiro"]),
        "Mexico": (35, ["Mexico City", "Monterrey"]),
        "Argentina": (20, ["Buenos Aires", "Cordoba"]),
    },
}
REGION_WEIGHTS = {"AMER": 34, "APAC": 30, "EMEA": 24, "LATAM": 12}


def place(rng: random.Random, region: str | None = None) -> tuple[str, str, str]:
    region = region or pick(rng, REGION_WEIGHTS)
    country = pick(rng, {name: weight for name, (weight, _) in GEOGRAPHY[region].items()})
    city = rng.choice(GEOGRAPHY[region][country][1])
    return region, country, city


# Consumer, Enterprise and SMB are the live dbo.Orders segments.
SEGMENTS = {"Enterprise": 30, "SMB": 36, "Consumer": 16, "Government": 11, "Education": 7}
INDUSTRIES = {
    "Manufacturing": 18, "Financial Services": 16, "Healthcare": 12, "Retail": 12,
    "Technology": 14, "Telecommunications": 10, "Energy": 8, "Logistics": 10,
}
COMPANY_SIZE_BY_SEGMENT = {
    "Enterprise": {"1000-4999": 45, "5000+": 55},
    "SMB": {"1-49": 35, "50-249": 45, "250-999": 20},
    "Government": {"250-999": 40, "1000-4999": 40, "5000+": 20},
    "Education": {"50-249": 30, "250-999": 45, "1000-4999": 25},
    "Consumer": {"Individual": 100},
}


@dataclass
class Customer:
    number: int
    id: str
    name: str
    segment: str
    industry: str
    size: str
    region: str
    country: str
    city: str
    activity: float  # relative ordering frequency (Pareto: a few big customers dominate)


def make_customers(rng: random.Random, count: int, id_prefix: str) -> list[Customer]:
    customers = []
    for i in range(count):
        segment = pick(rng, SEGMENTS)
        industry = ("Public Sector" if segment == "Government"
                    else "Education" if segment == "Education"
                    else "Consumer" if segment == "Consumer"
                    else pick(rng, INDUSTRIES))
        region, country, city = place(rng)
        name = person(rng) if segment == "Consumer" \
            else f"{rng.choice(COMPANY_PREFIXES)} {rng.choice(COMPANY_SUFFIXES)} {country}"
        customers.append(Customer(
            number=i + 1, id=f"{id_prefix}-{i + 1:05d}", name=name, segment=segment,
            industry=industry, size=pick(rng, COMPANY_SIZE_BY_SEGMENT[segment]),
            region=region, country=country, city=city,
            activity=rng.paretovariate(1.6) * (2.5 if segment == "Enterprise" else 1.0),
        ))
    return customers


# ─────────────────────────────────────────────────────────────────────────────
# 1. Sales orders — the live dbo.Orders columns, plus more
# ─────────────────────────────────────────────────────────────────────────────

# The first 19 columns repeat the live dbo.Orders table: same names, order, types and values (its 5
# categories and 10 products, Completed/Pending/Cancelled, CreditCard/Invoice/Wire, ...), with a few
# values added. Loading this over dbo.Orders keeps the app's example questions and any saved
# dashboard filter working; the extra columns follow.

# category → [(product, list price, unit cost as a share of price)]
PRODUCTS = {
    "Software": [("Cloud Suite", 690, 0.14), ("Security Pro", 540, 0.12), ("Analytics Hub", 820, 0.16)],
    "Hardware": [("Server X1", 960, 0.62), ("NAS Storage", 720, 0.58), ("Edge Router", 380, 0.55)],
    "Support": [("Gold Support", 320, 0.34), ("Platinum Support", 780, 0.30), ("Basic Support", 140, 0.40)],
    "Consulting": [("Impl Service", 900, 0.60), ("Migration Pack", 650, 0.52), ("Architecture Review", 980, 0.55)],
    "Training": [("Online Course", 160, 0.25), ("On-site Training", 850, 0.48), ("Certification Exam", 240, 0.30)],
}
CATEGORY_WEIGHTS = {"Software": 30, "Hardware": 20, "Support": 20, "Consulting": 15, "Training": 15}
CATEGORY_BY_SEGMENT = {
    "Enterprise": {"Software": 28, "Hardware": 22, "Support": 20, "Consulting": 18, "Training": 12},
    "SMB": {"Software": 34, "Hardware": 20, "Support": 20, "Consulting": 10, "Training": 16},
    "Consumer": {"Software": 55, "Hardware": 15, "Support": 10, "Consulting": 0, "Training": 20},
    "Government": {"Software": 25, "Hardware": 25, "Support": 20, "Consulting": 20, "Training": 10},
    "Education": {"Software": 30, "Hardware": 15, "Support": 15, "Consulting": 10, "Training": 30},
}
CHANNEL_BY_SEGMENT = {
    "Enterprise": {"Direct": 55, "Partner": 35, "Online": 10},
    "SMB": {"Direct": 15, "Partner": 40, "Online": 45},
    "Consumer": {"Direct": 0, "Partner": 10, "Online": 90},
    "Government": {"Direct": 40, "Partner": 60, "Online": 0},
    "Education": {"Direct": 25, "Partner": 50, "Online": 25},
}
CHANNEL_DISCOUNT = {"Direct": 0.06, "Partner": 0.12, "Online": 0.02}
BASE_QUANTITY = {"Enterprise": 8, "Government": 6, "Education": 5, "SMB": 3, "Consumer": 1}
SALES_CYCLE_DAYS = {"Enterprise": 75, "Government": 110, "Education": 55, "SMB": 21, "Consumer": 2}
LEAD_SOURCES = {"Website": 22, "Referral": 14, "Partner": 18, "Event": 12,
                "Outbound": 16, "Marketing Campaign": 18}
SUBSCRIPTION_CATEGORIES = ("Software", "Support")
# region → number of sales reps; SalesRepId runs 1..20 like the live table's 1..10.
REPS_PER_REGION = {"AMER": 6, "APAC": 6, "EMEA": 5, "LATAM": 3}


def sales_orders(ctx: Context, rows: int) -> Iterable[dict]:
    rng = ctx.rng("sales_orders")
    customers = make_customers(rng, max(200, rows // 10), "CUST")
    reps_by_region: dict[str, list[tuple[int, str]]] = {}
    for region, count in REPS_PER_REGION.items():
        for _ in range(count):
            rep_id = sum(len(r) for r in reps_by_region.values()) + 1
            reps_by_region.setdefault(region, []).append((rep_id, person(rng)))

    dates = sorted(weighted_days(ctx, rng, rows))
    picked = rng.choices(customers, weights=[c.activity for c in customers], k=rows)
    for n, (order_date, c) in enumerate(zip(dates, picked), start=1):
        category = pick(rng, CATEGORY_BY_SEGMENT[c.segment])
        product, list_price, cost_share = rng.choice(PRODUCTS[category])
        channel = pick(rng, CHANNEL_BY_SEGMENT[c.segment])
        rep_id, rep_name = rng.choice(reps_by_region[c.region])
        # Consumers shop in the evening, businesses in office hours.
        hour = int(clamp(rng.gauss(20 if c.segment == "Consumer" else 13, 3), 0, 23))
        ordered_at = dt.datetime.combine(order_date, dt.time(hour, rng.randint(0, 59), rng.randint(0, 59)))

        if c.segment == "Consumer":
            quantity = 2 if rng.random() < 0.12 else 1
        else:
            quantity = int(clamp(rng.lognormvariate(math.log(BASE_QUANTITY[c.segment]), 0.6), 1, 40))
        unit_price = money(list_price * rng.uniform(0.94, 1.06))
        discount = CHANNEL_DISCOUNT[channel] + min(0.10, quantity / 200) + rng.uniform(-0.03, 0.04)
        if c.segment in ("Government", "Education"):
            discount += 0.04
        discount = round(clamp(discount, 0, 0.35), 4)
        revenue = money(quantity * unit_price * (1 - discount))
        unit_cost = money(unit_price * cost_share * rng.uniform(0.9, 1.1))
        cost = money(quantity * unit_cost)

        if (ctx.end - order_date).days < 21:
            status = pick(rng, {"Pending": 55, "Completed": 40, "Cancelled": 5})
        else:
            status = pick(rng, {"Completed": 88, "Cancelled": 8, "Pending": 4})
        if c.segment == "Consumer":
            payment = pick(rng, {"CreditCard": 92, "Wire": 8})
        elif c.segment == "Government":
            payment = pick(rng, {"Invoice": 80, "Wire": 20})
        else:
            payment = pick(rng, {"CreditCard": 25, "Invoice": 50, "Wire": 25})
        subscription = category in SUBSCRIPTION_CATEGORIES
        renewal_p = (0.5 if subscription else 0.08) * (1.2 if c.segment == "Enterprise" else 1.0)
        team = ("Enterprise" if c.segment in ("Enterprise", "Government")
                else "Digital" if c.segment == "Consumer" else "Commercial")
        year, quarter, month = fiscal(order_date)
        yield {
            "OrderId": n,
            "CustomerId": c.number,
            "Region": c.region,
            "Country": c.country,
            "ProductCategory": category,
            "ProductName": product,
            "CustomerSegment": c.segment,
            "SalesChannel": channel,
            "SalesRepId": rep_id,
            "OrderStatus": status,
            "PaymentMethod": payment,
            "IsRenewal": rng.random() < renewal_p,
            "Quantity": quantity,
            "UnitPrice": unit_price,
            "DiscountPct": discount,
            "Revenue": revenue,
            "OrderDate": ordered_at,
            "FiscalYear": year,
            "FiscalQuarter": quarter,
            "FiscalMonth": month,
            "CustomerName": c.name,
            "Industry": c.industry,
            "CompanySize": c.size,
            "City": c.city,
            "SalesTeam": f"{c.region} {team}",
            "SalesRepName": rep_name,
            "LeadSource": pick(rng, LEAD_SOURCES),
            "IsRefunded": status == "Completed" and rng.random() < 0.02,
            "ContractTermMonths": (12 if c.segment == "Consumer" else pick(rng, {12: 60, 24: 25, 36: 15}))
                                  if subscription else 0,
            "SalesCycleDays": int(rng.lognormvariate(math.log(SALES_CYCLE_DAYS[c.segment]), 0.45)),
            "UnitCost": unit_cost,
            "Cost": cost,
            "GrossProfit": money(revenue - cost),
            "GrossMarginPct": round((revenue - cost) / revenue, 4) if revenue else 0.0,
        }


SALES_ORDERS = Dataset(
    "sales_orders",
    "銷售訂單（一列一張訂單）：前 19 欄與正式環境 dbo.Orders 相同（欄名、順序、型別、值域），另加客戶、產業、城市、業務、成本與毛利",
    [
        # The live dbo.Orders columns, in its order.
        Column("OrderId", "INT NOT NULL PRIMARY KEY"), Column("CustomerId", "INT"),
        Column("Region", "NVARCHAR(20)"), Column("Country", "NVARCHAR(50)"),
        Column("ProductCategory", "NVARCHAR(50)"), Column("ProductName", "NVARCHAR(100)"),
        Column("CustomerSegment", "NVARCHAR(30)"), Column("SalesChannel", "NVARCHAR(20)"),
        Column("SalesRepId", "INT"), Column("OrderStatus", "NVARCHAR(20)"), Column("PaymentMethod", "NVARCHAR(30)"),
        Column("IsRenewal", "BIT"), Column("Quantity", "INT"), Column("UnitPrice", "DECIMAL(12,2)"),
        Column("DiscountPct", "DECIMAL(5,4)"), Column("Revenue", "DECIMAL(14,2)"), Column("OrderDate", "DATETIME2(0)"),
        Column("FiscalYear", "INT"), Column("FiscalQuarter", "NVARCHAR(6)"),
        # Added.
        Column("FiscalMonth", "INT"), Column("CustomerName", "NVARCHAR(100)"), Column("Industry", "NVARCHAR(40)"),
        Column("CompanySize", "NVARCHAR(20)"), Column("City", "NVARCHAR(50)"), Column("SalesTeam", "NVARCHAR(40)"),
        Column("SalesRepName", "NVARCHAR(60)"), Column("LeadSource", "NVARCHAR(30)"), Column("IsRefunded", "BIT"),
        Column("ContractTermMonths", "INT"), Column("SalesCycleDays", "INT"), Column("UnitCost", "DECIMAL(12,2)"),
        Column("Cost", "DECIMAL(14,2)"), Column("GrossProfit", "DECIMAL(14,2)"), Column("GrossMarginPct", "DECIMAL(5,4)"),
    ],
    20_000,
    sales_orders,
)

# ─────────────────────────────────────────────────────────────────────────────
# 2. HR employees — one row per person who worked here in the period
# ─────────────────────────────────────────────────────────────────────────────

DEPARTMENTS = {
    "Engineering": (["Platform", "Product Development", "QA", "DevOps", "Data"], 32),
    "Sales": (["Enterprise Sales", "Commercial Sales", "Sales Operations"], 18),
    "Customer Support": (["Tier 1 Support", "Tier 2 Support", "Customer Success"], 14),
    "Marketing": (["Brand", "Demand Generation", "Product Marketing"], 8),
    "Finance": (["Accounting", "FP&A", "Procurement"], 6),
    "Human Resources": (["Talent Acquisition", "People Operations"], 5),
    "Operations": (["IT", "Facilities", "Supply Chain"], 9),
    "Research": (["Threat Research", "AI Lab"], 8),
}
OFFICES = {  # office → (country, region, salary cost index)
    "Taipei": ("Taiwan", "APAC", 0.62), "Kaohsiung": ("Taiwan", "APAC", 0.55),
    "Tokyo": ("Japan", "APAC", 0.85), "Singapore": ("Singapore", "APAC", 1.0),
    "Bengaluru": ("India", "APAC", 0.40), "London": ("UK", "EMEA", 1.05),
    "Munich": ("Germany", "EMEA", 1.0), "Dallas": ("USA", "AMER", 1.15),
    "San Francisco": ("USA", "AMER", 1.45), "Toronto": ("Canada", "AMER", 0.95),
    "Sao Paulo": ("Brazil", "LATAM", 0.5),
}
OFFICE_WEIGHTS = {"Taipei": 34, "Kaohsiung": 8, "Tokyo": 9, "Singapore": 6, "Bengaluru": 12,
                  "London": 6, "Munich": 5, "Dallas": 8, "San Francisco": 7, "Toronto": 5, "Sao Paulo": 4}
LEVELS = {  # level → (title prefix, base salary USD at cost index 1.0, share of headcount)
    "L1": ("Associate", 52_000, 18), "L2": ("", 68_000, 26), "L3": ("Senior", 88_000, 24),
    "L4": ("Lead", 112_000, 14), "L5": ("Manager", 132_000, 10), "L6": ("Senior Manager", 165_000, 5),
    "L7": ("Director", 210_000, 3),
}
DEPT_PAY = {"Engineering": 1.12, "Research": 1.15, "Sales": 1.05, "Customer Support": 0.82,
            "Marketing": 0.95, "Finance": 0.98, "Human Resources": 0.9, "Operations": 0.88}
ROLE_BY_DEPT = {"Engineering": "Software Engineer", "Research": "Researcher", "Sales": "Account Executive",
                "Customer Support": "Support Engineer", "Marketing": "Marketing Specialist",
                "Finance": "Financial Analyst", "Human Resources": "HR Partner", "Operations": "Operations Specialist"}
ATTRITION_REASONS = {"Voluntary": {"Better Compensation": 30, "Career Growth": 25, "Relocation": 10,
                                   "Work-Life Balance": 18, "Manager Relationship": 10, "Further Study": 7},
                     "Involuntary": {"Performance": 55, "Restructuring": 35, "Policy Violation": 10}}


def hr_employees(ctx: Context, rows: int) -> Iterable[dict]:
    rng = ctx.rng("hr_employees")
    company_founded = ctx.start - dt.timedelta(days=365 * 9)
    for n in range(1, rows + 1):
        dept = pick(rng, {d: w for d, (_, w) in DEPARTMENTS.items()})
        team = rng.choice(DEPARTMENTS[dept][0])
        office = pick(rng, OFFICE_WEIGHTS)
        country, region, cost_index = OFFICES[office]
        level = pick(rng, {lv: share for lv, (_, _, share) in LEVELS.items()})
        title_prefix, base, _ = LEVELS[level]
        is_manager = level in ("L5", "L6", "L7")
        role = ROLE_BY_DEPT[dept]
        title = f"{title_prefix} {dept} Manager".strip() if is_manager else f"{title_prefix} {role}".strip()
        hire = company_founded + dt.timedelta(days=int(((ctx.end - company_founded).days) * rng.random() ** 0.7))
        age = int(clamp(22 + int(level[1]) * 3.2 + rng.gauss(4, 5), 21, 64))

        performance = int(clamp(round(rng.gauss(3.3, 0.85)), 1, 5))
        overtime = round(clamp(rng.gauss(14 if dept in ("Engineering", "Customer Support") else 8, 7), 0, 60), 1)
        engagement = round(clamp(rng.gauss(72, 12) + (performance - 3) * 5 - overtime * 0.35, 15, 99), 1)
        mode = pick(rng, {"Hybrid": 55, "Onsite": 30, "Remote": 15})

        # Attrition risk rises with low engagement, low performance and heavy overtime.
        risk = 0.10 + (75 - engagement) * 0.006 + (3 - performance) * 0.04 + overtime * 0.003
        termination, attrition_type, reason = None, None, None
        if rng.random() < clamp(risk, 0.02, 0.6) and hire < ctx.end - dt.timedelta(days=60):
            leave = hire + dt.timedelta(days=rng.randint(60, max(61, (ctx.end - hire).days)))
            if leave >= ctx.start:
                termination = leave
                attrition_type = "Involuntary" if performance <= 2 and rng.random() < 0.6 else "Voluntary"
                reason = pick(rng, ATTRITION_REASONS[attrition_type])
        if termination is None and hire > ctx.end:
            continue
        tenure_end = termination or ctx.end
        salary = round(base * DEPT_PAY[dept] * cost_index * rng.uniform(0.9, 1.12) * (1 + (performance - 3) * 0.03), -2)
        bonus_pct = round(clamp({"Sales": 0.22}.get(dept, 0.08) + (performance - 3) * 0.03 + rng.uniform(-0.02, 0.02), 0, 0.4), 4)
        yield {
            "EmployeeId": f"EMP-{n:05d}",
            "EmployeeName": person(rng),
            "Gender": pick(rng, {"Female": 44, "Male": 54, "Non-binary": 2}),
            "Age": age,
            "Department": dept,
            "Team": team,
            "JobTitle": title,
            "JobLevel": level,
            "IsManager": is_manager,
            "Office": office,
            "Country": country,
            "Region": region,
            "EmploymentType": pick(rng, {"Full-time": 88, "Contract": 9, "Intern": 3}),
            "WorkMode": mode,
            "HireDate": hire,
            "HireYear": hire.year,
            "TerminationDate": termination,
            "IsActive": termination is None,
            "AttritionType": attrition_type,
            "AttritionReason": reason,
            "TenureYears": round((tenure_end - hire).days / 365.25, 1),
            "BaseSalaryUSD": salary,
            "BonusPct": bonus_pct,
            "AnnualBonusUSD": round(salary * bonus_pct, -1),
            "PerformanceRating": performance,
            "EngagementScore": engagement,
            "OvertimeHoursMonthly": overtime,
            "TrainingHoursYTD": round(clamp(rng.gauss(24, 10) + (5 if level in ("L1", "L2") else 0), 0, 80), 1),
            "PromotedLast2Years": rng.random() < (0.12 + (performance - 3) * 0.08),
        }


HR_EMPLOYEES = Dataset(
    "hr_employees",
    "人資名冊（一列一位員工，含已離職者）：部門、職級、據點、薪資、績效、敬業度、離職原因",
    [
        Column("EmployeeId", "NVARCHAR(12) NOT NULL PRIMARY KEY"), Column("EmployeeName", "NVARCHAR(60)"), Column("Gender", "NVARCHAR(12)"),
        Column("Age", "INT"), Column("Department", "NVARCHAR(30)"), Column("Team", "NVARCHAR(40)"),
        Column("JobTitle", "NVARCHAR(60)"), Column("JobLevel", "NVARCHAR(4)"), Column("IsManager", "BIT"),
        Column("Office", "NVARCHAR(30)"), Column("Country", "NVARCHAR(40)"), Column("Region", "NVARCHAR(10)"),
        Column("EmploymentType", "NVARCHAR(20)"), Column("WorkMode", "NVARCHAR(10)"), Column("HireDate", "DATE"),
        Column("HireYear", "INT"), Column("TerminationDate", "DATE"), Column("IsActive", "BIT"),
        Column("AttritionType", "NVARCHAR(12)"), Column("AttritionReason", "NVARCHAR(40)"),
        Column("TenureYears", "DECIMAL(5,1)"), Column("BaseSalaryUSD", "DECIMAL(12,2)"), Column("BonusPct", "DECIMAL(5,4)"),
        Column("AnnualBonusUSD", "DECIMAL(12,2)"), Column("PerformanceRating", "INT"), Column("EngagementScore", "DECIMAL(5,1)"),
        Column("OvertimeHoursMonthly", "DECIMAL(5,1)"), Column("TrainingHoursYTD", "DECIMAL(5,1)"),
        Column("PromotedLast2Years", "BIT"),
    ],
    1_500,
    hr_employees,
)

# ─────────────────────────────────────────────────────────────────────────────
# 3. Marketing campaigns — one row per campaign per day per device
# ─────────────────────────────────────────────────────────────────────────────

# Expected ROAS = 1000 × CTR × lead rate × close rate × deal ÷ CPM: about 4.7 for Google Search, 4.5 for
# LinkedIn, 17 for Email, 2-3.5 for the social channels and under 1 for YouTube (an awareness channel).
CHANNELS = {  # channel → (CPM USD, CTR, lead rate per click, close rate per lead, avg deal USD)
    "Google Search": (180.0, 0.045, 0.060, 0.12, 2600),
    "Google Display": (4.5, 0.004, 0.020, 0.06, 1800),
    "Facebook": (7.5, 0.011, 0.030, 0.04, 1500),
    "Instagram": (8.5, 0.009, 0.025, 0.06, 1400),
    "LinkedIn": (55.0, 0.006, 0.080, 0.10, 5200),
    "YouTube": (9.0, 0.005, 0.015, 0.05, 2000),
    "LINE": (6.0, 0.014, 0.028, 0.04, 1300),
    "Email": (6.0, 0.022, 0.035, 0.06, 2200),
}
CAMPAIGN_TYPES = {"Lead Generation": 30, "Product Launch": 15, "Brand Awareness": 15,
                  "Retargeting": 15, "Webinar": 12, "Seasonal Promotion": 13}
CAMPAIGN_THEMES = ["Cloud Suite", "Security Pro", "Analytics Hub", "Server Refresh", "Storage Upgrade",
                   "Support Renewal", "Cloud Migration", "Training Week", "SMB Bundle", "Partner Week", "Year-End",
                   "Free Trial"]
DEVICES = {"Mobile": 52, "Desktop": 40, "Tablet": 8}


def marketing_campaigns(ctx: Context, rows: int) -> Iterable[dict]:
    rng = ctx.rng("marketing_campaigns")
    per_campaign_rows = 60 * len(DEVICES)
    n_campaigns = max(6, rows // per_campaign_rows)
    span = (ctx.end - ctx.start).days
    for c in range(1, n_campaigns + 1):
        ctype = pick(rng, CAMPAIGN_TYPES)
        channel = rng.choice(list(CHANNELS))
        region = pick(rng, REGION_WEIGHTS)
        segment = pick(rng, SEGMENTS)
        theme = rng.choice(CAMPAIGN_THEMES)
        start = ctx.start + dt.timedelta(days=rng.randint(0, max(0, span - 20)))
        length = rng.randint(21, 90)
        daily_budget = rng.uniform(150, 2200) * (1.6 if segment == "Enterprise" else 1.0)
        if channel == "Email":
            daily_budget *= 0.2  # sends are capped by the mailing list; email can't buy reach like ads can
        cpm, ctr, lead_rate, close_rate, deal = CHANNELS[channel]
        quality = rng.uniform(0.7, 1.35)  # how well this campaign performs vs its channel
        name = f"{start.year} {theme} - {channel} - {region}"
        for day in range(length):
            d = start + dt.timedelta(days=day)
            if d > ctx.end:
                break
            # Spend ramps up, peaks mid-flight and tails off; weekends are cheaper and quieter.
            flight = 0.6 + 0.8 * math.sin(math.pi * (day + 0.5) / length)
            weekend = 0.6 if d.weekday() >= 5 else 1.0
            for device, share in DEVICES.items():
                spend = daily_budget * flight * weekend * share / 100 * rng.uniform(0.85, 1.15)
                impressions = int(spend / cpm * 1000)
                device_ctr = ctr * {"Mobile": 1.1, "Desktop": 0.95, "Tablet": 0.85}[device]
                clicks = sum(1 for _ in range(min(impressions, 3000)) if rng.random() < device_ctr * quality) \
                    if impressions < 3000 else int(impressions * device_ctr * quality * rng.uniform(0.9, 1.1))
                leads = int(clicks * lead_rate * quality * rng.uniform(0.7, 1.3) + rng.random())
                conversions = int(leads * close_rate * rng.uniform(0.6, 1.4) + rng.random())
                revenue = conversions * deal * rng.uniform(0.7, 1.4)
                year, quarter, _ = fiscal(d)
                yield {
                    "Date": d,
                    "FiscalYear": year,
                    "FiscalQuarter": quarter,
                    "CampaignId": f"CMP-{c:04d}",
                    "CampaignName": name,
                    "CampaignType": ctype,
                    "Theme": theme,
                    "Channel": channel,
                    "Region": region,
                    "TargetSegment": segment,
                    "Device": device,
                    "Impressions": impressions,
                    "Clicks": clicks,
                    "Leads": leads,
                    "Conversions": conversions,
                    "Spend": money(spend),
                    "Revenue": money(revenue),
                    "CTR": round(clicks / impressions, 4) if impressions else 0.0,
                    "CPC": money(spend / clicks) if clicks else 0.0,
                    "ConversionRate": round(conversions / clicks, 4) if clicks else 0.0,
                    "ROAS": round(revenue / spend, 2) if spend else 0.0,
                }


MARKETING_CAMPAIGNS = Dataset(
    "marketing_campaigns",
    "行銷活動成效（一列一個活動、一天、一種裝置）：曝光、點擊、名單、成交、花費、營收、ROAS",
    [
        Column("Date", "DATE"), Column("FiscalYear", "INT"), Column("FiscalQuarter", "NVARCHAR(6)"),
        Column("CampaignId", "NVARCHAR(10)"), Column("CampaignName", "NVARCHAR(100)"),
        Column("CampaignType", "NVARCHAR(30)"), Column("Theme", "NVARCHAR(40)"), Column("Channel", "NVARCHAR(30)"),
        Column("Region", "NVARCHAR(20)"), Column("TargetSegment", "NVARCHAR(30)"), Column("Device", "NVARCHAR(10)"),
        Column("Impressions", "INT"), Column("Clicks", "INT"), Column("Leads", "INT"), Column("Conversions", "INT"),
        Column("Spend", "DECIMAL(12,2)"), Column("Revenue", "DECIMAL(14,2)"), Column("CTR", "DECIMAL(7,4)"),
        Column("CPC", "DECIMAL(10,2)"), Column("ConversionRate", "DECIMAL(7,4)"), Column("ROAS", "DECIMAL(10,2)"),
    ],
    20_000,
    marketing_campaigns,
)

# ─────────────────────────────────────────────────────────────────────────────
# 4. Support tickets — one row per ticket
# ─────────────────────────────────────────────────────────────────────────────

TICKET_CATEGORIES = {  # category → (share, base resolution hours, escalation probability)
    "Technical Issue": (28, 20, 0.30), "Installation": (12, 10, 0.12), "Account Access": (14, 4, 0.05),
    "Billing": (12, 8, 0.08), "Licensing": (10, 12, 0.10), "Bug Report": (14, 46, 0.55),
    "Feature Request": (10, 70, 0.20),
}
PRIORITIES = {"P1 - Critical": (5, 4), "P2 - High": (18, 12), "P3 - Medium": (47, 48), "P4 - Low": (30, 120)}
TICKET_CHANNELS = {"Email": 34, "Portal": 26, "Chat": 22, "Phone": 13, "LINE": 5}


def support_tickets(ctx: Context, rows: int) -> Iterable[dict]:
    rng = ctx.rng("support_tickets")
    agents = {team: [person(rng) for _ in range(size)]
              for team, size in (("Tier 1", 28), ("Tier 2", 14), ("Engineering", 8))}
    days = sorted(weighted_days(ctx, rng, rows))
    for n, day in enumerate(days, start=1):
        created = dt.datetime.combine(day, dt.time(rng.randint(0, 23), rng.randint(0, 59)))
        category = pick(rng, {k: v[0] for k, v in TICKET_CATEGORIES.items()})
        _, base_hours, escalate_p = TICKET_CATEGORIES[category]
        priority = pick(rng, {k: v[0] for k, v in PRIORITIES.items()})
        sla_hours = PRIORITIES[priority][1]
        channel = pick(rng, TICKET_CHANNELS)
        segment = pick(rng, SEGMENTS)
        region, country, _ = place(rng)

        escalated = rng.random() < escalate_p * (1.4 if priority.startswith(("P1", "P2")) else 1.0)
        final_team = "Tier 1"
        if escalated:
            final_team = "Engineering" if category == "Bug Report" or rng.random() < 0.35 else "Tier 2"
        team_factor = {"Tier 1": 1.0, "Tier 2": 1.9, "Engineering": 3.2}[final_team]
        priority_factor = {"P1": 0.35, "P2": 0.6, "P3": 1.0, "P4": 1.6}[priority[:2]]
        first_response = round(clamp(rng.lognormvariate(math.log({"Chat": 3, "Phone": 1}.get(channel, 45)), 0.8)
                                     * priority_factor, 0.5, 2880), 1)
        resolution = round(clamp(rng.lognormvariate(math.log(base_hours * team_factor * priority_factor), 0.75),
                                 0.2, 24 * 45), 1)
        resolved_at = created + dt.timedelta(hours=resolution)
        if resolved_at > dt.datetime.combine(ctx.end, dt.time(23, 59)):
            status, resolved_at, resolution_out = pick(rng, {"Open": 60, "Pending Customer": 40}), None, None
        else:
            status, resolution_out = ("Closed" if rng.random() < 0.8 else "Resolved"), resolution
        sla_met = None if resolution_out is None else resolution_out <= sla_hours
        csat = None
        if resolution_out is not None and rng.random() < 0.42:  # surveyed and answered
            mood = 4.4 - (0 if sla_met else 1.0) - (0.4 if escalated else 0) - min(resolution, 200) / 400
            csat = int(clamp(round(rng.gauss(mood, 0.8)), 1, 5))
        year, quarter, _ = fiscal(day)
        yield {
            "TicketId": f"TKT-{n:06d}",
            "CreatedAt": created,
            "CreatedDate": day,
            "FiscalYear": year,
            "FiscalQuarter": quarter,
            "Channel": channel,
            "Category": category,
            "Priority": priority,
            "ProductCategory": pick(rng, CATEGORY_WEIGHTS),
            "CustomerSegment": segment,
            "Region": region,
            "Country": country,
            "EntryTeam": "Tier 1",
            "ResolvedByTeam": final_team,
            "AssignedAgent": rng.choice(agents[final_team]),
            "IsEscalated": escalated,
            "Status": status,
            "FirstResponseMinutes": first_response,
            "ResolutionHours": resolution_out,
            "SlaTargetHours": sla_hours,
            "SlaMet": sla_met,
            "ResolvedAt": resolved_at,
            "ReopenedCount": 0 if resolution_out is None else pick(rng, {0: 88, 1: 10, 2: 2}),
            "CsatScore": csat,
        }


SUPPORT_TICKETS = Dataset(
    "support_tickets",
    "客服工單（一列一張工單）：類別、優先度、升級路徑、首次回應與解決時間、SLA、滿意度",
    [
        Column("TicketId", "NVARCHAR(12) NOT NULL PRIMARY KEY"), Column("CreatedAt", "DATETIME2(0)"), Column("CreatedDate", "DATE"),
        Column("FiscalYear", "INT"), Column("FiscalQuarter", "NVARCHAR(6)"), Column("Channel", "NVARCHAR(20)"),
        Column("Category", "NVARCHAR(30)"), Column("Priority", "NVARCHAR(20)"), Column("ProductCategory", "NVARCHAR(50)"),
        Column("CustomerSegment", "NVARCHAR(30)"), Column("Region", "NVARCHAR(20)"), Column("Country", "NVARCHAR(50)"),
        Column("EntryTeam", "NVARCHAR(20)"), Column("ResolvedByTeam", "NVARCHAR(20)"),
        Column("AssignedAgent", "NVARCHAR(60)"), Column("IsEscalated", "BIT"), Column("Status", "NVARCHAR(20)"),
        Column("FirstResponseMinutes", "DECIMAL(8,1)"), Column("ResolutionHours", "DECIMAL(8,1)"),
        Column("SlaTargetHours", "INT"), Column("SlaMet", "BIT"), Column("ResolvedAt", "DATETIME2(0)"),
        Column("ReopenedCount", "INT"), Column("CsatScore", "INT"),
    ],
    15_000,
    support_tickets,
)

# ─────────────────────────────────────────────────────────────────────────────
# 5. Monthly P&L — one row per month × business unit × region × account
# ─────────────────────────────────────────────────────────────────────────────

BUSINESS_UNITS = {"Software": 1.25, "Hardware": 0.9, "Support": 0.7, "Services": 0.55}
# statement line → [(account, share of the unit's monthly revenue, sign)]; revenue lines carry +,
# costs carry -, so summing Actual by StatementLine draws a waterfall from revenue down to profit.
PNL_ACCOUNTS = {
    "Revenue": [("Subscription Revenue", 0.68, 1), ("License Revenue", 0.20, 1), ("Services Revenue", 0.12, 1)],
    "Cost of Revenue": [("Cloud Hosting", 0.09, -1), ("Support Delivery", 0.07, -1), ("Hardware Cost", 0.05, -1),
                        ("Third-party Licenses", 0.04, -1)],
    "Sales & Marketing": [("Sales Salaries", 0.12, -1), ("Commissions", 0.05, -1), ("Advertising", 0.06, -1),
                          ("Events", 0.02, -1)],
    "Research & Development": [("Engineering Salaries", 0.16, -1), ("R&D Tools", 0.02, -1)],
    "General & Administrative": [("G&A Salaries", 0.05, -1), ("Rent & Facilities", 0.03, -1),
                                 ("Professional Fees", 0.015, -1)],
    "Other Income/Expense": [("Interest Income", 0.006, 1), ("FX Gain/Loss", 0.004, 1)],
}
REGION_REVENUE = {"AMER": 2.8, "APAC": 2.4, "EMEA": 1.9, "LATAM": 0.7}  # USD million per month, base


def pnl_monthly(ctx: Context, rows: int) -> Iterable[dict]:
    rng = ctx.rng("pnl_monthly")
    months = month_starts(ctx.start, ctx.end)
    for month in months:
        year, quarter, m = fiscal(month)
        for bu, bu_scale in BUSINESS_UNITS.items():
            for region, region_base in REGION_REVENUE.items():
                revenue_base = region_base * bu_scale * 1_000_000 * demand_weight(month, ctx.start)
                for line, accounts in PNL_ACCOUNTS.items():
                    for account, share, sign in accounts:
                        budget = revenue_base * share * (0.97 if sign > 0 else 1.0)
                        noise = rng.gauss(1.0, 0.07 if sign > 0 else 0.05)
                        if account == "FX Gain/Loss":
                            actual = revenue_base * share * rng.gauss(0, 1.2)
                        else:
                            actual = budget * noise * sign
                        signed_budget = budget * sign if account != "FX Gain/Loss" else 0.0
                        variance = actual - signed_budget
                        yield {
                            "Month": month,
                            "FiscalYear": year,
                            "FiscalQuarter": quarter,
                            "FiscalMonth": m,
                            "BusinessUnit": bu,
                            "Region": region,
                            "StatementLine": line,
                            "Account": account,
                            "LineType": "Income" if sign > 0 else "Expense",
                            "Actual": money(actual),
                            "Budget": money(signed_budget),
                            "Variance": money(variance),
                            "VariancePct": round(variance / abs(signed_budget), 4) if signed_budget else 0.0,
                        }


PNL_MONTHLY = Dataset(
    "pnl_monthly",
    "月度損益（一列一個月×事業部×地區×會計科目）：實際、預算、差異；收入為正、費用為負，可直接畫瀑布圖",
    [
        Column("Month", "DATE"), Column("FiscalYear", "INT"), Column("FiscalQuarter", "NVARCHAR(6)"),
        Column("FiscalMonth", "INT"), Column("BusinessUnit", "NVARCHAR(30)"), Column("Region", "NVARCHAR(20)"),
        Column("StatementLine", "NVARCHAR(40)"), Column("Account", "NVARCHAR(40)"), Column("LineType", "NVARCHAR(10)"),
        Column("Actual", "DECIMAL(16,2)"), Column("Budget", "DECIMAL(16,2)"), Column("Variance", "DECIMAL(16,2)"),
        Column("VariancePct", "DECIMAL(9,4)"),
    ],
    0,  # sized by the date range: months × 4 units × 4 regions × 18 accounts
    pnl_monthly,
)

# ─────────────────────────────────────────────────────────────────────────────
# 6. Inventory — one row per SKU per warehouse per week
# ─────────────────────────────────────────────────────────────────────────────

WAREHOUSES = {"Taipei DC": ("Taiwan", "APAC"), "Kaohsiung DC": ("Taiwan", "APAC"), "Tokyo DC": ("Japan", "APAC"),
              "Singapore DC": ("Singapore", "APAC"), "Frankfurt DC": ("Germany", "EMEA"),
              "Dallas DC": ("USA", "AMER")}
HARDWARE = {  # category → [(product, unit cost USD, avg daily demand per warehouse)]
    "Security Appliances": [("Edge IPS 1100", 3800, 0.8), ("Edge IPS 4200", 9200, 0.3), ("NAC Box 300", 1600, 1.2)],
    "Network Devices": [("Core Switch 48P", 2400, 1.0), ("Access Switch 24P", 620, 3.5), ("Wi-Fi 6E AP", 310, 6.0),
                        ("SD-WAN Edge", 980, 1.4)],
    "Servers": [("Rack Server 1U", 5200, 0.6), ("Rack Server 2U", 7800, 0.4), ("Edge Server Mini", 1900, 0.9)],
    "Storage": [("NAS 8-Bay", 1350, 1.1), ("SAN Array 24", 14800, 0.15), ("SSD 3.84TB", 410, 7.5)],
    "Accessories": [("SFP+ Module", 45, 26.0), ("Rack Rail Kit", 60, 9.0), ("Power Supply 750W", 120, 5.0),
                    ("Console Cable", 12, 18.0)],
}
SUPPLIERS = {"Security Appliances": [("Formosa Devices", "Taiwan", 18), ("Kanto Electronics", "Japan", 24)],
             "Network Devices": [("Formosa Devices", "Taiwan", 16), ("Pacific Netware", "USA", 30)],
             "Servers": [("Granite Compute", "Taiwan", 21), ("Rhine Systems", "Germany", 35)],
             "Storage": [("Granite Compute", "Taiwan", 19), ("Sakura Storage", "Japan", 26)],
             "Accessories": [("Bright Components", "China", 12), ("Formosa Devices", "Taiwan", 9)]}


def inventory_weekly(ctx: Context, rows: int) -> Iterable[dict]:
    rng = ctx.rng("inventory_weekly")
    skus = [(category, product, cost, demand)
            for category, items in HARDWARE.items() for product, cost, demand in items]
    # Every week in the date range; a row count (--rows) keeps only the most recent weeks.
    last_monday = ctx.end - dt.timedelta(days=ctx.end.weekday())
    weeks = [last_monday - dt.timedelta(weeks=w) for w in range((last_monday - ctx.start).days // 7 + 1)][::-1]
    if rows:
        weeks = weeks[-max(4, rows // (len(skus) * len(WAREHOUSES))):]
    for warehouse, (country, region) in WAREHOUSES.items():
        size = {"Taipei DC": 1.6, "Dallas DC": 1.3, "Frankfurt DC": 1.1}.get(warehouse, 0.8)
        for i, (category, product, cost, demand) in enumerate(skus, start=1):
            supplier, supplier_country, lead_time = rng.choice(SUPPLIERS[category])
            daily = demand * size * rng.uniform(0.7, 1.3)
            # Stock is checked weekly, so safety stock covers at least a week. The order must be bigger
            # than the reorder point, or stock never climbs back above it and every week reorders.
            safety = math.ceil(daily * max(7, lead_time * 0.35))
            reorder_point = math.ceil(daily * lead_time + safety)
            order_qty = math.ceil(daily * max(56, lead_time * 3))
            on_hand = reorder_point + rng.randint(0, order_qty)
            inbound, inbound_eta = 0, None
            for week in weeks:
                weekly_demand = sum(1 for _ in range(7) if rng.random() < min(1, daily)) if daily < 1 \
                    else int(rng.gauss(daily * 7 * MONTH_SEASONALITY[week.month - 1], math.sqrt(daily * 7)))
                weekly_demand = max(0, weekly_demand)
                if inbound and inbound_eta and week >= inbound_eta:
                    on_hand, inbound, inbound_eta = on_hand + inbound, 0, None
                shipped = min(on_hand, weekly_demand)
                on_hand -= shipped
                if on_hand <= reorder_point and not inbound:
                    inbound = order_qty
                    inbound_eta = week + dt.timedelta(days=int(lead_time * rng.uniform(0.8, 1.4)))
                reserved = min(on_hand, int(daily * rng.uniform(0.5, 3)))
                year, quarter, _ = fiscal(week)
                yield {
                    "SnapshotDate": week,
                    "FiscalYear": year,
                    "FiscalQuarter": quarter,
                    "Warehouse": warehouse,
                    "Country": country,
                    "Region": region,
                    "ProductCategory": category,
                    "SKU": f"HW-{i:03d}",
                    "ProductName": product,
                    "Supplier": supplier,
                    "SupplierCountry": supplier_country,
                    "LeadTimeDays": lead_time,
                    "OnHandQty": on_hand,
                    "ReservedQty": reserved,
                    "AvailableQty": on_hand - reserved,
                    "InboundQty": inbound,
                    "WeeklyDemandQty": weekly_demand,
                    "UnfilledQty": weekly_demand - shipped,
                    "SafetyStock": safety,
                    "ReorderPoint": reorder_point,
                    "BelowReorderPoint": on_hand <= reorder_point,
                    "IsStockout": on_hand == 0,
                    "UnitCost": float(cost),
                    "InventoryValue": money(on_hand * cost),
                    "DaysOfSupply": round(on_hand / daily, 1) if daily else 0.0,
                }


INVENTORY_WEEKLY = Dataset(
    "inventory_weekly",
    "倉儲庫存週快照（一列一個 SKU、一個倉庫、一週）：庫存、在途、需求、缺貨、再訂購點、庫存金額",
    [
        Column("SnapshotDate", "DATE"), Column("FiscalYear", "INT"), Column("FiscalQuarter", "NVARCHAR(6)"),
        Column("Warehouse", "NVARCHAR(30)"), Column("Country", "NVARCHAR(40)"), Column("Region", "NVARCHAR(10)"),
        Column("ProductCategory", "NVARCHAR(30)"), Column("SKU", "NVARCHAR(10)"), Column("ProductName", "NVARCHAR(60)"),
        Column("Supplier", "NVARCHAR(40)"), Column("SupplierCountry", "NVARCHAR(40)"), Column("LeadTimeDays", "INT"),
        Column("OnHandQty", "INT"), Column("ReservedQty", "INT"), Column("AvailableQty", "INT"),
        Column("InboundQty", "INT"), Column("WeeklyDemandQty", "INT"), Column("UnfilledQty", "INT"),
        Column("SafetyStock", "INT"), Column("ReorderPoint", "INT"), Column("BelowReorderPoint", "BIT"),
        Column("IsStockout", "BIT"), Column("UnitCost", "DECIMAL(12,2)"), Column("InventoryValue", "DECIMAL(16,2)"),
        Column("DaysOfSupply", "DECIMAL(8,1)"),
    ],
    0,  # sized by the date range: weeks × 17 SKUs × 6 warehouses
    inventory_weekly,
)

# ─────────────────────────────────────────────────────────────────────────────
# 7. SaaS subscriptions — one row per customer per month (MRR movements)
# ─────────────────────────────────────────────────────────────────────────────

PLANS = {"Starter": (29, 3, 12), "Professional": (59, 10, 40), "Business": (99, 25, 30), "Enterprise": (149, 120, 18)}
CHURN_REASONS = {"Price": 28, "Switched to Competitor": 22, "Low Usage": 20, "Missing Features": 14,
                 "Budget Cut": 10, "Business Closed": 6}


def saas_subscriptions(ctx: Context, rows: int) -> Iterable[dict]:
    rng = ctx.rng("saas_subscriptions")
    months = month_starts(ctx.start, ctx.end)
    customers = make_customers(rng, max(100, rows // 14), "ACCT")
    upgrade = {"Starter": "Professional", "Professional": "Business", "Business": "Enterprise"}
    for c in customers:
        if c.segment == "Consumer":
            plan = "Starter"
        elif c.segment == "SMB":
            plan = pick(rng, {p: share for p, (_, _, share) in PLANS.items()})
        else:
            plan = pick(rng, {"Business": 50, "Enterprise": 50})
        price, base_seats, _ = PLANS[plan]
        seats = 1 if c.segment == "Consumer" else max(1, int(rng.lognormvariate(math.log(base_seats), 0.6)))
        billing = "Annual" if rng.random() < (0.65 if c.segment not in ("SMB", "Consumer") else 0.35) else "Monthly"
        start_index = min(len(months) - 1, int(len(months) * rng.random() ** 0.8))
        health = rng.gauss(70, 15)
        mrr_prev = 0.0
        churned = False
        for age, month in enumerate(months[start_index:]):
            movement, reason = ("New" if age == 0 else "Flat"), None
            churn_p = clamp(0.035 - (health - 70) * 0.0012 + (0.02 if billing == "Monthly" else -0.01), 0.002, 0.12)
            if age > 0 and (billing == "Monthly" or month.month == months[start_index].month) and rng.random() < churn_p:
                movement, reason, churned = "Churn", pick(rng, CHURN_REASONS), True
            elif age > 0 and rng.random() < 0.06 + max(0, health - 75) * 0.003:
                if plan in upgrade and rng.random() < 0.25:
                    plan = upgrade[plan]
                    price = PLANS[plan][0]
                seats = int(seats * rng.uniform(1.1, 1.5)) + 1
                movement = "Expansion"
            elif age > 0 and rng.random() < 0.025:
                seats = max(1, int(seats * rng.uniform(0.6, 0.9)))
                movement = "Contraction"
            health = clamp(health + rng.gauss(0, 4), 5, 100)
            discount = 0.17 if billing == "Annual" else 0.0
            mrr = 0.0 if churned else money(seats * price * (1 - discount))
            year, quarter, _ = fiscal(month)
            yield {
                "Month": month,
                "FiscalYear": year,
                "FiscalQuarter": quarter,
                "AccountId": c.id,
                "AccountName": c.name,
                "Industry": c.industry,
                "CompanySize": c.size,
                "CustomerSegment": c.segment,
                "Region": c.region,
                "Country": c.country,
                "Plan": plan,
                "BillingCycle": billing,
                "Seats": 0 if churned else seats,
                "MRR": mrr,
                "ARR": money(mrr * 12),
                "MrrChange": money(mrr - mrr_prev),
                "MovementType": movement,
                "IsChurned": churned,
                "ChurnReason": reason,
                "AccountAgeMonths": age,
                "HealthScore": round(health, 1),
            }
            mrr_prev = mrr
            if churned:
                break


SAAS_SUBSCRIPTIONS = Dataset(
    "saas_subscriptions",
    "SaaS 訂閱月報（一列一個客戶、一個月）：方案、席次、MRR/ARR、新增/擴充/縮減/流失、健康分數",
    [
        Column("Month", "DATE"), Column("FiscalYear", "INT"), Column("FiscalQuarter", "NVARCHAR(6)"),
        Column("AccountId", "NVARCHAR(12)"), Column("AccountName", "NVARCHAR(100)"), Column("Industry", "NVARCHAR(40)"),
        Column("CompanySize", "NVARCHAR(20)"), Column("CustomerSegment", "NVARCHAR(30)"), Column("Region", "NVARCHAR(20)"),
        Column("Country", "NVARCHAR(50)"), Column("Plan", "NVARCHAR(20)"), Column("BillingCycle", "NVARCHAR(10)"),
        Column("Seats", "INT"), Column("MRR", "DECIMAL(12,2)"), Column("ARR", "DECIMAL(14,2)"),
        Column("MrrChange", "DECIMAL(12,2)"), Column("MovementType", "NVARCHAR(12)"), Column("IsChurned", "BIT"),
        Column("ChurnReason", "NVARCHAR(40)"), Column("AccountAgeMonths", "INT"), Column("HealthScore", "DECIMAL(5,1)"),
    ],
    15_000,
    saas_subscriptions,
)

DATASETS = {d.name: d for d in (SALES_ORDERS, HR_EMPLOYEES, MARKETING_CAMPAIGNS, SUPPORT_TICKETS,
                                PNL_MONTHLY, INVENTORY_WEEKLY, SAAS_SUBSCRIPTIONS)}

# ─────────────────────────────────────────────────────────────────────────────
# Output
# ─────────────────────────────────────────────────────────────────────────────


def csv_value(value) -> str:
    if value is None:
        return ""
    if isinstance(value, bool):
        return "true" if value else "false"  # what the upload's type detection reads as Boolean
    if isinstance(value, dt.datetime):
        return value.strftime("%Y-%m-%d %H:%M:%S")
    if isinstance(value, dt.date):
        return value.isoformat()
    return str(value)


def json_value(value) -> str:
    if value is None:
        return "null"
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, dt.datetime):
        return f'"{value.strftime("%Y-%m-%dT%H:%M:%S")}"'
    if isinstance(value, dt.date):
        return f'"{value.isoformat()}"'
    if isinstance(value, float):
        text = repr(value)
        # SQL Server can't convert exponent notation ("1e-05") to DECIMAL, so spell it out.
        return text if "e" not in text.lower() else (f"{value:.10f}".rstrip("0").rstrip(".") or "0")
    if isinstance(value, int):
        return str(value)
    return json.dumps(str(value), ensure_ascii=False)


def write_csv(path: Path, dataset: Dataset, rows: list[dict], bom: bool) -> None:
    with path.open("w", newline="", encoding="utf-8-sig" if bom else "utf-8") as f:
        writer = csv.writer(f)
        writer.writerow([c.name for c in dataset.columns])
        for row in rows:
            writer.writerow([csv_value(row[c.name]) for c in dataset.columns])


SQL_CHUNK_ROWS = 2_000


def write_sql(path: Path, dataset: Dataset, rows: list[dict], table: str, replace: bool) -> None:
    # Rows travel as JSON arrays read by OPENJSON. INSERT ... VALUES with thousands of rows makes SQL
    # Server compile every constant, which takes many minutes on a small Azure SQL tier; one JSON
    # string per chunk compiles instantly. Needs SQL Server 2016+ or Azure SQL (compatibility level 130+).
    names = [c.name for c in dataset.columns]
    column_list = ", ".join(f"[{n}]" for n in names)
    # OPENJSON WITH takes the bare type: "INT NOT NULL PRIMARY KEY" -> "INT".
    shape = ",\n    ".join(f"[{c.name}] {c.sql_type.split(' NOT NULL')[0]} '$[{i}]'"
                           for i, c in enumerate(dataset.columns))
    with path.open("w", encoding="utf-8-sig", newline="\n") as f:
        f.write(f"-- {dataset.name}: {dataset.description}\n")
        f.write(f"-- {len(rows):,} rows, generated by scripts/demo-data/generate_demo_data.py\n")
        f.write("-- One transaction: if the script fails or is cancelled, the database is left as it was.\n")
        f.write("SET NOCOUNT ON;\nSET XACT_ABORT ON;\n\n")
        if not replace:
            f.write(f"IF OBJECT_ID(N'{table}', N'U') IS NOT NULL\n"
                    f"    THROW 50000, N'{table} already exists. Generate the script with --replace to drop and recreate it.', 1;\n\n")
        f.write("BEGIN TRANSACTION;\n\n")
        if replace:
            f.write(f"IF OBJECT_ID(N'{table}', N'U') IS NOT NULL DROP TABLE {table};\n\n")
        f.write(f"CREATE TABLE {table} (\n")
        f.write(",\n".join(f"    [{c.name}] {c.sql_type}{'' if 'NOT NULL' in c.sql_type else ' NULL'}"
                           for c in dataset.columns))
        f.write("\n);\n\n")
        # Dynamic SQL, compiled when it runs: SQL Server compiles a whole batch before running it, so a
        # plain INSERT would be checked against the table as it was before the DROP and CREATE above
        # (e.g. the old dbo.Orders without the added columns) and fail with "Invalid column name".
        insert = (f"INSERT INTO {table} ({column_list})\nSELECT {column_list}\n"
                  f"FROM OPENJSON(@rows) WITH (\n    {shape}\n);")
        f.write("DECLARE @rows NVARCHAR(MAX);\n")
        f.write("DECLARE @insert NVARCHAR(MAX) = N'" + insert.replace("'", "''") + "';\n")
        for start in range(0, len(rows), SQL_CHUNK_ROWS):
            chunk = rows[start:start + SQL_CHUNK_ROWS]
            data = ",\n".join("[" + ",".join(json_value(r[n]) for n in names) + "]" for r in chunk)
            f.write("\nSET @rows = N'[\n" + data.replace("'", "''") + "\n]';\n")
            f.write("EXEC sp_executesql @insert, N'@rows NVARCHAR(MAX)', @rows = @rows;\n")
            f.write(f"RAISERROR(N'{table}: {start + len(chunk):,} / {len(rows):,} rows', 0, 1) WITH NOWAIT;\n")
        f.write("\nCOMMIT TRANSACTION;\n")
        f.write(f"PRINT N'{table}: done, {len(rows):,} rows.';\n")


def default_table(name: str) -> str:
    return "dbo.Demo" + "".join(part.capitalize() for part in name.split("_"))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--datasets", default="all",
                        help=f"comma-separated, or 'all'. Available: {', '.join(DATASETS)}")
    parser.add_argument("--out", default="demo-data-out", help="output folder (default: ./demo-data-out)")
    parser.add_argument("--format", choices=["csv", "sql", "both"], default="csv")
    parser.add_argument("--scale", type=float, default=1.0,
                        help="row-count multiplier (defaults stay under the 50,000-row upload limit)")
    parser.add_argument("--rows", action="append", default=[], metavar="NAME=N",
                        help="exact row count for one dataset, e.g. --rows sales_orders=5000")
    parser.add_argument("--years", type=int, default=3,
                        help="calendar years of history, from January 1 up to --end (default 3)")
    parser.add_argument("--end", type=dt.date.fromisoformat, default=dt.date.today(),
                        help="last date in the data, YYYY-MM-DD (default: today)")
    parser.add_argument("--seed", type=int, default=42, help="same seed + same --end = same data")
    parser.add_argument("--table", action="append", default=[], metavar="NAME=SCHEMA.TABLE",
                        help="SQL table for a dataset (default dbo.Demo<Name>), e.g. sales_orders=dbo.Orders")
    parser.add_argument("--replace", action="store_true",
                        help="SQL scripts drop an existing table first (default: refuse to overwrite)")
    parser.add_argument("--no-bom", action="store_true", help="CSV without the UTF-8 BOM Excel needs")
    args = parser.parse_args()

    names = list(DATASETS) if args.datasets == "all" else [n.strip() for n in args.datasets.split(",")]
    unknown = [n for n in names if n not in DATASETS]
    if unknown:
        parser.error(f"unknown dataset(s): {', '.join(unknown)}. Available: {', '.join(DATASETS)}")
    exact_rows = dict(item.split("=", 1) for item in args.rows if "=" in item)
    tables = dict(item.split("=", 1) for item in args.table if "=" in item)
    for flag, items, mapping in (("--rows", args.rows, exact_rows), ("--table", args.table, tables)):
        if len(mapping) != len(items) or any(n not in DATASETS for n in mapping):
            parser.error(f"{flag} takes NAME=VALUE with NAME one of: {', '.join(DATASETS)}")
    if any(not n.isdigit() for n in exact_rows.values()):
        parser.error("--rows takes a whole number, e.g. --rows sales_orders=5000")

    start = dt.date(args.end.year - args.years + 1, 1, 1)
    ctx = Context(seed=args.seed, start=start, end=args.end)
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    print(f"Date range {start} .. {args.end}, seed {args.seed}")
    for name in names:
        dataset = DATASETS[name]
        target = int(exact_rows.get(name, dataset.default_rows * args.scale))
        rows = list(dataset.generate(ctx, target))
        written = []
        if args.format in ("csv", "both"):
            path = out / f"{name}.csv"
            write_csv(path, dataset, rows, bom=not args.no_bom)
            written.append(path.name)
        if args.format in ("sql", "both"):
            path = out / f"{name}.sql"
            write_sql(path, dataset, rows, tables.get(name, default_table(name)), args.replace)
            written.append(path.name)
        warn = "  (over the 50,000-row upload limit; load it with the SQL script instead)" \
            if len(rows) > 50_000 and args.format != "sql" else ""
        print(f"  {name:<20} {len(rows):>7,} rows x {len(dataset.columns):>2} columns -> {', '.join(written)}{warn}")
    print(f"Done: {out.resolve()}")


if __name__ == "__main__":
    main()
