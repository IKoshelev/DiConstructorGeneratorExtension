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
using System;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using DiConstructorGeneratorExtension.Attributes;

namespace DiConstructorGeneratorExtension
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(DiConstructorGeneratorExtensionCodeRefactoringProvider)), Shared]
    internal class DiConstructorGeneratorExtensionCodeRefactoringProvider : CodeRefactoringProvider
    {
        public sealed override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var root = await context.Document
                .GetSyntaxRootAsync(context.CancellationToken)
                .ConfigureAwait(false);

            // Find the node at the selection.
            var node = root.FindNode(context.Span);

            ClassDeclarationSyntax classDecl = null;
            ConstructorDeclarationSyntax constructorDecl = null;
            switch (node)
            {
                case ClassDeclarationSyntax @class:
                    classDecl = @class;
                    break;
                case ConstructorDeclarationSyntax @constructor 
                     when @constructor.Parent is ClassDeclarationSyntax @class:
                    constructorDecl = @constructor;
                    classDecl = @class;
                    break;
                default:
                    return;
                    break;
            } 
                
            var action = CodeAction.Create("(Re)Generate dependency injected constructor",
                cancelToken => RegenerateDependencyInjectedConstructor(context.Document, classDecl, constructorDecl, cancelToken));

            context.RegisterRefactoring(action);
        }

        private async Task<Document> RegenerateDependencyInjectedConstructor(
                                    Document document, 
                                    ClassDeclarationSyntax @class,
                                    ConstructorDeclarationSyntax constructor,
                                    CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken);

            var hasAllNeededParts = TryGetOrAddRequiredParts(
                                        ref document,
                                        ref root,
                                        ref @class,
                                        ref constructor,
                                        out MemberDeclarationSyntax[] injectables);

            if(hasAllNeededParts == false)
            {
                return document;
            }

            var newConstructor = RegenereateConstructorSyntax(injectables, constructor);
            var newDocumentRoot = root.ReplaceNode(constructor, newConstructor);
            document = document.WithSyntaxRoot(newDocumentRoot);
            return document;
        }

        private static bool TryGetOrAddRequiredParts(
                                ref Document document,
                                ref SyntaxNode root,
                                ref ClassDeclarationSyntax @class,
                                ref ConstructorDeclarationSyntax constructor,
                                out MemberDeclarationSyntax[] injectables)
        {
           
            injectables = null;

            injectables = GetInjectableMembers(@class);
            if (injectables.Any() == false)
            {
                var errorMessage = "Can't regenerate constructor, no unassgined candidate members found " +
                                    $"(readonly fields, properties markead with {nameof(InjectedDependencyAttribute)}).";
                document = NotifyErrorViaCommentToClassOrConstructor(document, root, @class, constructor, errorMessage);
                return false;
            }

            var injectablesWithSameType =
                injectables.GroupBy(x => x.GetMemberType().GetTypeName())
                            .FirstOrDefault(x => x.Count() > 1);

            if (injectablesWithSameType != null)
            {
                var namesOfOfenders = injectablesWithSameType
                                            .Select(x => x.GetMemberName());

                var joinedNamesOfOfenders = string.Join(",", namesOfOfenders);

                var errorMessage = $"Can't regenerate constructor, {joinedNamesOfOfenders} " +
                                    "have the same type (can't generate unique parameter).";

                document = NotifyErrorViaCommentToClassOrConstructor(document, root, @class, constructor, errorMessage);
                return false;
            }

            if (constructor != null)
            {
                // a constructor was already chosen
                return true;
            }

            ConstructorDeclarationSyntax[] publicConstructors = GetPublicEligableConstructors(@class);

            var constructorsCount = publicConstructors.Count();

            if (constructorsCount > 1)
            {
                var errorMessage = "Can't regenerate constructor, type contains multiple public constructors.";
                document = NotifyErrorViaCommentToClassOrConstructor(document, root, @class, constructor, errorMessage);
                return false;
            }
            else if (constructorsCount == 0)
            {
                (document, root, @class, constructor) =
                    GetTypeDeclarationWithEmptyConstructor(document, root, @class, injectables);
                return true;
            }
            else
            {
                constructor = publicConstructors.FirstOrDefault();
                return true;
            }
        }
       
        private static ConstructorDeclarationSyntax[] GetPublicEligableConstructors(ClassDeclarationSyntax @class)
        {
            var publicConstructors = @class.ChildNodes()
                       .Where(n => n.Fits(SyntaxKind.ConstructorDeclaration)
                                    && n.ChildTokens().Any(x => x.Fits(SyntaxKind.PublicKeyword)));

            var marked = publicConstructors
                       .Where(n =>
                       {
                           var attrMarkerType = typeof(DependencyInjectionConstructorAttribute);
                           return n
                                    .GetAttributeIdentifiers()
                                    .Any(x => x.FitsAttrIdentifier(attrMarkerType));

                       })
                       .ToArray();

            if (marked.Any())
            {
                return marked
                    .OfType<ConstructorDeclarationSyntax>()
                    .ToArray();
            }

            return publicConstructors
                        .OfType<ConstructorDeclarationSyntax>()
                        .ToArray();
        }

        private static (
                Document doc, 
                SyntaxNode root, 
                ClassDeclarationSyntax @class, 
                ConstructorDeclarationSyntax constructor) 
            GetTypeDeclarationWithEmptyConstructor(Document document,
                                                SyntaxNode root,
                                                ClassDeclarationSyntax @class,
                                                MemberDeclarationSyntax[] injectables)
        {
            var classsName = @class.Identifier.Text;

            var newConstructor = SF.ConstructorDeclaration(
                    SF.Identifier(classsName))
                        .WithModifiers(SF.TokenList(
                            SF.Token(
                                SF.TriviaList(),
                                SyntaxKind.PublicKeyword,
                                SF.TriviaList(SF.Space))))
                        .WithBody(SF.Block());

            var members = @class.Members;
            var lastInjectableIndex = @class.Members.IndexOf(injectables.Last());

            var lastInjectableIsLastMember = lastInjectableIndex == @class.Members.Count();
            ClassDeclarationSyntax newClass;
            if (lastInjectableIsLastMember)
            {
                newClass = @class.AddMembers(newConstructor);
            }
            else
            {
                var newMmebers = members.Insert(lastInjectableIndex + 1, newConstructor);
                newClass = @class.WithMembers(newMmebers);
            }

            var newDocumentRoot = root.ReplaceNode(@class, newClass);
            var newDocument = document.WithSyntaxRoot(newDocumentRoot);

            newDocumentRoot = newDocument.GetSyntaxRootAsync().Result;
            newClass = newDocumentRoot.GetMatchingClassDeclaration(newClass);
            newConstructor = GetPublicEligableConstructors(newClass).Single();

            return (newDocument, newDocumentRoot, newClass, newConstructor);
        }

        private static Document NotifyErrorViaCommentToClassOrConstructor(
                                         Document document,
                                         SyntaxNode root,
                                         ClassDeclarationSyntax @class,
                                         ConstructorDeclarationSyntax constructor,
                                         string errorMessage)
        {
            if(constructor != null)
            {
                return ConstructorWithCommentPrepended(document, root, @class, constructor, errorMessage);
            }
            return ClassDeclWithCommentAtOpeningBrace(document, root, @class, errorMessage);
        }

        private static Document ClassDeclWithCommentAtOpeningBrace(
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

        private static Document ConstructorWithCommentPrepended(
                                                 Document document,
                                                 SyntaxNode root,
                                                 ClassDeclarationSyntax @class,
                                                 ConstructorDeclarationSyntax constructor,
                                                 string errorMessage)
        {
            var commentWithEndOfLine = new[] {SF.Comment("//" + errorMessage)};
            var existingLeadingTrivia = constructor.GetLeadingTrivia();
            var combinedTrivia = SF.TriviaList(
                Enumerable.Concat(commentWithEndOfLine, existingLeadingTrivia));

            var constructorWithNewLeadingTrivia = constructor.WithLeadingTrivia(combinedTrivia);

            var newClass = @class.ReplaceNode(constructor, constructorWithNewLeadingTrivia);
            var newDocumentRoot = root.ReplaceNode(@class, newClass);
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
                    .OfType<MemberDeclarationSyntax>()
                    .Where(x =>
                    {
                        bool alreadyHasAssignments = x.DescendantNodes()
                                            .Any(n => n.Fits(SyntaxKind.EqualsValueClause));

                        if (alreadyHasAssignments)
                        {
                            return false;
                        }

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
                                                        string[] existingConstructorAssignments,
                                                        ConstructorDeclarationSyntax constructor,
                                                        int indentationLength)
        {
            var indentationStr = new String(' ', indentationLength);

            Dictionary<TypeSyntax, MemberDeclarationSyntax> missingInjectablesByTypeIdentifier =
                injectables
                    .Where(x => existingConstructorAssignments.Contains(x.GetMemberName()) == false)
                    .ToDictionary(
                        x => x.GetMemberType(),
                        x => x);

            var preexistingParameters =
                constructor.ParameterList
                           .ChildNodes()
                           .OfType<ParameterSyntax>()
                           .ToArray();

            var preexistingParameterTypeNames =
                preexistingParameters
                    .Select(x => x.Type.GetTypeName())
                    .ToArray();

            var missingParametersByType =
                missingInjectablesByTypeIdentifier
                     .Keys
                     .Where(x => preexistingParameterTypeNames.Contains(x.GetTypeName()) == false)
                     .ToArray();

            var onlyOneParameterExpected = 
                (preexistingParameters.Count() + missingParametersByType.Count()) == 1;

            if (onlyOneParameterExpected)
            {
                indentationStr = "";
            }

            var identation = SF.Whitespace(indentationStr);

            preexistingParameters = preexistingParameters
                                        .Select(x => x.WithLeadingTrivia(identation))
                                        .ToArray();

            var newParamters = missingParametersByType
                .Select(x =>
                {
                    var injectable = missingInjectablesByTypeIdentifier[x];
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
                             SF.ParseTypeName(x.GetText().ToString())
                                        .WithLeadingTrivia(identation),
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
            string[] existingAssignments = GetExistingAssignmentsInConstructor(constructor);
            int indentationLength = GetParameterIndentation(constructor);

            var updatedParamters = GetParametersListWithMissingInjectablesAdded(
                                                                                injectables,
                                                                                existingAssignments,
                                                                                constructor,
                                                                                indentationLength);

            SyntaxTrivia newLine = SF.SyntaxTrivia(SyntaxKind.EndOfLineTrivia, "\r\n");
            SyntaxToken comaWithNewLine = SF.Token(
                                                    SF.TriviaList(),
                                                    SyntaxKind.CommaToken,
                                                    SF.TriviaList(newLine));
            SyntaxToken[] comasList = Enumerable
                                            .Range(0, updatedParamters.Length - 1)
                                            .Select(x => comaWithNewLine)
                                            .ToArray();

            var separatedParameters = SF.SeparatedList(updatedParamters, comasList);

            var parameterList = SF.ParameterList(separatedParameters);

            if (updatedParamters.Count() > 1)
            {
                var openParenWithNewline = SF.Token(
                                                SF.TriviaList(),
                                                SyntaxKind.OpenParenToken,
                                                SF.TriviaList(newLine));

                parameterList = parameterList.WithOpenParenToken(openParenWithNewline);
            }

            constructor = constructor.WithParameterList(parameterList);

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

        private static int GetParameterIndentation(ConstructorDeclarationSyntax constructor)
        {
            var text = constructor.GetText().ToString();
            var parenIndex = text.IndexOf("(");
            var stringUpToParen = text.Substring(0, parenIndex);
            var lastNewLineIndex = stringUpToParen.LastIndexOfAny(new char[] { '\r', '\n' });
            if(lastNewLineIndex == -1)
            {
                return stringUpToParen.Length;
            } else
            {
                return stringUpToParen.Length - lastNewLineIndex - 1;
            }
        }

        private static IEnumerable<StatementSyntax> GetBodyStatementsWithMissinggAssignmentsPrepended(
                                        ConstructorDeclarationSyntax constructor, 
                                        ParameterSyntax[] updatedParamters, 
                                        IEnumerable<MemberDeclarationSyntax> injectablesMissingAnAssignment)
        {
            ExpressionStatementSyntax[] assignmentStatementsToAdd = 
                GetMissingAssignmentExpressions(updatedParamters, injectablesMissingAnAssignment);

            var newBodyStatements = Enumerable.Concat(
                                        assignmentStatementsToAdd,
                                        constructor.Body.Statements);
            return newBodyStatements;
        }

        private static ExpressionStatementSyntax[] GetMissingAssignmentExpressions(
                                    ParameterSyntax[] updatedParamters, 
                                    IEnumerable<MemberDeclarationSyntax> injectablesMissingAnAssignment)
        {
            return injectablesMissingAnAssignment.Select(injectable =>
            {
                var injectableTypeIdentifier = injectable.GetMemberType();
                var injectableType = injectableTypeIdentifier.GetTypeName();
                var injectableName = injectable.GetMemberIdentifier().Text;

                var correspondingParameter = updatedParamters
                    .SingleOrDefault(parameter =>
                    {
                        var paramType = parameter.Type.GetTypeName();

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

            })
            .Where(x => x != null)
            .ToArray();
        }

        private static string[] GetExistingAssignmentsInConstructor(
                                            ConstructorDeclarationSyntax constructor)
        {
            return constructor
                 .Body
                 .ChildNodes()
                 .OfType<ExpressionStatementSyntax>()
                 .Select(x => x.DescendantNodes()
                                .Where(y => y
                                            .Fits(SyntaxKind.SimpleAssignmentExpression))
                                            .SingleOrDefault())
                 .Where(x => x != null)
                 .OfType<AssignmentExpressionSyntax>()
                 .Select(x => x.Left)
                 .OfType<IdentifierNameSyntax>()
                 .Select(x => x.Identifier.Text)
                 .ToArray();
        }
    }
}