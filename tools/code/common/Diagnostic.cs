﻿using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace common;

public sealed record DiagnosticName : NonEmptyString
{
    private DiagnosticName(string value) : base(value)
    {
    }

    public static DiagnosticName From(string value) => new(value);

    public static DiagnosticName From(DiagnosticInformationFile file)
    {
        var jsonObject = file.ReadAsJsonObject();
        var diagnostic = Diagnostic.FromJsonObject(jsonObject);

        return new DiagnosticName(diagnostic.Name);
    }
}

public sealed record DiagnosticUri : UriRecord
{
    public DiagnosticUri(Uri value) : base(value)
    {
    }

    public static DiagnosticUri From(ServiceUri serviceUri, DiagnosticName diagnosticName) =>
        new(UriExtensions.AppendPath(serviceUri, "diagnostics").AppendPath(diagnosticName));
}

public sealed record DiagnosticsDirectory : DirectoryRecord
{
    private static readonly string name = "diagnostics";

    private DiagnosticsDirectory(RecordPath path) : base(path)
    {
    }

    public static DiagnosticsDirectory From(ServiceDirectory serviceDirectory) =>
        new(serviceDirectory.Path.Append(name));

    public static DiagnosticsDirectory? TryFrom(ServiceDirectory serviceDirectory, DirectoryInfo? directory) =>
        name.Equals(directory?.Name) && serviceDirectory.Path.PathEquals(directory.Parent?.FullName)
        ? new(RecordPath.From(directory.FullName))
        : null;
}

public sealed record DiagnosticInformationFile : FileRecord
{
    private static readonly string name = "diagnosticInformation.json";

    public DiagnosticInformationFile(RecordPath path) : base(path)
    {
    }

    public static DiagnosticInformationFile From(DiagnosticsDirectory diagnosticsDirectory, DiagnosticName diagnosticName)
        => new(diagnosticsDirectory.Path.Append(diagnosticName)
                                     .Append(name));

    public static DiagnosticInformationFile? TryFrom(ServiceDirectory serviceDirectory, FileInfo file) =>
        name.Equals(file.Name) && DiagnosticsDirectory.TryFrom(serviceDirectory, file.Directory?.Parent) is not null
        ? new(RecordPath.From(file.FullName))
        : null;
}

public sealed record Diagnostic([property: JsonPropertyName("name")] string Name, [property: JsonPropertyName("properties")] Diagnostic.DiagnosticContractProperties Properties)
{
    public record DiagnosticContractProperties([property: JsonPropertyName("loggerId")] string LoggerId)
    {
        [JsonPropertyName("alwaysLog")]
        public string? AlwaysLog { get; init; }

        [JsonPropertyName("backend")]
        public PipelineDiagnosticSettings? Backend { get; init; }

        [JsonPropertyName("frontend")]
        public PipelineDiagnosticSettings? Frontend { get; init; }

        [JsonPropertyName("httpCorrelationProtocol")]
        public string? HttpCorrelationProtocol { get; init; }

        [JsonPropertyName("logClientIp")]
        public bool? LogClientIp { get; init; }

        [JsonPropertyName("operationNameFormat")]
        public string? OperationNameFormat { get; init; }

        [JsonPropertyName("sampling")]
        public SamplingSettings? Sampling { get; init; }

        [JsonPropertyName("verbosity")]
        public string? Verbosity { get; init; }
    }

    public record PipelineDiagnosticSettings
    {
        [JsonPropertyName("request")]
        public HttpMessageDiagnostic? Request { get; init; }

        [JsonPropertyName("response")]
        public HttpMessageDiagnostic? Response { get; init; }
    }

    public record HttpMessageDiagnostic
    {
        [JsonPropertyName("body")]
        public BodyDiagnosticSettings? Body { get; init; }

        [JsonPropertyName("dataMasking")]
        public DataMasking? DataMasking { get; init; }

        [JsonPropertyName("headers")]
        public string[]? Headers { get; init; }
    }

    public record BodyDiagnosticSettings
    {
        [JsonPropertyName("bytes")]
        public int? Bytes { get; init; }
    }

    public record DataMasking
    {
        [JsonPropertyName("headers")]
        public DataMaskingEntity[]? Headers { get; init; }

        [JsonPropertyName("queryParams")]
        public DataMaskingEntity[]? QueryParams { get; init; }
    }

    public record DataMaskingEntity
    {
        [JsonPropertyName("mode")]
        public string? Mode { get; init; }

        [JsonPropertyName("value")]
        public string? Value { get; init; }
    }

    public record SamplingSettings
    {
        [JsonPropertyName("percentage")]
        public double? Percentage { get; init; }

        [JsonPropertyName("samplingType")]
        public string? SamplingType { get; init; }
    }

    public JsonObject ToJsonObject() =>
        JsonSerializer.SerializeToNode(this)?.AsObject() ?? throw new InvalidOperationException("Could not serialize object.");

    public static Diagnostic FromJsonObject(JsonObject jsonObject) =>
        JsonSerializer.Deserialize<Diagnostic>(jsonObject) ?? throw new InvalidOperationException("Could not deserialize object.");

    public static Uri GetListByServiceUri(ServiceUri serviceUri) => UriExtensions.AppendPath(serviceUri, "diagnostics");
}
