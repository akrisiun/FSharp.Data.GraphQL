﻿/// The MIT License (MIT)
/// Copyright (c) 2016 Bazinga Technologies Inc

namespace FSharp.Data.GraphQL.Types

open System
open FSharp.Data.GraphQL.Ast

type ResolveInfo = 
    {
        FieldDefinition: FieldDefinition
        Fields: Field list
        Fragments: Map<string, FragmentDefinition>
        RootValue: obj
    }
    
//NOTE: For references, see https://facebook.github.io/graphql/
and GraphQLError = GraphQLError of string
and Args = 
    {
        Args: Map<string, obj>
    }
    member x.Arg(name: string): 't option =
        match Map.tryFind name x.Args with
        | Some o -> Some (o :?> 't)
        | None -> None

/// 3.1.1.1 Build-in Scalars
and [<CustomEquality;NoComparison>] ScalarType = 
    {
        Name: string
        Description: string option
        CoerceInput: Value -> obj
        CoerceOutput: obj -> Value option
        CoerceValue: obj -> obj option
    }
    interface IEquatable<ScalarType> with
      member x.Equals s = x.Name = s.Name
    override x.Equals y = 
        match y with
        | :? ScalarType as s -> (x :> IEquatable<ScalarType>).Equals(s)
        | _ -> false
    override x.GetHashCode() = x.Name.GetHashCode()
    override x.ToString() = x.Name

and EnumValue = 
    {
        Name: string
        Value: obj
        Description: string option
    }
    override x.ToString() = x.Name

and EnumType = 
    {
        Name: string
        Description: string option
        Options: EnumValue list
    }
    override x.ToString() = 
        sprintf "enum %s {\n    %s\n}" x.Name (String.Join("\n    ", x.Options))

/// 3.1.2 Objects
and ObjectType = 
    {
        Name: string
        Description: string option
        Fields: FieldDefinition list
        Implements: GraphQLType list
    }
    override x.ToString() =
        let sb = System.Text.StringBuilder("type ")
        sb.Append(x.Name) |> ignore
        if not (List.isEmpty x.Implements) then 
            sb.Append(" implements ").Append(String.Join(", ", x.Implements)) |> ignore
        sb.Append("{") |> ignore
        x.Fields
        |> List.iter (fun f -> sb.Append("\n    ").Append(f.ToString()) |> ignore)
        sb.Append("\n}").ToString()

and [<CustomEquality;NoComparison>] FieldDefinition = 
    {
        Name: string
        Description: string option
        Type: GraphQLType
        Resolve: obj -> Args -> ResolveInfo -> obj
        Arguments: ArgumentDefinition list
    }
    interface IEquatable<FieldDefinition> with
      member x.Equals f = 
            x.Name = f.Name && 
            x.Description = f.Description &&
            x.Type = f.Type &&
            x.Arguments = f.Arguments
    override x.Equals y = 
        match y with
        | :? FieldDefinition as f -> (x :> IEquatable<FieldDefinition>).Equals(f)
        | _ -> false
    override x.GetHashCode() = 
        let mutable hash = x.Name.GetHashCode()
        hash <- (hash*397) ^^^ (match x.Description with | None -> 0 | Some d -> d.GetHashCode())
        hash <- (hash*397) ^^^ (x.Type.GetHashCode())
        hash <- (hash*397) ^^^ (x.Arguments.GetHashCode())
        hash
    override x.ToString() = 
        let mutable s = x.Name + ": " + x.Type.ToString()
        if not (List.isEmpty x.Arguments) then
            s <- "(" + String.Join(", ", x.Arguments) + ")"
        s

/// 3.1.3 Interfaces
and InterfaceType =
    {
        Name: string
        Description: string option
        Fields: FieldDefinition list
    }
    override x.ToString() = 
        let sb = System.Text.StringBuilder("interface ").Append(x.Name).Append(" {")
        x.Fields
        |> List.iter (fun f -> sb.Append("\n    ").Append(f.ToString()) |> ignore)
        sb.Append("\n}").ToString()        

/// 3.1.4 Unions
and UnionType = 
    {
        Name: string
        Description: string option
        Options: GraphQLType list
    }
    override x.ToString() =
        "union " + x.Name + " = " + String.Join(" | ", x.Options)



/// 3.1 Types
and GraphQLType =
    | Scalar of ScalarType
    | Enum of EnumType
    | Object of ObjectType
    | Interface of InterfaceType
    | Union of UnionType
    | ListOf of GraphQLType
    | NonNull of GraphQLType
    | InputObject of ObjectType
    override x.ToString() =
        match x with
        | Scalar y      -> y.ToString()
        | Enum y        -> y.ToString()
        | Object y      -> y.ToString()
        | Interface y   -> y.ToString()
        | Union y       -> y.ToString()
        | ListOf y      -> "[" + y.ToString() + "]"
        | NonNull y     -> y.ToString() + "!"
        | InputObject y -> y.ToString()
    member x.Name =
        match x with
        | Scalar s      -> s.Name
        | Enum e        -> e.Name
        | Object o      -> o.Name
        | Interface i   -> i.Name
        | Union u       -> u.Name
        | ListOf i      -> i.Name
        | NonNull i     -> i.Name
        | InputObject o -> o.Name

/// 3.1.6 Input Objects
and InputObject = 
    {
        Name: string
        Fields: FieldDefinition list
    }

/// 3.1.2.1 Object Field Arguments
and ArgumentDefinition = 
    {
        Name: string
        Description: string option
        Type: GraphQLType
        DefaultValue: obj option
    }
    override x.ToString() = x.Name + ": " + x.Type.ToString() + (if x.DefaultValue.IsSome then " = " + x.DefaultValue.Value.ToString() else "")

/// 5.7 Variables
and Variable = 
    {
        Name: string
        Schema: GraphQLType
        DefaultValue: obj
    }
    override x.ToString() =
        "$" + x.Name + ": " + x.Schema.ToString() + (if x.DefaultValue <> null then " = " + x.DefaultValue.ToString() else "")
                    
type GraphQLException(msg) = 
    inherit Exception(msg)
    
[<AutoOpen>]
module SchemaDefinitions =

    open System.Globalization

    let internal coerceIntValue (x: obj) : int option = 
        match x with
        | null -> None
        | :? int as i -> Some i
        | :? int64 as l -> Some (int l)
        | :? double as d -> Some (int d)
        | :? string as s -> 
            match Int32.TryParse(s) with
            | true, i -> Some i
            | false, _ -> None
        | :? bool as b -> Some (if b then 1 else 0)
        | other ->
            try
                Some (System.Convert.ToInt32 other)
            with
            | _ -> None
            
    let internal coerceFloatValue (x: obj) : double option = 
        match x with
        | null -> None
        | :? int as i -> Some (double i)
        | :? int64 as l -> Some (double l)
        | :? double as d -> Some d
        | :? string as s -> 
            match Double.TryParse(s) with
            | true, i -> Some i
            | false, _ -> None
        | :? bool as b -> Some (if b then 1. else 0.)
        | other ->
            try
                Some (System.Convert.ToDouble other)
            with
            | _ -> None
            
    let internal coerceBoolValue (x: obj) : bool option = 
        match x with
        | null -> None
        | :? int as i -> Some (i <> 0)
        | :? int64 as l -> Some (l <> 0L)
        | :? double as d -> Some (d <> 0.)
        | :? string as s -> 
            match Boolean.TryParse(s) with
            | true, i -> Some i
            | false, _ -> None
        | :? bool as b -> Some b
        | other ->
            try
                Some (System.Convert.ToBoolean other)
            with
            | _ -> None

    let private coerceIntOuput (x: obj) =
        match x with
        | :? int as y -> Some (IntValue y)
        | _ -> None
        
    let private coerceFloatOuput (x: obj) =
        match x with
        | :? float as y -> Some (FloatValue y)
        | _ -> None
        
    let private coerceBoolOuput (x: obj) =
        match x with
        | :? bool as y -> Some (BooleanValue y)
        | _ -> None
        
    let private coerceStringOuput (x: obj) =
        match x with
        | :? string as y -> Some (StringValue y)
        | _ -> None

    let internal coerceStringValue (x: obj) : string option = 
        match x with
        | null -> None
        | :? bool as b -> Some (if b then "true" else "false")
        | other -> Some (other.ToString())

    let private coerceIntInput = function
        | IntValue i -> Some i
        | FloatValue f -> Some (int f)
        | StringValue s -> 
            match Int32.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture) with
            | true, i -> Some i
            | false, _ -> None
        | BooleanValue b -> Some (if b then 1 else 0)
        | _ -> None

    let private coerceFloatInput = function
        | IntValue i -> Some (double i)
        | FloatValue f -> Some f
        | StringValue s -> 
            match Double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture) with
            | true, i -> Some i
            | false, _ -> None
        | BooleanValue b -> Some (if b then 1. else 0.)
        | _ -> None

    let private coerceStringInput = function
        | IntValue i -> Some (i.ToString(CultureInfo.InvariantCulture))
        | FloatValue f -> Some (f.ToString(CultureInfo.InvariantCulture))
        | StringValue s -> Some s
        | BooleanValue b -> Some (if b then "true" else "false")
        | _ -> None

    let private coerceBoolInput = function
        | IntValue i -> Some (if i = 0 then false else true)
        | FloatValue f -> Some (if f = 0. then false else true)
        | StringValue s -> 
            match Boolean.TryParse(s) with
            | true, i -> Some i
            | false, _ -> None
        | BooleanValue b -> Some b
        | _ -> None

    let private coerceIdInput = function
        | IntValue i -> Some (i.ToString())
        | StringValue s -> Some s
        | _ -> None

    /// GraphQL type of int
    let Int: GraphQLType = 
        Scalar {
            Name = "Int"
            Description = Some "The `Int` scalar type represents non-fractional signed whole numeric values. Int can represent values between -(2^31) and 2^31 - 1."
            CoerceInput = coerceIntInput >> box
            CoerceValue = coerceIntValue >> Option.map box
            CoerceOutput = coerceIntOuput
        }

    /// GraphQL type of boolean
    let Bool: GraphQLType = 
        Scalar {
            Name = "Boolean"
            Description = Some "The `Boolean` scalar type represents `true` or `false`."
            CoerceInput = coerceBoolInput >> box
            CoerceValue = coerceBoolValue >> Option.map box
            CoerceOutput = coerceBoolOuput
        }

    /// GraphQL type of float
    let Float: GraphQLType = 
        Scalar {
            Name = "Float"
            Description = Some "The `Float` scalar type represents signed double-precision fractional values as specified by [IEEE 754](http://en.wikipedia.org/wiki/IEEE_floating_point)."
            CoerceInput = coerceFloatInput >> box
            CoerceValue = coerceFloatValue >> Option.map box
            CoerceOutput = coerceFloatOuput
        }
    
    /// GraphQL type of string
    let String: GraphQLType = 
        Scalar {
            Name = "String"
            Description = Some "The `String` scalar type represents textual data, represented as UTF-8 character sequences. The String type is most often used by GraphQL to represent free-form human-readable text."
            CoerceInput = coerceStringInput >> box
            CoerceValue = coerceStringValue >> Option.map box
            CoerceOutput = coerceStringOuput
        }
    
    /// GraphQL type for custom identifier
    let ID: GraphQLType = 
        Scalar {
            Name = "ID"
            Description = Some "The `ID` scalar type represents a unique identifier, often used to refetch an object or as key for a cache. The ID type appears in a JSON response as a String; however, it is not intended to be human-readable. When expected as an input type, any string (such as `\"4\"`) or integer (such as `4`) input value will be accepted as an ID."
            CoerceInput = coerceIdInput >> box
            CoerceValue = coerceStringValue >> Option.map box
            CoerceOutput = coerceStringOuput
        }

    let rec internal coerceAstValue (variables: Map<string, obj option>) (value: Value) : obj =
        match value with
        | IntValue i -> upcast i
        | StringValue s -> upcast s
        | FloatValue f -> upcast f
        | BooleanValue b -> upcast b
        | EnumValue e -> upcast e
        | ListValue values ->
            let mapped =
                values
                |> List.map (coerceAstValue variables)
            upcast mapped
        | ObjectValue fields ->
            let mapped =
                fields
                |> Map.map (fun k v -> coerceAstValue variables v)
            upcast mapped
        | Variable variable -> variables.[variable] |> Option.toObj

    /// Adds a single field to existing object type, returning new object type in result.
    let mergeField (objectType: ObjectType) (field: FieldDefinition) : ObjectType = 
        match objectType.Fields |> Seq.tryFind (fun x -> x.Name = field.Name) with
        | None ->  { objectType with Fields = objectType.Fields @ [ field ] }     // we must append to the end
        | Some x when x = field -> objectType
        | Some x -> 
            let msg = sprintf "Cannot merge field %A into object type %s, because it already has field %A sharing the same name, but having a different signature." field objectType.Name x
            raise (GraphQLException msg)

    /// Adds list of fields to existing object type, returning new object type in result.
    let mergeFields (objectType: ObjectType) (fields: FieldDefinition list) : ObjectType = 
        fields
        |> List.fold mergeField objectType      //TODO: optimize

    /// Orders object type to implement collection of interfaces, applying all of their field to it.
    /// Returns new object type implementing all of the fields in result.
    let implements (objectType: GraphQLType) (interfaces: InterfaceType list) : GraphQLType =
        let o = 
            match objectType with
            | Object x -> { x with Implements = x.Implements @ (interfaces |> List.map (fun x -> Interface x)) }
            | other -> failwith ("Expected GrapQL type to be an object but got " + other.ToString())
        let modified = 
            interfaces
            |> List.map (fun i -> i.Fields)
            |> List.fold mergeFields o
        Object modified

    let internal defaultResolve<'t> (fieldName: string): (obj -> Args -> ResolveInfo -> obj) = 
        let getter = typeof<'t>.GetProperty(fieldName).GetMethod
        (fun v _ _ -> getter.Invoke(v, [||]))