namespace FSharp.Data.GraphQL.Client

open System
// open System.Reflection
open System.Collections.Generic
// open FSharp.Data.GraphQL
// open FSharp.Data.GraphQL.Types
// open FSharp.Data.GraphQL.Types.Introspection
// open FSharp.Data.GraphQL.Introspection
// open ProviderImplementation.ProvidedTypes
// open Microsoft.FSharp.Core.CompilerServices
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Reflection

module QuotationHelpers =
    let makeExprArray (exprs: Expr list) =
        Expr.NewArray(typeof<obj>, exprs |> List.map (fun e -> Expr.Coerce(e, typeof<obj>)))

    #if FABLE
    open Fable
    open Fable.Core
    open Fable.Core.JsInterop
    open Fable.AST
    open Fable.AST.Fable.Util

    type GraphQlFableEmitter() =
        member __.BuildQuery (com: ICompiler) (info: Fable.ApplyInfo) =
            ImportCall (Naming.fableInjectFile, "graphqlBuildQuery", None, false, info.args)
            |> makeCall com info.range info.returnType

        member __.LaunchQuery (com: ICompiler) (info: Fable.ApplyInfo) =
            ImportCall (Naming.fableInjectFile, "graphqlLaunchQuery", None, false, info.args)
            |> makeCall com info.range info.returnType

    type GraphQlFablePlugin() =
        // Ignore cont (arg2) for now
        let getLaunchQueryBody com args =
            """fetch($0, {
                method: 'post',
                body: JSON.stringify({query: $3})
            })
            .then(resp => resp.json())
            .then(resp => new Promise((resolve, reject) => {
                if (resp.errors) {
                    reject(errors.join('\n'))
                }
                else {
                    resolve($1 ? resp.data[$1] : resp.data);
                }
            }))"""
            |> makeEmit args
            |> fun p -> CoreLibCall ("Async", Some "awaitPromise", false, [p])
            |> makeCall com None Fable.Any

        let getBuildQueryBody com args =
            """var i = 0, openBraces = 0, closeBraces = 0;
            while (closeBraces == 0 || closeBraces < 0) {
                switch($1[i]) {
                    case "{": openBraces++; break;
                    case "}": closeBraces++; break;
                }
                i++;
            }
            var queryFields = $1.substr(0, i), queryFragments = $1.substr(i);
            var args = $2.map((k, i) => k + ": " + JSON.stringify($3[i])).join(", ");
            "{ " + $0 + "(" + args + ") " + queryFields + " }" + queryFragments;"""
            |> makeEmit args

        interface IInjectPlugin with
            member __.Inject com =
              [ { new IInjection with
                    member __.Name = "graphqlLaunchQuery"
                    member __.ArgumentsLength = 4
                    member __.GetBody args = getLaunchQueryBody com args }
                { new IInjection with
                    member __.Name = "graphqlBuildQuery"
                    member __.ArgumentsLength = 4
                    member __.GetBody args = getBuildQueryBody com args } ]

    let getDynamicField (name: string) (expr: Expr) =
        <@@ (%%expr: obj)?(name) @@>

    let makeOption (optType: Type) (expr: Expr) =
        expr

    [<Emit(typeof<GraphQlFableEmitter>, "LaunchQuery")>]
    let launchQuery (serverUrl: string) (queryName: string) (cont: obj->'T) (query: string)
                    : Async<'T> =
        failwith "JS only"

    [<Emit(typeof<GraphQlFableEmitter>, "BuildQuery")>]
    let buildQuery (queryName: string) (queryFields: string)
                   (argNames: string[]) (argValues: obj[]): string =
        failwith "JS only"
    #else
    open System.Net
    open Newtonsoft.Json
    open Newtonsoft.Json.Linq

    let getDynamicField (name: string) (expr: Expr) =
        let dicType = typeof<IDictionary<string,obj>>
        let mi = dicType.GetMethod("get_Item")
        Expr.Call(Expr.Coerce(expr, dicType), mi, [Expr.Value(name)])

    let makeOption (optType: Type) (expr: Expr) =
        let optArg = optType.GetGenericArguments().[0]
        let cases =
            FSharpType.GetUnionCases(optType)
            |> Seq.map (fun case -> case.Name, case)
            |> Map
        let var = Var("instance", typeof<obj>)
        Expr.Let(var, expr,
            Expr.IfThenElse(
                <@@ %%Expr.Var(var) = null @@>,
                Expr.NewUnionCase(cases.["None"], []),
                Expr.NewUnionCase(cases.["Some"], [Expr.Coerce(Expr.Var var, optArg)])))

    let rec jsonToObject (token: JToken) =
        match token.Type with
        | JTokenType.Object ->
            token.Children<JProperty>()
            |> Seq.map (fun prop -> prop.Name, jsonToObject prop.Value)
            |> dict :> obj
        | JTokenType.Array ->
            token |> Seq.map jsonToObject |> Seq.toArray :> obj
        | _ ->
            (token :?> JValue).Value

    let launchQuery (serverUrl: string) (queryName: string) (cont: obj->'T) (query: string) =
        async {
            use client = new WebClient()
            let queryJson = Map["query", query] |> JsonConvert.SerializeObject
            let! json = client.UploadStringTaskAsync(Uri(serverUrl), queryJson) |> Async.AwaitTask
            let res = JToken.Parse json |> jsonToObject :?> IDictionary<string,obj>
            if res.ContainsKey("errors") then
                res.["errors"] :?> obj[] |> Seq.map string |> String.concat "\n" |> failwith
            let data =
                // Options are problematic within quotations so we just use null here
                if queryName <> null
                then (res.["data"] :?> IDictionary<string,obj>).[queryName]
                else res.["data"]
            return cont(data)
        }

    let buildQuery (queryName: string) (queryFields: string)
                   (argNames: string[]) (argValues: obj[]) =
        let queryFields, queryFragments =
            let mutable i = 0
            let mutable openBraces = 0
            let mutable closeBraces = 0
            while closeBraces = 0 || closeBraces < openBraces do
                match queryFields.Chars(i) with
                | '{' -> openBraces <- openBraces + 1
                | '}' -> closeBraces <- closeBraces + 1
                | _ -> ()
                i <- i + 1
            queryFields.Substring(0, i), queryFields.Substring(i)
        Seq.zip argNames argValues
        |> Seq.map (fun (k,v) -> sprintf "%s: %s" k (JsonConvert.SerializeObject v))
        |> String.concat ", "
        |> fun args -> sprintf "{ %s(%s) %s }%s" queryName args queryFields queryFragments
    #endif