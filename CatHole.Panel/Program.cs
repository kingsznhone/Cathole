// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CatHole.Core;
using CatHole.Panel.Components;
using CatHole.Panel.Components.Account;
using CatHole.Panel.Data;
using CatHole.Panel.Models;
using CatHole.Panel.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;

namespace CatHole.Panel
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateBootstrapLogger();

            var builder = WebApplication.CreateBuilder(args);
            builder.Host.UseSerilog((context, services, configuration) => configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext());

            // Ensure data directories exist for SQLite and logs
            var dataPath = builder.Configuration["DataPath"] ?? "data";
            Directory.CreateDirectory(dataPath);
            Directory.CreateDirectory(Path.Combine(dataPath, "logs"));

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();
            builder.Services.AddAntDesign();

            builder.Services.AddCascadingAuthenticationState();
            builder.Services.AddScoped<IdentityRedirectManager>();
            builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

            builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));


            var keyHex = builder.Configuration[$"{JwtSettings.SectionName}:Key"];
            if (string.IsNullOrWhiteSpace(keyHex))
            {
                keyHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                var appSettingsPath = Path.Combine(builder.Environment.ContentRootPath, "appsettings.json");
                try
                {
                    var json = await File.ReadAllTextAsync(appSettingsPath);
                    var node = JsonNode.Parse(json)!;
                    if (node[JwtSettings.SectionName] is not JsonObject jwtSection)
                    {
                        jwtSection = new JsonObject();
                        node[JwtSettings.SectionName] = jwtSection;
                    }
                    jwtSection["Key"] = keyHex;
                    await File.WriteAllTextAsync(appSettingsPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                    Log.Warning("JwtSettings:Key not configured — auto-generated and saved to appsettings.json");
                }
                catch (IOException ex)
                {
                    Log.Warning(ex, "JwtSettings:Key auto-generated but could not be persisted — restart will generate a new key");
                }
            }

            var authBuilder = builder.Services
                .AddAuthentication(options =>
                    {
                        options.DefaultScheme = IdentityConstants.ApplicationScheme;
                        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
                    })
                .AddJwtBearer(options =>
                    {
                        options.RequireHttpsMetadata = false;
                        options.SaveToken = true;
                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromHexString(keyHex!)),
                            ValidateIssuer = false,
                            ValidateAudience = false,
                            ClockSkew = TimeSpan.Zero
                        };
                    })
                .AddIdentityCookies();


            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlite(connectionString));
            builder.Services.AddDatabaseDeveloperPageExceptionFilter();

            builder.Services.AddIdentityCore<ApplicationUser>(options =>
                {
                    options.SignIn.RequireConfirmedAccount = false;
                    options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
                    options.Password.RequireNonAlphanumeric = false;
                    options.Password.RequireDigit = false;
                    options.Password.RequireLowercase = false;
                    options.Password.RequireUppercase = false;
                })
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddSignInManager()
                .AddDefaultTokenProviders();

            builder.Services.AddSingleton<CatHoleRelayManager>();
            builder.Services.AddSingleton<RelayConfigService>();
            builder.Services.AddSingleton<RelayService>();
            builder.Services.AddHostedService<RelayStartupService>();
            builder.Services.AddScoped<JwtService>();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseMigrationsEndPoint();
            }
            else
            {
                app.UseExceptionHandler("/Error");
            }

            app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseAntiforgery();

            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            // Add additional endpoints required by the Identity /Account Razor components.
            app.MapAdditionalIdentityEndpoints();

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await context.Database.MigrateAsync();

                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

                var adminUserName = config["Admin:UserName"] ?? "admin";
                var adminPassword = config["Admin:Password"];

                if (await userManager.FindByNameAsync(adminUserName) is null)
                {
                    if (string.IsNullOrEmpty(adminPassword))
                    {
                        adminPassword = RandomNumberGenerator.GetString(
                            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 16);
                        Log.Warning("Admin:Password not configured — auto-generated: {Password}", adminPassword);
                    }
                    var admin = new ApplicationUser
                    {
                        UserName = adminUserName
                    };
                    var result = await userManager.CreateAsync(admin, adminPassword);
                    if (!result.Succeeded)
                        throw new InvalidOperationException(
                            $"Failed to seed admin user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
                }
            }

            app.Run();
        }
    }
}
