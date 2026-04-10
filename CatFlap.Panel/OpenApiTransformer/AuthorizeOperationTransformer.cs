// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;

namespace CatFlap.Panel.OpenApiTransformer;

/// <summary>
/// Transforms OpenAPI operations to include security requirements for actions decorated with the Authorize attribute.
/// </summary>
/// <remarks>This transformer adds a bearer security requirement to OpenAPI operations that require authorization,
/// based on the presence of the Authorize attribute on the controller action or its controller type. Use this class to
/// ensure that secured endpoints are properly documented in the OpenAPI specification, enabling client code generation
/// and UI tools to recognize authentication requirements.</remarks>
public class AuthorizeOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(Microsoft.OpenApi.OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        if (context.Description.ActionDescriptor is not ControllerActionDescriptor controllerAction)
        {
            return Task.CompletedTask;
        }

        var methodInfo = controllerAction.MethodInfo;
        var hasAuthorize =
            methodInfo.GetCustomAttributes(true).OfType<AuthorizeAttribute>().Any() ||
            controllerAction.ControllerTypeInfo.GetCustomAttributes(true).OfType<AuthorizeAttribute>().Any();

        if (hasAuthorize)
        {
            operation.Security =
            [
                new Microsoft.OpenApi.OpenApiSecurityRequirement
                {
                    [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("bearer", context.Document)] = []
                },
            ];
        }

        return Task.CompletedTask;
    }
}
