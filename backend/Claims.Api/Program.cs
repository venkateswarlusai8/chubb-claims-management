using System.Text.Json.Serialization;
using Claims.Api.Infrastructure.Auth;
using Claims.Api.Infrastructure.Messaging;
using Claims.Api.Infrastructure.Persistence;
using Claims.Api.Infrastructure.Web;
using Claims.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHealthChecks();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "Claims API", Version = "v1" });
    o.SupportNonNullableReferenceTypes();
    o.AddSecurityDefinition("DemoUser", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = DemoHeaderAuthenticationHandler.HeaderName,
        Description = "Demo identity: claimant-asha, claimant-daniel, claimant-mei, officer-priya, officer-james"
    });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "DemoUser" } }] = []
    });
});

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:4200"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddSingleton(TimeProvider.System);
// Resolved lazily so tests can override the connection string via configuration.
builder.Services.AddDbContext<ClaimsDbContext>((sp, o) =>
    o.UseSqlite(sp.GetRequiredService<IConfiguration>().GetConnectionString("ClaimsDb") ?? "Data Source=claims.db"));

builder.Services.AddDemoAuthentication();
builder.Services.AddClaimsMessaging(builder.Configuration);
builder.Services.AddScoped<IClaimEventHandler, NotificationEventHandler>();

builder.Services.Configure<DashboardOptions>(builder.Configuration.GetSection("Dashboard"));
builder.Services.Configure<FxOptions>(builder.Configuration.GetSection("Fx"));
builder.Services.AddScoped<ClaimService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<NotificationService>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

await app.InitialiseDatabaseAsync();
app.Run();

public partial class Program { }
