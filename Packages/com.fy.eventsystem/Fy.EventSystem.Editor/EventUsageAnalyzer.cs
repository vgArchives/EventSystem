using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using UnityEngine;

namespace Fy.EventSystem.Editor
{
    /// <summary>
    /// Finds every place in the project that publishes or subscribes to an event, by reading the compiled
    /// assemblies rather than the source text.
    /// </summary>
    /// <remarks>
    /// IL is used instead of a text search because it is unambiguous: a call to <c>Invoke</c> carries the event
    /// type as a generic argument, so there is no guessing about which <c>Invoke</c> a line refers to and no false
    /// positives from comments or similarly named methods.
    /// </remarks>
    internal static class EventUsageAnalyzer
    {
        private const string ScriptAssembliesPath = "Library/ScriptAssemblies";
        private const string EventSystemAssemblyName = "Fy.EventSystem";
        private const string GeneratedCodeAttributeName = "System.CodeDom.Compiler.GeneratedCodeAttribute";

        private static readonly string[] ServiceTypeNames =
        {
            "Fy.EventSystem.IEventService",
            "Fy.EventSystem.EventSystem"
        };

        /// <summary>
        /// Gets every event type in the project, whether or not it is used anywhere.
        /// </summary>
        internal static List<Type> FindEventTypes()
        {
            var result = new List<Type>();

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (Type type in SafeGetTypes(assembly))
                {
                    if (!type.IsAbstract && !type.IsInterface && typeof(IEvent).IsAssignableFrom(type))
                    {
                        result.Add(type);
                    }
                }
            }

