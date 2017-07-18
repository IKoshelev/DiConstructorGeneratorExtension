using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiConstructorGeneratorExtension
{
    public static class RoslynExtension
    {
        public static bool Fits(this SyntaxKind kind, params SyntaxKind[] options)
        {
            return options.Contains(kind);
        }

        public static bool Fits(this SyntaxNode node, params SyntaxKind[] options)
        {
            return options.Contains(node.Kind());
        }

        public static bool Fits(this SyntaxToken token, params SyntaxKind[] options)
        {
            return options.Contains(token.Kind());
        }

        public static SyntaxToken[] GetAttributeIdentifiers(this SyntaxNode target)
        {
            return target.DescendantNodes()
                    .Where(n => n.Fits(SyntaxKind.Attribute))
                    .SelectMany(n => n.DescendantTokens())
                    .Where(n => n.Fits(SyntaxKind.IdentifierToken))
                    .ToArray();
        }

        public static bool FitsAttrIdentifier(this SyntaxToken token, Type attributeType)
        {
            var attirubteNames = new[]
            {
                attributeType.Name,
                attributeType.Name.Replace("Attribute","")
            };

            return attirubteNames.Contains(token.Value);
        }

        public static ClassDeclarationSyntax GetMatchingClassDeclaration(
            this SyntaxNode token, 
            BaseTypeDeclarationSyntax identifierSource)
        {
            return (ClassDeclarationSyntax)token.DescendantNodes()
                .First(n => n.Fits(SyntaxKind.ClassDeclaration)
                            && ((TypeDeclarationSyntax)n).Identifier
                                                    == identifierSource.Identifier);
        }

        public static string GetMemberName(this MemberDeclarationSyntax member)
        {
            return member.GetMemberIdentifier().Text;
        }

        public static SyntaxToken GetMemberIdentifier(this MemberDeclarationSyntax member)
        {
            switch (member)
            {
                case PropertyDeclarationSyntax prop:
                    return prop.Identifier;
                    break;
                case FieldDeclarationSyntax field:
                    if(field.Declaration.Variables.Count() != 1)
                    {
                        throw new ArgumentException("Encountered injectable field declaration with many " +
                            "fields inside. This makes no sense for injectable properties.");
                    }
                    return field.Declaration.Variables.Single().Identifier;
                    break;
                default:
                    throw new ArgumentException($"Unknown MemberDeclarationSyntax node type : {member.Kind().ToString()}");
            }
        }

        public static TypeSyntax GetMemberType(this MemberDeclarationSyntax member)
        {
            switch (member)
            {
                case PropertyDeclarationSyntax prop:
                    return prop.Type;
                    break;
                case FieldDeclarationSyntax field:
                    return field.Declaration.Type;
                    break;
                default:
                    throw new ArgumentException($"Unknown MemberDeclarationSyntax node type : {member.Kind().ToString()}");
            }
        }

        public static string GetTypeName(this TypeSyntax member)
        {
            switch (member)
            {
                case PredefinedTypeSyntax predefined:
                    return predefined.Keyword.Text;
                    break;
                case IdentifierNameSyntax name:
                    return name.Identifier.Text;
                    break;
                default:
                    throw new ArgumentException($"Unknown TypeSyntax node type : {member.Kind().ToString()}");
            }
        }
    }
}
