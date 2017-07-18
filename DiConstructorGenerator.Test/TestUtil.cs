using DiConstructorGeneratorExtension;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiConstructorGenerator.Test
{
    public static class TestUtil
    {
        public static string ProjectName { get; set; } = 
                            "ClassLibraryProjecForAdhocWorkspaceForUnitTest";
        public static string ClassFileName { get; set; } = 
                            "ClassForDiagnosticsTest";

        public static void TestAssertingEndText(
                                    string sampleClassCode,
                                    string refactoringSite,
                                    Action<string> assertion,
                                    int refactoringNumber = 0)
        {
            TestAsertingRefactorings(
                sampleClassCode,
                refactoringSite,
                (workspace, document, proposedCodeRefactorings) =>
                {
                    CodeAction refactoring = proposedCodeRefactorings.Single();
                    CodeActionOperation operation = refactoring
                                        .GetOperationsAsync(CancellationToken.None)
                                        .Result
                                        .ElementAt(refactoringNumber);

                    operation.Apply(workspace, CancellationToken.None);

                    Document newDocument = workspace.CurrentSolution.GetDocument(document.Id);

                    SourceText newText = newDocument.GetTextAsync(CancellationToken.None).Result;

                    string text = newText.ToString();

                    assertion(text);
                });
        }

        public static void TestAssertingEndText(
                                    string sampleClassCode,
                                    string refactoringSite,
                                    string expectedText,
                                    int refactoringNumber = 0)
        {
            TestAssertingEndText(
                sampleClassCode, 
                refactoringSite, 
                actuallText => Assert.AreEqual(expectedText, actuallText), 
                refactoringNumber);
        }
        
        public static void TestAsertingRefactorings(
                                    string sampleClassCode, 
                                    string refactoringSite,
                                    Action<AdhocWorkspace, 
                                            Document, 
                                            IEnumerable<CodeAction>> assert)
        {          
            (AdhocWorkspace workspace, Document classDocument) 
                = CreateAdHocLibraryProjectWorkspace(sampleClassCode);

            TextSpan refactroingSiteSpan = 
                GetTextSpanFromCodeSite(sampleClassCode, refactoringSite);

            var refactoringsProposed = new List<CodeAction>();
            Action<CodeAction> proposeRefactoring = 
                (x) => refactoringsProposed.Add(x);

            var context = new CodeRefactoringContext(
                                            classDocument, 
                                            refactroingSiteSpan, 
                                            proposeRefactoring, 
                                            CancellationToken.None);

            var refacctoringProviderUnderTest = 
                        new DiConstructorGeneratorExtensionCodeRefactoringProvider();

            refacctoringProviderUnderTest
                .ComputeRefactoringsAsync(context)
                .Wait();

            assert(workspace, classDocument, refactoringsProposed);
        }

        public static (AdhocWorkspace workspace, Document document) 
                            CreateAdHocLibraryProjectWorkspace(string classFileContets)
        {
            return CreateAdHocLibraryProjectWorkspace(classFileContets,
                  MetadataReference.CreateFromFile(
                      typeof(object).Assembly.Location),
                  MetadataReference.CreateFromFile(
                      typeof(InjectedDependencyAttribute).Assembly.Location));

        }

        public static (AdhocWorkspace workspace, Document document) 
            CreateAdHocLibraryProjectWorkspace(string classFileContets, 
                                               params MetadataReference[] references)
        {
            var referencesCopy = references.ToArray();

            var resultWorkspace = new AdhocWorkspace();

            Document document = resultWorkspace
                .AddProject(ProjectName, LanguageNames.CSharp)
                .AddMetadataReferences(references)
                .AddDocument(ClassFileName, classFileContets);

            return (resultWorkspace, document);
        }

        public static TextSpan GetTextSpanFromCodeSite(
                                                string code, 
                                                string refactoringSite)
        {
            var start = code.IndexOf(refactoringSite);

            if (start < 0)
            {
                throw new ArgumentException(
                    $"Refactoring site \"{refactoringSite}\" " +
                    $"not found in code \"{code}\"");
            }

            var length = refactoringSite.Length;

            return new TextSpan(start, length);

        }
    }
}
