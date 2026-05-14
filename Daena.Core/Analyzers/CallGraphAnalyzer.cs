using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Daena.Core;

public class DtoInfo
{
    public string Name { get; set; } = "";
    public List<PropertyInfo2> Properties { get; set; } = new();
}

public class PropertyInfo2
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
}

public class CallNode
{
    public int Order { get; set; }
    public string Category { get; set; } = "";
    public string DeclaringType { get; set; } = "";
    public string MethodName { get; set; } = "";
    public string Summary { get; set; } = "";
    public DtoInfo? Accepts { get; set; }
    public DtoInfo? Returns { get; set; }
}

public class EndpointAnalysis
{
    public string HttpMethod { get; set; } = "";
    public string Route { get; set; } = "";
    public string Controller { get; set; } = "";
    public string Action { get; set; } = "";
    public bool RequiresAuthentication { get; set; }
    public DtoInfo? RequestBody { get; set; }
    public DtoInfo? ResponseBody { get; set; }
    public List<CallNode> CallFlow { get; set; } = new();
}

public class RichApiAnalyzer
{
    private readonly AssemblyDefinition _assembly;
    private readonly ReaderParameters _readerParams;
    private readonly string _rootNamespace;
    private readonly string _rootAssemblyName;

    public RichApiAnalyzer(string assemblyPath)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(assemblyPath)!);
        _readerParams = new ReaderParameters
        {
            AssemblyResolver = resolver,
            ReadSymbols = false
        };
        _assembly = AssemblyDefinition.ReadAssembly(assemblyPath, _readerParams);
        _rootAssemblyName = _assembly.Name.Name;
        // If you know your root namespace, set it explicitly instead of guessing.
        _rootNamespace = "Harmony.Api.Controllers"; // TODO: replace
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Entry points
    // ─────────────────────────────────────────────────────────────────────────────
    public List<EndpointAnalysis> AnalyzeControllers(bool includeCallFlow = true)
    {
        var endpoints = new List<EndpointAnalysis>();
        foreach (var typeDef in _assembly.MainModule.Types)
        {
            if (!IsApiController(typeDef)) continue;
            foreach (var method in typeDef.Methods)
            {
                if (!IsHttpAction(method)) continue;
                var analysis = AnalyzeAction(typeDef, method, includeCallFlow);
                if (analysis != null)
                    endpoints.Add(analysis);
            }
        }
        return endpoints;
    }

    public EndpointAnalysis? AnalyzeEndpoint(Type controllerType, string actionName, bool includeCallFlow = true)
    {
        var typeDef = _assembly.MainModule.Types
            .FirstOrDefault(t => t.FullName == controllerType.FullName);
        if (typeDef == null) return null;
        var method = typeDef.Methods.FirstOrDefault(m => m.Name == actionName);
        if (method == null) return null;
        return AnalyzeAction(typeDef, method, includeCallFlow);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Controller/action discovery (Swagger-like core)
    // ─────────────────────────────────────────────────────────────────────────────
    private EndpointAnalysis? AnalyzeAction(TypeDefinition controllerType, MethodDefinition actionMethod, bool includeCallFlow)
    {
        var route = BuildRoute(controllerType, actionMethod);
        var httpMethod = GetHttpMethod(actionMethod);
        var analysis = new EndpointAnalysis
        {
            Controller = NormalizeControllerName(controllerType.Name),
            Action = actionMethod.Name,
            HttpMethod = httpMethod,
            Route = route,
            RequiresAuthentication = HasAttribute(controllerType, "AuthorizeAttribute") ||
                                     HasAttribute(actionMethod, "AuthorizeAttribute"),
            RequestBody = ExtractRequestBodyDto(actionMethod),
            ResponseBody = ExtractResponseDto(actionMethod.ReturnType)
        };
        if (includeCallFlow)
        {
            int order = 1;
            var visited = new HashSet<string>();
            analysis.CallFlow = BuildCallFlow(actionMethod, visited, ref order);
        }
        return analysis;
    }

    private static string NormalizeControllerName(string typeName)
    {
        return typeName.Replace("Controller", "");
    }

    private bool IsApiController(TypeDefinition typeDef)
    {
        var hasApiControllerAttr = typeDef.CustomAttributes.Any(a =>
            a.AttributeType.FullName == "Microsoft.AspNetCore.Mvc.ApiControllerAttribute" ||
            a.AttributeType.Name == "ApiControllerAttribute");
        var baseType = typeDef.BaseType?.FullName;
        var inheritsControllerBase =
            baseType == "Microsoft.AspNetCore.Mvc.ControllerBase" ||
            baseType == "Microsoft.AspNetCore.Mvc.Controller";
        return hasApiControllerAttr || inheritsControllerBase;
    }

    private bool IsHttpAction(MethodDefinition method)
    {
        var verbAttrs = new HashSet<string>
        {
            "HttpGetAttribute", "HttpPostAttribute", "HttpPutAttribute",
            "HttpDeleteAttribute", "HttpPatchAttribute", "HttpOptionsAttribute",
        };
        return method.CustomAttributes.Any(a => verbAttrs.Contains(a.AttributeType.Name));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Route + HTTP verb
    // ─────────────────────────────────────────────────────────────────────────────
    private string BuildRoute(TypeDefinition controllerType, MethodDefinition actionMethod)
    {
        var controllerRoute = GetRouteTemplate(controllerType)
            ?.Replace("[controller]", NormalizeControllerName(controllerType.Name))
            ?? NormalizeControllerName(controllerType.Name);
        var actionRoute = GetRouteTemplate(actionMethod);
        if (!string.IsNullOrWhiteSpace(actionRoute))
            return $"{controllerRoute}/{actionRoute}".TrimEnd('/');
        return controllerRoute;
    }

    private string? GetRouteTemplate(ICustomAttributeProvider provider)
    {
        var attr = provider.CustomAttributes
            .FirstOrDefault(a => a.AttributeType.Name == "RouteAttribute");
        return attr?.ConstructorArguments.FirstOrDefault().Value as string;
    }

    private string GetHttpMethod(MethodDefinition method)
    {
        foreach (var attr in method.CustomAttributes)
        {
            var name = attr.AttributeType.Name;
            if (name == "HttpGetAttribute") return "GET";
            if (name == "HttpPostAttribute") return "POST";
            if (name == "HttpPutAttribute") return "PUT";
            if (name == "HttpDeleteAttribute") return "DELETE";
            if (name == "HttpPatchAttribute") return "PATCH";
            if (name == "HttpOptionsAttribute") return "OPTIONS";
        }
        return "GET";
    }

    private bool HasAttribute(ICustomAttributeProvider provider, string attrName) =>
            provider.CustomAttributes.Any(a => a.AttributeType.Name == attrName);

    // ─────────────────────────────────────────────────────────────────────────────
    // DTO extraction (best-effort)
    // ─────────────────────────────────────────────────────────────────────────────
    private DtoInfo? ExtractRequestBodyDto(MethodDefinition method)
    {
        var requestParam = method.Parameters.FirstOrDefault(p =>
            !IsPrimitive(p.ParameterType) &&
            p.ParameterType.Name != "CancellationToken");
        if (requestParam == null) return null;
        return BuildDtoInfo(requestParam.ParameterType);
    }

    private DtoInfo? ExtractResponseDto(TypeReference returnType)
    {
        var typeRef = UnwrapTask(returnType);
        if (typeRef.Name is "IActionResult" or "ActionResult")
            return new DtoInfo { Name = typeRef.Name };
        if (typeRef is GenericInstanceType art && art.Name.StartsWith("ActionResult"))
        {
            var inner = art.GenericArguments.FirstOrDefault();
            return inner != null ? BuildDtoInfo(inner) : null;
        }
        return BuildDtoInfo(typeRef);
    }

    private TypeReference UnwrapTask(TypeReference returnType)
    {
        if (returnType is GenericInstanceType git && git.Name.StartsWith("Task"))
        {
            var inner = git.GenericArguments.FirstOrDefault();
            if (inner != null) return inner;
        }
        return returnType;
    }

    private DtoInfo BuildDtoInfo(TypeReference typeRef)
    {
        var dto = new DtoInfo { Name = typeRef.Name };
        TypeDefinition? typeDef = null;
        try { typeDef = typeRef.Resolve(); } catch { }
        if (typeDef != null)
        {
            dto.Properties = typeDef.Properties
                .Where(p => p.Name != "EqualityContract")
                .Select(p => new PropertyInfo2
                {
                    Name = p.Name,
                    Type = FriendlyName(p.PropertyType)
                })
                .ToList();
        }
        return dto;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Call flow analysis with MediatR & Async support
    // ─────────────────────────────────────────────────────────────────────────────

    private List<CallNode> BuildCallFlow(MethodDefinition method, HashSet<string> visited, ref int order)
    {
        var nodes = new List<CallNode>();

        if (method == null || !method.HasBody) return nodes;

        // Redirect Async Methods to their compiler-generated MoveNext method
        var asyncAttr = method.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "AsyncStateMachineAttribute");
        if (asyncAttr != null && asyncAttr.ConstructorArguments.Count > 0)
        {
            var stateMachineType = asyncAttr.ConstructorArguments[0].Value as TypeReference;
            var resolvedStateMachine = stateMachineType?.Resolve();
            var moveNextMethod = resolvedStateMachine?.Methods.FirstOrDefault(m => m.Name == "MoveNext");

            if (moveNextMethod != null && moveNextMethod.HasBody)
            {
                method = moveNextMethod;
            }
        }

        if (!visited.Add(method.FullName)) return nodes;

        foreach (var instr in method.Body.Instructions)
        {
            if (instr.OpCode.Code == Code.Call || instr.OpCode.Code == Code.Callvirt)
            {
                if (instr.Operand is MethodReference calledMethodRef)
                {
                    var calledMethod = calledMethodRef.Resolve();
                    if (calledMethod == null) continue;

                    string declType = calledMethod.DeclaringType?.Name ?? "Unknown";
                    string methodName = calledMethod.Name;

                    // Skip Noise
                    if (IsFrameworkNoise(calledMethod.DeclaringType) || IsNoise(declType, methodName))
                        continue;

                    bool isUser = calledMethod.Module != null && calledMethod.Module.Name == _assembly.MainModule.Name;

                    var node = new CallNode
                    {
                        Order = order++,
                        DeclaringType = declType,
                        MethodName = methodName,
                        Category = Categorize(declType, methodName),
                        Summary = BuildSummary(declType, methodName, calledMethod)
                    };

                    // --- MEDIATOR SUPPORT & EXACT DTO EXTRACTION ---
                    if (declType.Contains("Mediator") && methodName.Contains("Send") && calledMethodRef is GenericInstanceMethod gim)
                    {
                        TypeReference? cmdType = null;
                        TypeReference? resType = null;

                        if (gim.GenericArguments.Count >= 2)
                        {
                            cmdType = gim.GenericArguments[0];
                            resType = gim.GenericArguments[1];
                        }
                        else if (gim.GenericArguments.Count == 1)
                        {
                            // MediatR often infers the command type and only exposes the response type generically
                            resType = gim.GenericArguments[0];
                        }

                        var handlerMethod = FindMediatorHandler(cmdType, resType);

                        if (handlerMethod != null)
                        {
                            // Get the exact Input and Output from the concrete Handle method
                            var actualCmd = handlerMethod.Parameters.FirstOrDefault(p => p.ParameterType.Name != "CancellationToken");
                            if (actualCmd != null) node.Accepts = BuildDtoInfo(actualCmd.ParameterType);

                            if (handlerMethod.ReturnType != null && handlerMethod.ReturnType.Name != "Task" && handlerMethod.ReturnType.Name != "Void")
                                node.Returns = ExtractResponseDto(handlerMethod.ReturnType);

                            nodes.Add(node);

                            // Recurse directly into the concrete Handler
                            RecurseInto(handlerMethod, nodes, visited, ref order, depth: 1);
                            continue;
                        }
                    }

                    // --- STANDARD EXTRACTION (Non-Mediator) ---
                    if (calledMethod.HasParameters)
                    {
                        var firstComplex = calledMethod.Parameters
                            .FirstOrDefault(p => !IsPrimitive(p.ParameterType) && p.ParameterType.Name != "CancellationToken");
                        if (firstComplex != null) node.Accepts = BuildDtoInfo(firstComplex.ParameterType);
                    }

                    if (calledMethod.ReturnType != null && calledMethod.ReturnType.Name != "Void" && calledMethod.ReturnType.Name != "Task")
                    {
                        node.Returns = ExtractResponseDto(calledMethod.ReturnType);
                    }

                    nodes.Add(node);

                    // Recurse into standard user code
                    if (isUser && !declType.Contains("Mediator"))
                    {
                        RecurseInto(calledMethod, nodes, visited, ref order, depth: 1);
                    }
                }
            }
        }

        return nodes;
    }

    private void RecurseInto(MethodDefinition method, List<CallNode> nodes, HashSet<string> visited, ref int order, int depth)
    {
        if (depth >= 10 || method == null || !method.HasBody)
            return;

        // Redirect Async Methods to their compiler-generated MoveNext method
        var asyncAttr = method.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "AsyncStateMachineAttribute");
        if (asyncAttr != null && asyncAttr.ConstructorArguments.Count > 0)
        {
            var stateMachineType = asyncAttr.ConstructorArguments[0].Value as TypeReference;
            var resolvedStateMachine = stateMachineType?.Resolve();
            var moveNextMethod = resolvedStateMachine?.Methods.FirstOrDefault(m => m.Name == "MoveNext");

            if (moveNextMethod != null && moveNextMethod.HasBody)
            {
                method = moveNextMethod;
            }
        }

        if (!visited.Add(method.FullName)) return;

        foreach (var instr in method.Body.Instructions)
        {
            if (instr.OpCode.Code == Code.Call || instr.OpCode.Code == Code.Callvirt)
            {
                if (instr.Operand is MethodReference calledMethodRef)
                {
                    var calledMethod = calledMethodRef.Resolve();
                    if (calledMethod == null) continue;

                    string declType = calledMethod.DeclaringType?.Name ?? "Unknown";
                    string methodName = calledMethod.Name;

                    // Skip Noise
                    if (IsFrameworkNoise(calledMethod.DeclaringType) || IsNoise(declType, methodName))
                        continue;

                    bool isUser = calledMethod.Module != null && calledMethod.Module.Name == _assembly.MainModule.Name;

                    var node = new CallNode
                    {
                        Order = order++,
                        DeclaringType = declType,
                        MethodName = methodName,
                        Category = Categorize(declType, methodName),
                        Summary = BuildSummary(declType, methodName, calledMethod)
                    };

                    // --- MEDIATOR SUPPORT & EXACT DTO EXTRACTION ---
                    if (declType.Contains("Mediator") && methodName.Contains("Send") && calledMethodRef is GenericInstanceMethod gim)
                    {
                        TypeReference? cmdType = null;
                        TypeReference? resType = null;

                        if (gim.GenericArguments.Count >= 2)
                        {
                            cmdType = gim.GenericArguments[0];
                            resType = gim.GenericArguments[1];
                        }
                        else if (gim.GenericArguments.Count == 1)
                        {
                            resType = gim.GenericArguments[0];
                        }

                        var handlerMethod = FindMediatorHandler(cmdType, resType);

                        if (handlerMethod != null)
                        {
                            var actualCmd = handlerMethod.Parameters.FirstOrDefault(p => p.ParameterType.Name != "CancellationToken");
                            if (actualCmd != null) node.Accepts = BuildDtoInfo(actualCmd.ParameterType);

                            if (handlerMethod.ReturnType != null && handlerMethod.ReturnType.Name != "Task" && handlerMethod.ReturnType.Name != "Void")
                                node.Returns = ExtractResponseDto(handlerMethod.ReturnType);

                            nodes.Add(node);

                            RecurseInto(handlerMethod, nodes, visited, ref order, depth + 1);
                            continue;
                        }
                    }

                    // --- STANDARD EXTRACTION (Non-Mediator) ---
                    if (calledMethod.HasParameters)
                    {
                        var firstComplex = calledMethod.Parameters
                            .FirstOrDefault(p => !IsPrimitive(p.ParameterType) && p.ParameterType.Name != "CancellationToken");
                        if (firstComplex != null) node.Accepts = BuildDtoInfo(firstComplex.ParameterType);
                    }

                    if (calledMethod.ReturnType != null && calledMethod.ReturnType.Name != "Void" && calledMethod.ReturnType.Name != "Task")
                    {
                        node.Returns = ExtractResponseDto(calledMethod.ReturnType);
                    }

                    nodes.Add(node);

                    // Recurse into standard user code
                    if (isUser && !declType.Contains("Mediator"))
                    {
                        RecurseInto(calledMethod, nodes, visited, ref order, depth + 1);
                    }
                }
            }
        }
    }

    private MethodDefinition? FindMediatorHandler(TypeReference? commandType, TypeReference? responseType)
    {
        foreach (var type in _assembly.MainModule.Types)
        {
            foreach (var intf in type.Interfaces)
            {
                if (intf.InterfaceType is GenericInstanceType git &&
                   (git.Name.Contains("ICommandHandler") || git.Name.Contains("IRequestHandler")))
                {
                    var cmdArg = git.GenericArguments.FirstOrDefault();
                    var resArg = git.GenericArguments.Skip(1).FirstOrDefault();

                    bool isMatch = false;

                    if (commandType != null && cmdArg != null && cmdArg.Name == commandType.Name)
                        isMatch = true;
                    else if (commandType == null && responseType != null && resArg != null && resArg.Name == responseType.Name)
                        isMatch = true;

                    if (isMatch)
                    {
                        return type.Methods.FirstOrDefault(m => m.Name == "Handle");
                    }
                }
            }
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private bool IsFrameworkNoise(TypeReference? type)
    {
        if (type == null) return false;
        var n = type.FullName ?? type.Name;
        return n.StartsWith("System.", StringComparison.Ordinal) ||
               n.StartsWith("Microsoft.", StringComparison.Ordinal) ||
               n.Contains("AsyncTaskMethodBuilder", StringComparison.Ordinal) ||
               n.Contains("IAsyncStateMachine", StringComparison.Ordinal) ||
               n.Contains("ExecutionContext", StringComparison.Ordinal) ||
               n.Contains("ThrowHelper", StringComparison.Ordinal) ||
               n.Contains("TaskScheduler", StringComparison.Ordinal) ||
               n.Contains("Thread", StringComparison.Ordinal);
    }

    private bool IsNoise(string typeName, string methodName) =>
            typeName is "Object" or "String" or "Task" or "Console" ||
            methodName is ".ctor" or "get_Result" or "GetAwaiter" or "ConfigureAwait" or "MoveNext" or "SetStateMachine";

    private string Categorize(string typeName, string methodName)
    {
        if (typeName.Contains("Mediator") || methodName.Contains("Send")) return "Mediator";
        if (typeName.Contains("Repository") || typeName.Contains("Repo")) return "Repository";
        if (typeName.Contains("DbContext") || typeName.Contains("Context")) return "Database";
        if (typeName.Contains("Service")) return "Service";
        if (typeName.Contains("Controller")) return "Controller";
        return "Internal";
    }

    private string BuildSummary(string typeName, string methodName, MethodReference method)
    {
        if (methodName.Contains("Send") && typeName.Contains("Mediator"))
            return $"Dispatches command/query via mediator";
        if (methodName.StartsWith("Get")) return $"Retrieves data from {typeName}";
        if (methodName.StartsWith("Add") || methodName.StartsWith("Create"))
            return $"Creates new entity via {typeName}";
        if (methodName.StartsWith("Update")) return $"Updates entity via {typeName}";
        if (methodName.StartsWith("Delete") || methodName.StartsWith("Remove"))
            return $"Deletes entity via {typeName}";
        return $"Calls {typeName}.{methodName}";
    }

    private bool IsPrimitive(TypeReference t) =>
            t.Name is "String" or "Int32" or "Int64" or "Boolean" or
                      "Guid" or "DateTime" or "Decimal" or "Double" or "Single";

    private string FriendlyName(TypeReference t)
    {
        if (t is GenericInstanceType git)
        {
            var core = t.Name.Split('`')[0];
            return $"{core}<{string.Join(", ", git.GenericArguments.Select(FriendlyName))}>";
        }
        return t.Name;
    }
}
