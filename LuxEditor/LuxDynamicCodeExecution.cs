using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Linalab.UnityAiBridge.Editor;
using Microsoft.CSharp;
using UnityEngine;

namespace Linalab.Lux.Editor
{
    public sealed class LuxDynamicCodeSingleFlightException : Exception
    {
        public LuxDynamicCodeSingleFlightException(string message)
            : base(message)
        {
        }
    }

    public sealed class LuxDynamicCodePolicyViolationException : Exception
    {
        public LuxDynamicCodePolicyViolationException(string blockedToken, string message)
            : base(message)
        {
            BlockedToken = blockedToken ?? string.Empty;
        }

        public string BlockedToken { get; }
    }

    public static class LuxDynamicCodeExecution
    {
        const string ActionCompileAndExecute = "compile_and_execute";
        const string MessageExecutionSucceeded = "Dynamic code executed.";
        const string MessageCompilationFailed = "Dynamic code compilation failed.";
        const string MessageExecutionFailed = "Dynamic code execution failed.";
        const string RunnerTypeName = "Linalab.Lux.Editor.DynamicCodeRuntime.LuxDynamicCodeSnippet";

        static readonly LuxCodeExecutionPolicy Policy = new LuxCodeExecutionPolicy();
        static int singleFlightActive;

        public static UnityAiBridgeDynamicCodeResultPayload Execute(string code)
        {
            if (Interlocked.CompareExchange(ref singleFlightActive, 1, 0) != 0)
            {
                throw new LuxDynamicCodeSingleFlightException("A Lux dynamic code execution is already running.");
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var normalizedCode = code ?? string.Empty;
                var decision = Policy.Evaluate(normalizedCode);
                if (!decision.Allowed)
                {
                    throw new LuxDynamicCodePolicyViolationException(decision.BlockedToken, decision.Message);
                }

                var compileResult = CompileSnippet(normalizedCode);
                if (!compileResult.Success)
                {
                    stopwatch.Stop();
                    return CreatePayload(
                        false,
                        string.Empty,
                        string.Empty,
                        MessageCompilationFailed,
                        compileResult.Diagnostics,
                        Array.Empty<UnityAiBridgeConsoleLogEntry>(),
                        stopwatch.ElapsedMilliseconds);
                }

                var logs = new List<UnityAiBridgeConsoleLogEntry>();
                Application.LogCallback callback = (condition, stackTrace, type) =>
                {
                    logs.Add(new UnityAiBridgeConsoleLogEntry
                    {
                        level = type.ToString(),
                        message = condition ?? string.Empty,
                        stackTrace = stackTrace ?? string.Empty,
                        timestampUtc = DateTime.UtcNow.ToString("O")
                    });
                };

                Application.logMessageReceived += callback;
                try
                {
                    var executionResult = compileResult.ExecuteMethod.Invoke(null, null);
                    stopwatch.Stop();
                    return CreatePayload(
                        true,
                        ConvertResultToString(executionResult),
                        GetResultTypeName(executionResult, compileResult.ExecuteMethod),
                        MessageExecutionSucceeded,
                        compileResult.Diagnostics,
                        logs.ToArray(),
                        stopwatch.ElapsedMilliseconds);
                }
                catch (TargetInvocationException exception)
                {
                    stopwatch.Stop();
                    var inner = exception.InnerException ?? exception;
                    return CreatePayload(
                        false,
                        string.Empty,
                        string.Empty,
                        $"{MessageExecutionFailed} {inner.Message}",
                        compileResult.Diagnostics,
                        logs.ToArray(),
                        stopwatch.ElapsedMilliseconds);
                }
                catch (Exception exception)
                {
                    stopwatch.Stop();
                    return CreatePayload(
                        false,
                        string.Empty,
                        string.Empty,
                        $"{MessageExecutionFailed} {exception.Message}",
                        compileResult.Diagnostics,
                        logs.ToArray(),
                        stopwatch.ElapsedMilliseconds);
                }
                finally
                {
                    Application.logMessageReceived -= callback;
                }
            }
            finally
            {
                Volatile.Write(ref singleFlightActive, 0);
            }
        }

