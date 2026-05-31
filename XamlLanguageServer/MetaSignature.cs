// Copyright © 2026 Oleksandr Kukhtin. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace XamlLanguageServer;

// A name-only view of a type decoded from a metadata signature blob. We never
// materialise a System.Type — completion only ever compares simple names — so the
// decoder yields these lightweight refs. A generic instantiation keeps its open
// type's name and arguments so a content collection's element type can be read
// (List`1<TableRow> -> "TableRow"). Handle is the Def/Ref of the type when it has
// one (Nil for primitives, arrays, generic instantiations resolved elsewhere),
// letting the builder inspect a same-file TypeDefinition further (base, enum).
internal sealed class MetaTypeRef
{
    public String Name { get; init; } = "";
    public String Namespace { get; init; } = "";
    public EntityHandle Handle { get; init; }
    public Boolean IsGenericInstance { get; init; }
    public IReadOnlyList<MetaTypeRef> GenericArgs { get; init; } = Array.Empty<MetaTypeRef>();
}

// Decodes signature and custom-attribute blobs into MetaTypeRef. Stateless — the
// MetadataReader is handed to each call, so one shared instance serves every
// assembly. Members our completion never needs (pointers, byref, modifiers,
// generic parameters) return a harmless pass-through or placeholder rather than
// throwing, so a stray such construct in a signature can't break index building.
internal sealed class MetaSignatureProvider :
    ISignatureTypeProvider<MetaTypeRef, Object?>,
    ICustomAttributeTypeProvider<MetaTypeRef>
{
    private static MetaTypeRef Named(String ns, String name) => new() { Namespace = ns, Name = name };

    public MetaTypeRef GetPrimitiveType(PrimitiveTypeCode typeCode) =>
        Named("System", typeCode switch
        {
            PrimitiveTypeCode.Boolean => "Boolean",
            PrimitiveTypeCode.Char => "Char",
            PrimitiveTypeCode.SByte => "SByte",
            PrimitiveTypeCode.Byte => "Byte",
            PrimitiveTypeCode.Int16 => "Int16",
            PrimitiveTypeCode.UInt16 => "UInt16",
            PrimitiveTypeCode.Int32 => "Int32",
            PrimitiveTypeCode.UInt32 => "UInt32",
            PrimitiveTypeCode.Int64 => "Int64",
            PrimitiveTypeCode.UInt64 => "UInt64",
            PrimitiveTypeCode.Single => "Single",
            PrimitiveTypeCode.Double => "Double",
            PrimitiveTypeCode.IntPtr => "IntPtr",
            PrimitiveTypeCode.UIntPtr => "UIntPtr",
            PrimitiveTypeCode.Object => "Object",
            PrimitiveTypeCode.String => "String",
            PrimitiveTypeCode.Void => "Void",
            PrimitiveTypeCode.TypedReference => "TypedReference",
            _ => "?"
        });

    public MetaTypeRef GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, Byte rawTypeKind)
    {
        var td = reader.GetTypeDefinition(handle);
        return new MetaTypeRef
        {
            Namespace = reader.GetString(td.Namespace),
            Name = reader.GetString(td.Name),
            Handle = handle
        };
    }

    public MetaTypeRef GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, Byte rawTypeKind)
    {
        var tr = reader.GetTypeReference(handle);
        return new MetaTypeRef
        {
            Namespace = reader.GetString(tr.Namespace),
            Name = reader.GetString(tr.Name),
            Handle = handle
        };
    }

    public MetaTypeRef GetTypeFromSpecification(MetadataReader reader, Object? genericContext, TypeSpecificationHandle handle, Byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public MetaTypeRef GetGenericInstantiation(MetaTypeRef genericType, ImmutableArray<MetaTypeRef> typeArguments) =>
        new()
        {
            Namespace = genericType.Namespace,
            Name = genericType.Name,
            Handle = genericType.Handle,
            IsGenericInstance = true,
            GenericArgs = typeArguments
        };

    public MetaTypeRef GetSZArrayType(MetaTypeRef elementType) => elementType;
    public MetaTypeRef GetArrayType(MetaTypeRef elementType, ArrayShape shape) => elementType;
    public MetaTypeRef GetByReferenceType(MetaTypeRef elementType) => elementType;
    public MetaTypeRef GetPointerType(MetaTypeRef elementType) => elementType;
    public MetaTypeRef GetPinnedType(MetaTypeRef elementType) => elementType;
    public MetaTypeRef GetModifiedType(MetaTypeRef modifier, MetaTypeRef unmodifiedType, Boolean isRequired) => unmodifiedType;
    public MetaTypeRef GetFunctionPointerType(MethodSignature<MetaTypeRef> signature) => Named("System", "IntPtr");
    public MetaTypeRef GetGenericMethodParameter(Object? genericContext, Int32 index) => Named("", "!!" + index);
    public MetaTypeRef GetGenericTypeParameter(Object? genericContext, Int32 index) => Named("", "!" + index);

    // ICustomAttributeTypeProvider — we only read primitive/string ctor arguments
    // (ContentPropertyAttribute(string)); the type-valued members are never hit.
    public MetaTypeRef GetSystemType() => Named("System", "Type");
    public Boolean IsSystemType(MetaTypeRef type) => type is { Namespace: "System", Name: "Type" };
    public MetaTypeRef GetTypeFromSerializedName(String name) => Named("", name);
    public PrimitiveTypeCode GetUnderlyingEnumType(MetaTypeRef type) => PrimitiveTypeCode.Int32;
}
