using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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

            // We will add role claims manually as "roles"
            RoleClaimType = "roles"
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
    });



var app = builder.Build();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
