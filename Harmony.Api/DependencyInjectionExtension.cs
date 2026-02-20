using Cortex.Mediator.DependencyInjection;
using FluentValidation;
using Harmony.Application.Contract.Requests;
using Mapster;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Radenoor.Filters;
using Serilog;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace Ahura.Web;

internal static class DependencyInjectionExtension
{

    //internal static IServiceCollection InjectLogger(this IServiceCollection services)
    //{
    //    Log.Logger = new LoggerConfiguration()
    //        .WriteTo.Console()
    //        .WriteTo.File(
    //            path: "logs/log.txt",
    //            rollingInterval: RollingInterval.Day,
    //            retainedFileCountLimit: 30,
    //            fileSizeLimitBytes: 10 * 1024 * 1024,
    //            restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Error
    //        )
    //        .MinimumLevel.Error()
    //        .CreateLogger();

    //    services.AddLogging(loggingBuilder =>
    //    {
    //        loggingBuilder.ClearProviders();
    //        loggingBuilder.AddSerilog();
    //    });

    //    return services;
    //}

    //internal static IServiceCollection InjectFluentValidation(this IServiceCollection services) =>
    //    services.AddValidatorsFromAssemblyContaining<AddUserDto>()
    //            .AddFluentValidationAutoValidation().AddFluentValidationClientsideAdapters();

    internal static IServiceCollection InjectControllers(this IServiceCollection services) =>
        services.AddControllers(options => options.Filters.Add<StatusCodeActionFilter>()).Services;

    //internal static IServiceCollection InjectServices(this IServiceCollection services) =>
    //   services.AddScoped<IForgeService, ForgeService>()
    //           .AddScoped<IWorkFlowService, WorkFlowService>()
    //           .AddScoped<IUserService, UserService>();

    //internal static IServiceCollection InjectUnitOfWork(this IServiceCollection services) =>
    //   services.AddScoped<IUnitOfWork, UnitOfWork>();

    //internal static IServiceCollection InjectMapster(this IServiceCollection services)
    //{
    //    var config = TypeAdapterConfig.GlobalSettings;
    //    config.Scan(typeof(ForgeMapper).Assembly);
    //    config.Compile();

    //    return services;
    //}

    //internal static IServiceCollection InjectDbContext(this IServiceCollection services, IConfiguration configuration)
    //{
    //    string connectionString = configuration.GetConnectionString("MariaDb")!;

    //    services.AddScoped<SaveEntityInterceptor>();

    //    services.AddDbContext<AhuraDbContext>((sp, options) =>
    //    {
    //        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString), mySqlOptions =>
    //        {
    //            mySqlOptions.EnableRetryOnFailure(
    //                maxRetryCount: 3,
    //                maxRetryDelay: TimeSpan.FromSeconds(5),
    //                errorNumbersToAdd: null
    //            );

    //            mySqlOptions.CommandTimeout(30);
    //        });

    //        options.AddInterceptors(sp.GetRequiredService<SaveEntityInterceptor>());
    //    });

    //    return services;
    //}


    internal static IServiceCollection InjectIdentity(this IServiceCollection services) =>
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = "http://localhost:8080/realms/Harmony";
            options.Audience = "harmony-api";
            options.RequireHttpsMetadata = false;

            options.IncludeErrorDetails = true;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,

                ValidIssuer = "http://localhost:8080/realms/Harmony",
                ValidAudiences = new[] { "harmony-api", "account" },

                ClockSkew = TimeSpan.Zero,

                NameClaimType = "preferred_username",
                RoleClaimType = "roles" // We will add role claims manually
            };

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("JwtBearerDebug");

                    var authHeader = context.Request.Headers.Authorization.ToString();

                    logger.LogInformation("JWT OnMessageReceived");
                    logger.LogInformation("Authorization header: {AuthHeader}", authHeader);

                    if (!string.IsNullOrWhiteSpace(authHeader) &&
                        authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        var token = authHeader["Bearer ".Length..].Trim();
                        logger.LogInformation("Token parts count: {Parts}", token.Split('.').Length);
                    }

                    return Task.CompletedTask;
                },

                OnTokenValidated = context =>
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("JwtBearerDebug");

                    logger.LogInformation("JWT validated successfully.");

                    // Dump basic token info
                    if (context.SecurityToken is JwtSecurityToken jwt)
                    {
                        logger.LogInformation("Issuer: {Issuer}", jwt.Issuer);
                        logger.LogInformation("Audiences: {Aud}", string.Join(", ", jwt.Audiences));
                        logger.LogInformation("ValidFrom: {From}", jwt.ValidFrom);
                        logger.LogInformation("ValidTo: {To}", jwt.ValidTo);
                    }

                    // Add realm roles
                    try
                    {
                        var identity = context.Principal?.Identity as ClaimsIdentity;
                        if (identity == null)
                            return Task.CompletedTask;

                        // Keycloak realm roles are in realm_access.roles
                        var realmAccessClaim = context.Principal!.FindFirst("realm_access")?.Value;

                        if (!string.IsNullOrWhiteSpace(realmAccessClaim))
                        {
                            using var doc = JsonDocument.Parse(realmAccessClaim);

                            if (doc.RootElement.TryGetProperty("roles", out var rolesElement) &&
                                rolesElement.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var role in rolesElement.EnumerateArray())
                                {
                                    var roleName = role.GetString();
                                    if (!string.IsNullOrWhiteSpace(roleName))
                                    {
                                        identity.AddClaim(new Claim("roles", roleName));
                                        logger.LogInformation("Added realm role claim: {Role}", roleName);
                                    }
                                }
                            }
                        }
                        else
                        {
                            logger.LogWarning("realm_access claim not found.");
                        }
                    }
                    catch (Exception ex)
                    {
                        // IMPORTANT: do not break authentication if role parsing fails
                        logger.LogError(ex, "Failed while parsing realm_access roles.");
                    }

                    return Task.CompletedTask;
                },

                OnAuthenticationFailed = context =>
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("JwtBearerDebug");

                    logger.LogError(context.Exception, "JWT authentication failed: {Message}", context.Exception.Message);
                    return Task.CompletedTask;
                },

                OnChallenge = context =>
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("JwtBearerDebug");

                    logger.LogWarning("JWT challenge triggered.");
                    logger.LogWarning("Error: {Error}", context.Error);
                    logger.LogWarning("ErrorDescription: {ErrorDescription}", context.ErrorDescription);

                    return Task.CompletedTask;
                },

                OnForbidden = context =>
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("JwtBearerDebug");

                    logger.LogWarning("JWT forbidden (403). User authenticated but lacks required role/permission.");

                    return Task.CompletedTask;
                }
            };
        })
        .Services;


    internal static IServiceCollection InjectCortext(this IServiceCollection services) =>
        services.AddCortexMediator(new[] { typeof(Program), typeof(AddPostHandler), typeof(AddPostDto) }, options => options.AddDefaultBehaviors());


    internal static IServiceCollection InjectAddSwaggerGen(this IServiceCollection services) =>
       services.AddSwaggerGen(c =>
       {
           c.SwaggerDoc("v1", new OpenApiInfo { Title = "Harmony", Version = "v1" });

           c.CustomSchemaIds(type => type.FullName);

           c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
           {
               Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
               Name = "Authorization",
               In = ParameterLocation.Header,
               Type = SecuritySchemeType.ApiKey,
               Scheme = "Bearer"
           });
       });
}
