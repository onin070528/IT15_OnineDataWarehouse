using Microsoft.EntityFrameworkCore;
using it15_webproject_mvc.Data;
using it15_webproject_mvc.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.DataProtection;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Enable response compression to reduce payload sizes
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        ["application/json", "text/html", "text/css", "application/javascript"]);
});

// Register HttpClient and ApiIntegrationService
builder.Services.AddHttpClient<ApiIntegrationService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

// Register PayMongoService
builder.Services.AddHttpClient<PayMongoService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Register application services
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IDataCleansingService, DataCleansingService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<WarehouseSummaryService>();
builder.Services.AddScoped<WarehouseTableService>();
builder.Services.AddScoped<SubscriptionService>();
builder.Services.AddSingleton<IEmailSender, EmailSender>();
builder.Services.AddHostedService<RetentionPolicyService>();
builder.Services.AddHostedService<DatabaseBackupService>();

var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    dataProtectionKeysPath = Path.Combine(builder.Environment.ContentRootPath, "keys");
}

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("it15_webproject_mvc");

// Configure cookie authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
.AddCookie(options =>
{
    options.LoginPath = "/Home/Login";
    options.LogoutPath = "/auth/logout";
    options.AccessDeniedPath = "/Home/Login?error=Access denied.";
    options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
    options.SlidingExpiration = true;
});

// Configure EF Core with the configured provider (SQL Server configured in appsettings.json)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
{
    // fallback to sqlite file if no connection string provided
    connectionString = "Data Source=./Data/it15.db";
    builder.Services.AddDbContext<it15_webproject_mvc.Data.ApplicationDbContext>(options =>
        options.UseSqlite(connectionString));
}
else
{
    builder.Services.AddDbContext<it15_webproject_mvc.Data.ApplicationDbContext>(options =>
        options.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.CommandTimeout(120);
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null);
        }));
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseResponseCompression();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// Ensure DB is created and seeded
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var context = services.GetRequiredService<it15_webproject_mvc.Data.ApplicationDbContext>();
        it15_webproject_mvc.Data.DbInitializer.Initialize(context, builder.Configuration, logger);
        logger.LogInformation("Database initialized and seeded successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred seeding the DB.");
        if (app.Environment.IsDevelopment())
            throw; // Re-throw only during development
    }
}


await app.RunAsync();
