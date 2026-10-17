using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ReworkGuard.Application.Auth;
using ReworkGuard.Application.Common;
using ReworkGuard.Application.Employees;
using ReworkGuard.Domain.Enums;
using ReworkGuard.Infrastructure.Persistence;
using ReworkGuard.Tests.Support;

namespace ReworkGuard.Tests.Integration;

[Collection(MySqlTestGroup.Name)]
public sealed class AuthApiTests : IAsyncLifetime
{
    private readonly MySqlContainerFixture _mysql;
    private ApplicationDbContext _db = null!;
    private ApiFactory _api = null!;
    private TestData _data = null!;

    public AuthApiTests(MySqlContainerFixture mysql)
    {
        _mysql = mysql;
    }

    public async Task InitializeAsync()
    {
        var connectionString = await _mysql.CreateMigratedDatabaseAsync();
        _db = MySqlContainerFixture.CreateContext(connectionString);
        _data = new TestData(_db);
        _api = new ApiFactory(connectionString, new TestClock { UtcNow = DateTime.UtcNow });
    }

    public async Task DisposeAsync()
    {
        await _api.DisposeAsync();
        await MySqlContainerFixture.DropDatabaseAsync(_db);
    }

    [Fact]
    public async Task Login_with_valid_credentials_returns_a_token_and_the_employee()
    {
        var manager = await _data.EmployeeAsync(Role.Manager);
        var client = _api.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(manager.Email, TestData.Password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(ApiFactory.Json);
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
        body.Employee.Role.Should().Be(Role.Manager);
    }

    [Theory]
    [InlineData("wrong-password")]
    [InlineData("")]
    public async Task Login_with_a_bad_password_is_rejected(string password)
    {
        var employee = await _data.EmployeeAsync(Role.Employee);
        var client = _api.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(employee.Email, password));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_for_an_unknown_email_gives_the_same_401_as_a_wrong_password()
    {
        var response = await _api.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("nobody@test.local", "whatever-123"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Invalid email or password");
    }

    [Fact]
    public async Task Passwords_are_stored_hashed()
    {
        var employee = await _data.EmployeeAsync(Role.Employee);

        var stored = await _db.Employees.AsNoTracking().Where(e => e.Id == employee.Id).Select(e => e.PasswordHash).SingleAsync();

        stored.Should().NotContain(TestData.Password);
        stored.Length.Should().BeGreaterThan(40);
    }

    [Theory]
    [InlineData("/api/v1/auth/me")]
    [InlineData("/api/v1/employees")]
    [InlineData("/api/v1/audit-events")]
    public async Task Endpoints_require_a_token(string url)
    {
        var response = await _api.CreateClient().GetAsync(new Uri(url, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_returns_the_signed_in_employee()
    {
        var qa = await _data.EmployeeAsync(Role.QA);
        var client = await _api.ClientForAsync(qa);

        var me = await client.GetFromJsonAsync<EmployeeDto>("/api/v1/auth/me", ApiFactory.Json);

        me!.Id.Should().Be(qa.Id);
        me.Role.Should().Be(Role.QA);
    }

    [Theory]
    [InlineData(Role.Employee, "/api/v1/employees", HttpStatusCode.Forbidden)]
    [InlineData(Role.QA, "/api/v1/employees", HttpStatusCode.Forbidden)]
    [InlineData(Role.Finance, "/api/v1/employees", HttpStatusCode.OK)]
    [InlineData(Role.Employee, "/api/v1/audit-events", HttpStatusCode.Forbidden)]
    [InlineData(Role.Manager, "/api/v1/audit-events", HttpStatusCode.OK)]
    public async Task Role_restrictions_are_enforced(Role role, string url, HttpStatusCode expected)
    {
        var user = await _data.EmployeeAsync(role);
        var client = await _api.ClientForAsync(user);

        var response = await client.GetAsync(new Uri(url, UriKind.Relative));

        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task List_endpoints_are_paginated_and_reject_oversized_pages()
    {
        var lead = await _data.EmployeeAsync(Role.TeamLead);
        await _data.EmployeeAsync(Role.Employee);
        await _data.EmployeeAsync(Role.Employee);
        var client = await _api.ClientForAsync(lead);

        var page = await client.GetFromJsonAsync<PagedResult<EmployeeDto>>("/api/v1/employees?page=1&pageSize=2", ApiFactory.Json);
        var tooBig = await client.GetAsync(new Uri("/api/v1/employees?pageSize=1000", UriKind.Relative));

        page!.Items.Should().HaveCount(2);
        page.TotalCount.Should().Be(3);
        tooBig.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
