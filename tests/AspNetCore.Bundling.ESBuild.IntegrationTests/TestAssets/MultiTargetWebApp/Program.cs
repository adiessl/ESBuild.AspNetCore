var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseStaticFiles();

app.MapGet("/", () => "AspNetCore.Bundling.ESBuild multitarget sample");

app.Run();
