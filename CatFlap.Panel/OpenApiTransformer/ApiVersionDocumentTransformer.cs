// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CatFlap.Panel.OpenApiTransformer;

/// <summary>
/// Add Api Version
/// </summary>
public sealed class ApiVersionDocumentTransformer : IOpenApiDocumentTransformer
{
    private readonly IApiVersionDescriptionProvider? _provider;

    public ApiVersionDocumentTransformer(IApiVersionDescriptionProvider? provider = null)
    {
        _provider = provider;
    }

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        if (_provider != null)
        {
            var apiVersion = context.DocumentName;
            var versionDescription = _provider.ApiVersionDescriptions
                .FirstOrDefault(d => d.GroupName == apiVersion);

            if (versionDescription != null)
            {
                document.Info.Version = versionDescription.ApiVersion.ToString();
                document.Info.Title = $"CatFlap Panel API {versionDescription.ApiVersion}";

                if (versionDescription.IsDeprecated)
                {
                    document.Info.Description = $"⚠️ This API version has been deprecated. {document.Info.Description}";
                }

                return Task.CompletedTask;
            }
        }

        document.Info.Version = context.DocumentName;
        document.Info.Title = $"CatFlap Panel API {context.DocumentName.ToUpperInvariant()}";

        return Task.CompletedTask;
    }
}
