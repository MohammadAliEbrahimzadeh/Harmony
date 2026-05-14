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
        // Common heuristic: take the namespace prefix from the first controller type.
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

    private bool IsUserCode(TypeReference? declaringType)
    {
        if (declaringType == null) return false;
        try
        {
            var resolved = declaringType.Resolve();
            if (resolved == null) return false;
            var asm = resolved.Module?.Assembly?.Name?.Name;
            if (asm != _rootAssemblyName) return false;
            // Allow compiler-generated async state machines (<Test>d__2) that are still in your assembly
            // (No name-based rejection here.)
            return true;
        }
        catch
        {
            return false;
        }
    }

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






    // Optional compatibility API: analyze one specific controller/action by type+name
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
        // Basic route + verb
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
            analysis.CallFlow = BuildCallFlow(actionMethod);
        return analysis;
    }
    private static string NormalizeControllerName(string typeName)
    {
        // "WeatherForecastController" => "WeatherForecast"
        return typeName.Replace("Controller", "");
    }
    private bool IsApiController(TypeDefinition typeDef)
    {
        // Option 1: [ApiController]
        var hasApiControllerAttr = typeDef.CustomAttributes.Any(a =>
            a.AttributeType.FullName == "Microsoft.AspNetCore.Mvc.ApiControllerAttribute" ||
            a.AttributeType.Name == "ApiControllerAttribute");
        // Option 2: inherits ControllerBase/Controller
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
            "HttpGetAttribute",
            "HttpPostAttribute",
            "HttpPutAttribute",
            "HttpDeleteAttribute",
            "HttpPatchAttribute",
            "HttpOptionsAttribute",
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
        // Matches only [RouteAttribute] (best-effort MVP).
        // You can expand later to check HttpGet("...") templates too.
        var attr = provider.CustomAttributes
            .FirstOrDefault(a => a.AttributeType.Name == "RouteAttribute");
        // Common case: [Route("test")] => ctor arg string
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
    // ─────────────────────────────────────────────────────────────────────────────
    // Auth
    // ─────────────────────────────────────────────────────────────────────────────
    private bool HasAttribute(ICustomAttributeProvider provider, string attrName) =>
            provider.CustomAttributes.Any(a => a.AttributeType.Name == attrName);
    // ─────────────────────────────────────────────────────────────────────────────
    // DTO extraction (best-effort)
    // ─────────────────────────────────────────────────────────────────────────────
    private DtoInfo? ExtractRequestBodyDto(MethodDefinition method)
    {
        // MVP rules:
        // - first parameter that is not CancellationToken
        // - not primitive
        // - not "simple" system types
        // If you have [FromBody] later, we can prefer it.
        var requestParam = method.Parameters.FirstOrDefault(p =>
            !IsPrimitive(p.ParameterType) &&
            p.ParameterType.Name != "CancellationToken");
        if (requestParam == null) return null;
        return BuildDtoInfo(requestParam.ParameterType);
    }
    private DtoInfo? ExtractResponseDto(TypeReference returnType)
    {
        var typeRef = UnwrapTask(returnType);
        // IActionResult / ActionResult — cannot know real T reliably
        if (typeRef.Name is "IActionResult" or "ActionResult")
            return new DtoInfo { Name = typeRef.Name };
        // ActionResult<T> => unwrap T
        if (typeRef is GenericInstanceType art && art.Name.StartsWith("ActionResult"))
        {
            var inner = art.GenericArguments.FirstOrDefault();
            return inner != null ? BuildDtoInfo(inner) : null;
        }
        // If response is still generic Task<...> we already unwrapped it above
        return BuildDtoInfo(typeRef);
    }
    private TypeReference UnwrapTask(TypeReference returnType)
    {
        // Task<T> => T
        if (returnType is GenericInstanceType git && git.Name.StartsWith("Task"))
        {
            var inner = git.GenericArguments.FirstOrDefault();
            if (inner != null) return inner;
        }
        // Task => keep as-is
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
                .Where(p => p.Name != "EqualityContract") // filter record artifact
                .Select(p => new PropertyInfo2
                {
                    Name = p.Name,
                    Type = FriendlyName(p.PropertyType)
                })
                .ToList();
        }
        return dto;
    }

    private void TryRecurseIntoMoveNext(
    TypeReference stateMachineType,
    List<CallNode> nodes,
    HashSet<string> visited,
    ref int order)
    {
        MethodDefinition? moveNext = null;
        try
        {
            var def = stateMachineType.Resolve();
            moveNext = def?.Methods.FirstOrDefault(m => m.Name == "MoveNext" && m.HasBody);
        }
        catch { }
        if (moveNext == null) return;
        // Use a key so you don't keep re-adding it
        var key = $"{stateMachineType.FullName}.MoveNext";
        if (!visited.Add(key)) return;
        // Add a node for MoveNext
        nodes.Add(new CallNode
        {
            Order = order++,
            DeclaringType = stateMachineType.Name,
            MethodName = "MoveNext",
            Category = "Async",
            Summary = $"State machine executes async body ({stateMachineType.Name}.MoveNext)"
        });
        // Now recurse into MoveNext's body
        // (use Cecil MethodReference)
        RecurseInto(moveNext, nodes, visited, ref order, depth: 1);
    }


    private static bool LooksLikeAsyncStateMachineType(TypeReference t)
    {
        var full = t.FullName ?? t.Name;
        return full.Contains("d__") && full.Contains("<") && full.Contains(">");
    }


    // ─────────────────────────────────────────────────────────────────────────────
    // Call flow (optional enrichment, still best-effort)
    // ─────────────────────────────────────────────────────────────────────────────
    private List<CallNode> BuildCallFlow(MethodDefinition method)
    {
        var nodes = new List<CallNode>();
        if (!method.HasBody) return nodes;
        int order = 1;
        var visited = new HashSet<string>();
        foreach (var instr in method.Body.Instructions)
        {
            if (instr.OpCode != OpCodes.Callvirt &&
                instr.OpCode != OpCodes.Call &&
                instr.OpCode != OpCodes.Newobj)
                continue;
            if (instr.Operand is not MethodReference calledMethod)
                continue;
            if (calledMethod.DeclaringType == null)
                continue;
            var declType = calledMethod.DeclaringType.Name;
            var methodName = calledMethod.Name;
            var key = $"{declType}.{methodName}";
            if (!visited.Add(key))
                continue;
            // Skip noise
            var isUser = IsUserCode(calledMethod.DeclaringType);
            if (IsFrameworkNoise(calledMethod.DeclaringType))
                continue;
            // Always record the node
            // (use isUser to decide recursion only)
            var node = new CallNode
            {
                Order = order++,
                DeclaringType = declType,
                MethodName = methodName,
                Category = Categorize(declType, methodName),
                Summary = BuildSummary(declType, methodName, calledMethod)
            };

            nodes.Add(node);
            // Recurse only if it's user code
            if (isUser && (instr.OpCode == OpCodes.Call || instr.OpCode == OpCodes.Callvirt))
                RecurseInto(calledMethod, nodes, visited, ref order, depth: 1);
            // Special async ctor/state machine handling can remain but should also key off isUser



            // Accepts: attach first complex argument best-effort
            if (calledMethod.HasParameters)
            {
                var firstComplex = calledMethod.Parameters
                    .FirstOrDefault(p => !IsPrimitive(p.ParameterType) &&
                                         p.ParameterType.Name != "CancellationToken");
                if (firstComplex != null)
                    node.Accepts = BuildDtoInfo(firstComplex.ParameterType);
            }
            // Returns: best-effort DTO for non-void
            if (calledMethod.ReturnType != null && calledMethod.ReturnType.Name != "Void")
                node.Returns = ExtractResponseDto(calledMethod.ReturnType);
            nodes.Add(node);
            // Recurse: include both call and callvirt (important)
            if (IsUserCode(calledMethod.DeclaringType) && !IsFrameworkNoise(calledMethod.DeclaringType))
            {
                // your node creation...
                // Special case: async state machine .ctor / initialization
                if (calledMethod.Name is ".ctor" &&
                    calledMethod.DeclaringType != null &&
                    LooksLikeAsyncStateMachineType(calledMethod.DeclaringType))
                {
                    // try to resolve and find MoveNext()
                    TryRecurseIntoMoveNext(calledMethod.DeclaringType, nodes, visited, ref order);
                }
                // Normal recursion
                if (instr.OpCode == OpCodes.Call || instr.OpCode == OpCodes.Callvirt)
                    RecurseInto(calledMethod, nodes, visited, ref order, depth: 1);
            }


        }
        return nodes;
    }
    private void RecurseInto(
            MethodReference methodRef,
            List<CallNode> nodes,
            HashSet<string> visited,
            ref int order,
            int depth)
    {
        if (depth > 4) return;
        MethodDefinition? def = null;
        try { def = methodRef.Resolve(); } catch { }
        if (def == null || !def.HasBody) return;
        foreach (var instr in def.Body.Instructions)
        {
            if (instr.OpCode != OpCodes.Callvirt &&
                instr.OpCode != OpCodes.Call)
                continue;
            if (instr.Operand is not MethodReference calledMethod)
                continue;
            if (calledMethod.DeclaringType == null)
                continue;
            var declType = calledMethod.DeclaringType.Name;
            var methodName = calledMethod.Name;
            var key = $"{declType}.{methodName}";
            if (!visited.Add(key)) continue;
            var isUser = IsUserCode(calledMethod.DeclaringType);
            if (IsFrameworkNoise(calledMethod.DeclaringType))
                continue;
            // Record node regardless
            nodes.Add(new CallNode
            {
                Order = order++,
                DeclaringType = declType,
                MethodName = methodName,
                Category = Categorize(declType, methodName),
                Summary = BuildSummary(declType, methodName, calledMethod)
            });
            // Recurse only if user code
            if (isUser)
                RecurseInto(calledMethod, nodes, visited, ref order, depth + 1);



       
            RecurseInto(calledMethod, nodes, visited, ref order, depth + 1);
        }
    }
    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────
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
            return $"Dispatches command/query via mediator (best-effort)";
        if (methodName.StartsWith("Get")) return $"Retrieves data from {typeName}";
        if (methodName.StartsWith("Add") || methodName.StartsWith("Create"))
            return $"Creates new entity via {typeName}";
        if (methodName.StartsWith("Update")) return $"Updates entity via {typeName}";
        if (methodName.StartsWith("Delete") || methodName.StartsWith("Remove"))
            return $"Deletes entity via {typeName}";
        return $"Calls {typeName}.{methodName}";
    }
    private bool IsNoise(string typeName, string methodName) =>
            typeName is "Object" or "String" or "Task" or "Console" ||
            methodName is ".ctor" or "get_Result" or "GetAwaiter" or "ConfigureAwait";
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
