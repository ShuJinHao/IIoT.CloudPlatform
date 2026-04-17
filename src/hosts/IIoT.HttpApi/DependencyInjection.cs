using System.Text;
using System.Threading.RateLimiting;
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
using IIoT.ProductionService.Commands.PassStations;
using IIoT.ProductionService.Profiles;
using IIoT.Services.Common.Behaviors;
using IIoT.Services.Common.Contracts.Identity;
using IIoT.Services.Common.Contracts.RecordQueries;
using IIoT.Services.Common.DependencyInjection;
using IIoT.Services.Common.Events.PassStations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
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

        builder.Services.AddConfiguredMediatR(builder.Configuration, cfg =>
        {
            cfg.RegisterServicesFromAssemblies(
                typeof(IIoT.IdentityService.Commands.LoginUserCommand).Assembly,
                typeof(OnboardEmployeeCommand).Assembly,
                typeof(CreateProcessCommand).Assembly,
                typeof(IIoT.ProductionService.Commands.Recipes.CreateRecipeCommand).Assembly);
            cfg.AddOpenBehavior(typeof(RequestKindGuardBehavior<,>));
            cfg.AddOpenBehavior(typeof(DeviceBindingBehavior<,>));
            cfg.AddOpenBehavior(typeof(AuthorizationBehavior<,>));
            cfg.AddOpenBehavior(typeof(DistributedLockBehavior<,>));
        });

        builder.Services.AddScoped<IPassStationReceiveService, PassStationReceiveService>();
        builder.Services
            .AddPassStationType<PassDataInjectionReceivedEvent, InjectionWriteModel, InjectionMapper>()
            .AddPassStationType<PassDataStackingReceivedEvent, StackingWriteModel, StackingMapper>()
            .AddPassStationQuery<InjectionPassListItemDto, InjectionPassDetailDto>();

        builder.Services.AddAutoMapper(cfg => { cfg.AddProfile<ProductionProfile>(); });
    }

    public static void AddWebServices(this IHostApplicationBuilder builder)
    {
        var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
                          ?? throw new NullReferenceException("JwtSettings is missing");
        var jwtSecret = JwtSecretResolver.Resolve(builder.Environment, jwtSettings.Secret);
        var rateLimiting = builder.Configuration
                               .GetSection(HttpApiRateLimitingOptions.SectionName)
                               .Get<HttpApiRateLimitingOptions>()
                           ?? new HttpApiRateLimitingOptions();
        var forwardedHeaders = builder.Configuration
                                   .GetSection(HttpApiForwardedHeadersOptions.SectionName)
                                   .Get<HttpApiForwardedHeadersOptions>()
                               ?? new HttpApiForwardedHeadersOptions();
        var authenticatedUserPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();

        forwardedHeaders.Validate();

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
        builder.Services.Configure<HttpApiForwardedHeadersOptions>(
            builder.Configuration.GetSection(HttpApiForwardedHeadersOptions.SectionName));
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            forwardedHeaders.ApplyTo(options);
        });
        builder.Services.Configure<HttpApiRateLimitingOptions>(
            builder.Configuration.GetSection(HttpApiRateLimitingOptions.SectionName));
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
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    RateLimitPartitionKeyResolver.ResolveClientPartitionKey(context, "global-anonymous"),
                    _ => rateLimiting.Global.ToRateLimiterOptions()));
            options.AddPolicy("login", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    RateLimitPartitionKeyResolver.ResolveClientPartitionKey(context, "login-anonymous"),
                    _ => rateLimiting.Login.ToRateLimiterOptions()));
            options.AddPolicy("bootstrap", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    RateLimitPartitionKeyResolver.ResolveClientPartitionKey(context, "bootstrap-anonymous"),
                    _ => rateLimiting.Bootstrap.ToRateLimiterOptions()));
            options.AddPolicy("edge-upload", context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    RateLimitPartitionKeyResolver.ResolveEdgeUploadPartitionKey(context),
                    _ => rateLimiting.EdgeUpload.ToRateLimiterOptions()));
        });

        builder.Services.AddScoped<ICurrentUser, CurrentUser>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddExceptionHandler<UseCaseExceptionHandler>();
        builder.Services.AddProblemDetails();
    }
}
