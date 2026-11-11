using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace Sibir.Tests
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class FactAttribute : Attribute { public string Skip { get; set; } }

    public sealed class SkipException : Exception { public SkipException(string m) : base(m) { } }

    public static class Assert
    {
        public static void Equal<T>(T expected, T actual, string what = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"{what}Expected: <{expected}>  Actual: <{actual}>");
        }
        public static void NotEqual<T>(T a, T b) { if (EqualityComparer<T>.Default.Equals(a, b)) throw new Exception($"Did not expect <{a}>"); }
        public static void True(bool c, string msg = "Expected true") { if (!c) throw new Exception(msg); }
        public static void False(bool c, string msg = "Expected false") { if (c) throw new Exception(msg); }
        public static void Null(object o, string msg = null) { if (o != null) throw new Exception(msg ?? $"Expected null, got <{o}>"); }
        public static void NotNull(object o, string msg = "Expected not null") { if (o == null) throw new Exception(msg); }
        public static void Contains(string sub, string s) { if (s == null || !s.Contains(sub)) throw new Exception($"<{s}> does not contain <{sub}>"); }
        public static void Contains<T>(T item, IEnumerable<T> list) { if (!list.Contains(item)) throw new Exception($"Collection does not contain <{item}>"); }
        public static void Empty<T>(IEnumerable<T> list) { if (list.Any()) throw new Exception("Expected empty"); }
        public static void NotEmpty<T>(IEnumerable<T> list) { if (list == null || !list.Any()) throw new Exception("Expected not empty"); }
        public static T Throws<T>(Action a) where T : Exception
        {
            try { a(); } catch (T e) { return e; } catch (Exception e) { throw new Exception($"Expected {typeof(T).Name}, got {e.GetType().Name}: {e.Message}"); }
            throw new Exception($"Expected {typeof(T).Name}, nothing thrown");
        }
        public static void Skip(string why) => throw new SkipException(why);
    }

    /// <summary>Runs every [Fact] in this assembly. Args: optional name filters (regex, any match).</summary>
    internal static class Runner
    {
        private static int Main(string[] args)
        {
            var filters = args.Select(a => new System.Text.RegularExpressions.Regex(a, System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
            var tests = typeof(Runner).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Select(m => (t, m)))
                .Where(x => x.m.GetCustomAttribute<FactAttribute>() != null)
                .Where(x => filters.Count == 0 || filters.Any(f => f.IsMatch(x.t.Name + "." + x.m.Name)))
                .OrderBy(x => x.t.Name).ThenBy(x => x.m.Name).ToList();
            int pass = 0, fail = 0, skip = 0;
            foreach (var (t, m) in tests)
            {
                var name = t.Name + "." + m.Name;
                var attr = m.GetCustomAttribute<FactAttribute>();
                if (attr.Skip != null) { skip++; Console.WriteLine($"SKIP {name}: {attr.Skip}"); continue; }
                var sw = Stopwatch.StartNew();
                object inst = null;
                try
                {
                    inst = m.IsStatic ? null : Activator.CreateInstance(t);
                    m.Invoke(inst, null);
                    pass++; Console.WriteLine($"PASS {name} ({sw.ElapsedMilliseconds} ms)");
                }
                catch (TargetInvocationException e) when (e.InnerException is SkipException s) { skip++; Console.WriteLine($"SKIP {name}: {s.Message}"); }
                catch (Exception e)
                {
                    var inner = e is TargetInvocationException tie ? tie.InnerException : e;
                    fail++; Console.WriteLine($"FAIL {name}: {inner.GetType().Name}: {inner.Message}\n{inner.StackTrace}");
                }
                finally { (inst as IDisposable)?.Dispose(); }
            }
            Console.WriteLine($"{(fail == 0 ? "Passed!" : "Failed!")} pass={pass} fail={fail} skip={skip}");
            return fail == 0 ? 0 : 1;
        }
    }
}
