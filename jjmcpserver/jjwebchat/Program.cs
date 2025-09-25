using Azure.AI.OpenAI;
using jjwebchat.Components;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI;
using System.ClientModel;

var builder = WebApplication.CreateBuilder(args);

// load configuration from appsettings.json
var config = builder.Configuration;
var connectionstring = config.GetConnectionString("openai") ?? throw new InvalidOperationException("Missing connection string: openai.");
var endpoint = connectionstring.Split(';').FirstOrDefault(x => x.StartsWith("Endpoint=", StringComparison.InvariantCultureIgnoreCase))?.Split('=')[1]
                   ?? throw new InvalidOperationException("Missing endpoint.");
var apiKey = connectionstring.Split(';').FirstOrDefault(x => x.StartsWith("Key=", StringComparison.InvariantCultureIgnoreCase))?.Split('=')[1]
                 ?? throw new InvalidOperationException("Missing API key.");

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// create OpenAI client
var credential = new ApiKeyCredential(apiKey);
var openAIOptions = new OpenAIClientOptions()
{
    Endpoint = new Uri(endpoint),
};
var openAIClient = new AzureOpenAIClient(new Uri(endpoint), credential);
var chatClient = openAIClient.GetChatClient(config["OpenAI:DeploymentName"]).AsIChatClient();
builder.Services.AddChatClient(chatClient)
                .UseFunctionInvocation()
                .UseLogging();

// add MCP client
builder.Services.AddSingleton<IMcpClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

    var uri = new Uri(config["McpServers:JJMcpServer"]!);

    var clientTransportOptions = new SseClientTransportOptions()
    {
        Endpoint = new Uri($"{uri.AbsoluteUri.TrimEnd('/')}/mcp"),
    };
    var clientTransport = new SseClientTransport(clientTransportOptions, loggerFactory);
    var clientOptions = new McpClientOptions()
    {
        ClientInfo = new Implementation()
        {
            Name = "JJ Web Chat",
            Version = "1.0.0",
        }
    };
    return McpClientFactory.CreateAsync(clientTransport, clientOptions, loggerFactory).GetAwaiter().GetResult();
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
