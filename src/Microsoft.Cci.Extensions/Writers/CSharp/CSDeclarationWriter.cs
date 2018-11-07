// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using Microsoft.Cci.Extensions;
using Microsoft.Cci.Extensions.CSharp;
using Microsoft.Cci.Filters;
using Microsoft.Cci.Writers.Syntax;

namespace Microsoft.Cci.Writers.CSharp
{
    public partial class CSDeclarationWriter : ICciDeclarationWriter
    {
        private readonly ISyntaxWriter _writer;
        private readonly ICciFilter _filter;
        private bool _forCompilation;
        private bool _forCompilationIncludeGlobalprefix;
        private string _platformNotSupportedExceptionMessage;
        private bool _includeFakeAttributes;
        private bool _alwaysIncludeBase;

        public CSDeclarationWriter(ISyntaxWriter writer)
            : this(writer, new PublicOnlyCciFilter())
        {
        }

        public CSDeclarationWriter(ISyntaxWriter writer, ICciFilter filter)
            : this(writer, filter, true)
        {
        }

        public CSDeclarationWriter(ISyntaxWriter writer, ICciFilter filter, bool forCompilation)
        {
            Contract.Requires(writer != null);
            _writer = writer;
            _filter = filter;
            _forCompilation = forCompilation;
            _forCompilationIncludeGlobalprefix = false;
            _platformNotSupportedExceptionMessage = null;
            _includeFakeAttributes = false;
            _alwaysIncludeBase = false;
        }

        public CSDeclarationWriter(ISyntaxWriter writer, ICciFilter filter, bool forCompilation, bool includePseudoCustomAttributes = false)
            : this(writer, filter, forCompilation)
        {
            _includeFakeAttributes = includePseudoCustomAttributes;
        }

        public bool ForCompilation
        {
            get { return _forCompilation; }
            set { _forCompilation = value; }
        }

        public bool ForCompilationIncludeGlobalPrefix
        {
            get { return _forCompilationIncludeGlobalprefix; }
            set { _forCompilationIncludeGlobalprefix = value; }
        }

        public string PlatformNotSupportedExceptionMessage
        {
            get { return _platformNotSupportedExceptionMessage; }
            set { _platformNotSupportedExceptionMessage = value; }
        }

        public bool AlwaysIncludeBase
        {
            get { return _alwaysIncludeBase; }
            set { _alwaysIncludeBase = value; }
        }

        public ISyntaxWriter SyntaxtWriter { get { return _writer; } }

        public ICciFilter Filter { get { return _filter; } }

        public void WriteDeclaration(IDefinition definition)
        {
            if (definition == null)
                return;

            IAssembly assembly = definition as IAssembly;
            if (assembly != null)
            {
                WriteAssemblyDeclaration(assembly);
                return;
            }

            INamespaceDefinition ns = definition as INamespaceDefinition;
            if (ns != null)
            {
                WriteNamespaceDeclaration(ns);
                return;
            }

            ITypeDefinition type = definition as ITypeDefinition;
            if (type != null)
            {
                WriteTypeDeclaration(type);
                return;
            }

            ITypeDefinitionMember member = definition as ITypeDefinitionMember;
            if (member != null)
            {
                WriteMemberDeclaration(member);
                return;
            }

            DummyInternalConstructor ctor = definition as DummyInternalConstructor;
            if (ctor != null)
            {
                WritePrivateConstructor(ctor.ContainingType);
                return;
            }

            INamedEntity named = definition as INamedEntity;
            if (named != null)
            {
                WriteIdentifier(named.Name);
                return;
            }

            _writer.Write("Unknown definition type {0}", definition.ToString());
        }

        public void WriteAttribute(ICustomAttribute attribute)
        {
            WriteSymbol("[");
            WriteAttribute(attribute, null);
            WriteSymbol("]");
        }

        public void WriteAssemblyVersion(IAssembly assembly) {
            _writer.Write(String.Format("[assembly:System.Reflection.AssemblyVersionAttribute(\"{0}\")]", assembly.Version));
            _writer.WriteLine();
        }
        public void WriteAssemblyDeclaration(IAssembly assembly)
        {
            WriteAttributes(assembly.Attributes, prefix: "assembly");
            WriteAttributes(assembly.SecurityAttributes, prefix: "assembly");
        }

        public void WriteTypeForwardedTo(IAssembly assembly)
        {
            var forwardedTypeNames = assembly
                .GetForwardedTypes()
                .Where(t => !(t.AliasedType is INestedTypeDefinition || t.AliasedType is Dummy))
                .Select(t => TypeHelper.GetTypeName(t.AliasedType, NameFormattingOptions.TypeParameters | NameFormattingOptions.EmptyTypeParameterList | NameFormattingOptions.UseTypeKeywords))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

            foreach (var typeName in forwardedTypeNames)
            {
                _writer.Write(String.Format("[assembly:System.Runtime.CompilerServices.TypeForwardedToAttribute(typeof({0}))]", typeName));
                _writer.WriteLine();
            }
        }

