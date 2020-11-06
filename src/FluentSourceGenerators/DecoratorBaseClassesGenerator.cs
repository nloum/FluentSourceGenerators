using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ComposableCollections.CodeGenerator
{
    public class DecoratorBaseClassesGenerator : GeneratorBase<DecoratorBaseClassesGeneratorSettings>
    {
        private DecoratorBaseClassesGeneratorSettings _settings;

        public override void Initialize(DecoratorBaseClassesGeneratorSettings settings)
        {
            _settings = settings;
        }

        public override ImmutableDictionary<string, string> Generate(CodeIndexerService codeIndexerService)
        {
            var results = new Dictionary<string, string>();

            foreach (var iface in _settings.InterfacesToImplement)
            {
                var interfaceDeclaration = codeIndexerService.GetInterfaceDeclaration(iface);
                
                var semanticModel = codeIndexerService.GetSemanticModel(interfaceDeclaration.SyntaxTree);

                var usings = new List<string>();

                var interfaceSymbol = semanticModel.GetDeclaredSymbol(interfaceDeclaration);
                var baseInterfaces = Utilities.GetBaseInterfaces(interfaceSymbol);
                foreach (var item in baseInterfaces.SelectMany(baseInterface => baseInterface.DeclaringSyntaxReferences))
                {
                    var moreUsings = Utilities.GetDescendantsOfType<UsingDirectiveSyntax>(item.SyntaxTree.GetRoot())
                        .Select(us => $"using {us.Name};");
                    usings.AddRange(moreUsings);
                }

                var duplicateMembers = new DuplicateMembersService(interfaceDeclaration, semanticModel);
                var delegateMember = new DelegateMemberService();
                
                var className = $"{iface.Substring(1)}DecoratorBase";
                var sourceCodeBuilder = new StringBuilder();

                var typeParameters = interfaceDeclaration.TypeParameterList.Parameters.Select(tps => tps.Identifier.Text).ToImmutableList();
                var genericParams = "";
                if (typeParameters.Count > 0)
                {
                    genericParams = $"<{string.Join(", ", typeParameters)}>";
                }

                sourceCodeBuilder.AppendLine(
                    $"namespace {_settings.Namespace} {{\npublic class {className}{genericParams} : {iface}{genericParams} {{");

                //var parameterName = Utilities.GenerateVariableName(interfaceDeclaration.Identifier.Text, true);
                var parameterName = "decoratedObject";
                
                var parameters = $"{interfaceDeclaration.Identifier}{genericParams} {parameterName}";

                var parameterString = string.Join(", ", parameters);
                //var fieldName = "_" + parameterName;
                var fieldName = "_decoratedObject";

                sourceCodeBuilder.AppendLine($"private readonly {interfaceDeclaration.Identifier}{genericParams} {fieldName};");

                sourceCodeBuilder.AppendLine($"public {className}({parameterString}) {{");
                
                sourceCodeBuilder.AppendLine($"{fieldName} = {parameterName};");
                sourceCodeBuilder.AppendLine("}");
                
                var members = Utilities.GetMembersGroupedByDeclaringType(interfaceDeclaration, semanticModel);
                
                foreach (var member in members)
                {
                    foreach (var memberItem in member.Value)
                    {
                        delegateMember.DelegateMember(memberItem, fieldName,null, duplicateMembers.IsDuplicate(memberItem), sourceCodeBuilder, usings);
                    }
                }

                sourceCodeBuilder.AppendLine("}\n}\n");

                usings = usings.Distinct().OrderBy(x => x).ToList();
                
                results.Add($"{className}.g.cs", $"{string.Join("\n",usings)}\n{sourceCodeBuilder}");
            }

            return results.ToImmutableDictionary();
        }
    }
}