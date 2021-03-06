using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FluentSourceGenerators
{
    public class CodeIndexerService
    {
        private readonly Func<SyntaxTree, SemanticModel?> _getSemanticModel;
        private Dictionary<string, List<InterfaceDeclarationSyntax>> _interfaceDeclarations;
        private Dictionary<string, List<ClassDeclarationSyntax>> _classDeclarations;

        public CodeIndexerService(IEnumerable<SyntaxTree> syntaxTrees, Func<SyntaxTree, SemanticModel> getSemanticModel)
        {
            _getSemanticModel = getSemanticModel;
            _interfaceDeclarations = new Dictionary<string, List<InterfaceDeclarationSyntax>>();
            _classDeclarations = new Dictionary<string, List<ClassDeclarationSyntax>>();

            var syntaxTreesList = syntaxTrees.ToImmutableList();
            
            foreach (var syntaxTree in syntaxTreesList)
            {
                Utilities.TraverseTree(syntaxTree.GetRoot(), node =>
                {
                    if (node is InterfaceDeclarationSyntax interfaceDeclarationSyntax)
                    {
                        if (!_interfaceDeclarations.ContainsKey(interfaceDeclarationSyntax.Identifier.Text))
                        {
                            _interfaceDeclarations[interfaceDeclarationSyntax.Identifier.Text] = new List<InterfaceDeclarationSyntax>();
                        }
                        
                        _interfaceDeclarations[interfaceDeclarationSyntax.Identifier.Text].Add(interfaceDeclarationSyntax);
                    }
                    else if (node is ClassDeclarationSyntax classDeclarationSyntax)
                    {
                        if (!_classDeclarations.ContainsKey(classDeclarationSyntax.Identifier.Text))
                        {
                            _classDeclarations[classDeclarationSyntax.Identifier.Text] = new List<ClassDeclarationSyntax>();
                        }
                        
                        _classDeclarations[classDeclarationSyntax.Identifier.Text].Add(classDeclarationSyntax);
                    }
                });
            }
        }

        public InterfaceDeclarationSyntax GetInterfaceDeclaration(string interfaceName)
        {
            return _interfaceDeclarations[interfaceName].First();
        }
        
        public IEnumerable<InterfaceDeclarationSyntax> GetInterfaceDeclarations(string interfaceName)
        {
            return _interfaceDeclarations[interfaceName];
        }

        public bool TryGetInterfaceDeclaration(string interfaceName,
            out InterfaceDeclarationSyntax interfaceDeclarationSyntax)
        {
            if (_interfaceDeclarations.TryGetValue(interfaceName, out var results))
            {
                interfaceDeclarationSyntax = results.FirstOrDefault();
                return interfaceDeclarationSyntax != null;
            }

            interfaceDeclarationSyntax = null;
            return false;
        }
        
        public ClassDeclarationSyntax GetClassDeclaration(string className)
        {
            return _classDeclarations[className].First();
        }
        
        public IEnumerable<ClassDeclarationSyntax> GetClassDeclarations(string className)
        {
            return _classDeclarations[className];
        }

        public bool TryGetClassDeclaration(string className,
            out ClassDeclarationSyntax classDeclarationSyntax)
        {
            if (_classDeclarations.TryGetValue(className, out var results))
            {
                classDeclarationSyntax = results.FirstOrDefault();
                return classDeclarationSyntax != null;
            }

            classDeclarationSyntax = null;
            return false;
        }

        public INamedTypeSymbol? GetSymbol(InterfaceDeclarationSyntax interfaceDecl)
        {
            return GetSemanticModel(interfaceDecl.SyntaxTree).GetDeclaredSymbol(interfaceDecl) as INamedTypeSymbol;
        }

        public INamedTypeSymbol? GetSymbol(ClassDeclarationSyntax classDecl)
        {
            return GetSemanticModel(classDecl.SyntaxTree).GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
        }
        
        public IEnumerable<InterfaceDeclarationSyntax> GetAllInterfaceDeclarations()
        {
            return _interfaceDeclarations.SelectMany(kvp => kvp.Value);
        }
        
        public IEnumerable<INamedTypeSymbol> GetAllInterfaceSymbols()
        {
            return _interfaceDeclarations.SelectMany(kvp => kvp.Value)
                .Select(iface =>
                {
                    var ifaceSemanticModel = GetSemanticModel(iface.SyntaxTree);
                    if (ifaceSemanticModel == null)
                    {
                        throw Utilities.MakeException(
                            $"The interface {iface.Identifier.Text} has no semantic model");
                    }
                    var ifaceSymbol = ifaceSemanticModel.GetDeclaredSymbol(iface);
                    if (ifaceSymbol == null)
                    {
                        throw Utilities.MakeException($"The interface {iface.Identifier.Text} has no symbol in the semantic model");
                    }

                    return (INamedTypeSymbol)ifaceSymbol;
                });
        }
        
        public IEnumerable<ClassDeclarationSyntax> GetAllClassDeclarations()
        {
            return _classDeclarations.SelectMany(kvp => kvp.Value);
        }

        public SemanticModel? GetSemanticModel(SyntaxTree syntaxTree)
        {
            return _getSemanticModel(syntaxTree);
        }
    }
}