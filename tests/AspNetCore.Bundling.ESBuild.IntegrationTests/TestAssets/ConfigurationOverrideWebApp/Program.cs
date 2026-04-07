var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseStaticFiles();

app.MapGet("/", () => "AspNetCore.Bundling.ESBuild configuration override sample");

app.Run();