        static DynamicCodeCompileResult CompileSnippet(string code)
        {
            var objectSource = DynamicCodeSource.Create(code, true);
            var objectResult = CompileSource(objectSource);
            if (objectResult.Success || !ShouldRetryAsVoid(objectResult.Diagnostics))
            {
                return objectResult;
            }

            var voidSource = DynamicCodeSource.Create(code, false);
            return CompileSource(voidSource);
        }

        static DynamicCodeCompileResult CompileSource(DynamicCodeSource source)
        {
            using (var provider = new CSharpCodeProvider())
            {
                var parameters = new CompilerParameters
                {
                    GenerateExecutable = false,
                    GenerateInMemory = true,
                    IncludeDebugInformation = false,
                    TreatWarningsAsErrors = false,
                    TempFiles = new TempFileCollection(GetCompilerTempDirectory(), false)
                };

                foreach (var assemblyPath in GetReferenceAssemblyPaths())
                {
                    parameters.ReferencedAssemblies.Add(assemblyPath);
                }

                var results = provider.CompileAssemblyFromSource(parameters, source.Code);
                var diagnostics = ConvertDiagnostics(results, source);
                if (diagnostics.Any(diagnostic => string.Equals(diagnostic.severity, "Error", StringComparison.Ordinal)))
                {
                    return DynamicCodeCompileResult.Failed(diagnostics);
                }

                var type = results.CompiledAssembly.GetType(RunnerTypeName, false);
                var executeMethod = type?.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static);
                if (executeMethod == null)
                {
                    return DynamicCodeCompileResult.Failed(new[]
                    {
                        new UnityAiBridgeDynamicCodeDiagnostic
                        {
                            id = "missing_execute_method",
                            severity = "Error",
                            message = "Compiled dynamic code did not expose the Lux execute method.",
                            line = 0,
                            column = 0
                        }
                    });
                }

                return DynamicCodeCompileResult.Succeeded(executeMethod, diagnostics);
            }
        }

        static UnityAiBridgeDynamicCodeDiagnostic[] ConvertDiagnostics(CompilerResults results, DynamicCodeSource source)
        {
            var diagnostics = new List<UnityAiBridgeDynamicCodeDiagnostic>();
            foreach (CompilerError error in results.Errors)
            {
                diagnostics.Add(new UnityAiBridgeDynamicCodeDiagnostic
                {
                    id = error.ErrorNumber ?? string.Empty,
                    severity = error.IsWarning ? "Warning" : "Error",
                    message = error.ErrorText ?? string.Empty,
                    line = MapLine(error.Line, source.CodeStartLine),
                    column = error.Column < 0 ? 0 : error.Column
                });
            }

            return diagnostics.ToArray();
        }

        static int MapLine(int generatedLine, int codeStartLine)
        {
            if (generatedLine <= 0)
            {
                return 0;
            }

            return Math.Max(1, generatedLine - codeStartLine + 1);
        }

        static bool ShouldRetryAsVoid(UnityAiBridgeDynamicCodeDiagnostic[] diagnostics)
        {
            return diagnostics != null
                && diagnostics.Any(diagnostic =>
                    string.Equals(diagnostic.severity, "Error", StringComparison.Ordinal) &&
                    string.Equals(diagnostic.id, "CS0161", StringComparison.Ordinal));
        }

