using System;

namespace Unity.IL2CPP.CompilerServices
{
    /// <summary>
    /// IL2CPP code-generation options (Unity recognises this attribute by its full name; the type is not shipped in
    /// the engine assemblies, so projects declare it themselves — see the Unity manual, "IL2CPP: Il2CppSetOption").
    /// Internal so it can never clash with a copy in another assembly. The DSP inner loops are covered by unit tests
    /// with bounds checks enabled, so they skip the per-access checks in IL2CPP player builds.
    /// </summary>
    internal enum Option
    {
        NullChecks = 1,
        ArrayBoundsChecks = 2,
        DivideByZeroChecks = 3,
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Property, Inherited = false, AllowMultiple = true)]
    internal sealed class Il2CppSetOptionAttribute : Attribute
    {
        public Il2CppSetOptionAttribute(Option option, object value)
        {
            Option = option;
            Value = value;
        }

        public Option Option { get; }

        public object Value { get; }
    }
}
