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
        public void TestMethod1()
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
        public void TestMethod2()
        {
            var testClassFileContents = @"
using System;

public class FooBar
{
}";

            var testClassExpectedNewContents = @"
using System;

public class raBooF
{
}";

            TestUtil.TestAssertingEndText(
                            testClassFileContents,
                            "FooBar",
                            testClassExpectedNewContents);
        }
    }
}