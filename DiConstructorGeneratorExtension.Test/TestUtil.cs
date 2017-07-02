using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiConstructorGeneratorExtension.Test
{
    public static class TestUtil
    {
        private static void TestAnalyzeNode(string code, TextSpan span,
  Action<IList<Diagnostic>> testDiagnostics)
        {
            var tree = SyntaxFactory.ParseSyntaxTree(code);
            var methodNode = tree.GetRoot().FindNode(span);
            var compilation = CSharpCompilation.Create(null,
              syntaxTrees: new[] { tree },
              references: new[]
              {
      new MetadataFileReference(
        typeof(object).Assembly.Location),
      new MetadataFileReference(
        typeof(OperationContractAttribute).Assembly.Location)
              });
            var diagnostics = new List<Diagnostic>(); ;
            var addDiagnostic = new Action<Diagnostic>(_ => { diagnostics.Add(_); });

            var analyzer = new IsOneWayOperationAnalyzer();
            analyzer.AnalyzeNode(methodNode, compilation.GetSemanticModel(tree),
              addDiagnostic, new CancellationToken(false));

            testDiagnostics(diagnostics);
        }
    }
}
