using System;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using BoardGameList.Attributes;
using BoardGameList.Constants;
using BoardGameList.Models;
using BoardGameList.Swagger;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Swashbuckle.AspNetCore.Annotations;

var builder = WebApplication.CreateBuilder(args);

// Entity Framework Core DbContext configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
                       ?? throw new InvalidOperationException("Connection string 'DefaultConnection' for database book store not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

builder.Logging.ClearProviders().AddSimpleConsole().AddDebug();

builder.Host.UseSerilog((ctx, lc) => {
        lc.ReadFrom.Configuration(ctx.Configuration);
        lc.Enrich.FromLogContext(); // Allows you to push custom properties
        lc.WriteTo.File("Logs/log.txt",
            outputTemplate:
            "{Timestamp:HH:mm:ss} [{Level:u3}] " +
            "[{MachineName} #{ThreadId}] " +
            "{Message:lj}{NewLine}{Exception}",
            rollingInterval: RollingInterval.Day, 
            retainedFileCountLimit: 7, 
            rollOnFileSizeLimit: true);
        lc.WriteTo.MySQL(connectionString, tableName: "LogEvents");
    },
    writeToProviders: true);

builder.Services.AddCors(options => {
    options.AddDefaultPolicy(cfg => {
        cfg.WithOrigins(builder.Configuration["AllowedOrigins"] ?? string.Empty);
        cfg.AllowAnyHeader();
        cfg.AllowAnyMethod();
    });
    options.AddPolicy(name: "AnyOrigin",
        cfg => {
            cfg.AllowAnyOrigin();
            cfg.AllowAnyHeader();
            cfg.AllowAnyMethod();
        });
});

builder.Services.AddControllers(options =>
{
    options.ModelBindingMessageProvider.SetValueIsInvalidAccessor(
        (x) => $"The value '{x}' is invalid.");
    options.ModelBindingMessageProvider.SetValueMustBeANumberAccessor(
        (x) => $"The field {x} must be a number.");
    options.ModelBindingMessageProvider.SetAttemptedValueIsInvalidAccessor(
        (x, y) => $"The value '{x}' is not valid for {y}.");
    options.ModelBindingMessageProvider.SetMissingKeyOrValueAccessor(
        () => $"A value is required.");
    
    options.CacheProfiles.Add("NoCache", new CacheProfile() { NoStore = true });
    options.CacheProfiles.Add("Any-60", new CacheProfile()
        {
            Location = ResponseCacheLocation.Any, 
            Duration = 60
        }
    );
});
// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.EnableAnnotations();
    
    options.ParameterFilter<SortColumnFilter>();
    options.ParameterFilter<SortOrderFilter>();

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Please enter token",
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        BearerFormat = "JWT",
        Scheme = "bearer"
    });
    
    options.OperationFilter<AuthRequirementFilter>();
    options.DocumentFilter<CustomDocumentFilter>();
    options.RequestBodyFilter<PasswordRequestFilter>();
    options.SchemaFilter<CustomKeyValueFilter>();
});

builder.Services.AddIdentity<ApiUser, IdentityRole>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredLength = 12;
    }).AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = 
        options.DefaultChallengeScheme =
            options.DefaultForbidScheme =
                options.DefaultScheme =
                    options.DefaultSignInScheme =
                        options.DefaultSignOutScheme =
                            JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        RequireExpirationTime = true,
        ValidIssuer = builder.Configuration["JWT:Issuer"],
        ValidAudience = builder.Configuration["JWT:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(builder.Configuration["JWT:SigningKey"] ?? string.Empty)
        )
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ModeratorWithMobilePhone", policy =>
        policy
            .RequireClaim(ClaimTypes.Role, RoleNames.Moderator)
            .RequireClaim(ClaimTypes.MobilePhone));
    
    options.AddPolicy("MinAge18", policy =>
        policy
            .RequireAssertion(ctx =>
                ctx.User.HasClaim(c => c.Type == ClaimTypes.DateOfBirth)
                && DateTime.ParseExact(
                    "yyyyMMdd",
                    ctx.User.Claims.First(c =>
                        c.Type == ClaimTypes.DateOfBirth).Value,
                    System.Globalization.CultureInfo.InvariantCulture)
                >= DateTime.Now.AddYears(-18)));
});

builder.Services.AddResponseCaching(options =>
{
    options.MaximumBodySize = 32 * 1024 * 1024;
    options.SizeLimit = 50 * 1024 * 1024;
});

