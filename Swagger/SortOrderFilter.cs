using BoardGameList.Attributes;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using BoardGameList.Attributes;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Linq;

namespace BoardGameList.Swagger
{
    public class SortOrderFilter : IParameterFilter
    {
        public void Apply(
            OpenApiParameter parameter,
            ParameterFilterContext context)
        {
            // Ensure context and ParameterInfo aren't null
            if (context.PropertyInfo == null && context.ParameterInfo == null) return;
            
            var attributes = context.ParameterInfo?
                .GetCustomAttributes(true)
                .Union(
                    context.ParameterInfo.ParameterType.GetProperties()
                    .Where(p => p.Name == parameter.Name)
                    .SelectMany(p => p.GetCustomAttributes(true))
                    )
                .OfType<SortOrderValidatorAttribute>();

            if (attributes != null)
            {
                foreach (var attribute in attributes)
                {
                    parameter.Schema.Extensions.Add(
                        "pattern",
                        new OpenApiString(string.Join("|", attribute.AllowedValues.Select(v => $"^{v}$")))
                        );
                }
            }
        }
    }
}
