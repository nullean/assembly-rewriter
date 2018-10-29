using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace AssemblyRewriter
{
    public class AssemblyRewriter
    {
        private readonly bool _verbose;

        public AssemblyRewriter(bool verbose) => _verbose = verbose;

        public void RewriteNamespaces(IEnumerable<string> inputPaths, IEnumerable<string> outputPaths)
        {
            var assemblies = inputPaths.Zip(outputPaths,
                (inputPath, outputPath) => new AssemblyToRewrite(inputPath, outputPath)).ToList();

            var resolver = new AssemblyResolver(assemblies.Select(a => a.InputDirectory));
            var readerParameters = new ReaderParameters { AssemblyResolver = resolver, ReadWrite = true };

            foreach (var assembly in assemblies)
            {
                RewriteAssembly(assembly, assemblies, readerParameters);
            }
        }

        private void RewriteAttributes(string assembly, IEnumerable<CustomAttribute> attributes,
            string currentName, string newName)
        {
            foreach (var attribute in attributes)
            {
                RewriteTypeReference(assembly, attribute.AttributeType, currentName, newName);
                RewriteMemberReference(assembly, attribute.Constructor, currentName, newName);

                if (attribute.HasConstructorArguments)
                {
                    foreach (var constructorArgument in attribute.ConstructorArguments)
                    {
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
                        RewriteTypeReference(assembly, property.Argument.Type, currentName, newName);
                }

                if (attribute.HasFields)
                {
                    foreach (var field in attribute.Fields)
                        RewriteTypeReference(assembly, field.Argument.Type, currentName, newName);
                }
            }
        }

        private void RewriteMemberReference(string assembly, MemberReference memberReference, string currentName, string newName)
        {
            if (memberReference.Name.StartsWith($"{currentName}.") ||
                memberReference.Name.StartsWith($"<{currentName}."))
            {
                var name = memberReference.Name.Replace($"{currentName}.", $"{newName}.");
                Write(assembly, memberReference.GetType().Name, $"{memberReference.Name} to {name}");
                memberReference.Name = name;
            }
        }

        private void RewriteGenericParameter(string assembly, GenericParameter genericParameter, string currentName, string newName)
        {
            foreach (var genericParameterConstraint in genericParameter.Constraints)
            {
                if (genericParameterConstraint.Name.StartsWith(currentName))
                {
                    var name = genericParameterConstraint.Name.Replace(currentName, newName);
                    Write(assembly, nameof(GenericParameter), $"{genericParameter.Name} to {name}");
                    genericParameterConstraint.Name = name;
                }
            }

            foreach (var nestedGenericParameter in genericParameter.GenericParameters)
                RewriteGenericParameter(assembly, nestedGenericParameter, currentName, newName);
        }

        private void RewriteAssembly(AssemblyToRewrite assemblyToRewrite, List<AssemblyToRewrite> assembliesToRewrite, ReaderParameters readerParameters)
        {
            if (assemblyToRewrite.Rewritten)
                return;

            string tempOutputPath = null;
            string currentName = null;
            using (var assembly = AssemblyDefinition.ReadAssembly(assemblyToRewrite.InputPath, readerParameters))
            {
                currentName = assembly.Name.Name;
                var newName = assemblyToRewrite.OutputName;

                Write(currentName, nameof(AssemblyDefinition), $"rewriting {currentName} from {assemblyToRewrite.InputPath}");

                foreach (var moduleDefinition in assembly.Modules)
                {
                    foreach (var assemblyReference in moduleDefinition.AssemblyReferences)
                    {
                        Write(currentName, nameof(AssemblyDefinition),
                            $"{assembly.Name} references {assemblyReference.Name}");

                        var assemblyReferenceToRewrite =
                            assembliesToRewrite.FirstOrDefault(a => a.InputName == assemblyReference.Name);

                        if (assemblyReferenceToRewrite != null)
                        {
                            if (!assemblyReferenceToRewrite.Rewritten)
                            {
                                Write(currentName, nameof(AssemblyNameReference),
                                    $"{assemblyReference.Name} will be rewritten first");
                                RewriteAssembly(assemblyReferenceToRewrite, assembliesToRewrite, readerParameters);
                            }
                            else
                            {
                                Write(currentName, nameof(AssemblyNameReference),
                                    $"{assemblyReference.Name} already rewritten");
                            }

                            foreach (var innerModuleDefinition in assembly.Modules)
                            {
                                RewriteTypeReferences(currentName, innerModuleDefinition.GetTypeReferences(),
                                    assemblyReferenceToRewrite.InputName,
                                    assemblyReferenceToRewrite.OutputName);

                                RewriteTypes(currentName, innerModuleDefinition.Types,
                                    assemblyReferenceToRewrite.InputName,
                                    assemblyReferenceToRewrite.OutputName);
                            }

                            assemblyReference.Name = assemblyReferenceToRewrite.OutputName;
                        }
                    }

                    RewriteTypes(currentName, moduleDefinition.Types, currentName, newName);
                    moduleDefinition.Name = moduleDefinition.Name.Replace(currentName, newName);
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

        private void RewriteTypeReferences(string assembly, IEnumerable<TypeReference> typeReferences,
            string currentName, string newName)
        {
            foreach (var typeReference in typeReferences)
                RewriteTypeReference(assembly, typeReference, currentName, newName);
        }

        private void RewriteTypeReference(string assembly, TypeReference typeReference, string currentName, string newName)
        {
            if ((typeReference.Namespace.StartsWith(currentName) && !typeReference.Namespace.StartsWith(newName)) ||
                (typeReference.Namespace == string.Empty && typeReference.Name.StartsWith($"<{currentName}-")))
            {
                if (typeReference is TypeSpecification)
                    typeReference = typeReference.GetElementType();

                if (typeReference.Namespace != string.Empty)
                {
                    var name = typeReference.Namespace.Replace(currentName, newName);
                    Write(assembly, nameof(TypeReference),
                        $"{typeReference.FullName} to {typeReference.FullName.Replace(currentName + ".", newName + ".")}");
                    typeReference.Namespace = name;
                }

                if (typeReference.Name.StartsWith($"<{currentName}-"))
                {
                    var name = typeReference.Name.Replace($"<{currentName}-", $"<{newName}-");
                    Write(assembly, nameof(TypeReference), $"{typeReference.FullName} to {typeReference.FullName.Replace($"<{currentName}-",$"<{newName}-")}");
                    typeReference.Name = name;
                }

                if (typeReference.DeclaringType != null)
                    RewriteTypeReference(assembly, typeReference.DeclaringType, currentName, newName);
            }
        }

        private void RewriteAssemblyTitleAttribute(AssemblyDefinition assembly, string currentName, string newName)
        {
            foreach (var attribute in assembly.CustomAttributes)
            {
                if (attribute.AttributeType.Name == nameof(AssemblyTitleAttribute))
                {
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
        }

        private void RewriteTypes(string assembly, IEnumerable<TypeDefinition> typeDefinitions, string currentName, string newName)
        {
            foreach (var typeDefinition in typeDefinitions)
            {
                if (typeDefinition.HasNestedTypes)
                    RewriteTypes(assembly, typeDefinition.NestedTypes, currentName, newName);

                if (typeDefinition.Namespace.StartsWith(currentName) &&
                    !typeDefinition.Namespace.StartsWith(newName))
                {
                    var name = typeDefinition.Namespace.Replace(currentName, newName);
                    Write(assembly, nameof(TypeDefinition), $"{typeDefinition.FullName} to {name}.{typeDefinition.Name}");
                    typeDefinition.Namespace = name;
                }

                RewriteAttributes(assembly, typeDefinition.CustomAttributes, currentName, newName);

                foreach (var methodDefinition in typeDefinition.Methods)
                {
                    RewriteMethodDefinition(assembly, methodDefinition, currentName, newName);
                }

                foreach (var propertyDefinition in typeDefinition.Properties)
                {
                    RewriteAttributes(assembly, propertyDefinition.CustomAttributes, currentName, newName);
                    RewriteMemberReference(assembly, propertyDefinition, currentName, newName);

                    if (propertyDefinition.Name.Contains($"<{currentName}."))
                    {
                        var name = propertyDefinition.Name.Replace($"<{currentName}.", $"<{newName}.");
                        Write(assembly, nameof(PropertyDefinition), $"{propertyDefinition.Name} to {name}");
                        propertyDefinition.Name = name;
                    }
                }

                foreach (var fieldDefinition in typeDefinition.Fields)
                {
                    RewriteAttributes(assembly, fieldDefinition.CustomAttributes, currentName, newName);
                    RewriteMemberReference(assembly, fieldDefinition, currentName, newName);
                }

                foreach (var interfaceImplementation in typeDefinition.Interfaces)
                {
                    RewriteAttributes(assembly, interfaceImplementation.CustomAttributes, currentName, newName);
                    RewriteMemberReference(assembly, interfaceImplementation.InterfaceType, currentName, newName);
                }

                foreach (var eventDefinition in typeDefinition.Events)
                {
                    RewriteAttributes(assembly, eventDefinition.CustomAttributes, currentName, newName);
                    RewriteMemberReference(assembly, eventDefinition.EventType, currentName, newName);
                }

                foreach (var genericParameter in typeDefinition.GenericParameters)
                {
                    RewriteAttributes(assembly, genericParameter.CustomAttributes, currentName, newName);
                    RewriteGenericParameter(assembly, genericParameter, currentName, newName);
                }
            }
        }

        private void RewriteMethodDefinition(string assembly,
            MethodDefinition methodDefinition, string currentName, string newName)
        {
            RewriteAttributes(assembly, methodDefinition.CustomAttributes, currentName, newName);
            RewriteMemberReference(assembly, methodDefinition, currentName, newName);

            foreach (var methodDefinitionOverride in methodDefinition.Overrides)
            {
                // explicit interface implementation of generic interface
                if (methodDefinition.Name.Contains("<" + currentName))
                {
                    var name = methodDefinition.Name.Replace($"<{currentName}", $"<{newName}");
                    Write(assembly, nameof(MethodDefinition), $"{methodDefinition.Name} to {name}");
                    methodDefinition.Name = name;
                }

                foreach (var genericParameter in methodDefinitionOverride.GenericParameters)
                {
                    RewriteAttributes(assembly, genericParameter.CustomAttributes, currentName, newName);
                    RewriteGenericParameter(assembly, genericParameter, currentName, newName);
                }

                RewriteMemberReference(assembly, methodDefinitionOverride, currentName, newName);
            }

            foreach (var genericParameter in methodDefinition.GenericParameters)
            {
                RewriteAttributes(assembly, genericParameter.CustomAttributes, currentName, newName);
                RewriteGenericParameter(assembly, genericParameter, currentName, newName);
            }

            foreach (var parameterDefinition in methodDefinition.Parameters)
            {
                RewriteAttributes(assembly, parameterDefinition.CustomAttributes, currentName, newName);
                RewriteTypeReference(assembly, parameterDefinition.ParameterType, currentName, newName);
            }

            RewriteTypeReference(assembly, methodDefinition.ReturnType, currentName, newName);
            RewriteMethodBody(assembly, methodDefinition, currentName, newName);
        }

        private void RewriteMethodBody(string assembly, MethodDefinition methodDefinition, string currentName, string newName)
        {
            if (methodDefinition.HasBody)
            {
                for (var index = 0; index < methodDefinition.Body.Instructions.Count; index++)
                {
                    var instruction = methodDefinition.Body.Instructions[index];

                    // Strings that reference the namespace
                    if (instruction.OpCode.Code == Code.Ldstr)
                    {
                        var operandString = (string) instruction.Operand;
                        if (operandString.StartsWith($"{currentName}."))
                        {
                            Write(assembly, nameof(Instruction), $"{instruction.OpCode.Code} {currentName}. to {newName}.");
                            instruction.Operand = operandString.Replace($"{currentName}.", $"{newName}.");
                        }
                    }
                    // Compiler generated backing fields
                    else if (instruction.OpCode.Code == Code.Ldfld || instruction.OpCode.Code == Code.Stfld)
                    {
                        var fieldReference = (FieldReference) instruction.Operand;
                        RewriteMemberReference(assembly, fieldReference, currentName, newName);

                        // Some generated fields start with {namespace}
                        RewriteTypeReference(assembly, fieldReference.DeclaringType, currentName, newName);
                    }
                    else if (instruction.OpCode.Code == Code.Call)
                    {
                        var methodReference = (MethodReference) instruction.Operand;
                        RewriteMemberReference(assembly, methodReference, currentName, newName);

                        if (methodReference.IsGenericInstance)
                        {
                            var genericInstance = (GenericInstanceMethod) methodReference;
                            RewriteTypeReferences(assembly, genericInstance.GenericArguments, currentName, newName);
                        }
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
