using System.Text;
using System.Reflection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using IIoT.HttpApi.Infrastructure;
using IIoT.Infrastructure;
using IIoT.Infrastructure.Authentication;
using IIoT.Services.Common.Behaviors;
using IIoT.Services.Common.Contracts;
using IIoT.EntityFrameworkCore;

namespace IIoT.HttpApi;

public static class DependencyInjection
{
    public static void AddApplicationService(this IHostApplicationBuilder builder)
    {
        // 1. 调用底层注入
        builder.AddInfrastructures();
        builder.AddEfCore();

        // 2. 🌟 注入 MediatR 并锁定你的 LicenseKey
        builder.Services.AddMediatR(cfg =>
        {
            cfg.LicenseKey = "eyJhbGciOiJSUzI1NiIsImtpZCI6Ikx1Y2t5UGVubnlTb2Z0d2FyZUxpY2Vuc2VLZXkvYmJiMTNhY2I1OTkwNGQ4OWI0Y2IxYzg1ZjA4OGNjZjkiLCJ0eXAiOiJKV1QifQ.eyJpc3MiOiJodHRwczovL2x1Y2t5cGVubnlzb2Z0d2FyZS5jb20iLCJhdWQiOiJMdWNreVBlbm55U29mdHdhcmUiLCJleHAiOiIxODA0MTE4NDAwIiwiaWF0IjoiMTc3MjYwOTI4MCIsImFjY291bnRfaWQiOiIwMTljYjdiYTA0NGM3Y2FjYTcyZDNhMWQ3YjRlYzZjNiIsImN1c3RvbWVyX2lkIjoiY3RtXzAxa2p2dnk2ZjFjdjM1bmF3NzNmNGZ0MTE4Iiwic3ViX2lkIjoiLSIsImVkaXRpb24iOiIwIiwidHlwZSI6IjIifQ.vuSHJIt34rSumtJdD5ZI6gorKQmaD5Msk28ucJr2GIPFR1TsOqtyvdMydzyN5nFIEv_EeGNOu_LfTHTCDz2G-Vu9atS1h7xhIoQqNT8PvuLPHEHrf90YjOKEe4rxjohth1fC2SqpkvrJ0VzEPWQNsy5lvoLOZmzw2WAHa6NBy5bc4R9tQNwOUUbxLSwhmnyOo6K1Td87CBXEjAveGrXuSwhNE0NnQWuTs1ptcK40tfkq3T3Bigh2NO-QDiGuipxoS5AQIkO6n-wLjuhFW1078IEeyh9wct2l7s8htWNQLIlmRvFFJPiN2m1-cI60ds4SYfr4FA4pM6DSNXIMDMeGyA";

            // 挂载鉴权管道
            cfg.AddOpenBehavior(typeof(AuthorizationBehavior<,>));
        });
    }

    public static void AddWebServices(this IHostApplicationBuilder builder)
    {
        // 1. JWT 校验逻辑 (复刻 AI 示例)
        var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
                          ?? throw new NullReferenceException("JwtSettings is missing");

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
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
                    ClockSkew = TimeSpan.Zero
                };
            });

        // 2. 基建
        builder.Services.AddScoped<ICurrentUser, CurrentUser>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddExceptionHandler<UseCaseExceptionHandler>();
        builder.Services.AddProblemDetails();
    }
}