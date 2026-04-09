// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CatHole.Panel.OpenApiTransformer;

/// <summary>
/// Provides an OpenAPI schema transformer that updates schema titles for generic types to improve clarity in
/// documentation and UI displays.
/// </summary>
/// <remarks>This transformer is typically used to ensure that generic types are represented with their concrete
/// type arguments in OpenAPI schemas, making models such as ApiRequest<RegisterRequest> appear with their full type
/// signature in tools like Swagger UI. This can help consumers of the API better understand the structure of generic
/// models. The transformer does not modify other schema properties.</remarks>
public class GenericTypeSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken ct)
    {
        var type = context.JsonTypeInfo.Type;

        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            var genericTypeDef = type.GetGenericTypeDefinition();
            var genericArgs = type.GetGenericArguments();

            var genericTypeName = genericTypeDef.Name.Replace("`" + genericArgs.Length, "");
            var genericArgsNames = string.Join(", ", genericArgs.Select(t => t.Name));

            if (string.IsNullOrEmpty(schema.Title))
            {
                schema.Title = $"{genericTypeName}<{genericArgsNames}>";
            }
        }

        return Task.CompletedTask;
    }
}
