using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ComposableCollections.CodeGenerator
{
    public class SubclassCombinationImplementationsGenerator : GeneratorBase<SubclassCombinationImplementationsGeneratorSettings>
    {
        private SubclassCombinationImplementationsGeneratorSettings _settings;

        public override void Initialize(SubclassCombinationImplementationsGeneratorSettings settings)
        {
            _settings = settings;
        }

        public override ImmutableDictionary<string, string> Generate(CodeIndexerService codeIndexerService)
        {
            var result = new Dictionary<string, string>();

            var theClass = codeIndexerService.GetClassDeclaration(_settings.BaseClass);
            var theClassSemanticModel = codeIndexerService.GetSemanticModel(theClass.SyntaxTree);

            var baseInterfaceName = theClass.BaseList.Types.Select(baseType =>
            {
                var key = baseType.Type.ToString();
                if (key.Contains("<"))
                {
                    key = key.Substring(0, key.IndexOf('<'));
                }

                if (codeIndexerService.TryGetInterfaceDeclaration(key, out var _))
                {
                    return key;
                }

                return "";
            }).First(str => !string.IsNullOrWhiteSpace(str));
            var baseInterface = codeIndexerService.GetInterfaceDeclaration(baseInterfaceName);
            var baseInterfaceSymbol = codeIndexerService.GetSymbol(baseInterface);

            var subInterfaces = new List<InterfaceDeclarationSyntax>();

            foreach(var interfaceDeclaration in codeIndexerService.GetAllInterfaceDeclarations())
            {
                if (Utilities.IsBaseInterface(codeIndexerService.GetSymbol(interfaceDeclaration), baseInterfaceSymbol))
                {
                    subInterfaces.Add(interfaceDeclaration);
                }
            }

            if (subInterfaces.Contains(baseInterface))
            {
                subInterfaces.Remove(baseInterface);
            }

            var delegateMemberService = new DelegateMemberService();
            var memberDeduplicationService = new MemberDeduplicationService();

            var constructors = theClass.Members.OfType<ConstructorDeclarationSyntax>()
                .ToImmutableList();
            var baseInterfaces = Utilities.GetBaseInterfaces(theClassSemanticModel.GetDeclaredSymbol(theClass));
            var baseMembers = baseInterfaces.SelectMany(baseInterface => baseInterface.GetMembers());
            var baseMemberExplicitImplementationProfiles =
                baseMembers.Select(baseMember => memberDeduplicationService.GetExplicitImplementationProfile(baseMember)).Distinct().ToImmutableHashSet();
            var baseMemberImplementationProfiles =
                baseMembers.Select(baseMember => memberDeduplicationService.GetImplementationProfile(baseMember)).Distinct().ToImmutableHashSet();
            
            foreach (var subInterface in subInterfaces)
            {
                var usings = new List<string>();
                var classDefinition = new List<string>();

                var subClassName = subInterface.Identifier.Text.Substring(1);
                foreach (var modifier in _settings.ClassNameModifiers)
                {
                    subClassName = Regex.Replace(subClassName, modifier.Search ?? "", modifier.Replace ?? "");
                }

                if (_settings.ClassNameBlacklist.Any(classNameBlacklistItem =>
                    Regex.IsMatch(subClassName, classNameBlacklistItem)))
                {
                    continue;
                }
                
                if (!_settings.ClassNameWhitelist.All(classNameWhitelistItem =>
                    Regex.IsMatch(subClassName, classNameWhitelistItem)))
                {
                    continue;
                }

                usings.AddRange(Utilities.GetDescendantsOfType<UsingDirectiveSyntax>(subInterface.SyntaxTree.GetRoot())
                    .Select(us => $"using {us.Name};\n"));
                usings.AddRange(Utilities.GetDescendantsOfType<UsingDirectiveSyntax>(theClass.SyntaxTree.GetRoot())
                    .Select(us => $"using {us.Name};\n"));

                classDefinition.Add($"\nnamespace {_settings.Namespace} {{\n");
                var subInterfaceTypeArgs =
                    string.Join(", ", subInterface.TypeParameterList.Parameters.Select(p => p.Identifier));
                if (!string.IsNullOrWhiteSpace(subInterfaceTypeArgs))
                {
                    subInterfaceTypeArgs = $"<{subInterfaceTypeArgs}>";
                }
                classDefinition.Add($"public class {subClassName}{theClass.TypeParameterList} : {theClass.Identifier}{theClass.TypeParameterList}, {subInterface.Identifier}{subInterfaceTypeArgs} {{\n");

                var stuffAddedForSubInterface =
                    Utilities.GetBaseInterfaces(codeIndexerService.GetSemanticModel(subInterface.SyntaxTree).GetDeclaredSymbol(subInterface))
                        .Except(Utilities.GetBaseInterfaces(theClassSemanticModel.GetDeclaredSymbol(theClass)));

                var adaptedParameter = constructors.First().ParameterList.Parameters.First();
                var desiredAdaptedBaseInterfaces = Utilities
                    .GetBaseInterfaces(
                        theClassSemanticModel.GetSymbolInfo(adaptedParameter.Type).Symbol as INamedTypeSymbol)
                    .Concat(stuffAddedForSubInterface).Select(x => x.ToString()).ToImmutableHashSet();
                var adaptedParameterTypeArgs = "";
                var tmp = adaptedParameter.Type.ToString();
                if (tmp.Contains("<"))
                {
                    adaptedParameterTypeArgs = tmp.Substring(tmp.IndexOf('<'));
                }

                if (_settings.AllowDifferentTypeParameters)
                {
                    desiredAdaptedBaseInterfaces = desiredAdaptedBaseInterfaces.Select(Utilities.GetWithoutTypeArguments).ToImmutableHashSet();
                }

                InterfaceDeclarationSyntax bestAdaptedInterface = null;

                foreach (var iface in codeIndexerService.GetAllInterfaceDeclarations())
                {
                    var ifaceBaseInterfaces = Utilities
                        .GetBaseInterfaces(codeIndexerService.GetSemanticModel(iface.SyntaxTree)
                            .GetDeclaredSymbol(iface))
                        .Select(x => x.ToString()).ToImmutableHashSet();
                    
                    if (_settings.AllowDifferentTypeParameters)
                    {
                         ifaceBaseInterfaces = ifaceBaseInterfaces.Select(Utilities.GetWithoutTypeArguments).ToImmutableHashSet();
                    }
                    
                    //if (iface.Identifier == subInterface.Identifier)
                    if (subInterface.Identifier.Text == "IDisposableDictionary")
                    {
                        int a = 3;
                        var union = ifaceBaseInterfaces.Union(desiredAdaptedBaseInterfaces);
                        var except1 = ifaceBaseInterfaces.Except(desiredAdaptedBaseInterfaces);
                        var except2 = desiredAdaptedBaseInterfaces.Except(ifaceBaseInterfaces);
                    }

                    if (desiredAdaptedBaseInterfaces.Count == ifaceBaseInterfaces.Count)
                    {
                        if (ifaceBaseInterfaces.All(ifaceBaseInterface =>
                            desiredAdaptedBaseInterfaces.Contains(ifaceBaseInterface)))
                        {
                            bestAdaptedInterface = iface;
                            break;
                        }
                    }
                }

                classDefinition.Add(
                    $"private readonly {bestAdaptedInterface.Identifier}{adaptedParameterTypeArgs} _adapted;\n");

                foreach (var constructor in constructors)
                {
                    var constructorParameters = new List<string>();
                    var baseConstructorArguments = new List<string>();

                    constructorParameters.Add(
                        $"{bestAdaptedInterface.Identifier}{adaptedParameterTypeArgs} adapted");
                    baseConstructorArguments.Add("adapted");

                    for (var i = 1; i < constructor.ParameterList.Parameters.Count; i++)
                    {
                        var parameter = constructor.ParameterList.Parameters[i];
                        constructorParameters.Add($"{parameter.Type} {parameter.Identifier}");
                        baseConstructorArguments.Add(parameter.Identifier.ToString());
                    }

                    classDefinition.Add($"public {subClassName}(");
                    classDefinition.Add(string.Join(", ", constructorParameters));
                    classDefinition.Add(") : base(" + string.Join(", ", baseConstructorArguments) + ") {\n");
                    classDefinition.Add("_adapted = adapted;");
                    classDefinition.Add("}\n");
                }

                foreach (var member in memberDeduplicationService.GetDeduplicatedMembers(codeIndexerService.GetSemanticModel(bestAdaptedInterface.SyntaxTree).GetDeclaredSymbol(bestAdaptedInterface)))
                {
                    if (member.Duplicates.All(duplicate => baseMemberImplementationProfiles.Contains(memberDeduplicationService.GetImplementationProfile(duplicate))))
                    {
                        continue;
                    }

                    usings.AddRange(member.Duplicates.SelectMany(duplicate => duplicate.DeclaringSyntaxReferences).SelectMany(syntaxRef =>
                    {
                        var root = syntaxRef.SyntaxTree.GetRoot();
                        var usingDirectives = Utilities.GetDescendantsOfType<UsingDirectiveSyntax>(root);
                        return usingDirectives.Select(usingDirective => $"{usingDirective}\n");
                    }));
                    
                    var sourceCodeBuilder = new StringBuilder();
                    var shouldOverride = member.Duplicates.Any(duplicate => duplicate.ContainingType.TypeKind == TypeKind.Class);
                    delegateMemberService.DelegateMember(member.Value, "_adapted", null, member.ImplementExplicitly, sourceCodeBuilder, usings, DelegateType.DelegateObject, shouldOverride);
                    classDefinition.Add(sourceCodeBuilder + "\n");
                }

                classDefinition.Add("}\n}\n");
                result[subClassName + ".g.cs"] = string.Join("", usings.Distinct().Concat(classDefinition));
            }

            return result.ToImmutableDictionary();
        }
    }
}