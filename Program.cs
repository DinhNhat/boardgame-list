using BoardGameList.Constants;
using BoardGameList.Models;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serilog;

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
builder.Services.AddSwaggerGen();

builder.Services.AddResponseCaching(options =>
{
    options.MaximumBodySize = 32 * 1024 * 1024;
    options.SizeLimit = 50 * 1024 * 1024;
});

builder.Services.AddMemoryCache();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (app.Configuration.GetValue<bool>("UseDeveloperExceptionPage"))
    app.UseDeveloperExceptionPage();
else
    app.UseExceptionHandler("/error");

app.UseHttpsRedirection();
app.UseCors();
app.UseResponseCaching();
app.UseAuthorization();

// app.Use((context, next) =>
// {
//     context.Response.GetTypedHeaders().CacheControl = 
//         new Microsoft.Net.Http.Headers.CacheControlHeaderValue()
//         {
//             NoCache = true,
//             NoStore = true
//         };
//     return next.Invoke();
// });

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

// Controllers
app.MapControllers().RequireCors("AnyOrigin");

app.Run();