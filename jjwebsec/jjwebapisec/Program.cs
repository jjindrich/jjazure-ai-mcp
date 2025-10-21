using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.OpenApi.Models; // added

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

app.Run();
