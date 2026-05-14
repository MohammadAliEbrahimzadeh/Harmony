using Ahura.Web;
using Daena.Core;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .InjectAddSwaggerGen()
    .InjectControllers()
    .InjectCortext();


var assemblyPath = Assembly.GetEntryAssembly()?.Location;
if (assemblyPath == null) return;

var analyzer = new RichApiAnalyzer(assemblyPath);

var analysis = analyzer.AnalyzeEndpoint(
    typeof(Harmony.Api.Controllers.WeatherForecastController),
    "Test"
);

if (analysis == null)
{
    Console.WriteLine("Could not analyze endpoint");
    return;
}

Console.WriteLine("\n=== API ANALYSIS ===\n");
Console.WriteLine($"{analysis.HttpMethod} {analysis.Route}");
Console.WriteLine($"Controller: {analysis.Controller}");
Console.WriteLine($"Action: {analysis.Action}");
Console.WriteLine($"Auth Required: {analysis.RequiresAuthentication}");

if (analysis.RequestBody != null)
{
    Console.WriteLine($"\nRequest Body: {analysis.RequestBody.Name}");
    foreach (var prop in analysis.RequestBody.Properties)
    {
        Console.WriteLine($"  - {prop.Name}: {prop.Type}");
    }
}

if (analysis.ResponseBody != null)
{
    Console.WriteLine($"\nResponse: {analysis.ResponseBody.Name}");
    foreach (var prop in analysis.ResponseBody.Properties)
    {
        Console.WriteLine($"  - {prop.Name}: {prop.Type}");
    }
}

Console.WriteLine("\n=== CALL FLOW ===\n");
foreach (var call in analysis.CallFlow)
{
    Console.WriteLine($"{call.Order}. [{call.Category}] {call.DeclaringType}.{call.MethodName}");
    Console.WriteLine($"   {call.Summary}");
    if (call.Accepts != null)
        Console.WriteLine($"   Accepts: {call.Accepts.Name}");
    if (call.Returns != null)
        Console.WriteLine($"   Returns: {call.Returns.Name}");
}

Console.WriteLine("Press any key to exit...");
Console.ReadKey();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
