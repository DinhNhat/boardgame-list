using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace BoardGameList.Swagger
{
    internal class CustomDocumentFilter : IDocumentFilter
    {
        public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
        {
            // 1. Define the namespace that contains your Entity classes
            string entityNamespace = "BoardGameList.Models";
            
            // 2. Find all schemas that do NOT belong to that namespace
            var schemasToRemove = swaggerDoc.Components.Schemas
                .Where(schema => 
                {
                    // We find the C# type associated with this schema key
                    // Use context.SchemaGenerator to find the type information
                    var type = context.SchemaRepository.Schemas.ContainsKey(schema.Key) 
                        ? context.SchemaRepository.Schemas[schema.Key].Reference?.Id 
                        : null;

                    // Since we can't easily map Key -> Type in the Filter, 
                    // we usually check if the Key (class name) exists in our target assembly/namespace
                    var reflectedType = System.AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => a.GetTypes())
                        .FirstOrDefault(t => t.Name == schema.Key);

                    return reflectedType != null && !reflectedType.FullName.StartsWith(entityNamespace) && !reflectedType.FullName.StartsWith("BoardGameList.DTO");
                })
                .ToList();

            // 3. Remove them from the document
            foreach (var schema in schemasToRemove)
            {
                swaggerDoc.Components.Schemas.Remove(schema.Key);
            }
            
            swaggerDoc.Info.Title = "My BoardGameList Web API";
        }
    }
}
