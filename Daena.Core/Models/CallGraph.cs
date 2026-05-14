using System;
using System.Collections.Generic;
using System.Text;

namespace Daena.Core.Models;

public record RichEndpointAnalysis
{
    // API Identity
    public string Controller { get; set; } = "";
    public string Action { get; set; } = "";
    public string HttpMethod { get; set; } = "";
    public string Route { get; set; } = "";

    // Request
    public DtoInfo? RequestBody { get; set; }
    public List<ParameterInfo> RouteParameters { get; set; } = [];
    public List<ParameterInfo> QueryParameters { get; set; } = [];

    // Response
    public DtoInfo? ResponseBody { get; set; }

    // Call Flow
    public List<MethodCall> CallFlow { get; set; } = [];

    // Auth
    public bool RequiresAuthentication { get; set; }
    public List<string> RequiredRoles { get; set; } = [];
}

public record DtoInfo
{
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public List<PropertyInfo> Properties { get; set; } = [];
}

public record PropertyInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool IsRequired { get; set; }
    public object? DefaultValue { get; set; }
}

public record ParameterInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Source { get; set; } = ""; // "route", "query", "header", "body"
}

public record MethodCall
{
    public int Order { get; set; }
    public string MethodName { get; set; } = "";
    public string DeclaringType { get; set; } = "";
    public string Category { get; set; } = ""; // Service, Repository, etc.

    public DtoInfo? Accepts { get; set; }
    public DtoInfo? Returns { get; set; }

    public string Summary { get; set; } = "";
}
