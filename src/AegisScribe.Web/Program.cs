var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/config", (IConfiguration configuration) => Results.Json(new
{
    gatewayUrl = configuration["services:gateway:https:0"] ?? configuration["services:gateway:http:0"]
}));

app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();
