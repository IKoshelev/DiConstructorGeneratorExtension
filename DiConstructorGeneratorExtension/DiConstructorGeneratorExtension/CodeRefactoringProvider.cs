using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using System;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory; 

namespace DiConstructorGeneratorExtension
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(DiConstructorGeneratorExtensionCodeRefactoringProvider)), Shared]
    internal class DiConstructorGeneratorExtensionCodeRefactoringProvider : CodeRefactoringProvider
    {
        public sealed override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            // TODO: Replace the following code with your own analysis, generating a CodeAction for each refactoring to offer

            var root = await context.Document
                .GetSyntaxRootAsync(context.CancellationToken)
                .ConfigureAwait(false);

            // Find the node at the selection.
            var node = root.FindNode(context.Span);

            // Only offer a refactoring if the selected node is a class declaration node.
            var typeDecl = node as ClassDeclarationSyntax;
            if (typeDecl == null)
            {
                return;
            }

            // For any type declaration node, create a code action to reverse the identifier text.
            var action = CodeAction.Create("(Re)Generate dependency injected constructor", 
                c => RegenerateDependencyInjectedConstructor(context.Document, typeDecl, c));

            // Register this code action.
            context.RegisterRefactoring(action);
        }

        private async Task<Document> RegenerateDependencyInjectedConstructor(
                                    Document document, 
                                    TypeDeclarationSyntax typeDecl, 
                                    CancellationToken cancellationToken)
        {
            SyntaxKind[] typeDeclarationSyntax = new[]
            {
                SyntaxKind.ClassDeclaration,
                SyntaxKind.StructDeclaration
            };

            var root = await document.GetSyntaxRootAsync();

            var @type = (ClassDeclarationSyntax)
                root.DescendantNodes()
                    .First(n => n.Fits(typeDeclarationSyntax)
                                && ((ClassDeclarationSyntax)n).Identifier 
                                                        == typeDecl.Identifier);

            var injectableMembers = GetInjectableMembers(type);

            var constructors =
               @type.ChildNodes()
               .Where(n => n.Fits(SyntaxKind.ConstructorDeclaration))
               .Cast<ConstructorDeclarationSyntax>()
               .ToArray();

            var count = constructors.Count();
            if (count > 1)
            {
                var errorMessage = "Can't regenerate constructor, type contains multiple constructors.";
                var explanatoryCommentTrivia = SF.Comment("//" + errorMessage);

                var endOfLineTrivia = SF.EndOfLine("\r\n");

                var typeUpdatedWithExplanatoryComment = @type.WithOpenBraceToken(
                        SF.Token(
                            SF.TriviaList(),
                            SyntaxKind.OpenBraceToken,
                            SF.TriviaList(
                                explanatoryCommentTrivia,
                                endOfLineTrivia)));

                var newDocumentRoot = root.ReplaceNode(@type, typeUpdatedWithExplanatoryComment);
                var newDocument = document.WithSyntaxRoot(newDocumentRoot);
                return newDocument;

            }
            else if (count == 0)
            {
                return null;
            }

            var constructor = constructors.FirstOrDefault();

            var parameters = constructor.ParameterList
                .ChildNodes()
                .Cast<ParameterSyntax>()
                .OrderBy(node => ((IdentifierNameSyntax)node.Type).Identifier.ToString())
                .Select(node => SF.Parameter(
                    SF.List<AttributeListSyntax>(),
                    SF.TokenList(),
                    SF.ParseTypeName(((IdentifierNameSyntax)node.Type).Identifier.Text),
                    SF.Identifier(node.Identifier.Text),
                    null));

            //var updatedParameterList = SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters));

            //((SyntaxNode)constructor).ReplaceNode(constructor.ParameterList, updatedParameterList);



            return null;
            //// Produce a reversed version of the type declaration's identifier token.
            //var identifierToken = typeDecl.Identifier;
            //var newName = new string(identifierToken.Text.ToCharArray().Reverse().ToArray());

            //// Get the symbol representing the type to be renamed.
            //var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            //var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken);

            //// Produce a new solution that has all references to that type renamed, including the declaration.
            //var originalSolution = document.Project.Solution;
            //var optionSet = originalSolution.Workspace.Options;
            //var newSolution = await Renamer.RenameSymbolAsync(document.Project.Solution, typeSymbol, newName, optionSet, cancellationToken).ConfigureAwait(false);

            //// Return the new solution with the now-uppercase type name.
            //return newSolution;

        }

        private static MemberDeclarationSyntax[] GetInjectableMembers(TypeDeclarationSyntax type)
        {
            SyntaxKind[] propOrFieldDeclaration = new[]
            {
                SyntaxKind.FieldDeclaration,
                SyntaxKind.PropertyDeclaration
            };

            var injectableMembers = 
                @type
                    .ChildNodes()
                    .Where(x => x.Fits(propOrFieldDeclaration))
                    .Cast<MemberDeclarationSyntax>()
                    .Where(x =>
                    {
                        bool isReadonlyField = x.DescendantTokens()
                                            .Any(n => n.Fits(SyntaxKind.ReadOnlyKeyword));

                        SyntaxToken[] attributes = x.GetAttributeIdentifiers();

                        bool hasDependencyAttr = attributes
                                .Any(y => y.FitsAttrIdentifier(typeof(InjectedDependencyAttribute)));

                        bool hasExcludedForDepAttr = attributes
                                .Any(y => y.FitsAttrIdentifier(typeof(ExcludeFromInjectedDependenciesAttribute)));

                        if (hasExcludedForDepAttr)
                        {
                            return false;
                        }

                        return isReadonlyField || hasDependencyAttr;

                    })
                    .Where(x => true)
                    .ToArray();

            return injectableMembers;
        }
    }
}