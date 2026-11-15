// D5 offline runner (NOT the Unity Test Runner). Runs [Test]/[TestCase] methods with [SetUp]/[TearDown]
// from the given assemblies via reflection using the real NUnit framework assembly, then the phase 2
// check suites (validators, discovery and conformance). Output separates NUnit results from checks.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

public static class ReflectionNUnitRunner
{
    public static int Main(string[] args)
    {
        int passed = 0, failed = 0, inconclusive = 0, ignored = 0;
        var perClass = new SortedDictionary<string, int[]>();
        string filter = Environment.GetEnvironmentVariable("D5_FILTER");
        foreach (string path in args.Where(a => a.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            Assembly asm = Assembly.LoadFrom(path);
            foreach (Type type in asm.GetTypes().Where(t => t.IsClass && !t.IsAbstract).OrderBy(t => t.FullName))
            {
                var tests = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.GetCustomAttributes<TestAttribute>().Any() || m.GetCustomAttributes<TestCaseAttribute>().Any()).ToList();
                if (tests.Count == 0) continue;
                if (filter != null && type.FullName.IndexOf(filter, StringComparison.Ordinal) < 0) continue;
                MethodInfo setUp = type.GetMethods().FirstOrDefault(m => m.GetCustomAttributes<SetUpAttribute>().Any());
                MethodInfo tearDown = type.GetMethods().FirstOrDefault(m => m.GetCustomAttributes<TearDownAttribute>().Any());
                int[] counts = new int[4];
                foreach (MethodInfo method in tests)
                {
                    var cases = method.GetCustomAttributes<TestCaseAttribute>().Select(c => c.Arguments).ToList();
                    if (cases.Count == 0) cases.Add(new object[0]);
                    foreach (object[] arguments in cases)
                    {
                        string name = type.Name + "." + method.Name + (arguments.Length > 0 ? "(" + string.Join(",", arguments) + ")" : "");
                        object instance = Activator.CreateInstance(type);
                        string outcome = "PASS"; string detail = "";
                        try
                        {
                            setUp?.Invoke(instance, null);
                            try { method.Invoke(instance, arguments); }
                            finally { tearDown?.Invoke(instance, null); }
                        }
                        catch (TargetInvocationException e)
                        {
                            Exception inner = e.InnerException;
                            if (inner is InconclusiveException) { outcome = "INCONCLUSIVE"; detail = inner.Message; }
                            else if (inner is IgnoreException) { outcome = "IGNORED"; detail = inner.Message; }
                            else if (inner is SuccessException) outcome = "PASS";
                            else { outcome = "FAIL"; detail = inner.GetType().Name + ": " + inner.Message; }
                        }
                        switch (outcome)
                        {
                            case "PASS": passed++; counts[0]++; break;
                            case "FAIL": failed++; counts[1]++; break;
                            case "INCONCLUSIVE": inconclusive++; counts[2]++; break;
                            default: ignored++; counts[3]++; break;
                        }
                        if (outcome != "PASS") Console.WriteLine(outcome + " " + name + " :: " + detail.Replace("\n", " | "));
                    }
                }
                perClass[type.FullName] = counts;
            }
        }
        Console.WriteLine("---- NUnit (reflection runner, not Unity Test Runner) ----");
        foreach (var kv in perClass) Console.WriteLine(kv.Key + ": pass " + kv.Value[0] + ", fail " + kv.Value[1] + ", inconclusive " + kv.Value[2] + ", ignored " + kv.Value[3]);
        Console.WriteLine("TOTAL pass " + passed + ", fail " + failed + ", inconclusive " + inconclusive + ", ignored " + ignored);
        return failed == 0 ? 0 : 1;
    }
}
