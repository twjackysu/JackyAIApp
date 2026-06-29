using JackyAIApp.Server.Data.Models.Portfolio;

namespace JackyAIApp.Server.DTO.Portfolio
{
    public class AddTransactionRequest
    {
        public required string Symbol { get; set; }
        public required string Market { get; set; }
        public required string CompanyName { get; set; }
        public required TransactionType Type { get; set; }
        public required DateTime Date { get; set; }
        public required decimal Quantity { get; set; }
        public required decimal Price { get; set; }
        public required string Currency { get; set; }
        public decimal Fee { get; set; } = 0;
        public string? Note { get; set; }
    }

    public class TransactionDto
    {
        public int Id { get; set; }
        public string Symbol { get; set; } = "";
        public string Market { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public TransactionType Type { get; set; }
        public DateTime Date { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public string Currency { get; set; } = "";
        public decimal Fee { get; set; }
        public string? Note { get; set; }
    }

    public class HoldingDto
    {
        public string Symbol { get; set; } = "";
        public string Market { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string Currency { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal AvgCost { get; set; }
        public decimal CurrentPrice { get; set; }
        public decimal MarketValueLocal { get; set; }  // in original currency
        public decimal MarketValueTWD { get; set; }
        public decimal CostBasisTWD { get; set; }
        public decimal UnrealizedPnlTWD { get; set; }
        public decimal UnrealizedPnlPct { get; set; }
        public bool PriceFetchFailed { get; set; }
    }

    public class PortfolioSummaryDto
    {
        public decimal TotalNetWorthTWD { get; set; }
        public decimal TwStockTWD { get; set; }
        public decimal UsStockTWD { get; set; }
        public decimal UsdTwdRate { get; set; }
        public decimal TotalUnrealizedPnlTWD { get; set; }
        public decimal TotalUnrealizedPnlPct { get; set; }
        public List<HoldingDto> Holdings { get; set; } = [];
    }
}
