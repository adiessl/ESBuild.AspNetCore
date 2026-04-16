var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseStaticFiles();

app.MapGet("/", () => "ESBuild.AspNetCore configuration override sample");

app.Run();
