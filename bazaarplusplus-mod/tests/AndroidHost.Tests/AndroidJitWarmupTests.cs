using System.Reflection;
using BazaarPlusPlus.Android.Host;
using Xunit;

namespace AndroidHost.Tests;

public sealed class AndroidJitWarmupTests
{
    [Fact]
    public void Each_frame_prepares_methods_until_its_budget_is_spent()
    {
        var clock = 0L;
        var prepared = new List<MethodBase>();
        var warmup = new AndroidJitWarmup(
            [typeof(Sample)],
            method =>
            {
                prepared.Add(method);
                clock += Millisecond;
            },
            () => clock,
            TimeSpan.FromMilliseconds(2.5)
        );

        warmup.Tick();

        // The type's expansion is free on this clock; the third 1 ms prepare passes 2.5 ms.
        Assert.Equal(3, prepared.Count);
        Assert.False(warmup.IsDone);

        while (!warmup.IsDone)
            warmup.Tick();
        Assert.Equal(Methods(typeof(Sample)), prepared.Select(Name).Order());
        Assert.Equal(prepared.Count, warmup.Prepared);

        warmup.Tick();
        Assert.Equal(warmup.Prepared, prepared.Count);
    }

    [Fact]
    public void Nested_types_are_prepared_too_and_a_generic_nested_type_closes_over_its_outer()
    {
        var prepared = new List<MethodBase>();
        var warmup = new AndroidJitWarmup(
            [typeof(Outer<int>)],
            prepared.Add,
            () => 0,
            TimeSpan.FromSeconds(1)
        );

        while (!warmup.IsDone)
            warmup.Tick();

        Assert.Contains(prepared, method => method.DeclaringType == typeof(Outer<int>.Inner));
        Assert.DoesNotContain(prepared, method => method.ContainsGenericParameters);
        Assert.DoesNotContain(prepared, method => method.IsAbstract);
    }

    [Fact]
    public void A_method_that_will_not_prepare_is_counted_and_the_rest_still_are()
    {
        var warmup = new AndroidJitWarmup(
            [typeof(Sample)],
            method =>
            {
                if (method.Name == nameof(Sample.First))
                    throw new InvalidOperationException("not compiled ahead");
            },
            () => 0,
            TimeSpan.FromSeconds(1)
        );

        while (!warmup.IsDone)
            warmup.Tick();

        Assert.Equal(1, warmup.Failed);
        Assert.Equal(Methods(typeof(Sample)).Count - 1, warmup.Prepared);
    }

    [Fact]
    public void The_real_paths_prepare_on_the_test_runtime()
    {
        var warmup = new AndroidJitWarmup(
            [typeof(Sample), typeof(Outer<string>)],
            method =>
                System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(method.MethodHandle),
            () => 0,
            TimeSpan.FromSeconds(1)
        );

        while (!warmup.IsDone)
            warmup.Tick();

        Assert.Equal(0, warmup.Failed);
    }

    private static readonly long Millisecond = System.Diagnostics.Stopwatch.Frequency / 1000;

    private static string Name(MethodBase method) => method.Name;

    private static List<string> Methods(Type type) =>
        type.GetMethods(
                BindingFlags.DeclaredOnly
                    | BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
            )
            .Where(method => !method.ContainsGenericParameters)
            .Cast<MethodBase>()
            .Concat(type.GetConstructors(BindingFlags.Instance | BindingFlags.Public))
            .Select(Name)
            .Order()
            .ToList();

    private sealed class Sample
    {
        public int First() => 1;

        public int Second() => 2;

        public static int Third() => 3;

        public int Fourth() => 4;

        public T Generic<T>(T value) => value;
    }

    private sealed class Outer<T>
    {
        public T? Value { get; set; }

        public sealed class Inner
        {
            public T? Echo(T? value) => value;
        }
    }
}