            result.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));

            return result;
        }

        /// <summary>
        /// Scans the compiled assemblies for call sites of the given event types.
        /// </summary>
        /// <param name="eventTypes">
        /// The events to look for, normally everything <see cref="FindEventTypes"/> found.
        /// </param>
        /// <returns>Call sites grouped by event type full name.</returns>
        internal static Dictionary<string, List<EventCallSite>> FindCallSites(IEnumerable<Type> eventTypes)
        {
            var eventTypeNames = new HashSet<string>(eventTypes.Select(type => type.FullName));
            var result = new Dictionary<string, List<EventCallSite>>();

            if (!Directory.Exists(ScriptAssembliesPath))
            {
                return result;
            }

            foreach (string assemblyPath in Directory.GetFiles(ScriptAssembliesPath, "*.dll"))
            {
                ScanAssembly(assemblyPath, eventTypeNames, result);
            }

            return result;
        }

        private static void ScanAssembly(string assemblyPath, HashSet<string> eventTypeNames,
            Dictionary<string, List<EventCallSite>> result)
        {
            AssemblyDefinition assembly = null;

            try
            {
                assembly = ReadAssemblyPreferringSymbols(assemblyPath);

                if (assembly == null || !ReferencesEventSystem(assembly))
                {
                    return;
                }

                foreach (TypeDefinition type in EnumerateTypes(assembly.MainModule.Types))
                {
                    foreach (MethodDefinition method in type.Methods)
                    {
                        ScanMethod(method, eventTypeNames, result);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Event System] Could not scan '{Path.GetFileName(assemblyPath)}': " +
                                 $"{exception.Message}");
            }
            finally
            {
                assembly?.Dispose();
            }
        }

        private static AssemblyDefinition ReadAssemblyPreferringSymbols(string assemblyPath)
        {
            try
            {
                return AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters { ReadSymbols = true });
            }
            catch (Exception)
            {
                return AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters { ReadSymbols = false });
            }
        }

        private static bool ReferencesEventSystem(AssemblyDefinition assembly)
        {
            if (assembly.Name.Name == EventSystemAssemblyName)
            {
                return true;
            }

            foreach (AssemblyNameReference reference in assembly.MainModule.AssemblyReferences)
            {
                if (reference.Name == EventSystemAssemblyName)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ScanMethod(MethodDefinition method, HashSet<string> eventTypeNames,
            Dictionary<string, List<EventCallSite>> result)
        {
            if (!method.HasBody || IsGeneratedForwarder(method))
            {
                return;
            }

            foreach (Instruction instruction in method.Body.Instructions)
            {
                if (instruction.OpCode.Code != Code.Call && instruction.OpCode.Code != Code.Callvirt)
                {
                    continue;
                }

                if (!(instruction.Operand is MethodReference call)
                 || !TryGetCallSiteKind(call, eventTypeNames, out EventCallSiteKind kind, out string eventTypeName))
                {
                    continue;
                }

                AddCallSite(result, method, instruction, kind, eventTypeName);
            }
        }

        private static bool TryGetCallSiteKind(MethodReference call, HashSet<string> eventTypeNames,
            out EventCallSiteKind kind, out string eventTypeName)
        {
            kind = default;
            eventTypeName = null;

            switch (call.Name)
            {
                case "Invoke":
                    kind = EventCallSiteKind.Publisher;

                    break;

                case "AddListener":
                    kind = EventCallSiteKind.Subscriber;

                    break;

                default:
                    return false;
            }

            eventTypeName = GetEventTypeName(call, eventTypeNames);

            return eventTypeName != null;
        }

        private static string GetEventTypeName(MethodReference call, HashSet<string> eventTypeNames)
        {
            if (call is GenericInstanceMethod genericServiceCall
             && genericServiceCall.GenericArguments.Count == 1
             && IsEventServiceCall(call))
            {
                return MatchEventType(genericServiceCall.GenericArguments[0], eventTypeNames);
            }

            if (call.Name == "Invoke" && call.Parameters.Count == 2
             && call.Parameters[0].ParameterType is ByReferenceType generatedInvokeParameter)
            {
                return MatchEventType(generatedInvokeParameter.ElementType, eventTypeNames);
            }

            if (call.Name == "AddListener")
            {
                return MatchEventType(call.DeclaringType, eventTypeNames);
            }

            return null;
        }

        private static bool IsEventServiceCall(MethodReference call)
        {
            string declaringTypeName = NormalizeTypeName(call.DeclaringType.FullName);

            return Array.IndexOf(ServiceTypeNames, declaringTypeName) >= 0;
        }

        private static string MatchEventType(TypeReference typeReference, HashSet<string> eventTypeNames)
        {
            string name = NormalizeTypeName(typeReference.FullName);

            return eventTypeNames.Contains(name) ? name : null;
        }

        private static string NormalizeTypeName(string fullName)
        {
            return fullName.Replace('/', '+');
        }

        private static void AddCallSite(Dictionary<string, List<EventCallSite>> result, MethodDefinition method,
            Instruction instruction, EventCallSiteKind kind, string eventTypeName)
        {
            SequencePoint sequencePoint = FindNearestPrecedingSequencePoint(method, instruction);

            var callSite = new EventCallSite(
                kind,
                eventTypeName,
                NormalizeTypeName(method.DeclaringType.FullName),
                method.Name,
                sequencePoint?.Document?.Url,
                sequencePoint?.StartLine ?? 0);

            if (!result.TryGetValue(eventTypeName, out List<EventCallSite> callSites))
            {
                callSites = new List<EventCallSite>();
                result.Add(eventTypeName, callSites);
            }

            callSites.Add(callSite);
        }

        private static SequencePoint FindNearestPrecedingSequencePoint(MethodDefinition method,
            Instruction instruction)
        {
            if (!method.DebugInformation.HasSequencePoints)
            {
                return null;
            }

            for (Instruction current = instruction; current != null; current = current.Previous)
            {
                SequencePoint sequencePoint = method.DebugInformation.GetSequencePoint(current);

                if (sequencePoint != null && !sequencePoint.IsHidden)
                {
                    return sequencePoint;
                }
            }

            return null;
        }

        private static bool IsGeneratedForwarder(MethodDefinition method)
        {
            foreach (CustomAttribute attribute in method.CustomAttributes)
            {
                if (attribute.AttributeType.FullName == GeneratedCodeAttributeName)
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<TypeDefinition> EnumerateTypes(IEnumerable<TypeDefinition> types)
        {
            foreach (TypeDefinition type in types)
            {
                yield return type;

                foreach (TypeDefinition nested in EnumerateTypes(type.NestedTypes))
                {
                    yield return nested;
                }
            }
        }

        private static Type[] SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.Where(type => type != null).ToArray();
            }
        }
    }
}
