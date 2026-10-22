using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Tests.Controllers
{
    /// <summary>
    /// A controller does HTTP — binding, the current user, turning a ServiceResult into a response —
    /// and reaches the database only through services (JackyAIApp.Server/CLAUDE.md). The controllers
    /// that still take a DbContext are listed until they are moved; the list only ever shrinks.
    /// </summary>
    public class ControllersDoNotTouchTheDatabaseTests
    {
        private static readonly HashSet<string> NotYetMoved =
        [
            "AccountController",
            "DictionaryController",
            "ExamController",
            "ReviewController",
            "SitemapController",
            "StatsController",
        ];

        private static IEnumerable<Type> Controllers() =>
            typeof(DashboardController).Assembly.GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

        private static bool TakesTheDatabase(Type controller) =>
            controller.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Any(p => p.ParameterType == typeof(AzureSQLDBContext)
                    || p.ParameterType == typeof(IDbContextFactory<AzureSQLDBContext>));

        [Fact]
        public void NoController_TakesADbContext()
        {
            var offenders = Controllers()
                .Where(c => TakesTheDatabase(c) && !NotYetMoved.Contains(c.Name))
                .Select(c => c.Name)
                .ToList();

            Assert.Empty(offenders);
        }

        [Fact]
        public void TheNotYetMovedList_NamesOnlyControllersThatStillDo()
        {
            var stale = NotYetMoved
                .Where(name => !Controllers().Any(c => c.Name == name && TakesTheDatabase(c)))
                .ToList();

            // Moved one? Take it off the list.
            Assert.Empty(stale);
        }
    }
}
