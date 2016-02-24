using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace AssemblyPlaceholder
{
    class Program
    {
        static string CurrentModuleName = "";
        const string PlaceholderExceptionMessage = "This is a dummy assembly for compilation purposes only.";

        // NOTSUPPORTED: Initial field values, these are set by the constructor and would require shenanigans to get right
        // NOTSUPPORTED: Events are implemented, but not sure they work

        // TODO: Make sure custom attributes are working (of ALL kinds, including ASSEMBLY ones)

        static MethodReference CloneMethodReference(MethodReference realMethodReference, ModuleDefinition intoModule)
        {
            var clonedType = CloneTypeReference(realMethodReference.DeclaringType, intoModule, null);

            var clonedMethod = new MethodReference(realMethodReference.Name, CloneTypeReference(realMethodReference.ReturnType, intoModule, null), CloneTypeReference(realMethodReference.DeclaringType, intoModule, null))
            {
               CallingConvention = realMethodReference.CallingConvention,
               ExplicitThis = realMethodReference.ExplicitThis,
               HasThis = realMethodReference.HasThis,
            };

            foreach (var realGenericParamter in realMethodReference.GenericParameters)
            {
                CloneGenericParameter(realGenericParamter, clonedMethod, intoModule, clonedMethod.GenericParameters);
            }

            foreach (var realParameter in realMethodReference.Parameters)
            {
                IGenericParameterProvider genericParameterProvider;

                // FIXME: This seems SUPER HACKY, what if the generic paramters come from BOTH?!
                if (clonedMethod.HasGenericParameters)
                {
                    genericParameterProvider = clonedMethod;
                }
                else
                {
                    genericParameterProvider = clonedType;
                }

                var clonedParameter = new ParameterDefinition(realParameter.Name, realParameter.Attributes, CloneTypeReference(realParameter.ParameterType, intoModule, genericParameterProvider))
                {
                    Constant = realParameter.Constant,
                    HasConstant = realParameter.HasConstant, // NOTE: This HAS to be set AFTER Constant
                    //MetadataToken = realParameter.MetadataToken
                };

                clonedMethod.Parameters.Add(clonedParameter);
            }

            return clonedMethod;
        }

        static CustomAttributeArgument CloneCustomAttributeArgument(CustomAttributeArgument realCustomAttributeArgument, ModuleDefinition intoModule)
        {
            var clonedCustomAttributeArgument = new CustomAttributeArgument(CloneTypeReference(realCustomAttributeArgument.Type, intoModule, null), realCustomAttributeArgument.Value);

            return clonedCustomAttributeArgument;
        }

        static CustomAttributeNamedArgument CloneCustomAttributeNamedArgument(CustomAttributeNamedArgument realCustomAttributeNamedArgument, ModuleDefinition intoModule)
        {
            var clonedCustomAttributeNamedArgument = new CustomAttributeNamedArgument(realCustomAttributeNamedArgument.Name, CloneCustomAttributeArgument(realCustomAttributeNamedArgument.Argument, intoModule));

            return clonedCustomAttributeNamedArgument;
        }

        static CustomAttribute CloneCustomAttribute(CustomAttribute realCustomAttribute, ModuleDefinition intoModule)
        {
            var clonedCustomAttribute = new CustomAttribute(CloneMethodReference(realCustomAttribute.Constructor, intoModule), realCustomAttribute.GetBlob());
            
            foreach (var realConstructorArgument in realCustomAttribute.ConstructorArguments)
            {
                var clonedConstructorArgument = CloneCustomAttributeArgument(realConstructorArgument, intoModule);

                clonedCustomAttribute.ConstructorArguments.Add(clonedConstructorArgument);
            }
            
            foreach (var realField in realCustomAttribute.Fields)
            {
                var clonedField = CloneCustomAttributeNamedArgument(realField, intoModule);

                clonedCustomAttribute.Fields.Add(clonedField);
            }

            foreach (var realProperty in realCustomAttribute.Properties)
            {
                var clonedProperty = CloneCustomAttributeNamedArgument(realProperty, intoModule);

                clonedCustomAttribute.Properties.Add(clonedProperty);
            }

            return clonedCustomAttribute;
        }

        static void CloneCustomAttributes(ICustomAttributeProvider realProvider, ICustomAttributeProvider clonedProvider, ModuleDefinition intoModule)
        {
            foreach (var realCustomAttribute in realProvider.CustomAttributes)
            {
                var clonedCustomAttribute = CloneCustomAttribute(realCustomAttribute, intoModule);

                clonedProvider.CustomAttributes.Add(clonedCustomAttribute);
            }
        }

        static void CloneAssembly(FileInfo filePath)
        {
            var realAssembly = AssemblyDefinition.ReadAssembly(filePath.FullName);
            var clonedAssembly = AssemblyDefinition.CreateAssembly(realAssembly.Name, realAssembly.MainModule.Name, realAssembly.MainModule.Kind);

            clonedAssembly.MainModule.Mvid = realAssembly.MainModule.Mvid;
            clonedAssembly.MainModule.Characteristics = realAssembly.MainModule.Characteristics;

            CurrentModuleName = realAssembly.MainModule.Name;

            CloneCustomAttributes(realAssembly, clonedAssembly, clonedAssembly.MainModule);

            foreach (var realModuleReference in realAssembly.MainModule.ModuleReferences)
            {
                var clonedModuleReference = new ModuleReference(realModuleReference.Name);

                clonedAssembly.MainModule.ModuleReferences.Add(clonedModuleReference);
            }

            clonedAssembly.MainModule.AssemblyReferences.Clear();

            foreach (var assemblyReference in realAssembly.MainModule.AssemblyReferences)
            {
                clonedAssembly.MainModule.AssemblyReferences.Add(assemblyReference);
            }

            foreach (var type in realAssembly.MainModule.Types.Where(type => type.IsPublic))
            {
                CloneType(type, clonedAssembly.MainModule, clonedAssembly.MainModule.Types);
            }


            AssemblyNameReference assemblyNameReference = null;

            // FIXME: These shenanigans are very specific to my use case
            foreach (var assemblyReference in clonedAssembly.MainModule.AssemblyReferences)
            {
                if (assemblyReference.Name == "mscorlib" && assemblyReference.Version == new Version(2, 0, 0, 0))
                {
                    assemblyNameReference = assemblyReference;
                }
            }

            var dummyAssemblyFile = Path.Combine(Path.Combine(filePath.Directory.FullName, "Dummies"), filePath.Name);

            clonedAssembly.Write(dummyAssemblyFile, new WriterParameters { WriteSymbols = false });
        }

        static void Main(string[] args)
        {
            var directory = new DirectoryInfo(args[0]);

            var dummyDirectory = Path.Combine(directory.FullName, "Dummies");

            if (!Directory.Exists(dummyDirectory))
            {
                Directory.CreateDirectory(dummyDirectory);
            }

            foreach (var file in directory.GetFiles())
            {
                if (file.Name.EndsWith(".dll"))
                {
                    CloneAssembly(file);
                }
            }

            Console.WriteLine("DONE!");
        }

        static void CloneType(TypeDefinition realType, ModuleDefinition intoModule, Collection<TypeDefinition> addTo)
        {
            var clonedType = new TypeDefinition(realType.Namespace, realType.Name, realType.Attributes, null)
            {
                //MetadataToken = realType.MetadataToken
                ClassSize = realType.ClassSize,
                PackingSize = realType.PackingSize
            };

            addTo.Add(clonedType);

            foreach (var realGenericParameter in realType.GenericParameters)
            {
                CloneGenericParameter(realGenericParameter, clonedType, intoModule, clonedType.GenericParameters);
            }

            clonedType.BaseType = CloneTypeReference(realType.BaseType, intoModule, clonedType);

            CloneCustomAttributes(realType, clonedType, intoModule);

            foreach (var realInterface in realType.Interfaces)
            {
                var clonedInterface = CloneTypeReference(realInterface, intoModule, clonedType);

                clonedType.Interfaces.Add(clonedInterface);
            }

            foreach (var realField in realType.Fields.Where(i => i.IsPublic))
            {
                var clonedField = new FieldDefinition(realField.Name, realField.Attributes, CloneTypeReference(realField.FieldType, intoModule, clonedType))
                {
                    InitialValue = realField.InitialValue,
                    Constant = realField.Constant,
                    HasConstant = realField.HasConstant,
                    //MetadataToken = realField.MetadataToken,
                };

                clonedType.Fields.Add(clonedField);

                CloneCustomAttributes(realField, clonedField, intoModule);
            }

            var eventAndPropertyMethods = new HashSet<string>();

            // TODO: Theoretically this should work, but it's untested
            foreach (var realEvent in realType.Events.Where(i => (i.AddMethod != null && i.AddMethod.IsPublic) || (i.RemoveMethod != null && i.RemoveMethod.IsPublic)))
            {
                var clonedEvent = new EventDefinition(realEvent.Name, realEvent.Attributes, CloneTypeReference(realEvent.EventType, intoModule, clonedType));

                if (realEvent.AddMethod != null && realEvent.AddMethod.IsPublic)
                {
                    var clonedAddMethod = CloneMethod(realEvent.AddMethod, intoModule, clonedType, clonedType.Methods);

                    clonedEvent.AddMethod = clonedAddMethod;

                    eventAndPropertyMethods.Add(clonedAddMethod.FullName);
                }

                if (realEvent.RemoveMethod != null && realEvent.RemoveMethod.IsPublic)
                {
                    var clonedRemoveMethod = CloneMethod(realEvent.RemoveMethod, intoModule, clonedType, clonedType.Methods);

                    clonedEvent.RemoveMethod = clonedRemoveMethod;

                    eventAndPropertyMethods.Add(clonedRemoveMethod.FullName);
                }

                clonedType.Events.Add(clonedEvent);

                CloneCustomAttributes(realEvent, clonedEvent, intoModule);
            }

            foreach (var realProperty in realType.Properties.Where(i => (i.GetMethod != null && i.GetMethod.IsPublic) || (i.SetMethod != null && i.SetMethod.IsPublic)))
            {
                var clonedProperty = new PropertyDefinition(realProperty.Name, realProperty.Attributes, CloneTypeReference(realProperty.PropertyType, intoModule, clonedType))
                {
                    HasThis = realProperty.HasThis
                };

                if (realProperty.GetMethod != null && realProperty.GetMethod.IsPublic)
                {
                    var clonedGetMethod = CloneMethod(realProperty.GetMethod, intoModule, clonedType, clonedType.Methods);

                    clonedProperty.GetMethod = clonedGetMethod;

                    eventAndPropertyMethods.Add(clonedGetMethod.FullName);
                }

                if (realProperty.SetMethod != null && realProperty.SetMethod.IsPublic)
                {
                    var clonedSetMethod = CloneMethod(realProperty.SetMethod, intoModule, clonedType, clonedType.Methods);

                    clonedProperty.SetMethod = clonedSetMethod;

                    eventAndPropertyMethods.Add(clonedSetMethod.FullName);
                }

                clonedType.Properties.Add(clonedProperty);

                CloneCustomAttributes(realProperty, clonedProperty, intoModule);
            }

            foreach (var realMethod in realType.Methods.Where(i => i.IsPublic && !eventAndPropertyMethods.Contains(i.FullName)))
            {
                CloneMethod(realMethod, intoModule, clonedType, clonedType.Methods);
            }

            foreach (var nestedType in realType.NestedTypes.Where(i => i.IsNestedPublic))
            {
                CloneType(nestedType, intoModule, clonedType.NestedTypes);
            }
        }

        static MethodDefinition CloneMethod(MethodDefinition realMethod, ModuleDefinition intoModule, TypeDefinition clonedType, Collection<MethodDefinition> addTo)
        {
            // FIXME: CHICKEN AND EGG
            // In order to determine the return type we need the generic context
            // Which is the method
            // But in order to get the method we need the return type

            var clonedMethod = new MethodDefinition(realMethod.Name, realMethod.Attributes, realMethod.ReturnType) // NOTE: Using the realMethod.ReturnType is just a work around for the chicken and egg problem
            {
                IsRuntime = realMethod.IsRuntime,
                //MetadataToken = realMethod.MetadataToken,
                SemanticsAttributes = realMethod.SemanticsAttributes,
                CallingConvention = realMethod.CallingConvention,
                IsInternalCall = realMethod.IsInternalCall,
                ImplAttributes = realMethod.ImplAttributes
            };
            addTo.Add(clonedMethod);

            foreach (var realGenericParamter in realMethod.GenericParameters)
            {
                CloneGenericParameter(realGenericParamter, clonedMethod, intoModule, clonedMethod.GenericParameters);
            }

            IGenericParameterProvider genericParameterProvider1;

            // FIXME: This seems SUPER HACKY, what if the generic paramters come from BOTH?!
            if (clonedMethod.HasGenericParameters)
            {
                genericParameterProvider1 = clonedMethod;
            }
            else
            {
                genericParameterProvider1 = clonedType;
            }

            clonedMethod.ReturnType = CloneTypeReference(realMethod.ReturnType, intoModule, genericParameterProvider1);

            CloneCustomAttributes(realMethod, clonedMethod, intoModule);

            foreach (var realParameter in realMethod.Parameters)
            {
                IGenericParameterProvider genericParameterProvider;

                // FIXME: This seems SUPER HACKY, what if the generic paramters come from BOTH?!
                if (clonedMethod.HasGenericParameters)
                {
                    genericParameterProvider = clonedMethod;
                }
                else
                {
                    genericParameterProvider = clonedType;
                }

                var clonedParameter = new ParameterDefinition(realParameter.Name, realParameter.Attributes, CloneTypeReference(realParameter.ParameterType, intoModule, genericParameterProvider))
                {
                    Constant = realParameter.Constant,
                    HasConstant = realParameter.HasConstant, // NOTE: This HAS to be set AFTER Constant
                    //MetadataToken = realParameter.MetadataToken
                };

                clonedMethod.Parameters.Add(clonedParameter);

                CloneCustomAttributes(realParameter, clonedParameter, intoModule);
            }

            if (realMethod.HasBody)
            { // NOTE: Interface methods don't have bodies.. may have to deal with other stuff on interfaces
                StripMethod(intoModule, clonedMethod);
            }

            return clonedMethod;
        }

        static TypeReference CloneTypeReference(TypeReference realTypeReference, ModuleDefinition intoModule, IGenericParameterProvider genericContext = null)
        {
            if (realTypeReference == null)
            {
                return null;
            }

            if (TypeReferenceCache.ContainsKey(realTypeReference))
            {
                return TypeReferenceCache[realTypeReference];
            }

            TypeReference retValue;

            if (realTypeReference.Scope.Name == CurrentModuleName)
            {
                if (realTypeReference is ArrayType)
                {
                    var realArrayType = realTypeReference as ArrayType;

                    var placeholderArrayType = new ArrayType(CloneTypeReference(realArrayType.ElementType, intoModule, genericContext), realArrayType.Rank);

                    retValue = placeholderArrayType;
                }
                else if (realTypeReference is GenericInstanceType)
                {
                    var realGenericInstanceType = realTypeReference as GenericInstanceType;

                    var realElementTypeReference = realGenericInstanceType.ElementType;
                    var clonedElementTypeReference = CloneTypeReference(realElementTypeReference, intoModule, genericContext);

                    var placeholderGenericInstanceType = new GenericInstanceType(clonedElementTypeReference);

                    foreach (var realGenericArgument in realGenericInstanceType.GenericArguments)
                    {
                        placeholderGenericInstanceType.GenericArguments.Add(CloneTypeReference(realGenericArgument, intoModule, genericContext));
                    }

                    retValue = placeholderGenericInstanceType;
                }
                else if (realTypeReference is GenericParameter)
                {
                    var realGenericParameter = realTypeReference as GenericParameter;

                    retValue = CloneGenericParameter(realGenericParameter, genericContext, intoModule, null);
                }
                else if (realTypeReference is ByReferenceType)
                {
                    var realByReferenceType = realTypeReference as ByReferenceType;

                    var clonedByReferenceType = new ByReferenceType(CloneTypeReference(realByReferenceType.ElementType, intoModule, genericContext));

                    retValue = clonedByReferenceType;
                }
                else
                {
                    var placeholderTypeReference = new TypeReference(realTypeReference.Namespace, realTypeReference.Name, intoModule, intoModule, realTypeReference.IsValueType)
                    {
                        DeclaringType = CloneTypeReference(realTypeReference.DeclaringType, intoModule, genericContext),
                        //MetadataToken = realTypeReference.MetadataToken,
                    };

                    foreach (var realGenericParameter in realTypeReference.GenericParameters)
                    {
                        CloneGenericParameter(realGenericParameter, genericContext, intoModule, placeholderTypeReference.GenericParameters);
                    }

                    retValue = placeholderTypeReference;
                }
            }
            else
            {
                // https://groups.google.com/forum/#!searchin/mono-cecil/Import$20Generic/mono-cecil/9kd-2nsO9MU/92tjFMtlBusJ
                // "Nope, that's definitely a bug, we should never throw a NRE."

                if (genericContext == null)
                {
                    retValue = intoModule.Import(realTypeReference);
                }
                else
                {
                    retValue = intoModule.Import(realTypeReference, genericContext);
                }
            }

            TypeReferenceCache[realTypeReference] = retValue;

            return retValue;
        }

        static void StripType(ModuleDefinition module, TypeDefinition type)
        {
            var nestedTypesToRemove = new List<TypeDefinition>();
            var methodsToRemove = new List<MethodDefinition>();
            var fieldsToRemove = new List<FieldDefinition>();

            foreach (var nestedType in type.NestedTypes)
            {
                if (nestedType.IsPublic)
                {
                    StripType(module, nestedType);
                }
                else
                {
                    nestedTypesToRemove.Add(nestedType);
                }
            }

            foreach (var method in type.Methods)
            {
                if (method.IsPublic)
                {
                    StripMethod(module, method);
                }
                else
                {
                    methodsToRemove.Add(method);
                }
            }

            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var field in type.Fields)
            {
                if (!field.IsPublic)
                {
                    fieldsToRemove.Add(field);
                }
            }

            foreach (var nestedType in nestedTypesToRemove)
            {
                type.NestedTypes.Remove(nestedType);
            }

            foreach (var method in methodsToRemove)
            {
                type.Methods.Remove(method);
            }

            foreach (var field in fieldsToRemove)
            {
                type.Fields.Remove(field);
            }
        }

        static void StripMethod(ModuleDefinition module, MethodDefinition method)
        {
            var exceptionType = typeof(NotImplementedException);
            var exceptionCtor = exceptionType.GetConstructor(new [] { typeof(String) });
            var constructorReference = module.Import(exceptionCtor);

            method.Body = new MethodBody(method);
            var ilProcessor = method.Body.GetILProcessor();
            ilProcessor.Append(ilProcessor.Create(OpCodes.Ldstr, PlaceholderExceptionMessage));
            ilProcessor.Append(ilProcessor.Create(OpCodes.Newobj, constructorReference));
            ilProcessor.Append(ilProcessor.Create(OpCodes.Throw));
        }

        static GenericParameter CloneGenericParameter(GenericParameter realGenericParameter, IGenericParameterProvider owner, ModuleDefinition intoModule, Collection<GenericParameter> addTo)
        {
            GenericParameter clonedGenericParameter;

            if (realGenericParameter.Position >= 0)
            {
                clonedGenericParameter = new GenericParameter(realGenericParameter.Position, realGenericParameter.Type, intoModule);
            }
            else
            {
                clonedGenericParameter = new GenericParameter(realGenericParameter.Name, owner);
            }

            clonedGenericParameter.Attributes = realGenericParameter.Attributes;
            //clonedGenericParameter.MetadataToken = realGenericParameter.MetadataToken;
            clonedGenericParameter.Name = realGenericParameter.Name;

            if (addTo != null)
            {
                addTo.Add(clonedGenericParameter);
            }

            CloneCustomAttributes(realGenericParameter, clonedGenericParameter, intoModule);

            return clonedGenericParameter;
        }

        static readonly Dictionary<TypeReference, TypeReference> TypeReferenceCache = new Dictionary<TypeReference, TypeReference>();
    }
}
