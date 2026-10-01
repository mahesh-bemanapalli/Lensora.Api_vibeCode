using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Lensora.Api.Infrastructure;

internal static class EventExceptionDetails
{
    public static void Add(Dictionary<string, string?> properties, Exception exception)
    {
        properties["ExceptionType"] = exception.GetType().FullName;
        properties["CauseType"] = exception.GetBaseException().GetType().FullName;
        var chain = new List<Exception>();
        for (Exception? current = exception; current is not null && chain.Count < 8; current = current.InnerException)
            chain.Add(current);

        // Root cause first. Keep source locations only as basenames, never machine paths.
        var locations = chain.AsEnumerable().Reverse()
            .SelectMany(error => new StackTrace(error, true).GetFrames() ?? [])
            .Select(frame => new { Frame = frame, Method = ResolveMethod(frame.GetMethod()) })
            .Where(item => item.Method is not null).ToArray();
        var application = locations.Where(item => item.Method!.DeclaringType?.FullName?
            .StartsWith("Lensora.Api.", StringComparison.Ordinal) == true).ToArray();
        var failure = application.FirstOrDefault();
        if (failure is not null)
        {
            properties["FailureClass"] = EventLogQueue.Limit(failure.Method!.DeclaringType?.FullName, 256);
            properties["FailureMethod"] = EventLogQueue.Limit(failure.Method.Name, 256);
            properties["FailureFile"] = Path.GetFileName(failure.Frame.GetFileName()?.Replace((char)92, '/'));
            properties["FailureLine"] = failure.Frame.GetFileLineNumber() > 0
                ? failure.Frame.GetFileLineNumber().ToString(CultureInfo.InvariantCulture) : null;
        }
        string Describe(StackFrame frame, MethodBase method) =>
            $"{method.DeclaringType?.FullName}.{method.Name}" +
            (frame.GetFileLineNumber() > 0 ? $" (line {frame.GetFileLineNumber()})" : "");
        properties["ApplicationStack"] = EventLogQueue.Limit(string.Join("\n", application
            .Select(item => Describe(item.Frame, item.Method!)).Distinct().Take(12)), 2000);
        properties["Stack"] = EventLogQueue.Limit(string.Join("\n", locations
            .Select(item => Describe(item.Frame, item.Method!)).Distinct().Take(10)), 1500);
        var sql = chain.OfType<SqlException>().FirstOrDefault();
        if (sql is null) return;
        properties["SqlErrorCount"] = sql.Errors.Count.ToString(CultureInfo.InvariantCulture);
        // Multiple errors may be returned for one command. Capture a bounded subset, without
        // messages, server names, command text, parameters or connection information.
        for (var index = 0; index < Math.Min(sql.Errors.Count, 5); index++)
        {
            var error = sql.Errors[index];
            var prefix = index == 0 ? "Sql" : $"SqlError{index + 1}";
            properties[prefix + "Number"] = error.Number.ToString(CultureInfo.InvariantCulture);
            properties[prefix + "State"] = error.State.ToString(CultureInfo.InvariantCulture);
            properties[prefix + "Class"] = error.Class.ToString(CultureInfo.InvariantCulture);
            properties[prefix + "LineNumber"] = error.LineNumber.ToString(CultureInfo.InvariantCulture);
            properties[prefix + "Procedure"] = string.IsNullOrEmpty(error.Procedure) ? "(none)"
                : Regex.IsMatch(error.Procedure, @"\A[A-Za-z0-9_.\[\]#@$-]{1,128}\z") ? error.Procedure : "(omitted)";
        }
    }
    private static MethodBase? ResolveMethod(MethodBase? method)
    {
        var generatedType = method?.DeclaringType;
        if (method?.Name != "MoveNext" || generatedType?.DeclaringType is not Type owner) return method;
        return owner.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate => candidate.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType == generatedType
                || candidate.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType == generatedType) ?? method;
    }}
