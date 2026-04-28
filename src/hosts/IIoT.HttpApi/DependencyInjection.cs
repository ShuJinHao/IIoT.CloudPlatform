using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using IIoT.Core.Production.Contracts.PassStation;
using IIoT.Dapper;
using IIoT.EmployeeService.Commands.Employees;
using IIoT.EntityFrameworkCore;
using IIoT.EventBus;
using IIoT.HttpApi.Infrastructure;
using IIoT.Infrastructure;
using IIoT.Infrastructure.Authentication;
using IIoT.MasterDataService.Commands.Processes;
using IIoT.ProductionService;
using IIoT.ProductionService.Caching;
using IIoT.ProductionService.Commands.PassStations;
using IIoT.ProductionService.Profiles;
using IIoT.ProductionService.Queries.PassStations;
using IIoT.Services.CrossCutting.Behaviors;
using IIoT.Services.Contracts.Caching;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.DependencyInjection;
using IIoT.Services.Contracts.Events.PassStations;
using IIoT.SharedKernel.Configuration;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Result;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

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
            cfg.AddOpenBehavior(typeof(AuthorizationBehavior<,>));
            cfg.AddOpenBehavior(typeof(DistributedLockBehavior<,>));
        });

        builder.Services.AddScoped<IPassStationReceiveService, PassStationReceiveService>();
        builder.Services.AddScoped<IDeviceCacheInvalidationService, DeviceCacheInvalidationService>();
        builder.Services
            .AddPassStationType<PassDataInjectionReceivedEvent, InjectionWriteModel, InjectionMapper>()
            .AddPassStationType<PassDataStackingReceivedEvent, StackingWriteModel, StackingMapper>();
        builder.Services.AddScoped<IPassStationQueryDescriptor, InjectionPassStationQueryDescriptor>();
        builder.Services.AddScoped<IPassStationQueryDescriptor, StackingPassStationQueryDescriptor>();
        builder.Services.AddTransient<
            IRequestHandler<GetPassStationListByTypeQuery, Result<PagedList<PassStationListItemDto>>>,
            GetPassStationListByTypeHandler>();
        builder.Services.AddTransient<
            IRequestHandler<GetPassStationDetailByTypeQuery, Result<PassStationDetailDto>>,
            GetPassStationDetailByTypeHandler>();

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
            });

        builder.Services.AddAuthorizationBuilder()
            .SetDefaultPolicy(authenticatedUserPolicy)
            .SetFallbackPolicy(authenticatedUserPolicy)
            .AddPolicy(HttpApiPolicies.RequireEdgeDeviceToken, policy =>
                policy.RequireAuthenticatedUser()
                    .RequireClaim(IIoTClaimTypes.ActorType, IIoTClaimTypes.EdgeDeviceActor)
                    .RequireClaim(IIoTClaimTypes.DeviceId));

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
                        Title = "Too Many Requests",
                        Type = "https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status/429",
                        Detail = "Too many requests. Please retry later."
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
                    .WithHeaders("Authorization", "Content-Type", RefreshTokenHeaderNames.RefreshToken)
                    .WithExposedHeaders(RefreshTokenHeaderNames.ExposedHeaders);
            });
        });

        builder.Services.AddScoped<ICurrentUser, CurrentUser>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddExceptionHandler<UseCaseExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.AddHealthChecks()
            .AddCheck<PostgresReadinessHealthCheck>("postgres-ready");
    }
}
