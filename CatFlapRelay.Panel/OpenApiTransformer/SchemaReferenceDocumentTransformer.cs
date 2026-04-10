// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CatFlapRelay.Panel.OpenApiTransformer;

/// <summary>
/// Transforms an OpenAPI document by simplifying schema definitions for required properties that use oneOf with nullability.
/// </summary>
public class SchemaReferenceDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        if (document.Components?.Schemas == null)
            return Task.CompletedTask;

        var schemasToUpdate = new List<(string Key, string PropertyKey, IOpenApiSchema NewSchema)>();

        foreach (var schemaEntry in document.Components.Schemas)
        {
            var schemaKey = schemaEntry.Key;
            var schema = schemaEntry.Value;

            if (schema.Properties != null && schema.Required != null)
            {
                foreach (var propertyEntry in schema.Properties)
                {
                    var propertyKey = propertyEntry.Key;
                    var propertySchema = propertyEntry.Value;

                    bool isRequired = schema.Required.Contains(propertyKey);

                    if (isRequired && propertySchema.OneOf?.Count > 0)
                    {
                        var nonNullSchemas = propertySchema.OneOf
                            .Where(s => s.Type != JsonSchemaType.Null)
                            .ToList();

                        if (nonNullSchemas.Count > 0)
                        {
                            var targetSchema = nonNullSchemas.Count == 1
                                ? nonNullSchemas[0]
                                : CreateCombinedSchema(nonNullSchemas);

                            schemasToUpdate.Add((schemaKey, propertyKey, targetSchema));
                        }
                    }
                }
            }
        }

        foreach (var (schemaKey, propertyKey, newSchema) in schemasToUpdate)
        {
            if (document.Components.Schemas.TryGetValue(schemaKey, out var schema) &&
                schema.Properties != null)
            {
                var newProperties = new Dictionary<string, IOpenApiSchema>();

                foreach (var kvp in schema.Properties)
                {
                    if (kvp.Key == propertyKey)
                    {
                        newProperties[kvp.Key] = newSchema;
                    }
                    else
                    {
                        newProperties[kvp.Key] = kvp.Value;
                    }
                }

                schema.Properties.Clear();
                foreach (var kvp in newProperties)
                {
                    schema.Properties[kvp.Key] = kvp.Value;
                }

                Debug.WriteLine($"✅ Simplified schema for {schemaKey}.{propertyKey}");
            }
        }

        return Task.CompletedTask;
    }

    private static OpenApiSchema CreateCombinedSchema(List<IOpenApiSchema> schemas) =>
        new()
        {
            AnyOf = schemas
        };
}