        static IEnumerable<string> GetReferenceAssemblyPaths()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string location;
                try
                {
                    if (assembly.IsDynamic)
                    {
                        continue;
                    }

                    location = assembly.Location;
                }
                catch (NotSupportedException)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(location) || !File.Exists(location) || !seen.Add(location))
                {
                    continue;
                }

                yield return location;
            }
        }

        static string GetCompilerTempDirectory()
        {
            var directory = Path.Combine(GetProjectRoot(), "Temp", "Lux", "DynamicCode");
            Directory.CreateDirectory(directory);
            return directory;
        }

        static string GetProjectRoot()
        {
            var assetsDirectory = new DirectoryInfo(Application.dataPath);
            return assetsDirectory.Parent == null ? Directory.GetCurrentDirectory() : assetsDirectory.Parent.FullName;
        }

        static string ConvertResultToString(object executionResult)
        {
            if (executionResult == null)
            {
                return string.Empty;
            }

            if (executionResult is IFormattable formattable)
            {
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            }

            return executionResult.ToString() ?? string.Empty;
        }

        static string GetResultTypeName(object executionResult, MethodInfo executeMethod)
        {
            if (executionResult != null)
            {
                return executionResult.GetType().FullName ?? executionResult.GetType().Name;
            }

            return executeMethod.ReturnType == typeof(void)
                ? "void"
                : executeMethod.ReturnType.FullName ?? executeMethod.ReturnType.Name;
        }

        static UnityAiBridgeDynamicCodeResultPayload CreatePayload(
            bool success,
            string result,
            string resultType,
            string message,
            UnityAiBridgeDynamicCodeDiagnostic[] diagnostics,
            UnityAiBridgeConsoleLogEntry[] logs,
            long elapsedTimeMs)
        {
            return new UnityAiBridgeDynamicCodeResultPayload
            {
                success = success,
                action = ActionCompileAndExecute,
                result = result ?? string.Empty,
                resultType = resultType ?? string.Empty,
                message = message ?? string.Empty,
                diagnostics = diagnostics ?? Array.Empty<UnityAiBridgeDynamicCodeDiagnostic>(),
                logs = logs ?? Array.Empty<UnityAiBridgeConsoleLogEntry>(),
                elapsedTimeMs = elapsedTimeMs
            };
        }

        readonly struct DynamicCodeSource
        {
            DynamicCodeSource(string code, int codeStartLine)
            {
                Code = code;
                CodeStartLine = codeStartLine;
            }

            public string Code { get; }
            public int CodeStartLine { get; }

            public static DynamicCodeSource Create(string snippet, bool returnsObject)
            {
                var returnType = returnsObject ? "object" : "void";
                var prefix =
                    "using System;\n" +
                    "using UnityEditor;\n" +
                    "using UnityEngine;\n" +
                    "\n" +
                    "namespace Linalab.Lux.Editor.DynamicCodeRuntime\n" +
                    "{\n" +
                    "    public static class LuxDynamicCodeSnippet\n" +
                    "    {\n" +
                    $"        public static {returnType} Execute()\n" +
                    "        {\n";
                var suffix =
                    "\n" +
                    "        }\n" +
                    "    }\n" +
                    "}\n";
                return new DynamicCodeSource(prefix + (snippet ?? string.Empty) + suffix, CountLines(prefix) + 1);
            }

            static int CountLines(string text)
            {
                return string.IsNullOrEmpty(text) ? 0 : text.Count(character => character == '\n');
            }
        }

        readonly struct DynamicCodeCompileResult
        {
            DynamicCodeCompileResult(bool success, MethodInfo executeMethod, UnityAiBridgeDynamicCodeDiagnostic[] diagnostics)
            {
                Success = success;
                ExecuteMethod = executeMethod;
                Diagnostics = diagnostics ?? Array.Empty<UnityAiBridgeDynamicCodeDiagnostic>();
            }

            public bool Success { get; }
            public MethodInfo ExecuteMethod { get; }
            public UnityAiBridgeDynamicCodeDiagnostic[] Diagnostics { get; }

            public static DynamicCodeCompileResult Succeeded(MethodInfo executeMethod, UnityAiBridgeDynamicCodeDiagnostic[] diagnostics)
            {
                return new DynamicCodeCompileResult(true, executeMethod, diagnostics);
            }

            public static DynamicCodeCompileResult Failed(UnityAiBridgeDynamicCodeDiagnostic[] diagnostics)
            {
                return new DynamicCodeCompileResult(false, null, diagnostics);
            }
        }
    }
}
