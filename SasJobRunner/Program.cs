using SasJobRunner.Services;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ── Service registrations ────────────────────────────────────────────────────

// MVC with views (anti-forgery is registered automatically by AddControllersWithViews)
builder.Services.AddControllersWithViews();

// Typed HttpClient for the Altair SLC Hub
builder.Services.AddHttpClient<SlcHubClient>();

// Distributed memory cache required by session middleware
builder.Services.AddDistributedMemoryCache();

// Server-side session (stores Bearer Token, active Job ID, and job status)
builder.Services.AddSession(opts =>
{
    opts.IdleTimeout = TimeSpan.FromMinutes(
        config.GetValue<double>("Session:TimeoutMinutes"));
    opts.Cookie.HttpOnly = true;
    opts.Cookie.IsEssential = true;
});

// Anti-forgery: accept the token from the custom request header used by fetch()
builder.Services.AddAntiforgery(opts =>
    opts.HeaderName = "RequestVerificationToken");

// ── Kestrel: 1 MB global request body limit (Req 7.7) ───────────────────────
builder.WebHost.ConfigureKestrel(opts =>
    opts.Limits.MaxRequestBodySize = 1_048_576);

// ── Build the application ────────────────────────────────────────────────────
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for
    // production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// Session must be added before routing so controllers can read/write session data
app.UseSession();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
