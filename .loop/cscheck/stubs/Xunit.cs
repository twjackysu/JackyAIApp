// cscheck 用的 xUnit 屬性樁（只供型別檢查；Assert 用 xunit/assert.xunit 的原始碼）。
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;

namespace Xunit
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class FactAttribute : Attribute
    {
        public string? DisplayName { get; set; }
        public string? Skip { get; set; }
        public int Timeout { get; set; }
    }

    public class TheoryAttribute : FactAttribute { }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class InlineDataAttribute : Attribute
    {
#nullable disable
        // xUnit v2 的這個建構子沒有 nullable 註記，[InlineData(null)] 不會有警告。
        public InlineDataAttribute(params object[] data) { }
#nullable enable
        public string? Skip { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class MemberDataAttribute : Attribute
    {
        public MemberDataAttribute(string memberName, params object?[] parameters) { }
        public Type? MemberType { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class TraitAttribute : Attribute
    {
        public TraitAttribute(string name, string value) { }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class CollectionAttribute : Attribute
    {
        public CollectionAttribute(string name) { }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class CollectionDefinitionAttribute : Attribute
    {
        public CollectionDefinitionAttribute(string name) { }
        public bool DisableParallelization { get; set; }
    }

    public interface IClassFixture<TFixture> where TFixture : class { }
    public interface ICollectionFixture<TFixture> where TFixture : class { }

    public interface IAsyncLifetime
    {
        System.Threading.Tasks.Task InitializeAsync();
        System.Threading.Tasks.Task DisposeAsync();
    }

    public class TheoryData : IEnumerable<object?[]>
    {
        private readonly List<object?[]> rows = new();
        protected void AddRow(params object?[] values) => rows.Add(values);
        public IEnumerator<object?[]> GetEnumerator() => rows.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public class TheoryData<T1> : TheoryData { public void Add(T1 p1) => AddRow(p1); }
    public class TheoryData<T1, T2> : TheoryData { public void Add(T1 p1, T2 p2) => AddRow(p1, p2); }
    public class TheoryData<T1, T2, T3> : TheoryData { public void Add(T1 p1, T2 p2, T3 p3) => AddRow(p1, p2, p3); }
    public class TheoryData<T1, T2, T3, T4> : TheoryData { public void Add(T1 p1, T2 p2, T3 p3, T4 p4) => AddRow(p1, p2, p3, p4); }
}

namespace Xunit.Abstractions
{
    public interface ITestOutputHelper
    {
        void WriteLine(string message);
        void WriteLine(string format, params object[] args);
    }
}
