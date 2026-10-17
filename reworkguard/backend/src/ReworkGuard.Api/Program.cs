using ReworkGuard.Api;
using ReworkGuard.Api.DevTools;
using ReworkGuard.Application;
using ReworkGuard.Infrastructure;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
builder.Services.AddSerilog((services, logger) =>
{
    logger.ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "ReworkGuard.Api");
    if (builder.Environment.IsDevelopment())
    {
        logger.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {SourceContext}: {Message:lj}{NewLine}{Exception}");
    }
    else
    {
        // One JSON object per line for CloudWatch; message templates keep values as fields.
        logger.WriteTo.Console(new RenderedCompactJsonFormatter());
    }
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApi(builder.Configuration);
builder.Services.AddDevTools(builder.Environment, builder.Configuration);

var app = builder.Build();

await app.PrepareDatabaseAsync();
app.UseApi();
app.MapDevTools();

await app.RunAsync();

/// <summary>Entry point; public so integration tests can host the API.</summary>
public partial class Program
{
}
