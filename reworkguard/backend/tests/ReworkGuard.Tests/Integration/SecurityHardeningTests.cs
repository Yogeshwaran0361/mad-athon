using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Auth;
using ReworkGuard.Application.Deductions;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Tests.Support;

namespace ReworkGuard.Tests.Integration;

/// <summary>Lockout, refresh-token rotation and reuse detection, session revocation, headers, correlation ids and IDOR.</summary>
public sealed class SecurityHardeningTests : ApiTestBase
{
    public SecurityHardeningTests(MySqlContainerFixture mysql)
        : base(mysql)
    {
    }

    [Fact]
    public async Task Five_failed_sign_ins_lock_the_account_until_it_expires_or_an_admin_unlocks_it()
    {
        var employee = await Data.EmployeeAsync(Role.Employee);
        var admin = await Api.ClientForAsync(await Data.EmployeeAsync(Role.Admin));
        var client = Api.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            await (await LoginAsync(client, employee.Email, "Wrong-password-1")).ShouldBeProblemAsync(HttpStatusCode.Unauthorized);
        }

        var locked = await LoginAsync(client, employee.Email, TestData.Password);
        var problem = await locked.ShouldBeProblemAsync(HttpStatusCode.Unauthorized);
        problem.GetProperty("detail").GetString().Should().Be("Invalid email or password.", "a lockout does not reveal that the account exists");

