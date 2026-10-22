using JackyAIApp.Server.Controllers;
using JackyAIApp.Server.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JackyAIApp.Server.Tests.Controllers
{
    /// <summary>
    /// A controller does HTTP — binding, the current user, turning a ServiceResult into a response —
    /// and reaches the database only through services (JackyAIApp.Server/CLAUDE.md).
    /// </summary>
    public class ControllersDoNotTouchTheDatabaseTests
    {
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
                .Where(TakesTheDatabase)
                .Select(c => c.Name)
                .ToList();

            Assert.Empty(offenders);
        }

        [Fact]
        public void TheCheck_SeesTheControllers()
        {
            // Guards the test above against passing because it found nothing to look at.
            Assert.Contains(Controllers(), c => c == typeof(DashboardController));
        }
    }
}
