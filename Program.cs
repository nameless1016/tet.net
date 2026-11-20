using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using TetNET;
using TetNET.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

// ── Supabase ─────────────────────────────────────────────────────────────────
var supabaseUrl = builder.Configuration["Supabase:Url"]
    ?? throw new InvalidOperationException("Supabase:Url is not configured in appsettings.json");
var supabaseKey = builder.Configuration["Supabase:Key"]
    ?? throw new InvalidOperationException("Supabase:Key is not configured in appsettings.json");

var supabase = new Supabase.Client(supabaseUrl, supabaseKey,
    new Supabase.SupabaseOptions { AutoConnectRealtime = false });

await supabase.InitializeAsync();

builder.Services.AddSingleton(supabase);
builder.Services.AddSingleton<SupabaseService>();
// ─────────────────────────────────────────────────────────────────────────────

await builder.Build().RunAsync();
