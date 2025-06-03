using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/values", (HttpContext httpContext) =>
{    
    var user = httpContext.User;
    string username = user.FindFirst("preferred_username")?.Value ?? "Unknown";

    return new string[] { "value1", "value2", username };
})
.RequireAuthorization();

app.Run();
