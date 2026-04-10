// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using CatFlapRelay;
using CatFlapRelay.Panel.Components;
using CatFlapRelay.Panel.Components.Account;
using CatFlapRelay.Panel.Data;
using CatFlapRelay.Panel.Models;
using CatFlapRelay.Panel.OpenApiTransformer;
using CatFlapRelay.Panel.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;

namespace CatFlapRelay.Panel
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            ConfigureBootstrapLogger();

            try
            {
                var builder = WebApplication.CreateBuilder(args);

                EnsureDataDirectories(builder);
                ConfigureKestrel(builder);
                ConfigureSerilog(builder);
                ConfigureBlazorAndUiServices(builder);
                ConfigureServices(builder);

                ConfigureAuthentication(builder);
                ConfigureApiVersioning(builder.Services);
                ConfigureControllers(builder);
                ConfigureOpenApi(builder.Services);
                ConfigureHealthChecks(builder.Services);
                ConfigureCors(builder);

                var app = builder.Build();

                ConfigureMiddleware(app);
                ConfigureStartupLogging(app);
                await InitializeDatabaseAsync(app);

                app.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        /// <summary>
        /// Configures the bootstrap logger used before the host is built.
        /// </summary>
        private static void ConfigureBootstrapLogger()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateBootstrapLogger();
        }

        /// <summary>
        /// Tries to create the data directory required for SQLite.
        /// If the directory cannot be created (read-only filesystem, missing volume mount, etc.),
        /// the app falls back to an in-memory SQLite database and logs a warning.
        /// The log subdirectory is created on demand by Serilog's file sink.
        /// </summary>
        private static void EnsureDataDirectories(WebApplicationBuilder builder)
        {
            var dataPath = builder.Configuration["DataPath"] ?? "data";
            try
            {
                Directory.CreateDirectory(dataPath);
            }
            catch (Exception ex)
            {
                const string inMemoryCs = "Data Source=CatFlapEphemeral;Mode=Memory;Cache=Shared";
                Log.Warning("==========================================================");
                Log.Warning(ex,
                    "Data directory '{DataPath}' is not accessible — falling back to IN-MEMORY SQLite.",
                    dataPath);
                Log.Warning("⚠️  ALL DATA (relay configs, users) WILL BE LOST ON RESTART.");
                Log.Warning("⚠️  Mount a persistent volume at '{DataPath}' to persist data.", dataPath);
                Log.Warning("==========================================================");
                builder.Configuration["ConnectionStrings:DefaultConnection"] = inMemoryCs;
                builder.Services.AddSingleton(_ =>
                {
                    var conn = new SqliteConnection(inMemoryCs);
                    conn.Open();
                    return conn;
                });
            }
        }

        /// <summary>
        /// Configures Kestrel server options. Endpoint binding is intentionally left to
        /// environment variables (ASPNETCORE_HTTP_PORTS / ASPNETCORE_URLS) so that the
        /// same image runs on any port without rebuilding.
        /// </summary>
        private static void ConfigureKestrel(WebApplicationBuilder builder)
        {
            builder.WebHost.ConfigureKestrel((context, options) =>
            {
                // Don't advertise the server version in the "Server" response header.
                options.AddServerHeader = false;

                // Apply limit overrides from configuration (Kestrel:Limits section).
                // Values can be overridden per-environment without touching code.
                options.Configure(context.Configuration.GetSection("Kestrel"));
            });
        }

        /// <summary>
        /// Configures Serilog from host configuration and DI services.
        /// </summary>
        private static void ConfigureSerilog(WebApplicationBuilder builder)
        {
            builder.Host.UseSerilog((context, services, configuration) => configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext());
        }

        /// <summary>
        /// Configures Blazor, AntDesign UI, and cascading authentication state services.
        /// </summary>
        private static void ConfigureBlazorAndUiServices(WebApplicationBuilder builder)
        {
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();
            builder.Services.AddAntDesign();

            builder.Services.AddCascadingAuthenticationState();
            builder.Services.AddScoped<IdentityRedirectManager>();
            builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
        }

        /// <summary>
        /// Configures database, identity, and relay application services.
        /// </summary>
        private static void ConfigureServices(WebApplicationBuilder builder)
        {
            builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
            builder.Services.Configure<PanelSettings>(builder.Configuration.GetSection(PanelSettings.SectionName));

            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
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

            builder.Services.AddSingleton<FlapRelayManager>();
            builder.Services.AddSingleton<RelayConfigService>();
            builder.Services.AddSingleton<RelayService>();
            builder.Services.AddHostedService<RelayStartupService>();
            builder.Services.AddScoped<JwtService>();
        }

        /// <summary>
        /// Configures JWT bearer and Identity cookie authentication.
        /// Uses the configured JwtSettings:Key, or generates an ephemeral in-memory key if absent.
        /// The ephemeral key is not persisted and all tokens will be invalidated on process restart.
        /// </summary>
        private static void ConfigureAuthentication(WebApplicationBuilder builder)
        {
            var keyHex = builder.Configuration[$"{JwtSettings.SectionName}:Key"];
            if (string.IsNullOrWhiteSpace(keyHex))
            {
                keyHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                Log.Warning("JwtSettings:Key not configured — using an ephemeral in-memory key. All tokens will be invalidated on restart. Set JwtSettings:Key in appsettings.json to persist tokens across restarts.");
            }

            // Write the resolved key (configured or ephemeral) back so that
            // IOptions<JwtSettings> / IOptionsSnapshot<JwtSettings> sees the same
            // value that the JWT Bearer handler uses.
            builder.Configuration[$"{JwtSettings.SectionName}:Key"] = keyHex;

            builder.Services
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
                        IssuerSigningKey = new SymmetricSecurityKey(Convert.FromHexString(keyHex)),
                        ValidateIssuer = false,
                        ValidateAudience = false,
                        ClockSkew = TimeSpan.Zero
                    };
                })
                .AddIdentityCookies(cookies =>
                {
                    // For API routes the cookie scheme must not redirect to the login page —
                    // callers expect a 401/403 status code, not a 302 that resolves to Blazor HTML.
                    cookies.ApplicationCookie?.Configure(o =>
                    {
                        o.Events.OnRedirectToLogin = ctx =>
                        {
                            if (ctx.Request.Path.StartsWithSegments("/api"))
                                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            else
                                ctx.Response.Redirect(ctx.RedirectUri);
                            return Task.CompletedTask;
                        };
                        o.Events.OnRedirectToAccessDenied = ctx =>
                        {
                            if (ctx.Request.Path.StartsWithSegments("/api"))
                                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                            else
                                ctx.Response.Redirect(ctx.RedirectUri);
                            return Task.CompletedTask;
                        };
                    });
                });
        }

        /// <summary>
        /// Configures MVC controllers and JSON serialization options.
        /// </summary>
        private static void ConfigureControllers(WebApplicationBuilder builder)
        {
            builder.Services.AddControllers()
                .ConfigureApplicationPartManager(options =>
                {
                })
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.WriteIndented = false;
                    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
                    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
                    options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
                    options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
                    options.JsonSerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
                });
        }

        private static void ConfigureApiVersioning(IServiceCollection services)
        {
            services.AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new ApiVersion(1, 0);
                options.AssumeDefaultVersionWhenUnspecified = true;
                options.ReportApiVersions = true;
                options.ApiVersionReader = ApiVersionReader.Combine(
                    new UrlSegmentApiVersionReader(),
                    new HeaderApiVersionReader("X-Api-Version"),
                    new QueryStringApiVersionReader("api-version")
                );
            })
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'V";
                options.SubstituteApiVersionInUrl = true;
            });
        }

        /// <summary>
        /// Configures OpenAPI document generation and transformers.
        /// </summary>
        private static void ConfigureOpenApi(IServiceCollection services)
        {
            services.AddEndpointsApiExplorer();

            var apiVersions = new[] { "v1" };
            foreach (var version in apiVersions)
            {
                services.AddOpenApi(version, options =>
                {
                    options.AddSchemaTransformer<GenericTypeSchemaTransformer>();
                    options.AddDocumentTransformer<BearerSecurityDocumentTransformer>();
                    options.AddDocumentTransformer<SchemaReferenceDocumentTransformer>();
                    options.AddDocumentTransformer<ApiVersionDocumentTransformer>();
                    options.AddDocumentTransformer<RemoveServersDocumentTransformer>();
                    options.AddOperationTransformer<AuthorizeOperationTransformer>();
                    options.AddOperationTransformer<FileUploadOperationTransformer>();
                });
            }
        }

        /// <summary>
        /// Configures ASP.NET Core health checks.
        /// </summary>
        private static void ConfigureHealthChecks(IServiceCollection services)
        {
            services.AddHealthChecks();
        }

        /// <summary>
        /// Configures CORS for the API controllers.
        /// AllowCredentials is intentionally omitted: the API uses JWT bearer tokens
        /// (explicit Authorization header), not cookies, so credentials CORS is not needed
        /// and would create a cross-site request vulnerability with the Identity cookie.
        /// Allowed origins can be restricted via Cors:AllowedOrigins in configuration.
        /// </summary>
        private static void ConfigureCors(WebApplicationBuilder builder)
        {
            var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();

            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    if (allowedOrigins is { Length: > 0 })
                        policy.WithOrigins(allowedOrigins);
                    else
                        Log.Warning("Cors:AllowedOrigins is empty — CORS requests will be rejected. "
                            + "Set Cors:AllowedOrigins in appsettings.json to allow cross-origin access.");

                    policy.AllowAnyMethod()
                          .AllowAnyHeader()
                          .SetPreflightMaxAge(TimeSpan.FromHours(2));
                });
            });
        }

        /// <summary>
        /// Configures the HTTP request pipeline, middleware, and endpoint mappings.
        /// </summary>
        private static void ConfigureMiddleware(WebApplication app)
        {
            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
                KnownProxies = { },
                KnownIPNetworks =
                {
                    new System.Net.IPNetwork(IPAddress.Loopback, 8),
                    new System.Net.IPNetwork(IPAddress.Parse("10.0.0.0"), 8),
                    new System.Net.IPNetwork(IPAddress.Parse("172.16.0.0"), 12),
                    new System.Net.IPNetwork(IPAddress.Parse("192.168.0.0"), 16),
                    new System.Net.IPNetwork(IPAddress.Parse("fc00::"), 7),
                    new System.Net.IPNetwork(IPAddress.IPv6Loopback, 128)
                }
            });

            app.UseSerilogRequestLogging();

            if (app.Environment.IsDevelopment())
            {
                app.UseMigrationsEndPoint();
            }
            else
            {
                app.UseExceptionHandler("/Error");
            }

            app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
            {
                ResponseWriter = async (context, report) =>
                {
                    var result = new
                    {
                        status = report.Status.ToString(),
                        checks = report.Entries.Select(entry => new
                        {
                            name = entry.Key,
                            status = entry.Value.Status.ToString(),
                            description = entry.Value.Description,
                            duration = entry.Value.Duration
                        }),
                        totalDuration = report.TotalDuration
                    };

                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(result);
                }
            });

            var versionProvider = app.Services.GetRequiredService<Asp.Versioning.ApiExplorer.IApiVersionDescriptionProvider>();
            foreach (var description in versionProvider.ApiVersionDescriptions)
            {
                app.MapOpenApi($"openapi/{description.GroupName}.json");
            }
            app.UseSwaggerUI(options =>
            {
                foreach (var description in versionProvider.ApiVersionDescriptions)
                {
                    options.SwaggerEndpoint(
                        $"/openapi/{description.GroupName}.json",
                        $"CatFlap API {description.GroupName.ToUpperInvariant()}"
                    );
                }
                options.DisplayRequestDuration();
                options.ShowExtensions();
                options.EnablePersistAuthorization();
                options.EnableFilter();
                options.RoutePrefix = "swagger";
            });

            // Only apply the Blazor error page re-execution for non-API routes.
            // API routes must return their own status codes as-is (JSON, not HTML).
            app.UseWhen(
                ctx => !ctx.Request.Path.StartsWithSegments("/api"),
                branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
            app.UseCors();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseAntiforgery();

            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.MapControllers();
            app.MapAdditionalIdentityEndpoints().ExcludeFromDescription();
        }

        /// <summary>
        /// Registers an ApplicationStarted callback that logs the listening addresses and key endpoints.
        /// </summary>
        private static void ConfigureStartupLogging(WebApplication app)
        {
            app.Lifetime.ApplicationStarted.Register(() =>
            {
                var logger = app.Services.GetRequiredService<ILogger<Program>>();
                var server = app.Services.GetRequiredService<IServer>();
                var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
                var baseAddress = addresses?.FirstOrDefault() ?? "http://localhost";

                var serviceName = "CatFlap Panel";
                var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
                var debugEnabled = app.Environment.IsDevelopment();

                logger.LogInformation("==============================================");
                logger.LogInformation("🚀 {ServiceName} v{Version} | {Environment}", serviceName, version, app.Environment.EnvironmentName);
                logger.LogInformation("==============================================");

                if (addresses?.Any() == true)
                    logger.LogInformation("🌐 Listening: {Address}", string.Join(", ", addresses));

                logger.LogInformation("📍 Endpoints:");
                logger.LogInformation("   Swagger: {Address}/swagger", baseAddress);
                logger.LogInformation("   Health:  {Address}/health", baseAddress);
                logger.LogInformation("==============================================");
            });
        }

        /// <summary>
        /// Applies pending database migrations and seeds the initial admin user.
        /// </summary>
        private static async Task InitializeDatabaseAsync(WebApplication app)
        {
            await using var scope = app.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var connectionString = config.GetConnectionString("DefaultConnection") ?? "";
            if (connectionString.Contains("Mode=Memory", StringComparison.OrdinalIgnoreCase))
                await context.Database.EnsureCreatedAsync();
            else
                await context.Database.MigrateAsync();

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var adminUserName = config["Admin:UserName"] ?? "admin";
            var adminPassword = config["Admin:Password"];
            var resetAdmin = string.Equals(
                Environment.GetEnvironmentVariable("CATFLAP_RESET_ADMIN"), "true", StringComparison.OrdinalIgnoreCase);

            var existingAdmin = await userManager.FindByNameAsync(adminUserName);

            if (resetAdmin && existingAdmin is not null)
            {
                var newPassword = adminPassword;
                if (string.IsNullOrEmpty(newPassword))
                {
                    newPassword = RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 16);
                    Log.Warning("Auto-generated new admin password: {Password}", newPassword);
                }
                var resetToken = await userManager.GeneratePasswordResetTokenAsync(existingAdmin);
                var resetResult = await userManager.ResetPasswordAsync(existingAdmin, resetToken, newPassword);
                if (resetResult.Succeeded)
                    Log.Warning("Admin password has been reset for user '{UserName}'. Remove CATFLAP_RESET_ADMIN env var to prevent resetting on every restart.", adminUserName);
                else
                    Log.Error("Failed to reset admin password: {Errors}", string.Join(", ", resetResult.Errors.Select(e => e.Description)));
            }
            else if (existingAdmin is null)
            {
                if (string.IsNullOrEmpty(adminPassword))
                {
                    adminPassword = RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 16);
                    Log.Warning("Admin & Password not configured");
                    Log.Warning("Using default username: {admin}","admin");
                    Log.Warning("Auto-generated Password: {Password}", adminPassword);
                }
                var admin = new ApplicationUser { UserName = adminUserName };
                var result = await userManager.CreateAsync(admin, adminPassword);
                if (!result.Succeeded)
                    throw new InvalidOperationException(
                        $"Failed to seed admin user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }
        }
    }
}
