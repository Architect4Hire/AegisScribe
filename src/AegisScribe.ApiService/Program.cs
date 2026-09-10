using AegisScribe.ApiService.Auth;
using AegisScribe.ApiService.Data;
using AegisScribe.ApiService.Managers.Models.Identity;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddSqlServerDbContext<AegisScribeDbContext>("aegisscribedb");

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDataProtection();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AegisScribeDbContext>()
    .AddDefaultTokenProviders();

// Placeholder scheme: no issuer validates a token yet, so it only ever fails to authenticate —
// which is exactly what lets [Authorize]/RequireAuthorization() return a bare 401 for now instead of
// throwing for want of a default challenge scheme. Swapped for OpenIddict's
// AddValidation(o => o.UseLocalServer()) in 1B.4/1B.5 — see auth.md's "do not use AddJwtBearer" note,
// which is about validating OpenIddict's encrypted tokens, not about this placeholder.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthPolicies.PlatformAdmin, policy => policy.RequireRole(AuthPolicies.PlatformAdmin));

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await RoleSeeder.SeedPlatformAdminRoleAsync(scope.ServiceProvider);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapAuthEndpoints();

app.MapDefaultEndpoints();

app.Run();
