﻿using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace common;

public class AzureHttpClient
{
    private readonly HttpPipeline pipeline;

    public Uri ResourceManagerEndpoint { get; }

    public AzureHttpClient(TokenCredential credential, AzureEnvironment environment)
    {
        this.pipeline = GetPipeline(credential, environment);
        this.ResourceManagerEndpoint = new Uri(environment.ResourceManagerEndpoint);
    }

    private static HttpPipeline GetPipeline(TokenCredential credential, AzureEnvironment environment)
    {
        var scope = new Uri(environment.ResourceManagerEndpoint).AppendPath(".default").ToString();
        var policy = new BearerTokenAuthenticationPolicy(credential, scope);

        return HttpPipelineBuilder.Build(ClientOptions.Default, policy);
    }

    public async Task<JsonObject> GetResourceAsJsonObject(Uri uri, CancellationToken cancellationToken)
    {
        var jsonObject = await TryGetResourceAsJsonObject(uri, cancellationToken);

        return jsonObject ?? throw new InvalidOperationException($"Could not get find resource at URL.");
    }

    public async IAsyncEnumerable<JsonObject> GetResourcesAsJsonObjects(Uri uri, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Uri? nextLinkUri = uri;

        while (nextLinkUri is not null)
        {
            var resourcesJson = await GetResourceAsJsonObject(nextLinkUri, cancellationToken);

            var resources = resourcesJson.GetJsonArrayProperty("value").Where(node => node is not null).Select(node => node!.AsObject());
            foreach (var resource in resources)
            {
                yield return resource;
            }

            var nextLinkUrl = resourcesJson.TryGetStringProperty("nextLink");
            nextLinkUri = Uri.TryCreate(nextLinkUrl, UriKind.Absolute, out nextLinkUri) ? nextLinkUri : null;
        }
    }

    public async Task<JsonObject?> TryGetResourceAsJsonObject(Uri uri, CancellationToken cancellationToken)
    {
        var request = pipeline.CreateRequest(RequestMethod.Get, uri);
        var response = await pipeline.SendRequestAsync(request, cancellationToken);

        return response.TryGetResourceJson();
    }

    public async Task PutJsonObject(Uri uri, JsonObject jsonObject, CancellationToken cancellationToken)
    {
        var request = pipeline.CreateRequest(RequestMethod.Put, uri, jsonObject);
        var response = await pipeline.SendRequestAsync(request, cancellationToken);
        response.ValidateSuccess();
    }

    public async Task PutStream(Uri uri, Stream stream, CancellationToken cancellationToken)
    {
        var request = pipeline.CreateRequest(RequestMethod.Put, uri, stream);
        var response = await pipeline.SendRequestAsync(request, cancellationToken);
        response.ValidateSuccess();
    }
}

internal static class AzureHttpExtensions
{
    public static Request CreateRequest(this HttpPipeline pipeline, RequestMethod requestMethod, Uri uri)
    {
        var request = pipeline.CreateRequest();

        request.Method = requestMethod;
        request.Uri.Reset(uri);

        return request;
    }

    public static Request CreateRequest(this HttpPipeline pipeline, RequestMethod requestMethod, Uri uri, Stream contentStream)
    {
        var request = pipeline.CreateRequest();

        request.Method = requestMethod;
        request.Uri.Reset(uri);
        request.Content = RequestContent.Create(contentStream);

        return request;
    }

    public static Request CreateRequest(this HttpPipeline pipeline, RequestMethod requestMethod, Uri uri, JsonObject jsonObject)
    {
        var request = pipeline.CreateRequest();

        request.Method = requestMethod;
        request.Uri.Reset(uri);
        request.Content = RequestContent.Create(JsonSerializer.SerializeToUtf8Bytes(jsonObject));
        request.Headers.Add("Content-type", "application/json");

        return request;
    }

    public static JsonObject? TryGetResourceJson(this Response response)
    {
        using var stream = response.TryGetResourceStream();

        if (stream is null)
        {
            return null;
        }
        else
        {
            var nodeOptions = new JsonNodeOptions { PropertyNameCaseInsensitive = true };
            return JsonNode.Parse(stream, nodeOptions)?.AsObject() ?? throw new InvalidOperationException("Failed to deserialize stream to JSON object.");
        }
    }

    private static Stream? TryGetResourceStream(this Response response)
    {
        return response.Status switch
        {
            404 => null,
            _ => response.ValidateSuccess().ContentStream ?? throw new InvalidOperationException("Resource content stream is null.")
        };
    }

    public static Response ValidateSuccess(this Response response)
    {
        return response.IsSuccesful()
            ? response
            : throw new InvalidOperationException($"REST API call failed. Status code is {response.Status}, response content is '{response.Content}'.");
    }

    private static bool IsSuccesful(this Response response)
    {
        return response.Status is >= 200 and <= 299;
    }
}