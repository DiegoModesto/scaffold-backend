var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
await app.RunAsync();

namespace Gateway
{
    public partial class Program;
}