        Clock.Advance(TimeSpan.FromMinutes(16));
        await (await LoginAsync(client, employee.Email, TestData.Password)).ShouldHaveStatusAsync(HttpStatusCode.OK);

        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(client, employee.Email, "Wrong-password-1");
        }

        await (await admin.PostAsync(new Uri($"/api/v1/admin/employees/{employee.Id}/unlock", UriKind.Relative), null)).ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        await (await LoginAsync(client, employee.Email, TestData.Password)).ShouldHaveStatusAsync(HttpStatusCode.OK);

        var actions = await FreshDb().AuditEvents.Where(a => a.EntityType == "Employee" && a.EntityId == employee.Id.ToString()).OrderBy(a => a.Sequence).Select(a => a.Action).ToListAsync();
        actions.Should().ContainInOrder("LoginFailed", "LockedOut", "LoginRejected", "LoggedIn", "LockedOut", "Unlocked", "LoggedIn");
    }

    [Fact]
    public async Task Refresh_rotates_the_cookie_and_a_replayed_token_ends_the_whole_session_family()
    {
        var employee = await Data.EmployeeAsync(Role.Employee);
        var client = Api.CreateClient();
        var login = await LoginAsync(client, employee.Email, TestData.Password);
        await login.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var first = RefreshCookie(login);
        first.ToLowerInvariant().Should().Contain("httponly").And.Contain("samesite=strict").And.Contain("path=/api/v1/auth");

        var refreshed = await client.PostAsync(new Uri("/api/v1/auth/refresh", UriKind.Relative), null);
        var body = await refreshed.ReadAsync<LoginResponse>();
        body.AccessToken.Should().NotBeNullOrEmpty();
        var second = RefreshCookie(refreshed);
        second.Should().NotBe(first);

        // An attacker replays the first (already rotated) token: every token in the family is revoked.
        var attacker = Api.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var replay = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        replay.Headers.Add("Cookie", first.Split(';')[0]);
        await (await attacker.SendAsync(replay)).ShouldBeProblemAsync(HttpStatusCode.Unauthorized);
        await (await client.PostAsync(new Uri("/api/v1/auth/refresh", UriKind.Relative), null)).ShouldBeProblemAsync(HttpStatusCode.Unauthorized);

        var db = FreshDb();
        (await db.RefreshTokens.Where(t => t.EmployeeId == employee.Id).AllAsync(t => t.RevokedAtUtc != null)).Should().BeTrue();
        (await db.AuditEvents.CountAsync(a => a.Action == "RefreshTokenReuse" && a.EntityId == employee.Id.ToString())).Should().Be(1);
        (await db.RefreshTokens.AnyAsync(t => t.TokenHash.Length != 64)).Should().BeFalse("only hashes are stored");
    }

    [Fact]
    public async Task Refresh_fails_after_logout_expiry_deactivation_or_password_reset()
    {
        var employee = await Data.EmployeeAsync(Role.Employee);
        var admin = await Api.ClientForAsync(await Data.EmployeeAsync(Role.Admin));

        var a = Api.CreateClient();
        await LoginAsync(a, employee.Email, TestData.Password);
        var logout = await a.PostAsync(new Uri("/api/v1/auth/logout", UriKind.Relative), null);
        await logout.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        await (await a.PostAsync(new Uri("/api/v1/auth/refresh", UriKind.Relative), null)).ShouldBeProblemAsync(HttpStatusCode.Unauthorized);
        await (await Api.CreateClient().PostAsync(new Uri("/api/v1/auth/refresh", UriKind.Relative), null)).ShouldBeProblemAsync(HttpStatusCode.Unauthorized);

        var b = Api.CreateClient();
        await LoginAsync(b, employee.Email, TestData.Password);
        Clock.Advance(TimeSpan.FromDays(8));
        await (await b.PostAsync(new Uri("/api/v1/auth/refresh", UriKind.Relative), null)).ShouldBeProblemAsync(HttpStatusCode.Unauthorized);

        var c = Api.CreateClient();
        await LoginAsync(c, employee.Email, TestData.Password);
        await (await admin.PostJsonAsync($"/api/v1/admin/employees/{employee.Id}/password", new { password = "Another-Horse-42" })).ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        await (await c.PostAsync(new Uri("/api/v1/auth/refresh", UriKind.Relative), null)).ShouldBeProblemAsync(HttpStatusCode.Unauthorized);
        (await FreshDb().RefreshTokens.Where(t => t.EmployeeId == employee.Id).Select(t => t.RevokedReason).ToListAsync())
            .Should().BeEquivalentTo(["signed out", "expired", "password reset"]);
    }

    [Fact]
    public async Task Responses_carry_security_headers_and_a_correlation_id()
    {
        var employee = await Data.EmployeeAsync(Role.Employee);
        var client = await Api.ClientForAsync(employee);

        var ok = await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));
        ok.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        ok.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
        ok.Headers.GetValues("Referrer-Policy").Should().Equal("no-referrer");
        ok.Headers.GetValues("Content-Security-Policy").Single().Should().StartWith("default-src 'none'");
        ok.Headers.CacheControl!.NoStore.Should().BeTrue();
        ok.Headers.Contains("Server").Should().BeFalse();
        ok.Headers.GetValues("X-Correlation-ID").Single().Should().MatchRegex("^[0-9a-f]{32}$");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/deductions/999999");
        request.Headers.Add("X-Correlation-ID", "client-trace-0001");
        var missing = await client.SendAsync(request);
        missing.Headers.GetValues("X-Correlation-ID").Should().Equal("client-trace-0001");
        (await missing.ShouldBeProblemAsync(HttpStatusCode.NotFound)).GetProperty("correlationId").GetString().Should().Be("client-trace-0001");

        var unsafeId = new HttpRequestMessage(HttpMethod.Get, "/health");
        unsafeId.Headers.TryAddWithoutValidation("X-Correlation-ID", "bad id\r\nInjected: 1");
        (await Api.CreateClient().SendAsync(unsafeId)).Headers.GetValues("X-Correlation-ID").Single().Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    public async Task Ids_belonging_to_someone_else_are_refused()
    {
        var s = await ReworkScenario.CreateAsync(Data, Api, Clock);
        var (bugId, logId) = await s.DeductibleBugAsync(hours: 2m);
        var priyaLine = (await ReworkScenario.LinesAsync(s.Manager, $"&bugId={bugId}&employeeId={s.Priya.Id}")).Single();
        await s.NotifyAsync(priyaLine.Id);
        var outsider = await Api.ClientForAsync(await Data.EmployeeAsync(Role.Employee));
        var priyaNotification = await FreshDb().Notifications.Where(n => n.RecipientEmployeeId == s.Priya.Id).Select(n => n.Id).FirstAsync();

        await (await s.HanaClient.GetAsync(new Uri($"/api/v1/deductions/{priyaLine.Id}", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        await (await outsider.GetAsync(new Uri($"/api/v1/deductions/{priyaLine.Id}", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        await (await outsider.PostJsonAsync($"/api/v1/deductions/{priyaLine.Id}/dispute", new RaiseDisputeRequest("Not my line at all, testing"))).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        await (await outsider.PostAsync(new Uri($"/api/v1/deductions/{priyaLine.Id}/acknowledge", UriKind.Relative), null)).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        (await ReworkScenario.LinesAsync(outsider)).Should().BeEmpty();
        (await ReworkScenario.LinesAsync(s.PriyaClient)).Should().OnlyContain(l => l.EmployeeId == s.Priya.Id);

        var markOther = await outsider.PostAsync(new Uri($"/api/v1/notifications/{priyaNotification}/read", UriKind.Relative), null);
        markOther.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
        var inbox = await (await outsider.GetAsync(new Uri("/api/v1/notifications", UriKind.Relative))).ReadAsync<System.Text.Json.JsonElement>();
        inbox.GetProperty("items").EnumerateArray().Select(n => n.GetProperty("id").GetInt64()).Should().NotContain(priyaNotification);

        await (await outsider.PostAsync(new Uri($"/api/v1/timelogs/{logId}/approve", UriKind.Relative), null)).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        await (await s.PriyaClient.GetAsync(new Uri("/api/v1/payroll/runs", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
        await (await s.PriyaClient.GetAsync(new Uri($"/api/v1/admin/employees/{s.Hana.Id}/pay", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.Forbidden);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password), ApiFactory.Json);

    private static string RefreshCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("rg_refresh=", StringComparison.Ordinal));
}
