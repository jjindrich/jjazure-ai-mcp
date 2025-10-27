using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.OpenApi.Models; // added
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorization();

// Add health check services
builder.Services.AddHealthChecks();

// Add OpenAPI/Swagger services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "jjwebapisec API",
        Version = "v1",
        Description = "Secure API with Azure AD authentication"
    });

    // Add server URL from configuration (fallback to local)
    var serverUrl = builder.Configuration["Api:BaseUrl"] ?? "https://appurl";
    options.AddServer(new OpenApiServer { Url = serverUrl, Description = "Primary server" });

    // OAuth2 authorization code flow against Azure AD (optional interactive auth in Swagger UI)
    options.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.OAuth2,
        Flows = new OpenApiOAuthFlows
        {
            AuthorizationCode = new OpenApiOAuthFlow
            {
                AuthorizationUrl = new Uri(builder.Configuration["AzureAd:Instance"] + builder.Configuration["AzureAd:TenantId"] + "/oauth2/v2.0/authorize"),
                TokenUrl = new Uri(builder.Configuration["AzureAd:Instance"] + builder.Configuration["AzureAd:TenantId"] + "/oauth2/v2.0/token"),
                Scopes = new Dictionary<string, string>
                {
                    { builder.Configuration["AzureAd:Scopes:0"] ?? "api://unknown/.default", "Access to API" }
                }
            }
        }
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "oauth2" }
            },
            new List<string>()
        }
    });
});

var app = builder.Build();

// Enable middleware for Swagger only in Development (adjust if needed)
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "jjwebapisec API v1");
    options.OAuthClientId(builder.Configuration["AzureAd:ClientId"]);
    // PKCE recommended for public clients
    options.OAuthUsePkce();
});

app.UseAuthentication();
app.UseAuthorization();

// Add health endpoint that doesn't require authentication
app.MapHealthChecks("/healthz").AllowAnonymous();

// Existing sample endpoints
app.MapGet("/api/values", (HttpContext httpContext) =>
{
    var user = httpContext.User;
    string username = user.FindFirst("preferred_username")?.Value ?? "Unknown";

    return new string[] { "value1", "value2", username };
})
.RequireAuthorization();

app.MapGet("/api/value/{id}", (int id, HttpContext httpContext) =>
{
    var user = httpContext.User;
    string username = user.FindFirst("preferred_username")?.Value ?? "Unknown";
    return new string[] { $"{id} for {username}" };
})
.RequireAuthorization();

// ================= Ticket Management =================
// In-memory ticket store (static for app lifetime)
var ticketStore = new TicketStore();

// Create ticket
app.MapPost("/api/tickets", (TicketCreateRequest request, HttpContext ctx) =>
{
    var email = GetUserEmail(ctx.User);
    if (string.IsNullOrWhiteSpace(email)) return Results.BadRequest("User email claim not found.");
    if (string.IsNullOrWhiteSpace(request.Description)) return Results.BadRequest("Description required.");

    var ticket = ticketStore.Create(email, request.Description, request.Priority);
    return Results.Created($"/api/tickets/{ticket.Id}", ticket);
})
.RequireAuthorization();

// Change ticket status
app.MapPatch("/api/tickets/{id:int}/status", (int id, TicketStatusUpdateRequest request, HttpContext ctx) =>
{
    if (!Enum.IsDefined(typeof(TicketStatus), request.Status)) return Results.BadRequest("Invalid status.");
    var email = GetUserEmail(ctx.User);
    if (string.IsNullOrWhiteSpace(email)) return Results.BadRequest("User email claim not found.");

    var ticket = ticketStore.Get(id);
    if (ticket is null) return Results.NotFound();
    // Optional ownership check: only submitter can change status (remove if not desired)
    if (!string.Equals(ticket.SubmittedEmail, email, StringComparison.OrdinalIgnoreCase))
        return Results.Forbid();

    var updated = ticketStore.UpdateStatus(id, request.Status);
    return Results.Ok(updated);
})
.RequireAuthorization();

// Get list of my tickets
app.MapGet("/api/my/tickets", (HttpContext ctx) =>
{
    var email = GetUserEmail(ctx.User);
    if (string.IsNullOrWhiteSpace(email)) return Results.BadRequest("User email claim not found.");
    var tickets = ticketStore.GetByEmail(email);
    return Results.Ok(tickets);
})
.RequireAuthorization();

app.Run();

// ================= Supporting Types =================
static string GetUserEmail(System.Security.Claims.ClaimsPrincipal user)
{
    return user.FindFirst("preferred_username")?.Value
        ?? user.FindFirst("email")?.Value
        ?? user.Identity?.Name
        ?? string.Empty;
}

enum TicketStatus
{
    New = 0,
    InProgress = 1,
    Closed = 2
}

record Ticket
{
    public int Id { get; init; }
    public DateTimeOffset Created { get; init; } = DateTimeOffset.UtcNow;
    public string SubmittedEmail { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Priority { get; init; } = "Normal"; // free-form or could be enum later
    public TicketStatus Status { get; init; } = TicketStatus.New;
}

record TicketCreateRequest(string Description, string Priority);
record TicketStatusUpdateRequest(TicketStatus Status);

class TicketStore
{
    private int _nextId = 1;
    private readonly ConcurrentDictionary<int, Ticket> _tickets = new();

    public Ticket Create(string email, string description, string priority)
    {
        var id = System.Threading.Interlocked.Increment(ref _nextId);
        var ticket = new Ticket
        {
            Id = id,
            SubmittedEmail = email,
            Description = description.Trim(),
            Priority = string.IsNullOrWhiteSpace(priority) ? "Normal" : priority.Trim(),
            Status = TicketStatus.New,
            Created = DateTimeOffset.UtcNow
        };
        _tickets[id] = ticket;
        return ticket;
    }

    public Ticket? Get(int id) => _tickets.TryGetValue(id, out var t) ? t : null;

    public IEnumerable<Ticket> GetByEmail(string email) => _tickets.Values
        .Where(t => string.Equals(t.SubmittedEmail, email, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(t => t.Created);

    public Ticket? UpdateStatus(int id, TicketStatus status)
    {
        if (!_tickets.TryGetValue(id, out var existing)) return null;
        var updated = existing with { Status = status };
        _tickets[id] = updated;
        return updated;
    }
}
