using Cortex.Mediator.DependencyInjection;
using FluentValidation;
using Harmony.Application.Contract.Requests;
using Microsoft.OpenApi;

namespace Ahura.Web;

internal static class DependencyInjectionExtension
{
    internal static IServiceCollection InjectControllers(this IServiceCollection services) =>
        services.AddControllers().Services;


    internal static IServiceCollection InjectCortext(this IServiceCollection services) =>
        services.AddCortexMediator(new[] { typeof(Program), typeof(AddPostHandler), typeof(AddPostDto) }, options => options.AddDefaultBehaviors());


    internal static IServiceCollection InjectAddSwaggerGen(this IServiceCollection services) =>
       services.AddSwaggerGen(c =>
       {
           c.SwaggerDoc("v1", new OpenApiInfo { Title = "Harmony", Version = "v1" });

           c.CustomSchemaIds(type => type.FullName);

           c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
           {
               Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
               Name = "Authorization",
               In = ParameterLocation.Header,
               Type = SecuritySchemeType.ApiKey,
               Scheme = "Bearer"
           });
       });
}
