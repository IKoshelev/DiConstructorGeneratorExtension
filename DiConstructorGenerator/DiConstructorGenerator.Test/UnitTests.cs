using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using TestHelper;
using DiConstructorGenerator;
using System.Linq;
using Microsoft.CodeAnalysis.CodeActions;
using System.Threading;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Text;

namespace DiConstructorGenerator.Test
{
    [TestClass]
    public class UnitTest : CodeFixVerifier
    {

        //No diagnostics expected to show up
        [TestMethod]
        public void TestMethod1()
        {
            var testClassFileContents = @" ";

            TestUtil.TestClass(
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

            TestUtil.TestClass(
                testClassFileContents,
                "FooBar",
                (workspace, document, proposedCodeRefactorings) =>
                {
                    CodeAction refactoring = proposedCodeRefactorings.Single();
                    CodeActionOperation operation = refactoring
                                        .GetOperationsAsync(CancellationToken.None)
                                        .Result
                                        .Single();

                    operation.Apply(workspace, CancellationToken.None);

                    Document newDocument = workspace.CurrentSolution.GetDocument(document.Id);

                    SourceText newText = newDocument.GetTextAsync(CancellationToken.None).Result;

                    string text = newText.ToString();

                    Assert.AreEqual(text, testClassExpectedNewContents);
                });
        }
    }
}