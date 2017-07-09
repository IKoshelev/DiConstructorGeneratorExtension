using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiConstructorGeneratorExtension
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, 
                    AllowMultiple = false, 
                    Inherited =true)]
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
