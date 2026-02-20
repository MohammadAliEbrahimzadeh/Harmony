using Ahura.Web;
using Radenoor.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .InjectAddSwaggerGen()
    .InjectControllers()
    .InjectIdentity()
    .InjectCortext();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseMiddleware<ExceptionHandler>();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
