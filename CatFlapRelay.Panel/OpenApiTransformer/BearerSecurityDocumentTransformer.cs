// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CatFlapRelay.Panel.OpenApiTransformer;

/// <summary>
/// Transforms an OpenAPI document to include a bearer token security scheme for JWT authentication.
/// </summary>
/// <remarks>This transformer adds a 'bearer' security scheme to the document's components, enabling support for
/// HTTP Bearer authentication using JWTs. The scheme is added under the 'Authorization' header. Use this transformer
/// when generating OpenAPI documents that require JWT-based bearer authentication for API endpoints.</remarks>
public class BearerSecurityDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Name = "Authorization",
        };
        return Task.CompletedTask;
    }
}
