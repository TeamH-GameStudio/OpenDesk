// Polyfill for C# 9 init-only setters / records on .NET Standard 2.1 (Unity 2022.3).
// Without this, compiler emits CS0518: predefined type 'System.Runtime.CompilerServices.IsExternalInit' is not defined.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
