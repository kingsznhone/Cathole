// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CatHole.Panel.OpenApiTransformer;

/// <summary>
/// Removes hardcoded server URLs from the OpenAPI document.
/// This allows Swagger UI to construct URLs dynamically based on the current request.
/// Useful when API is behind a reverse proxy.
/// </summary>
public class RemoveServersDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Servers?.Clear();
        return Task.CompletedTask;
    }
}