builder.Services.AddMemoryCache();

var app = builder.Build();

// CRITICAL: Add this BEFORE Swagger and UseHttpsRedirection
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error");
    
    app.UseSwagger();
    app.UseSwaggerUI();
    // HTTP Security Headers
    app.Use(async (context, next) =>
    {
        context.Response.Headers.Add("X-Frame-Options", "sameorigin");
        context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
        context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
        context.Response.Headers.Add("Content-Security-Policy", "default-src 'self'; script-src 'self' 'nonce-23a98b38c'");
        context.Response.Headers.Add("Referrer-Policy", "strict-origin");
        await next();
    });
}

app.UseCors();
app.UseResponseCaching();
app.UseAuthentication();
app.UseAuthorization();

// Minimal API
app.MapGet("/error",
    [EnableCors("AnyOrigin")]
    [ResponseCache(NoStore = true)] (HttpContext context) =>
    {
        var exceptionHandler = context.Features.Get<IExceptionHandlerPathFeature>();
        var details = new ProblemDetails();
        details.Detail = exceptionHandler?.Error.Message;
        details.Extensions["traceId"] = System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier;
        details.Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1";
        details.Status = StatusCodes.Status500InternalServerError;
        app.Logger.LogError(CustomLogEvents.Error_Get, exceptionHandler?.Error, "An unhandled exception occurred.");
        return Results.Problem(details);
    });

app.MapGet("/error/test", 
    [EnableCors("AnyOrigin")] 
    [ResponseCache(NoStore = true)] () => { throw new Exception("test"); }
    );

app.MapGet("/cod/test",
    [EnableCors("AnyOrigin")]
    [ResponseCache(NoStore = true)] () =>
        Results.Text("<script>" +
                     "window.alert('Your client supports JavaScript!" +
                     "\\r\\n\\r\\n" +
                     $"Server time (UTC): {DateTime.UtcNow.ToString("o")}" +
                     "\\r\\n" +
                     "Client time (UTC): ' + new Date().toISOString());" +
                     "</script>" +
                     "<noscript>Your client does not support JavaScript</noscript>",
            "text/html"));


app.MapGet("/auth/test/1",
    [Authorize]
    [EnableCors("AnyOrigin")]
    [SwaggerOperation(Tags = new[] { "Auth" }, 
        Summary = "Auth test #1 (authenticated users).",
        Description = "Returns 200 - OK if called by " +
                      "an authenticated user regardless of its role(s).")]
    [SwaggerResponse(StatusCodes.Status200OK, "Authorized")]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "Not authorized")]
    [ResponseCache(NoStore = true)] () =>
    {
        return Results.Ok("You are authorized!");
    });

app.MapGet("/auth/test/2",
    [Authorize(Roles = RoleNames.Moderator)]
    [EnableCors("AnyOrigin")]
    [SwaggerOperation(
        Tags = new[] { "Auth" },
        Summary = "Auth test #2 (Moderator role).",
        Description = "Returns 200 - OK status code if called by " +
                      "an authenticated user assigned to the Moderator role.")]
    [ResponseCache(NoStore = true)] () =>
    {
        return Results.Ok("You are authorized!");
    });

app.MapGet("/auth/test/3",
    [Authorize(Roles = RoleNames.Administrator)]
    [EnableCors("AnyOrigin")]
    [SwaggerOperation(
        Tags = new[] { "Auth" },
        Summary = "Auth test #3 (Administrator role).",
        Description = "Returns 200 - OK if called by " +
                      "an authenticated user assigned to the Administrator role.")]
    [ResponseCache(NoStore = true)] () =>
    {
        return Results.Ok("You are authorized!");
    });

app.MapGet("/auth/test/4",
    [Authorize(Policy = "ModeratorWithMobilePhone")]
    [EnableCors("AnyOrigin")]
    [SwaggerOperation(
        Tags = new[] { "Auth" },
        Summary = "Auth test #4 (Claims-based Access Control).",
        Description = "Returns 200 - OK if called by " +
                      "an authenticated user has claims which must be explicitly declared by defining and registering a policy.")]
    [ResponseCache(NoStore = true)] () =>
    {
        return Results.Ok("You are authorized!");
    });

// Controllers
app.MapControllers().RequireCors("AnyOrigin");

app.Run();