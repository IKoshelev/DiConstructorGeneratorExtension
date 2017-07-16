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
        public void IfNoInjectablesNotifiesWithAComment()
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
        public void IfMultiplePublicConstructorsNotifiesWithAComment()
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
    }
}