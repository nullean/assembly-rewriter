using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Schema;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace AssemblyRewriter
{
    public class AssemblyRewriter
    {
        private readonly bool _verbose;
        private Dictionary<string, string> _renames = new Dictionary<string, string>();

        public AssemblyRewriter(bool verbose) => _verbose = verbose;

        public void RewriteNamespaces(
            IEnumerable<string> inputPaths,
            IEnumerable<string> outputPaths,
            IEnumerable<string> additionalResolveDirectories
            )
        {
            var assemblies = inputPaths.Zip(outputPaths,
                (inputPath, outputPath) => new AssemblyToRewrite(inputPath, outputPath)).ToList();

            _renames = assemblies.ToDictionary(k => k.InputName, v => v.OutputName);

            var resolveDirs = assemblies.Select(a => a.InputDirectory)
                .Concat(assemblies.Select(a => a.OutputDirectory))
                .Concat(additionalResolveDirectories)
                .Distinct();

            var resolver = new AssemblyResolver(resolveDirs);
            var readerParameters = new ReaderParameters { AssemblyResolver = resolver, ReadWrite = true };

            foreach (var assembly in assemblies)
            {
                RewriteAssembly(assembly, assemblies, readerParameters);
            }
        }

        private string RenameTypeName(string typeName, Func<string, string,string, string> replace = null)
        {
            replace = replace ?? ((t, o, n) => t.Replace(o, n));
            foreach (var rename in _renames)
            {
                //safeguard e.g Nest7 to be renamed to Nest77
                if (typeName.StartsWith(rename.Value)) continue;
                var n = replace(typeName, rename.Key, rename.Value);
                if (typeName != n) return n;
            }
            return typeName;
        }

        private bool IsRewritableType(string typeName) =>
            _renames.Keys.Any(r => r.StartsWith($"{typeName}.") || r.StartsWith($"<{typeName}."));

        private bool IsRewritableType(Func<string, string, bool> act) =>
            _renames.Any(kv => act(kv.Key, kv.Value));

        private void RewriteAttributes(string assembly, IEnumerable<CustomAttribute> attributes)
        {
            foreach (var attribute in attributes)
            {
                RewriteTypeReference(assembly, attribute.AttributeType);
                RewriteMemberReference(assembly, attribute.Constructor);

                if (attribute.HasConstructorArguments)
                {
                    foreach (var constructorArgument in attribute.ConstructorArguments)
                    {
                        var genericInstanceType =constructorArgument.Value as GenericInstanceType;
                        var valueTypeReference = constructorArgument.Value as TypeReference;
                        var valueTypeDefinition = constructorArgument.Value as TypeDefinition;
                        RewriteTypeReference(assembly, constructorArgument.Type);
                        if (valueTypeReference != null) RewriteTypeReference(assembly, valueTypeReference);
                        if (genericInstanceType != null) RewriteTypeReference(assembly, genericInstanceType);
                        if (valueTypeDefinition == null)
                            RewriteTypeReference(assembly, valueTypeDefinition);

                        if (constructorArgument.Type.Name == nameof(Type))
                        {
                            // intentional no-op, but required for Cecil
                            // to update the ctor arguments
                        }
                    }
                }

                if (attribute.HasProperties)
                {
                    foreach (var property in attribute.Properties)
                        RewriteTypeReference(assembly, property.Argument.Type);
                }

                if (attribute.HasFields)
                {
                    foreach (var field in attribute.Fields)
                        RewriteTypeReference(assembly, field.Argument.Type);
                }
            }
        }

        private void RewriteMemberReference(string assembly, MemberReference memberReference)
        {
            if (!IsRewritableType(memberReference.Name)) return;

            var name = RenameTypeName(memberReference.Name, (t, o, n) => t.Replace($"{o}.", $"{n}."));
            Write(assembly, memberReference.GetType().Name, $"{memberReference.Name} to {name}");
            memberReference.Name = name;
        }

        private void RewriteGenericParameter(string assembly, GenericParameter genericParameter)
        {
            foreach (var genericParameterConstraint in genericParameter.Constraints)
            {
                if (!IsRewritableType(genericParameterConstraint.Name)) continue;

                var name = RenameTypeName(genericParameterConstraint.Name);
                Write(assembly, nameof(GenericParameter), $"{genericParameter.Name} to {name}");
                genericParameterConstraint.Name = name;
            }

            foreach (var nestedGenericParameter in genericParameter.GenericParameters)
                RewriteGenericParameter(assembly, nestedGenericParameter);
        }

        private void RewriteAssembly(AssemblyToRewrite assemblyToRewrite, List<AssemblyToRewrite> assembliesToRewrite, ReaderParameters readerParameters)
        {
            if (assemblyToRewrite.Rewritten) return;

            string tempOutputPath = null;
            string currentName;
            using (var assembly = AssemblyDefinition.ReadAssembly(assemblyToRewrite.InputPath, readerParameters))
            {
                currentName = assembly.Name.Name;
                var newName = assemblyToRewrite.OutputName;

                Write(currentName, nameof(AssemblyDefinition), $"rewriting {currentName} from {assemblyToRewrite.InputPath}");

                foreach (var moduleDefinition in assembly.Modules)
                {
                    foreach (var assemblyReference in moduleDefinition.AssemblyReferences)
                    {
                        Write(currentName, nameof(AssemblyDefinition), $"{assembly.Name} references {assemblyReference.Name}");

                        var assemblyReferenceToRewrite = assembliesToRewrite.FirstOrDefault(a => a.InputName == assemblyReference.Name);

                        if (assemblyReferenceToRewrite != null)
                        {
                            if (!assemblyReferenceToRewrite.Rewritten)
                            {
                                Write(currentName, nameof(AssemblyNameReference), $"{assemblyReference.Name} will be rewritten first");
                                RewriteAssembly(assemblyReferenceToRewrite, assembliesToRewrite, readerParameters);
                            }
                            else
                            {
                                Write(currentName, nameof(AssemblyNameReference), $"{assemblyReference.Name} already rewritten");
                            }

                            foreach (var innerModuleDefinition in assembly.Modules)
                            {
                                RewriteTypeReferences(currentName, innerModuleDefinition.GetTypeReferences());
                                RewriteTypes(currentName, innerModuleDefinition.Types);
                            }

                            assemblyReference.Name = assemblyReferenceToRewrite.OutputName;
                        }
                    }

                    RewriteTypes(currentName, moduleDefinition.Types);
                    moduleDefinition.Name = RenameTypeName(moduleDefinition.Name);
                }

                RewriteAssemblyTitleAttribute(assembly, currentName, newName);
                assembly.Name.Name = newName;
                if (assemblyToRewrite.OutputPath == assemblyToRewrite.InputPath)
                {
                    tempOutputPath = assemblyToRewrite.OutputPath + ".temp";
                    assembly.Write(tempOutputPath);
                    assemblyToRewrite.Rewritten = true;
                    Write(currentName, nameof(AssemblyDefinition), $"finished rewriting {currentName} into {tempOutputPath}");
                }
                else
                {
                    assembly.Write(assemblyToRewrite.OutputPath);
                    assemblyToRewrite.Rewritten = true;
                    Write(currentName, nameof(AssemblyDefinition), $"finished rewriting {currentName} into {assemblyToRewrite.OutputPath}");
                }
            }
            if (!string.IsNullOrWhiteSpace(tempOutputPath))
            {
                System.IO.File.Delete(assemblyToRewrite.OutputPath);
                System.IO.File.Move(tempOutputPath, assemblyToRewrite.OutputPath);
                Write(currentName, nameof(AssemblyDefinition), $"Rename {tempOutputPath} back to {assemblyToRewrite.OutputPath}");
            }

        }

        private void RewriteTypeReferences(string assembly, IEnumerable<TypeReference> typeReferences)
        {
            foreach (var typeReference in typeReferences)
                RewriteTypeReference(assembly, typeReference);
        }

        private void RewriteTypeReference(string assembly, TypeReference typeReference)
        {
            //var oReference = typeReference;
//            var doNotRewrite = IsRewritableType((o, n) =>
//                (!oReference.Namespace.StartsWith(o) || oReference.Namespace.StartsWith(n)) &&
//                (oReference.Namespace != string.Empty || !oReference.Name.StartsWith($"<{o}-"))
//            );
//
//            if (doNotRewrite) return;
            if (typeReference == null) return;

            if (typeReference is TypeSpecification) typeReference = typeReference.GetElementType();

            if (typeReference == null) return;

            if (typeReference.Namespace != string.Empty)
            {
                var name = RenameTypeName(typeReference.Namespace);
                var newFullName = RenameTypeName(typeReference.FullName, (t, o, n) => t.Replace(o + ".", n + "."));
                Write(assembly, nameof(TypeReference), $"{typeReference.FullName} to {newFullName}");
                typeReference.Namespace = name;
            }

            if (IsRewritableType((o,n)=> typeReference.Name.StartsWith($"<{o}-")))
            {
                var name = RenameTypeName(typeReference.Name, (t, o, n)=>t.Replace($"<{o}-", $"<{n}-"));
                var newFullName = RenameTypeName(typeReference.FullName, (t, o, n) => t.Replace($"<{o}-",$"<{n}-"));
                Write(assembly, nameof(TypeReference), $"{typeReference.FullName} to {newFullName}");
                typeReference.Name = name;
            }

            if (typeReference.DeclaringType != null)
                RewriteTypeReference(assembly, typeReference.DeclaringType);
        }

        private void RewriteAssemblyTitleAttribute(AssemblyDefinition assembly, string currentName, string newName)
        {
            foreach (var attribute in assembly.CustomAttributes)
            {
                if (attribute.AttributeType.Name != nameof(AssemblyTitleAttribute)) continue;

                var currentAssemblyName = (string)attribute.ConstructorArguments[0].Value;
                var newAssemblyName = Regex.Replace(currentAssemblyName, Regex.Escape(currentName), newName);

                // give the assembly a new title, even when the top level namespace is not part of it
                if (newAssemblyName == currentAssemblyName)
                    newAssemblyName += $" ({newName})";

                Write(assembly.Name.Name, nameof(AssemblyTitleAttribute), $"{currentAssemblyName} to {newAssemblyName}");
                attribute.ConstructorArguments[0] =
                    new CustomAttributeArgument(assembly.MainModule.TypeSystem.String, newAssemblyName);
            }
        }

        private void RewriteTypes(string assembly, IEnumerable<TypeDefinition> typeDefinitions)
        {
            foreach (var typeDefinition in typeDefinitions)
            {
                if (typeDefinition.HasNestedTypes)
                    RewriteTypes(assembly, typeDefinition.NestedTypes);

                var needsRewrite = IsRewritableType((o, n) =>
                    typeDefinition.Namespace.StartsWith(o) && !typeDefinition.Namespace.StartsWith(n)
                );
                if (needsRewrite)
                {
                    var name = RenameTypeName(typeDefinition.Namespace, (t, o, n) => t.Replace(o, n));
                    Write(assembly, nameof(TypeDefinition), $"{typeDefinition.FullName} to {name}.{typeDefinition.Name}");
                    typeDefinition.Namespace = name;
                }

                RewriteAttributes(assembly, typeDefinition.CustomAttributes);

                foreach (var methodDefinition in typeDefinition.Methods)
                {
                    RewriteMethodDefinition(assembly, methodDefinition);
                }

                foreach (var propertyDefinition in typeDefinition.Properties)
                {
                    RewriteAttributes(assembly, propertyDefinition.CustomAttributes);
                    RewriteMemberReference(assembly, propertyDefinition);

                    if (propertyDefinition.GetMethod != null)
                        RewriteMethodDefinition(assembly, propertyDefinition.GetMethod);
                    if (propertyDefinition.SetMethod != null)
                        RewriteMethodDefinition(assembly, propertyDefinition.SetMethod);

                    if (IsRewritableType((o, n) => propertyDefinition.Name.Contains($"<{o}.")))
                    {
                        var name = RenameTypeName(propertyDefinition.Name, (t,o,n) => t.Replace($"<{o}.", $"<{n}."));
                        Write(assembly, nameof(PropertyDefinition), $"{propertyDefinition.Name} to {name}");
                        propertyDefinition.Name = name;
                    }
                }

                foreach (var fieldDefinition in typeDefinition.Fields)
                {
                    RewriteAttributes(assembly, fieldDefinition.CustomAttributes);
                    RewriteMemberReference(assembly, fieldDefinition);
                }

                foreach (var interfaceImplementation in typeDefinition.Interfaces)
                {
                    RewriteAttributes(assembly, interfaceImplementation.CustomAttributes);
                    RewriteMemberReference(assembly, interfaceImplementation.InterfaceType);
                }

                foreach (var eventDefinition in typeDefinition.Events)
                {
                    RewriteAttributes(assembly, eventDefinition.CustomAttributes);
                    RewriteMemberReference(assembly, eventDefinition.EventType);
                }

                foreach (var genericParameter in typeDefinition.GenericParameters)
                {
                    RewriteAttributes(assembly, genericParameter.CustomAttributes);
                    RewriteGenericParameter(assembly, genericParameter);
                }
            }
        }

        private void RewriteMethodDefinition(string assembly, MethodDefinition methodDefinition)
        {
            RewriteAttributes(assembly, methodDefinition.CustomAttributes);
            RewriteMemberReference(assembly, methodDefinition);

            foreach (var methodDefinitionOverride in methodDefinition.Overrides)
            {
                // explicit interface implementation of generic interface
                if (IsRewritableType((o, n) => methodDefinition.Name.Contains("<" + o)))
                {
                    var name = RenameTypeName(methodDefinition.Name, (t,o,n) => t.Replace($"<{o}", $"<{n}"));
                    Write(assembly, nameof(MethodDefinition), $"{methodDefinition.Name} to {name}");
                    methodDefinition.Name = name;
                }

                foreach (var genericParameter in methodDefinitionOverride.GenericParameters)
                {
                    RewriteAttributes(assembly, genericParameter.CustomAttributes);
                    RewriteGenericParameter(assembly, genericParameter);
                }

                RewriteMemberReference(assembly, methodDefinitionOverride);
            }

            foreach (var genericParameter in methodDefinition.GenericParameters)
            {
                RewriteAttributes(assembly, genericParameter.CustomAttributes);
                RewriteGenericParameter(assembly, genericParameter);
            }

            foreach (var parameterDefinition in methodDefinition.Parameters)
            {
                RewriteAttributes(assembly, parameterDefinition.CustomAttributes);
                RewriteTypeReference(assembly, parameterDefinition.ParameterType);
            }

            RewriteTypeReference(assembly, methodDefinition.ReturnType);
            RewriteMethodBody(assembly, methodDefinition);
        }

        private void RewriteMethodBody(string assembly, MethodDefinition methodDefinition)
        {
            if (!methodDefinition.HasBody) return;

            for (var index = 0; index < methodDefinition.Body.Instructions.Count; index++)
            {
                var instruction = methodDefinition.Body.Instructions[index];

                // Strings that reference the namespace
                if (instruction.OpCode.Code == Code.Ldstr)
                {
                    var operandString = (string) instruction.Operand;
                    if (IsRewritableType((o, n) => operandString.StartsWith($"{o}.")))
                    {
                        var name = RenameTypeName(operandString, (t, o, n) => t.Replace($"{o}.", $"{n}."));
                        Write(assembly, nameof(Instruction), $"{instruction.OpCode.Code}. {name}");
                        instruction.Operand = operandString;
                    }
                }
                // Compiler generated backing fields
                else if (instruction.OpCode.Code == Code.Ldfld || instruction.OpCode.Code == Code.Stfld)
                {
                    var fieldReference = (FieldReference) instruction.Operand;
                    RewriteMemberReference(assembly, fieldReference);

                    // Some generated fields start with {namespace}
                    RewriteTypeReference(assembly, fieldReference.DeclaringType);
                }
                else if (instruction.OpCode.Code == Code.Call)
                {
                    var methodReference = (MethodReference) instruction.Operand;
                    RewriteMemberReference(assembly, methodReference);

                    if (methodReference.IsGenericInstance)
                    {
                        var genericInstance = (GenericInstanceMethod) methodReference;
                        RewriteTypeReferences(assembly, genericInstance.GenericArguments);
                    }
                }
            }
        }

        private void Write(string assembly, string operation, string message)
        {
            void Write()
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"[{DateTime.Now:yyyy-MM-ddTHH:mm:ss.ffzzz}][");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(assembly.PadRight(18));
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("][");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"{operation.PadRight(23)}");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("] ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"{message}");
                Console.ResetColor();
            }

            switch (operation)
            {
                 case nameof(AssemblyDefinition):
                 case nameof(AssemblyNameReference):
                 case nameof(AssemblyTitleAttribute):
                 case nameof(RewriteNamespaces):
                    Write();
                    break;
                 default:
                    if (_verbose)
                        Write();
                    break;
            }
        }
    }
}
