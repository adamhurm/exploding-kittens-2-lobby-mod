// Polyfill: NullableAttribute and NullableContextAttribute are normally in System.Private.CoreLib,
// but Il2Cppmscorlib replaces that reference, hiding these types. Define them here so the compiler
// can roundtrip nullable annotations from EKLobbyShared (which is compiled with nullable=enable).
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false)]
    sealed class NullableAttribute : Attribute
    {
        public NullableAttribute(byte b) { }
        public NullableAttribute(byte[] b) { }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = false)]
    sealed class NullableContextAttribute : Attribute
    {
        public NullableContextAttribute(byte b) { }
    }
}