        public void WriteMemberDeclaration(ITypeDefinitionMember member)
        {
            IMethodDefinition method = member as IMethodDefinition;
            if (method != null)
            {
                WriteMethodDefinition(method);
                return;
            }

            IPropertyDefinition property = member as IPropertyDefinition;
            if (property != null)
            {
                WritePropertyDefinition(property);
                return;
            }

            IEventDefinition evnt = member as IEventDefinition;
            if (evnt != null)
            {
                WriteEventDefinition(evnt);
                return;
            }

            IFieldDefinition field = member as IFieldDefinition;
            if (field != null)
            {
                WriteFieldDefinition(field);
                return;
            }

            _writer.Write("Unknown member definitions type {0}", member.ToString());
        }

        private void WriteVisibility(TypeMemberVisibility visibility)
        {
            switch (visibility)
            {
                case TypeMemberVisibility.Public:
                    WriteKeyword("public"); break;
                case TypeMemberVisibility.Private:
                    WriteKeyword("private"); break;
                case TypeMemberVisibility.Assembly:
                    WriteKeyword("internal"); break;
                case TypeMemberVisibility.Family:
                    WriteKeyword("protected"); break;
                case TypeMemberVisibility.FamilyOrAssembly:
                    WriteKeyword("protected"); WriteKeyword("internal"); break;
                case TypeMemberVisibility.FamilyAndAssembly:
                    WriteKeyword("internal"); WriteKeyword("protected"); break; // Is this right?
                default:
                    WriteKeyword("<Unknown-Visibility>"); break;
            }
        }

        private void WriteCustomModifiers(IEnumerable<ICustomModifier> modifiers)
        {
            foreach (ICustomModifier modifier in modifiers)
            {
                if (modifier.Modifier.FullName() == "System.Runtime.CompilerServices.IsVolatile")
                    WriteKeyword("volatile");
            }
        }

        // Writer Helpers these are the only methods that should directly acess _writer
        private void WriteKeyword(string keyword, bool noSpace = false)
        {
            _writer.WriteKeyword(keyword);
            if (!noSpace) WriteSpace();
        }

        private void WriteSymbol(string symbol, bool addSpace = false)
        {
            _writer.WriteSymbol(symbol);
            if (addSpace)
                WriteSpace();
        }

        private void Write(string literal)
        {
            _writer.Write(literal);
        }

        private void WriteTypeName(ITypeReference type, bool noSpace = false, bool useTypeKeywords = true, IEnumerable<ICustomAttribute> attributes = null, int typeIndex = 0, bool omitTypeArguments = false, bool omitGenericTypeList = false) {
            ICustomAttribute dynamicAttribute;
            if (attributes != null && IsDynamic(attributes, out dynamicAttribute))
            {
                if (dynamicAttribute.Arguments.Count() == 0)
                {
                    WriteKeyword("dynamic", noSpace: noSpace);
                    return;
                }

                var array = (IMetadataCreateArray)dynamicAttribute.Arguments.ElementAt(0);
                var hasDynamicEntry = ((bool)((IMetadataConstant)array.Initializers.ElementAt(typeIndex)).Value == true);

                if (hasDynamicEntry)
                {
                    WriteKeyword("dynamic", noSpace: noSpace);
                    return;
                }

                if (type is IGenericTypeInstanceReference)
                {
                    WriteTypeName(type, noSpace: true, omitTypeArguments: true);
                    typeIndex++;
                    _writer.WriteSymbol("<");

                    var argumentCount = ((IGenericTypeInstanceReference)type).GenericArguments.Count();
                    foreach (var t in ((IGenericTypeInstanceReference)type).GenericArguments)
                    {
                        WriteTypeName(t, noSpace: true, useTypeKeywords: useTypeKeywords, attributes: attributes, typeIndex: typeIndex);
                        argumentCount--;
                        typeIndex++;
                        if (argumentCount > 0)
                            _writer.WriteSymbol(",");
                    }
                    _writer.WriteSymbol(">");
                    if (!noSpace) WriteSpace();
                    return;
                }
            }

            NameFormattingOptions namingOptions = omitTypeArguments ? NameFormattingOptions.OmitTypeArguments : NameFormattingOptions.TypeParameters;

            if (useTypeKeywords)
                namingOptions |= NameFormattingOptions.UseTypeKeywords;

            if (_forCompilationIncludeGlobalprefix)
                namingOptions |= NameFormattingOptions.UseGlobalPrefix;

            if (!_forCompilation)
                namingOptions |= NameFormattingOptions.OmitContainingNamespace;

            if (omitGenericTypeList)
                namingOptions |= NameFormattingOptions.EmptyTypeParameterList;

            string name = TypeHelper.GetTypeName(type, namingOptions);

            if (CSharpCciExtensions.IsKeyword(name))
                _writer.WriteKeyword(name);
            else
                _writer.WriteTypeName(name);

            if (!noSpace) WriteSpace();
        }

        public void WriteIdentifier(string id)
        {
            WriteIdentifier(id, true);
        }

        public void WriteIdentifier(string id, bool escape)
        {
            // Escape keywords
            if (escape && CSharpCciExtensions.IsKeyword(id))
                id = "@" + id;
            _writer.WriteIdentifier(id);
        }

        private void WriteIdentifier(IName name)
        {
            WriteIdentifier(name.Value);
        }

        private void WriteSpace()
        {
            _writer.Write(" ");
        }

        private void WriteList<T>(IEnumerable<T> list, Action<T> writeItem)
        {
            _writer.WriteList(list, writeItem);
        }
    }
}
