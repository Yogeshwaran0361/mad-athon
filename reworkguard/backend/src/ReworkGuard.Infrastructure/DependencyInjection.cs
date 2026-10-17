using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ReworkGuard.Application.Abstractions;
using Amazon;
using Amazon.SimpleEmailV2;
using ReworkGuard.Application.Notifications;
using ReworkGuard.Application.Auth;
using ReworkGuard.Infrastructure.Pdf;
using ReworkGuard.Application.Reports;
using ReworkGuard.Infrastructure.Auth;
using ReworkGuard.Infrastructure.Email;
using ReworkGuard.Infrastructure.Jobs;
using ReworkGuard.Infrastructure.Persistence;
using ReworkGuard.Infrastructure.Persistence.Seeding;
using ReworkGuard.Infrastructure.Reports;
using ReworkGuard.Infrastructure.Services;

namespace ReworkGuard.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} is not configured. Set it with user-secrets or the ConnectionStrings__{ConnectionStringName} environment variable.");
        }

        services.AddDbContext<ApplicationDbContext>(options =>
            ((DbContextOptionsBuilder<ApplicationDbContext>)options).UseReworkGuardMySql(connectionString));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddScoped<DbSeeder>();
        services.AddSingleton<IReportRenderer, XlsxReportRenderer>();
        services.AddSingleton<IReportRenderer, CsvReportRenderer>();
        services.AddSingleton<IReportRenderer, PdfReportRenderer>();

        services.AddHttpContextAccessor();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IAuditChainVerifier, AuditChainVerifier>();
        services.AddSingleton<IPayslipPdfRenderer, QuestPayslipRenderer>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        services.AddOptions<NotificationOptions>().Bind(configuration.GetSection(NotificationOptions.SectionName));
        services.AddOptions<ReworkGuard.Application.Deductions.DeductionOptions>().Bind(configuration.GetSection(ReworkGuard.Application.Deductions.DeductionOptions.SectionName));
        AddEmail(services, configuration);
        services.AddJobs(configuration, connectionString);

        services.AddOptions<AuthOptions>().Bind(configuration.GetSection(AuthOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                bearer.MapInboundClaims = false;
                // Browsers cannot set headers on WebSocket requests, so the SignalR hub takes the token from the query string.
                bearer.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var token = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    },
                };
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Value.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Value.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = ClaimNames.Name,
                    RoleClaimType = ClaimNames.Role,
                };
            });

        return services;
    }

    private static void AddEmail(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(EmailOptions.SectionName);
        services.AddOptions<EmailOptions>().Bind(section).ValidateDataAnnotations().ValidateOnStart();
        var provider = section.GetValue<EmailProvider?>(nameof(EmailOptions.Provider)) ?? EmailProvider.Log;
        switch (provider)
        {
            case EmailProvider.Smtp:
                services.AddScoped<IEmailSender, SmtpEmailSender>();
                break;
            case EmailProvider.Ses:
                services.AddSingleton<IAmazonSimpleEmailServiceV2>(sp =>
                {
                    var region = sp.GetRequiredService<IOptions<EmailOptions>>().Value.Ses.Region;
                    return string.IsNullOrWhiteSpace(region)
                        ? new AmazonSimpleEmailServiceV2Client()
                        : new AmazonSimpleEmailServiceV2Client(RegionEndpoint.GetBySystemName(region));
                });
                services.AddScoped<IEmailSender, SesEmailSender>();
                break;
            default:
                services.AddScoped<IEmailSender, LogEmailSender>();
                break;
        }
    }
}
