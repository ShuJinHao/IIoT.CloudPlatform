using System.Text;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using FluentValidation;
using IIoT.Dapper;
using IIoT.EmployeeService.Commands.Employees;
using IIoT.EntityFrameworkCore;
using IIoT.EventBus;
using IIoT.HttpApi.Infrastructure;
using IIoT.HttpApi.Infrastructure.Authentication;
using IIoT.HttpApi.Infrastructure.Oidc;
using IIoT.Infrastructure;
using IIoT.Infrastructure.Authentication;
using IIoT.MasterDataService.Commands.Processes;
using IIoT.ProductionService;
using IIoT.ProductionService.AiRead;
using IIoT.ProductionService.BusinessTime;
using IIoT.ProductionService.Caching;
using IIoT.ProductionService.ClientReleases;
using IIoT.ProductionService.PassStations;
using IIoT.ProductionService.Profiles;
using IIoT.Services.CrossCutting.Behaviors;
using IIoT.Services.CrossCutting.Authorization;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Caching;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.CrossCutting.DependencyInjection;
using IIoT.SharedKernel.Configuration;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;

namespace IIoT.HttpApi;

public static class DependencyInjection
{
    public static void AddApplicationService(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton(TimeProvider.System);
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

        builder.Services.AddConfiguredMediatR(
            builder.Configuration,
            ConfigureApplicationMediatR);

        builder.Services.AddScoped<IDeviceCacheInvalidationService, DeviceCacheInvalidationService>();
        builder.Services.AddScoped<IRecipeCacheInvalidationService, RecipeCacheInvalidationService>();
        builder.AddValidatedOptions<PassStationTypesOptions>(
            PassStationTypesOptions.SectionName,
            static options => options.Validate());
        builder.AddValidatedOptions<AiReadOptions>(
            AiReadOptions.SectionName,
            static options => options.Validate());
        builder.AddValidatedOptions<BusinessTimeOptions>(
            BusinessTimeOptions.SectionName,
            static options => options.Validate());
        builder.Services.AddSingleton<IBusinessTimeProvider, BusinessTimeProvider>();
        builder.AddValidatedOptions<EdgeInstallerArtifactOptions>(
            EdgeInstallerArtifactOptions.SectionName,
            options => options.Validate(builder.Environment.IsProduction()));
        builder.AddValidatedOptions<PluginReleaseSignatureOptions>(
            PluginReleaseSignatureOptions.SectionName,
            options => options.Validate(builder.Environment.IsProduction()));
        builder.AddValidatedOptions<EdgeReleaseRetentionOptions>(
            EdgeReleaseRetentionOptions.SectionName,
            static options => options.Validate());
        builder.AddValidatedOptions<EdgeReleaseUploadOptions>(
            EdgeReleaseUploadOptions.SectionName,
            static options => options.Validate());
        builder.Services.AddScoped<IClientReleaseRetentionService, ClientReleaseRetentionService>();
        builder.Services.AddScoped<IClientReleaseRetentionPolicyReader>(sp =>
            sp.GetRequiredService<IClientReleaseRetentionService>());
        builder.Services.AddScoped<IClientReleaseComponentDeletionProcessor, ClientReleaseComponentDeletionProcessor>();
        builder.Services.AddHostedService<EdgeInstallerReadyPackageCleanupService>();
        builder.Services.AddScoped<ClientReleaseUploadCoordinator>();
        builder.Services.AddPassStationRuntime();

        builder.Services.AddAutoMapper(cfg => { cfg.AddProfile<ProductionProfile>(); });
    }

