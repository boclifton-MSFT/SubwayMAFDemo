using System.ComponentModel;
using System.Collections.Concurrent;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

const string instructions = """
You are the Subway Store Assistant, a concise and upbeat demo assistant for store teams.
Use menu_lookup before making a recommendation or answering questions about menu availability or details. Use estimate_order for any price or order total.
Only report menu items and prices returned by the tools. All catalog data is illustrative demo data, not Subway's real menu or current prices.
Do not make ingredient, nutrition, or allergen guarantees. For those questions, say the demo catalog cannot verify them and direct the customer to current official Subway information or the store team.
For recommendations, ask a brief follow-up when the customer's preferences are unclear. Keep replies customer-friendly and easy to say aloud.
""";

var endpoint = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT")
    ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
var model = Environment.GetEnvironmentVariable("FOUNDRY_MODEL")
    ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME")
    ?? "gpt-5.4-mini";

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["ASPNETCORE_URLS"] ?? "http://localhost:5080");
var app = builder.Build();

AIAgent? agent = null;
if (!string.IsNullOrWhiteSpace(endpoint))
{
    // DefaultAzureCredential uses the signed-in Azure CLI identity for local development.
    agent = new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential())
        .AsAIAgent(
            model: model,
            name: "SubwayStoreAssistant",
            instructions: instructions,
            tools:
            [
                AIFunctionFactory.Create(SubwayDemoTools.SearchMenu, name: "menu_lookup"),
                AIFunctionFactory.Create(SubwayDemoTools.EstimateOrder, name: "estimate_order")
            ]);
}

var sessions = new ConcurrentDictionary<Guid, Lazy<Task<DemoChatSession>>>();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/status", () => Results.Ok(new
{
    ready = agent is not null,
    model,
    message = agent is null
        ? "Set FOUNDRY_PROJECT_ENDPOINT to connect the assistant."
        : "The assistant is ready."
}));

app.MapPost("/api/chat", HandleChatAsync);

if (Directory.Exists(app.Environment.WebRootPath))
{
    app.MapFallbackToFile("index.html");
}

app.Logger.LogInformation(
    "Subway Store Assistant listening at {Urls}. Model: {Model}. Agent configured: {Ready}",
    builder.Configuration["ASPNETCORE_URLS"] ?? "http://localhost:5080",
    model,
    agent is not null);

await app.RunAsync();

async Task<IResult> HandleChatAsync(ChatRequest request)
{
    if (agent is null)
    {
        return Results.Problem(
            detail: "Set FOUNDRY_PROJECT_ENDPOINT to your Foundry project endpoint and restart the app.",
            title: "Agent not configured",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var message = request.Message?.Trim();
    if (string.IsNullOrWhiteSpace(message))
    {
        return Results.BadRequest(new { error = "Enter a message before sending." });
    }

    if (message.Length > 2_000)
    {
        return Results.BadRequest(new { error = "Messages must be 2,000 characters or fewer." });
    }

    if (!Guid.TryParse(request.SessionId, out var sessionId))
    {
        return Results.BadRequest(new { error = "The chat session ID is invalid. Start a new chat and try again." });
    }

    var chatSessionTask = sessions.GetOrAdd(
        sessionId,
        _ => new Lazy<Task<DemoChatSession>>(
            async () => new DemoChatSession(await agent.CreateSessionAsync()),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    var chatSession = await chatSessionTask;

    await chatSession.Gate.WaitAsync();
    try
    {
        var response = await agent.RunAsync(message, chatSession.Session);
        return Results.Ok(new { reply = response.ToString(), sessionId });
    }
    finally
    {
        chatSession.Gate.Release();
    }
}

internal sealed record ChatRequest(string? SessionId, string? Message);

internal sealed class DemoChatSession(AgentSession session)
{
    public AgentSession Session { get; } = session;
    public SemaphoreSlim Gate { get; } = new(1, 1);
}

internal static class SubwayDemoTools
{
    private sealed record MenuItem(
        string Name,
        string Category,
        decimal SixInchPrice,
        decimal FootlongPrice,
        string Tags);

    private static readonly MenuItem[] Items =
    [
        new("Turkey Breast", "Sandwich", 6.99m, 11.99m, "turkey, poultry"),
        new("Black Forest Ham", "Sandwich", 6.49m, 10.99m, "ham, pork"),
        new("Italian B.M.T.", "Sandwich", 7.49m, 12.99m, "italian, savory"),
        new("Steak & Cheese", "Sandwich", 8.49m, 14.49m, "steak, beef"),
        new("Veggie Delite", "Sandwich", 5.99m, 9.99m, "veggie, vegetable")
    ];

    [Description("Search the illustrative Subway demo menu by sandwich name or keyword. Use this for menu availability and suggestions.")]
    public static string SearchMenu(
        [Description("A menu item, ingredient-style keyword, or 'all' to show the demo catalog.")]
        string query)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"  [TOOL] menu_lookup: {query}");
        Console.ResetColor();

        var normalizedQuery = query.Trim();
        var matches = string.IsNullOrWhiteSpace(normalizedQuery)
            || normalizedQuery.Equals("all", StringComparison.OrdinalIgnoreCase)
            || query.Contains("menu", StringComparison.OrdinalIgnoreCase)
            || query.Contains("sandwich", StringComparison.OrdinalIgnoreCase)
                ? Items
                : Items.Where(item =>
                    item.Name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                    || item.Category.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                    || item.Tags.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

        if (matches.Length == 0)
        {
            return $"No demo menu items matched '{normalizedQuery}'. Do not infer availability; offer to show the full demo menu.";
        }

        return string.Join(
            Environment.NewLine,
            matches.Select(item =>
                $"{item.Name} ({item.Category}); illustrative demo prices: 6-inch {FormatPrice(item.SixInchPrice)}, footlong {FormatPrice(item.FootlongPrice)}."));
    }

    [Description("Calculate an illustrative demo subtotal for one sandwich item. Use whenever the customer asks for a price or order total.")]
    public static string EstimateOrder(
        [Description("The exact sandwich name from the demo menu.")]
        string itemName,
        [Description("Sandwich size: '6-inch' or 'footlong'.")]
        string size,
        [Description("Number of sandwiches, from 1 to 20.")]
        int quantity = 1)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"  [TOOL] estimate_order: {quantity} x {size} {itemName}");
        Console.ResetColor();

        var item = Items.FirstOrDefault(candidate =>
            candidate.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return $"Cannot estimate: '{itemName}' is not in the demo menu. Use menu_lookup first.";
        }

        if (quantity is < 1 or > 20)
        {
            return "Cannot estimate: quantity must be between 1 and 20.";
        }

        var unitPrice = size.Trim().ToLowerInvariant() switch
        {
            "6-inch" or "6 inch" or "six-inch" or "six inch" => item.SixInchPrice,
            "footlong" or "12-inch" or "12 inch" or "twelve-inch" or "twelve inch" => item.FootlongPrice,
            _ => -1m
        };

        if (unitPrice < 0)
        {
            return "Cannot estimate: size must be '6-inch' or 'footlong'.";
        }

        return $"{quantity} x {size} {item.Name} at {FormatPrice(unitPrice)} each = illustrative demo subtotal {FormatPrice(unitPrice * quantity)} before tax. These are not official Subway prices.";
    }

    private static string FormatPrice(decimal amount) =>
        amount.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
}
