using System.Reflection;
using JackyAIApp.Server.Common;
using JackyAIApp.Server.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JackyAIApp.Server.Tests.Controllers
{
    /// <summary>
    /// Which endpoints only admins may call. The check is the [AdminOnly] filter, not code in the
    /// action, so a test of the action alone would not notice it going missing; this does.
    /// </summary>
    public class AdminOnlyEndpointsTests
    {
        private static IEnumerable<(Type Controller, MethodInfo Action)> Actions() =>
            typeof(DashboardController).Assembly.GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Select(m => (t, m)));

        [Fact]
        public void TheAdminEndpoints_AreExactlyThese()
        {
            var adminOnly = Actions()
                .Where(a => a.Action.GetCustomAttribute<AdminOnlyAttribute>() != null)
                .Select(a => $"{a.Controller.Name}.{a.Action.Name}")
                .Order();

            Assert.Equal(
            [
                "DictionaryController.Invalid",
                "ListeningExamController.GenerateListeningQuestion",
                "ListeningExamController.GetListeningBank",
            ], adminOnly);
        }

        [Fact]
        public void EveryAdminEndpoint_AlsoNeedsASignIn()
        {
            var open = Actions()
                .Where(a => a.Action.GetCustomAttribute<AdminOnlyAttribute>() != null)
                .Where(a => a.Action.GetCustomAttribute<AuthorizeAttribute>() == null
                         && a.Controller.GetCustomAttribute<AuthorizeAttribute>() == null)
                .Select(a => $"{a.Controller.Name}.{a.Action.Name}");

            // Anonymous should get the usual 401 before [AdminOnly]'s 403.
            Assert.Empty(open);
        }
    }
}