    internal static void ConfigureApplicationMediatR(MediatRServiceConfiguration cfg)
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
        cfg.AddOpenBehavior(typeof(AdminOnlyBehavior<,>));
        cfg.AddOpenBehavior(typeof(AuthorizationBehavior<,>));
        cfg.AddOpenBehavior(typeof(DistributedLockBehavior<,>));
    }

    public static void AddWebServices(this IHostApplicationBuilder builder)
    {
        var jwtSettings = builder.AddValidatedOptions<JwtSettings>(
            JwtSettings.SectionName,
            static options => options.Validate());
        var jwtSecret = JwtSecretResolver.Resolve(builder.Environment, jwtSettings.Secret);
        var aiIdentityStatusTokenOptions = builder.AddValidatedOptions<AiIdentityStatusTokenOptions>(
            AiIdentityStatusTokenOptions.SectionName,
            static options => options.Validate());
        var aiIdentityStatusSigningSecret = aiIdentityStatusTokenOptions.Enabled
            ? aiIdentityStatusTokenOptions.SigningSecret
            : Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
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
        ApplyIntranetHttpOidcEnvironmentOverride(builder.Configuration);
        var oidcProviderOptions = builder.AddValidatedOptions<OidcProviderOptions>(
            OidcProviderOptions.SectionName,
            options => options.Validate(builder.Environment.EnvironmentName));
        var identityOnlyTestHost = builder.Configuration.GetValue<bool>(
            HttpApiTestingConfiguration.IdentityOnlyHostConfigurationKey);
        HttpApiTestingConfiguration.EnsureAllowed(
            identityOnlyTestHost,
            builder.Environment.EnvironmentName);
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
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var validator = context.HttpContext.RequestServices
                            .GetRequiredService<HumanJwtStatusValidator>();
                        if (context.Principal is null ||
                            !await validator.IsCurrentAsync(
                                context.Principal,
                                context.HttpContext.RequestAborted))
                        {
                            context.Fail("Human identity status is unavailable or no longer current.");
                        }
                    }
                };
            })
            .AddCookie(CloudOidcDefaults.SessionScheme, options =>
            {
                options.Cookie.Name = oidcProviderOptions.GetEffectiveSessionCookieName();
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = oidcProviderOptions.AllowIntranetHttpOidc
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
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
            })
            .AddJwtBearer(AiIdentityStatusTokenDefaults.AuthenticationScheme, options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = aiIdentityStatusTokenOptions.Issuer,
                    ValidAudience = aiIdentityStatusTokenOptions.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(aiIdentityStatusSigningSecret)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.Zero
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (!AiIdentityStatusTokenValidator.IsValid(context.SecurityToken))
                        {
                            context.Fail(
                                "Identity-status token claims are missing, duplicated or outside the fixed five-minute contract.");
                        }

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

                options.RegisterScopes(
                    OpenIddictConstants.Scopes.Profile,
                    AiReadDelegationDefaults.Scope);
                options.SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(
                    oidcProviderOptions.AuthorizationCodeLifetimeMinutes));
                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(
                    oidcProviderOptions.AccessTokenLifetimeMinutes));
                options.SetIdentityTokenLifetime(TimeSpan.FromMinutes(
                    oidcProviderOptions.IdentityTokenLifetimeMinutes));

                if (identityOnlyTestHost)
                {
                    options.DisableAccessTokenEncryption();
                }

                ConfigureOpenIddictCertificates(options, oidcProviderOptions, builder.Environment);

                var aspNetCore = options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough();

                if (builder.Environment.IsDevelopment() || oidcProviderOptions.AllowIntranetHttpOidc)
                {
                    aspNetCore.DisableTransportSecurityRequirement();
                }
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.EnableTokenEntryValidation();
                options.UseAspNetCore();
            });

        builder.Services.AddScoped<HumanJwtStatusValidator>();

        ConfigureHttpAuthorization(
            builder.Services.AddAuthorizationBuilder(),
            authenticatedUserPolicy);

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
            options.AddPolicy(HttpApiRateLimitPolicies.EdgeHostPlcStateUpload, context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    RateLimitPartitionKeyResolver.ResolveEdgeUploadPartitionKey(context),
                    _ => rateLimiting.EdgeHostPlcStateUpload.ToRateLimiterOptions()));
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
                    .WithExposedHeaders(
                        [
                            .. RefreshTokenHeaderNames.ExposedHeaders,
                            InstallerPackageHeaderNames.GenerationId,
                            "Content-Disposition"
                        ]);
            });
        });

        builder.Services.AddScoped<ICurrentUser, CurrentUser>();
        builder.Services.AddScoped<ICurrentUserDeviceAccessService, CurrentUserDeviceAccessService>();
        builder.Services.AddScoped<IAdminTargetGuard, AdminTargetGuard>();
        builder.Services.AddScoped<IClientReleaseUploadSource, CurrentClientReleaseUploadSource>();
        builder.Services.AddScoped<ICloudOidcSessionService, CloudOidcSessionService>();
        builder.Services.AddScoped<HttpAiReadScopeAccessor>();
        builder.Services.AddScoped<IAiReadScopeAccessor>(provider =>
            provider.GetRequiredService<HttpAiReadScopeAccessor>());
        builder.Services.AddScoped<IAiReadAuthorizationContext>(provider =>
            provider.GetRequiredService<HttpAiReadScopeAccessor>());
        builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, CloudAuthorizationResultHandler>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddExceptionHandler<UseCaseExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.AddHealthChecks()
            .AddCheck<PostgresReadinessHealthCheck>("postgres-ready");
    }

    internal static void ConfigureHttpAuthorization(
        AuthorizationBuilder authorizationBuilder,
        AuthorizationPolicy authenticatedUserPolicy)
    {
        authorizationBuilder
            .SetDefaultPolicy(authenticatedUserPolicy)
            .SetFallbackPolicy(authenticatedUserPolicy)
            .AddPolicy(HttpApiPolicies.RequireHumanUserToken, policy =>
                policy.RequireAuthenticatedUser()
                    .RequireClaim(IIoTClaimTypes.ActorType, IIoTClaimTypes.HumanActor))
            .AddPolicy(HttpApiPolicies.RequireEdgeDeviceToken, policy =>
                policy.RequireAuthenticatedUser()
                    .RequireClaim(IIoTClaimTypes.ActorType, IIoTClaimTypes.EdgeDeviceActor)
                    .RequireClaim(IIoTClaimTypes.DeviceId))
            .AddPolicy(HttpApiPolicies.RequireEdgeActivationToken, policy =>
                policy.RequireAuthenticatedUser()
                    .RequireClaim(
                        IIoTClaimTypes.ActorType,
                        IIoTClaimTypes.EdgeActivationActor)
                    .RequireClaim(IIoTClaimTypes.DeviceId)
                    .RequireClaim(IIoTClaimTypes.InstallerGenerationId))
            .AddPolicy(HttpApiPolicies.RequireAiReadDelegation, policy =>
                policy.AddAuthenticationSchemes(
                        OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser()
                    .RequireClaim(
                        IIoTClaimTypes.ActorType,
                        IIoTClaimTypes.AiDelegatedUserActor)
                    .RequireAssertion(context =>
                        HasSpaceDelimitedClaimValue(
                            context.User,
                            OpenIddictConstants.Claims.Scope,
                            AiReadDelegationDefaults.Scope) &&
                        HasSpaceDelimitedClaimValue(
                            context.User,
                            OpenIddictConstants.Claims.Audience,
                            AiReadDelegationDefaults.Audience)))
            .AddPolicy(HttpApiPolicies.RequireAiIdentityStatusToken, policy =>
                policy.AddAuthenticationSchemes(
                        AiIdentityStatusTokenDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser()
                    .RequireClaim(
                        IIoTClaimTypes.ActorType,
                        IIoTClaimTypes.AiIdentityStatusActor));
    }

    private static bool HasSpaceDelimitedClaimValue(
        System.Security.Claims.ClaimsPrincipal principal,
        string claimType,
        string expectedValue)
    {
        return principal.FindAll(claimType)
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(expectedValue, StringComparer.Ordinal);
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
        else if (options.AllowIntranetHttpOidc)
        {
            builder.AddEphemeralSigningKey();
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
        else if (options.AllowIntranetHttpOidc)
        {
            builder.AddEphemeralEncryptionKey();
        }
        else
        {
            throw new InvalidOperationException(
                "OidcProvider:EncryptionCertificatePath is required outside Development.");
        }
    }

    private static void ApplyIntranetHttpOidcEnvironmentOverride(IConfiguration configuration)
    {
        var value = configuration[OidcProviderOptions.AllowIntranetHttpOidcEnvironmentVariable];
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        configuration[$"{OidcProviderOptions.SectionName}:{nameof(OidcProviderOptions.AllowIntranetHttpOidc)}"] = value;
    }
}
