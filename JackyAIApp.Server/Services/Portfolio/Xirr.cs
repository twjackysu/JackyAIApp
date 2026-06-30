namespace JackyAIApp.Server.Services.Portfolio
{
    /// <summary>
    /// Money-weighted internal rate of return for irregularly spaced cashflows (Excel's XIRR).
    /// Outflows (money invested) are negative, inflows (current value / proceeds) positive.
    /// </summary>
    public static class Xirr
    {
        /// <summary>
        /// Returns the annual rate as a fraction (0.12 = 12%), or null when it can't be solved
        /// (fewer than two flows, or no sign change among them).
        /// </summary>
        public static double? Compute(IReadOnlyList<(DateOnly Date, double Amount)> flows)
        {
            if (flows.Count < 2) return null;
            if (!flows.Any(f => f.Amount > 0) || !flows.Any(f => f.Amount < 0)) return null;

            var d0 = flows.Min(f => f.Date).DayNumber;

            double Npv(double rate)
            {
                double sum = 0;
                foreach (var (date, amount) in flows)
                {
                    var years = (date.DayNumber - d0) / 365.0;
                    sum += amount / Math.Pow(1 + rate, years);
                }
                return sum;
            }

            // Newton-Raphson from a 10% guess.
            double rate = 0.1;
            for (var i = 0; i < 100; i++)
            {
                var f = Npv(rate);
                var df = (Npv(rate + 1e-6) - f) / 1e-6;
                if (Math.Abs(df) < 1e-12) break;
                var next = rate - f / df;
                if (double.IsNaN(next) || double.IsInfinity(next)) break;
                if (next <= -0.9999) next = -0.9999; // keep (1+rate) positive for fractional powers
                if (Math.Abs(next - rate) < 1e-7) return next;
                rate = next;
            }

            // Bisection fallback over a wide bracket.
            double lo = -0.9999, hi = 10;
            double flo = Npv(lo), fhi = Npv(hi);
            if (flo * fhi > 0) return null;
            for (var i = 0; i < 200; i++)
            {
                var mid = (lo + hi) / 2;
                var fm = Npv(mid);
                if (Math.Abs(fm) < 1e-6) return mid;
                if (flo * fm < 0) { hi = mid; fhi = fm; }
                else { lo = mid; flo = fm; }
            }
            return (lo + hi) / 2;
        }
    }
}
