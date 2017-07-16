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
                                    ClassDeclarationSyntax classDecl, 
                                    CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken);

            var @class = root.GetMatchingClassDeclaration(classDecl);

            var hasAllNeededParts = TryGetRequiredParts(document, root, @class,
                out MemberDeclarationSyntax[] injectables,
                out ConstructorDeclarationSyntax constructor,
                out Document newDocument);

            if(hasAllNeededParts == false)
            {
                return newDocument;
            }

            var newConstructor = RegenereateConstructorSyntax(injectables, constructor);

            var newClass = @class.ReplaceNode(constructor, newConstructor);
            var newDocumentRoot = root.ReplaceNode(@class, newClass);
            newDocument = document.WithSyntaxRoot(newDocumentRoot);
            return newDocument;
        }

        private static bool TryGetRequiredParts(
                                Document document,
                                SyntaxNode root,
                                ClassDeclarationSyntax @class, 
                                out MemberDeclarationSyntax[] injectables, 
                                out ConstructorDeclarationSyntax constructor,
                                out Document newDocument)
        {
            injectables = null;
            constructor = null;
            newDocument = null;

            injectables = GetInjectableMembers(@class);
            if (injectables.Any() == false)
            {
                var errorMessage = "Can't regenerate constructor, no candidate members found " +
                                    $"(readonly fields, properties markead with {nameof(InjectedDependencyAttribute)}).";
                newDocument = TypeDeclWithCommentAtOpeningBrace(document, root, @class, errorMessage);
                return false;
            }

            var publicConstructors =
               @class.ChildNodes()
                       .Where(n => n.Fits(SyntaxKind.ConstructorDeclaration) 
                                    && n.ChildTokens().Any(x => x.Fits(SyntaxKind.PublicKeyword)))
                       .Cast<ConstructorDeclarationSyntax>()
                       .ToArray();

            var count = publicConstructors.Count();
            if (count > 1)
            {
                var errorMessage = "Can't regenerate constructor, type contains multiple public constructors.";
                newDocument = TypeDeclWithCommentAtOpeningBrace(document, root, @class, errorMessage);
                return false;
            }
            else if (count == 0)
            {
                //todo create new constructor
                newDocument = null;
                return false;
            }

            constructor = publicConstructors.FirstOrDefault();

            return true;
        }

        private static Document TypeDeclWithCommentAtOpeningBrace(
                                                        Document document, 
                                                        SyntaxNode root, 
                                                        ClassDeclarationSyntax type, 
                                                        string errorMessage)
        {
            var explanatoryCommentTrivia = SF.Comment("//" + errorMessage);
            var endOfLineTrivia = SF.EndOfLine("\r\n");
            var leadingTrivia = @type.OpenBraceToken.LeadingTrivia;

            var typeUpdatedWithExplanatoryComment = @type.WithOpenBraceToken(
                    SF.Token(
                        leadingTrivia,
                        SyntaxKind.OpenBraceToken,
                        SF.TriviaList(
                            explanatoryCommentTrivia,
                            endOfLineTrivia)));

            var newDocumentRoot = root.ReplaceNode(@type, typeUpdatedWithExplanatoryComment);
            var newDocument = document.WithSyntaxRoot(newDocumentRoot);
            return newDocument;
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

        private static ParameterSyntax[] GetParametersListWithMissingInjectablesAdded(
                                                        MemberDeclarationSyntax[] injectables,
                                                        ConstructorDeclarationSyntax constructor)
        {
            Dictionary<TypeSyntax, MemberDeclarationSyntax> injectablesByTypeIdentifier =
                injectables
                    .ToDictionary(
                        x => x.GetMemberType(),
                        x => x);

            var preexistingParameters =
                constructor.ParameterList
                           .ChildNodes()
                           .Cast<ParameterSyntax>()
                           .ToArray();

            var preexistingParameterTypes =
                preexistingParameters
                    .Select(x => x.Type)
                    .ToArray();

            var missingParametersByType =
                injectablesByTypeIdentifier
                     .Keys
                     .Where(x => preexistingParameterTypes.Contains(x) == false)
                     .ToArray();

            var newParamters = missingParametersByType
                .Select(x =>
                {
                    var injectable = injectablesByTypeIdentifier[x];
                    var injectableIdentifier = injectable.GetMemberIdentifier();
                    var paramIdentifierName = injectableIdentifier.ValueText;

                    if (paramIdentifierName.StartsWith("_"))
                    {
                        paramIdentifierName = paramIdentifierName.Substring(1);
                    }
                    else
                    {
                        paramIdentifierName = "_" + paramIdentifierName;
                    }
                    var parameterIdentifierSyntax = SF.Identifier(paramIdentifierName);

                    return SF.Parameter(
                             SF.List<AttributeListSyntax>(),
                             SF.TokenList(),
                             SF.ParseTypeName(x.GetText().ToString()),
                             parameterIdentifierSyntax,
                             null);
                })
                .ToArray();

            var combinedNewAndPreexistingParameters =
                Enumerable.Concat(preexistingParameters, newParamters)
                            .ToArray();

            return combinedNewAndPreexistingParameters;

        }

        private static ConstructorDeclarationSyntax RegenereateConstructorSyntax(
                                                        MemberDeclarationSyntax[] injectables,
                                                        ConstructorDeclarationSyntax constructor)
        {
            var updatedParamters = GetParametersListWithMissingInjectablesAdded(
                                                                                injectables,
                                                                                constructor);

            var separatedParameters = SF.SeparatedList(updatedParamters);

            constructor = constructor.WithParameterList(SF.ParameterList(separatedParameters));

            string[] existingAssignments = GetExistingAssignmentsInConstructor(constructor);

            var injectablesMissingAnAssignment = injectables.Where(x =>
            {
                var name = x.GetMemberIdentifier().Text;
                return existingAssignments.Contains(name) == false;
            });

            IEnumerable<StatementSyntax> newBodyStatements = 
                GetBodyStatementsWithMissinggAssignmentsPrepended(
                                                            constructor, 
                                                            updatedParamters, 
                                                            injectablesMissingAnAssignment);

            var newBodySyntaxList = SF.List(newBodyStatements);

            var newBody = constructor.Body.WithStatements(newBodySyntaxList);

            constructor = constructor.WithBody(newBody);

            return constructor;
        }

        private static IEnumerable<StatementSyntax> GetBodyStatementsWithMissinggAssignmentsPrepended(ConstructorDeclarationSyntax constructor, ParameterSyntax[] updatedParamters, IEnumerable<MemberDeclarationSyntax> injectablesMissingAnAssignment)
        {
            var assignmentStatementsToAdd = injectablesMissingAnAssignment.Select(injectable =>
            {
                var injectableTypeIdentifier = (IdentifierNameSyntax)injectable.GetMemberType();
                var injectableType = injectableTypeIdentifier.Identifier.Text;
                var injectableName = injectable.GetMemberIdentifier().Text;

                var correspondingParameter = updatedParamters
                    .SingleOrDefault(parameter =>
                    {
                        var paramType = ((IdentifierNameSyntax)parameter.Type).Identifier.Text;

                        return paramType == injectableType;
                    });

                if (correspondingParameter == null)
                {
                    return null;
                }

                var paramName = correspondingParameter.Identifier.Text;

                return SF.ExpressionStatement(
                                    SF.AssignmentExpression(
                                        SyntaxKind.SimpleAssignmentExpression,
                                        SF.IdentifierName(injectableName),
                                        SF.IdentifierName(paramName)));

            }).ToArray();

            var newBodyStatements = Enumerable.Concat(
                                        assignmentStatementsToAdd,
                                        constructor.Body.Statements);
            return newBodyStatements;
        }

        private static string[] GetExistingAssignmentsInConstructor(ConstructorDeclarationSyntax constructor)
        {
            return constructor
                 .Body
                 .ChildNodes()
                 .Where(n => n.Fits(SyntaxKind.ExpressionStatement))
                 .Cast<ExpressionStatementSyntax>()
                 .Select(x => x.DescendantNodes()
                                .Where(y => y
                                            .Fits(SyntaxKind.SimpleAssignmentExpression))
                                            .SingleOrDefault())
                 .Where(x => x != null)
                 .Cast<AssignmentExpressionSyntax>()
                 .Where(x => x.Left.Fits(SyntaxKind.IdentifierName))
                 .Select(x => (IdentifierNameSyntax)x.Left)
                 .Select(x => x.Identifier.Text)
                 .ToArray();
        }
    }
}