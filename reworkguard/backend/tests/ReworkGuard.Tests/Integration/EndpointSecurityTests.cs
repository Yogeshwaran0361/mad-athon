using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace ReworkGuard.Tests.Integration;

public sealed class EndpointSecurityTests
{
    [Fact]
    public void Every_endpoint_declares_its_authorization()
    {
        var actions = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToList();

        actions.Should().NotBeEmpty();
        actions
            .Where(m => !m.GetCustomAttributes<AuthorizeAttribute>().Any(a => !string.IsNullOrEmpty(a.Roles))
                        && m.GetCustomAttribute<AllowAnonymousAttribute>() is null)
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .Should().BeEmpty("every action must name the roles allowed to call it");
    }

    /// <summary>
    /// The authorization matrix in docs/AUTHORIZATION_MATRIX.md is generated from the code. This fails when an endpoint
    /// or its roles change without the document; rerun with RG_UPDATE_MATRIX=1 to rewrite it, then review the diff.
    /// </summary>
    [Fact]
    public void The_documented_authorization_matrix_matches_the_code()
    {
        string[] roles = ["Employee", "QA", "TeamLead", "Manager", "Finance", "Admin"];
        var rows = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>().Select(h => (Controller: t, Method: m, Http: h))))
            .Select(x =>
            {
                var prefix = x.Controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;
                var template = x.Http.Template ?? string.Empty;
                var route = template.StartsWith("~/", StringComparison.Ordinal) ? template[2..] : string.Join('/', new[] { prefix, template }.Where(p => p.Length > 0));
                var allowed = x.Method.GetCustomAttribute<AllowAnonymousAttribute>() is not null
                    ? roles.ToHashSet()
                    : x.Method.GetCustomAttributes<AuthorizeAttribute>().SelectMany(a => (a.Roles ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)).ToHashSet();
                var anonymous = x.Method.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
                var cells = roles.Select(r => anonymous ? "public" : allowed.Contains(r) ? "✓" : string.Empty);
                return $"| {x.Http.HttpMethods.Single()} | `/{route}` | {string.Join(" | ", cells)} |";
            })
            .Order(StringComparer.Ordinal)
            .ToList();

        var expected = "# Authorization matrix\n\n"
            + "Generated from the controllers by `EndpointSecurityTests`; do not edit by hand. Every endpoint not marked public requires a signed-in user "
            + "with one of the ticked roles. Row-level rules (own records, team scope) are enforced in the services and covered by the IDOR tests.\n\n"
            + $"| Method | Route | {string.Join(" | ", roles)} |\n|---|---|{string.Concat(roles.Select(_ => "---|"))}\n"
            + string.Join("\n", rows) + "\n";

        var path = Path.Combine(RepoRoot(), "docs", "AUTHORIZATION_MATRIX.md");
        if (Environment.GetEnvironmentVariable("RG_UPDATE_MATRIX") == "1")
        {
            File.WriteAllText(path, expected);
        }

        File.Exists(path).Should().BeTrue("docs/AUTHORIZATION_MATRIX.md must exist (run with RG_UPDATE_MATRIX=1)");
        File.ReadAllText(path).ReplaceLineEndings("\n").Should().Be(expected);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "docs")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("docs");
    }
}
