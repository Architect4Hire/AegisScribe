var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();

// The SPA only works from its https origin: that is the one origin the gateway's CORS policy names,
// and the session cookie is Secure. Served over plain http — the dashboard lists this project's http
// endpoint beside its https one — the page loads, every gateway call is refused, and a signed-in user
// is shown the signed-out landing page. Health probes are left alone; they are not a browser.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/health")
        && !context.Request.Path.StartsWithSegments("/alive"),
    branch => branch.UseHttpsRedirection());

app.MapGet("/config", (IConfiguration configuration) => Results.Json(new
{
    gatewayUrl = configuration["services:gateway:https:0"] ?? configuration["services:gateway:http:0"]
}));

// index.html names this build's content-hashed bundles, so it must revalidate on every load or a
// browser keeps running the previous build after a deploy. The hashed files can cache freely.
var staticFileOptions = new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (string.Equals(context.File.Name, "index.html", StringComparison.OrdinalIgnoreCase))
        {
            context.Context.Response.Headers.CacheControl = "no-cache";
        }
    },
};

app.UseStaticFiles(staticFileOptions);
app.MapFallbackToFile("index.html", staticFileOptions);

app.Run();
