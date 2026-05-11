using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using IIoT.Dapper;
using IIoT.EmployeeService.Commands.Employees;
using IIoT.EntityFrameworkCore;
using IIoT.EventBus;
using IIoT.HttpApi.Infrastructure;
using IIoT.HttpApi.Infrastructure.Oidc;
using IIoT.Infrastructure;
using IIoT.Infrastructure.Authentication;
using IIoT.MasterDataService.Commands.Processes;
using IIoT.ProductionService;
using IIoT.ProductionService.AiRead;
using IIoT.ProductionService.Caching;
using IIoT.ProductionService.PassStations;
using IIoT.ProductionService.Profiles;
using IIoT.Services.CrossCutting.Behaviors;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Caching;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.CrossCutting.DependencyInjection;
using IIoT.SharedKernel.Configuration;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;

namespace IIoT.HttpApi;

public static class DependencyInjection
{
    public static void AddApplicationService(this IHostApplicationBuilder builder)
    {
        builder.AddInfrastructures();
        builder.AddEfCore();
        builder.AddEventBus();
        builder.AddDapper();

        builder.Services.AddValidatorsFromAssemblies(
        [
            typeof(IIoT.IdentityService.Commands.LoginUserCommand).Assembly,
            typeof(OnboardEmployeeCommand).Assembly,
            typeof(CreateProcessCommand).Assembly,
            typeof(IIoT.ProductionService.Commands.Recipes.CreateRecipeCommand).Assembly
        ]);

        builder.Services.AddConfiguredMediatR(builder.Configuration, cfg =>
        {
            cfg.RegisterServicesFromAssemblies(
                typeof(IIoT.IdentityService.Commands.LoginUserCommand).Assembly,
                typeof(OnboardEmployeeCommand).Assembly,
                typeof(CreateProcessCommand).Assembly,
                typeof(IIoT.ProductionService.Commands.Recipes.CreateRecipeCommand).Assembly);
            cfg.AddOpenBehavior(typeof(RequestKindGuardBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(DeviceBindingBehavior<,>));
            cfg.AddOpenBehavior(typeof(AiReadAuditBehavior<,>));
            cfg.AddOpenBehavior(typeof(AiReadAuthorizationBehavior<,>));
            cfg.AddOpenBehavior(typeof(AuthorizationBehavior<,>));
            cfg.AddOpenBehavior(typeof(DistributedLockBehavior<,>));
        });

        builder.Services.AddScoped<IDeviceCacheInvalidationService, DeviceCacheInvalidationService>();
        builder.Services.AddScoped<IRecipeCacheInvalidationService, RecipeCacheInvalidationService>();
        builder.AddValidatedOptions<PassStationTypesOptions>(
            PassStationTypesOptions.SectionName,
            static options => options.Validate());
        builder.AddValidatedOptions<AiReadOptions>(
            AiReadOptions.SectionName,
            static options => options.Validate());
        builder.Services.AddPassStationRuntime();

        builder.Services.AddAutoMapper(cfg => { cfg.AddProfile<ProductionProfile>(); });
    }

    public static void AddWebServices(this IHostApplicationBuilder builder)
    {
        var jwtSettings = builder.AddValidatedOptions<JwtSettings>(
            JwtSettings.SectionName,
            static options => options.Validate());
        var jwtSecret = JwtSecretResolver.Resolve(builder.Environment, jwtSettings.Secret);
        var rateLimiting = builder.Configuration.GetRequiredValidatedOptions<HttpApiRateLimitingOptions>(
            HttpApiRateLimitingOptions.SectionName,
            static options => options.Validate());
        var forwardedHeaders = builder.Configuration.GetRequiredValidatedOptions<HttpApiForwardedHeadersOptions>(
            HttpApiForwardedHeadersOptions.SectionName,
            static options => options.Validate());
        var corsOptions = builder.Configuration.GetRequiredValidatedOptions<HttpApiCorsOptions>(
            HttpApiCorsOptions.SectionName,
            static options => options.Validate());
        _ = builder.AddValidatedOptions<RefreshTokenOptions>(
            RefreshTokenOptions.SectionName,
            static options => options.Validate());
        _ = builder.AddValidatedOptions<BootstrapAuthOptions>(
            BootstrapAuthOptions.SectionName,
            static options => options.Validate());
        var oidcProviderOptions = builder.AddValidatedOptions<OidcProviderOptions>(
            OidcProviderOptions.SectionName,
            static options => options.Validate());
        var authenticatedUserPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidAudience = jwtSettings.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                    ClockSkew = TimeSpan.Zero
                };
            })
            .AddCookie(CloudOidcDefaults.SessionScheme, options =>
            {
                options.Cookie.Name = oidcProviderOptions.SessionCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(oidcProviderOptions.SessionIdleMinutes);
                options.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }
                };
            });

        builder.Services.AddOpenIddict()
            .AddServer(options =>
            {
                options.SetIssuer(new Uri(oidcProviderOptions.Issuer));
                options.SetAuthorizationEndpointUris("/connect/authorize");
                options.SetTokenEndpointUris("/connect/token");
                options.SetUserInfoEndpointUris("/connect/userinfo");
                options.SetEndSessionEndpointUris("/connect/logout");

                options.AllowAuthorizationCodeFlow()
                    .RequireProofKeyForCodeExchange();

                options.RegisterScopes(OpenIddictConstants.Scopes.Profile);
                options.SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(
                    oidcProviderOptions.AuthorizationCodeLifetimeMinutes));
                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(
                    oidcProviderOptions.AccessTokenLifetimeMinutes));
                options.SetIdentityTokenLifetime(TimeSpan.FromMinutes(
                    oidcProviderOptions.IdentityTokenLifetimeMinutes));

                ConfigureOpenIddictCertificates(options, oidcProviderOptions, builder.Environment);

                var aspNetCore = options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough();

                if (builder.Environment.IsDevelopment())
                {
                    aspNetCore.DisableTransportSecurityRequirement();
                }
            });

        builder.Services.AddAuthorizationBuilder()
            .SetDefaultPolicy(authenticatedUserPolicy)
            .SetFallbackPolicy(authenticatedUserPolicy)
            .AddPolicy(HttpApiPolicies.RequireEdgeDeviceToken, policy =>
                policy.RequireAuthenticatedUser()
                    .RequireClaim(IIoTClaimTypes.ActorType, IIoTClaimTypes.EdgeDeviceActor)
                    .RequireClaim(IIoTClaimTypes.DeviceId))
            .AddPolicy(HttpApiPolicies.RequireAiReadToken, policy =>
                policy.RequireAuthenticatedUser()
                    .RequireClaim(IIoTClaimTypes.ActorType, IIoTClaimTypes.AiServiceActor));

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            forwardedHeaders.ApplyTo(options);
        });

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "请求过于频繁",
                        Type = "https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status/429",
                        Detail = "请求过于频繁，请稍后重试。"
                    },
                    token);
            };
            options.AddPolicy(HttpApiRateLimitPolicies.GeneralApi, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    RateLimitPartitionKeyResolver.ResolveClientPartitionKey(context, "general-anonymous"),
                    _ => rateLimiting.GeneralApi.ToRateLimiterOptions()));
            options.AddPolicy(HttpApiRateLimitPolicies.PasswordLogin, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    RateLimitPartitionKeyResolver.ResolveClientPartitionKey(context, "password-login-anonymous"),
                    _ => rateLimiting.PasswordLogin.ToRateLimiterOptions()));
            options.AddPolicy(HttpApiRateLimitPolicies.Refresh, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    RateLimitPartitionKeyResolver.ResolveClientPartitionKey(context, "refresh-anonymous"),
                    _ => rateLimiting.Refresh.ToRateLimiterOptions()));
            options.AddPolicy(HttpApiRateLimitPolicies.EdgeOperatorLogin, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    RateLimitPartitionKeyResolver.ResolveClientPartitionKey(context, "edge-operator-login-anonymous"),
                    _ => rateLimiting.EdgeOperatorLogin.ToRateLimiterOptions()));
            options.AddPolicy(HttpApiRateLimitPolicies.Bootstrap, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    RateLimitPartitionKeyResolver.ResolveClientPartitionKey(context, "bootstrap-anonymous"),
                    _ => rateLimiting.Bootstrap.ToRateLimiterOptions()));
            options.AddPolicy(HttpApiRateLimitPolicies.AiRead, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    RateLimitPartitionKeyResolver.ResolveClientPartitionKey(context, "ai-read-anonymous"),
                    _ => rateLimiting.AiRead.ToRateLimiterOptions()));
            options.AddPolicy(HttpApiRateLimitPolicies.CapacityUpload, context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    RateLimitPartitionKeyResolver.ResolveEdgeUploadPartitionKey(context),
                    _ => rateLimiting.CapacityUpload.ToRateLimiterOptions()));
            options.AddPolicy(HttpApiRateLimitPolicies.DeviceLogUpload, context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    RateLimitPartitionKeyResolver.ResolveEdgeUploadPartitionKey(context),
                    _ => rateLimiting.DeviceLogUpload.ToRateLimiterOptions()));
            options.AddPolicy(HttpApiRateLimitPolicies.PassStationUpload, context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    RateLimitPartitionKeyResolver.ResolveEdgeUploadPartitionKey(context),
                    _ => rateLimiting.PassStationUpload.ToRateLimiterOptions()));
        });

        builder.Services.AddCors(options =>
        {
            options.AddPolicy(HttpApiCorsOptions.PolicyName, policy =>
            {
                if (corsOptions.AllowedOrigins.Length > 0)
                {
                    policy.WithOrigins(corsOptions.AllowedOrigins);
                }

                policy.WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
                    .WithHeaders(
                        "Authorization",
                        "Content-Type",
                        RefreshTokenHeaderNames.RefreshToken,
                        BootstrapSecretHeaderNames.Secret)
                    .WithExposedHeaders(RefreshTokenHeaderNames.ExposedHeaders);
            });
        });

        builder.Services.AddScoped<ICurrentUser, CurrentUser>();
        builder.Services.AddScoped<ICloudOidcSessionService, CloudOidcSessionService>();
        builder.Services.AddScoped<IAiReadScopeAccessor, HttpAiReadScopeAccessor>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddExceptionHandler<UseCaseExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.AddHealthChecks()
            .AddCheck<PostgresReadinessHealthCheck>("postgres-ready");
    }

    private static void ConfigureOpenIddictCertificates(
        OpenIddictServerBuilder builder,
        OidcProviderOptions options,
        IHostEnvironment environment)
    {
        var signingCertificate = OidcCertificateLoader.LoadSigningCertificate(options);
        var encryptionCertificate = OidcCertificateLoader.LoadEncryptionCertificate(options);

        if (signingCertificate is not null)
        {
            builder.AddSigningCertificate(signingCertificate);
        }
        else if (environment.IsDevelopment())
        {
            builder.AddDevelopmentSigningCertificate();
        }
        else
        {
            throw new InvalidOperationException(
                "OidcProvider:SigningCertificatePath is required outside Development.");
        }

        if (encryptionCertificate is not null)
        {
            builder.AddEncryptionCertificate(encryptionCertificate);
        }
        else if (environment.IsDevelopment())
        {
            builder.AddDevelopmentEncryptionCertificate();
        }
        else
        {
            throw new InvalidOperationException(
                "OidcProvider:EncryptionCertificatePath is required outside Development.");
        }
    }
}
