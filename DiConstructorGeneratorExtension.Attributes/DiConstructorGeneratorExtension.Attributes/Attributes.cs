using System;

namespace DiConstructorGeneratorExtension.Attributes
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property,
                    AllowMultiple = false,
                    Inherited = true)]
    public class InjectedDependencyAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property,
                AllowMultiple = false,
                Inherited = true)]
    public class ExcludeFromInjectedDependenciesAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Constructor,
            AllowMultiple = false,
            Inherited = false)]
    public class DependencyInjectionConstructorAttribute : Attribute
    {
    }
}
