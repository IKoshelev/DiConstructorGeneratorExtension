using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace DiConstructorGenerator.Test
{
    [TestClass]
    public class UnitTest
    {
        //No refactoring
        [TestMethod]
        public void OnEmptyCode_NothingHappens()
        {
            var testClassFileContents = @" ";

            TestUtil.TestAsertingRefactorings(
                testClassFileContents,
                " ",
                (workspace, document, proposedCodeRefactorings) =>
                {
                    var len = proposedCodeRefactorings.Count();
                    Assert.AreEqual(len, 0);
                });
        }

        [TestMethod]
        public void IfNoInjectablesNotifiesErrorWithAComment()
        {
            var testClassFileContents = @"
using System;

public class FooBar
{
}";

            var testClassExpectedNewContents = @"
using System;

public class FooBar
{//Can't regenerate constructor, no candidate members found (readonly fields, properties markead with InjectedDependencyAttribute).
}";

            TestUtil.TestAssertingEndText(
                            testClassFileContents,
                            "FooBar",
                            testClassExpectedNewContents);
        }

        [TestMethod]
        public void IfMultiplePublicConstructorsNotifiesErrorWithAComment()
        {
            var testClassFileContents = @"
using System;

public class FooBar
{
    private readonly FooBar injectMe;
    public FooBar(){}
    public FooBar(int a){}
}";

            var testClassExpectedNewContents = @"
using System;

public class FooBar
{//Can't regenerate constructor, type contains multiple public constructors.
    private readonly FooBar injectMe;
    public FooBar(){}
    public FooBar(int a){}
}";

            TestUtil.TestAssertingEndText(
                            testClassFileContents,
                            "FooBar",
                            testClassExpectedNewContents);
        }

        [TestMethod]
        public void CanAddSingleParameterInjectionToConstructor()
        {
            var testClassFileContents = @"
using System;
public class FooBar
{
    public readonly FooBar _p1;
    public FooBar()
    {
        
    }
}";

            var testClassExpectedNewContents = @"
using System;
public class FooBar
{
    public readonly FooBar _p1;
    public FooBar(FooBar p1)
    {
        _p1 = p1;
    }
}";

            TestUtil.TestAssertingEndText(
                            testClassFileContents,
                            "FooBar",
                            testClassExpectedNewContents);
        }


        [TestMethod]
        public void CanHandleParametersOfPredefinedTypes()
        {
            var testClassFileContents = @"
using System;
public class FooBar
{
    public readonly FooBar _p1;
    public FooBar(int a)
    {
        
    }
}";

            var testClassExpectedNewContents = @"
using System;
public class FooBar
{
    public readonly FooBar _p1;
    public FooBar(int a, FooBar p1)
    {
        _p1 = p1;
    }
}";

            TestUtil.TestAssertingEndText(
                            testClassFileContents,
                            "FooBar",
                            testClassExpectedNewContents);
        }

        [TestMethod]
        public void CanHandleMoreThanOneMissingParameeter()
        {
            var testClassFileContents = @"
using System;
public class FooBar
{
    public readonly FooBar _p1;
    public readonly Baz _p2;
    public FooBar(int a)
    {
        
    }
}
public class Baz{}";

            var testClassExpectedNewContents = @"
using System;
public class FooBar
{
    public readonly FooBar _p1;
    public readonly Baz _p2;
    public FooBar(int a, FooBar p1, Baz p2)
    {
        _p1 = p1;
        _p2 = p2;
    }
}
public class Baz{}";

            TestUtil.TestAssertingEndText(
                            testClassFileContents,
                            "FooBar",
                            testClassExpectedNewContents);
        }

        [TestMethod]
        public void DoesNotTouchExistingCode()
        {
            var testClassFileContents = @"
using System;
public class FooBar
{
    public readonly FooBar _p1;
    public readonly Baz _p2;
    public FooBar(int a, FooBar existing)
    {
        //i'm a comment!
        System.WriteLine(""i'm code!"");
        _p1 = existing;
    }
}
public class Baz{}";

            var testClassExpectedNewContents = @"
using System;
public class FooBar
{
    public readonly FooBar _p1;
    public readonly Baz _p2;
    public FooBar(int a, FooBar existing, Baz p2)
    {
        _p2 = p2;
        //i'm a comment!
        System.WriteLine(""i'm code!"");
        _p1 = existing;
    }
}
public class Baz{}";

            TestUtil.TestAssertingEndText(
                            testClassFileContents,
                            "FooBar",
                            testClassExpectedNewContents);
        }

        [TestMethod]
        public void YouCanMarkPropertiesAndNonReadonlyFieldsForInjection()
        {
            var testClassFileContents = @"
using System;
public class FooBar
{
    [InjectedDependencyAttribute]
    public FooBar _p1;
    [InjectedDependency]
    public Baz _p2 {get;set;}
    public FooBar(int a)
    {
        //i'm a comment!
        System.WriteLine(""i'm code!"");
    }
}
public class Baz{}";

            var testClassExpectedNewContents = @"
using System;
public class FooBar
{
    [InjectedDependencyAttribute]
    public FooBar _p1;
    [InjectedDependency]
    public Baz _p2 {get;set;}
    public FooBar(int a, FooBar p1, Baz p2)
    {
        _p1 = p1;
        _p2 = p2;
        //i'm a comment!
        System.WriteLine(""i'm code!"");
    }
}
public class Baz{}";

            TestUtil.TestAssertingEndText(
                            testClassFileContents,
                            "FooBar",
                            testClassExpectedNewContents);
        }

        [TestMethod]
        public void YouCanMarkReadonlyFieldsToExcludeThemFromInjection()
        {
            var testClassFileContents = @"
using System;
public class FooBar
{
    public readonly FooBar _p1;
    [ExcludeFromInjectedDependencies]
    public readonly Baz _p2;
    public FooBar(int a)
    {
        
    }
}
public class Baz{}";

            var testClassExpectedNewContents = @"
using System;
public class FooBar
{
    public readonly FooBar _p1;
    [ExcludeFromInjectedDependencies]
    public readonly Baz _p2;
    public FooBar(int a, FooBar p1)
    {
        _p1 = p1;
    }
}
public class Baz{}";

            TestUtil.TestAssertingEndText(
                            testClassFileContents,
                            "FooBar",
                            testClassExpectedNewContents);
        }


        [TestMethod]
        public void OnlyAssignsInjectablesThatAreNotAssignedCurrently()
        {
            var testClassFileContents = @"
using System;
public class FooBar
{
    public readonly FooBar _p1;
    public readonly Baz _p2;
    public FooBar(int a)
    {
        _p2 = new Baz();
    }
}
public class Baz{}";

            var testClassExpectedNewContents = @"
using System;
public class FooBar
{
    public readonly FooBar _p1;
    public readonly Baz _p2;
    public FooBar(int a, FooBar p1)
    {
        _p1 = p1;
        _p2 = new Baz();
    }
}
public class Baz{}";

            TestUtil.TestAssertingEndText(
                            testClassFileContents,
                            "FooBar",
                            testClassExpectedNewContents);
        }

        [TestMethod]
        public void WillCreateConstructorIfOneDoesNotExist()
        {
            var testClassFileContents = @"
using System;
public class FooBar
{
    public readonly FooBar _p1;
}
public class Baz{}";

            var testClassExpectedNewContents = @"
using System;
public class FooBar
{
    public readonly FooBar _p1;
public FooBar(FooBar p1)
    {
        _p1 = p1;
    }
}
public class Baz{}";

            TestUtil.TestAssertingEndText(
                            testClassFileContents,
                            "FooBar",
                            testClassExpectedNewContents);
        }

        [TestMethod]
        public void CantCreateConstuctorIfTwoInjectablesHaveTheSameTypeAndNotifesErrorWithAComment()
        {
            var testClassFileContents = @"
using System;
public class FooBar
{
    public readonly FooBar _p1;
    public readonly FooBar _p2;
}";

            var testClassExpectedNewContents = @"
using System;
public class FooBar
{//Can't regenerate constructor, _p1,_p2 have the same type (can't generate unique parameter).
    public readonly FooBar _p1;
    public readonly FooBar _p2;
}";

            TestUtil.TestAssertingEndText(
                            testClassFileContents,
                            "FooBar",
                            testClassExpectedNewContents);
        }

        [TestMethod]
        public void UserCanMarkConstructorToBeUsed()
        {
            var testClassFileContents = @"
using System;

public class FooBar
{
    private readonly FooBar injectMe;
    public FooBar(){}
    [DependencyInjectionConstructor]
    public FooBar(int a):this()
    {
    }
}";

            var testClassExpectedNewContents = @"
using System;

public class FooBar
{
    private readonly FooBar injectMe;
    public FooBar(){}
    [DependencyInjectionConstructor]
    public FooBar(int a, FooBar _injectMe) : this()
    {
        injectMe = _injectMe;
    }
}";

            TestUtil.TestAssertingEndText(
                            testClassFileContents,
                            "FooBar",
                            testClassExpectedNewContents);
        }
    }
}