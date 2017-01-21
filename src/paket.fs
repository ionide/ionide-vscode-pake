[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Ionide.VSCode.PaketService

open System
open System.Text.RegularExpressions

open Fable.Core
open Fable.Import
open Fable.Import.Node
open Fable.Import.Node.child_process_types
open Fable.Core.JsInterop

open Ionide.VSCode
open Fable.Import.vscode
open Ionide.VSCode.Helpers
let (</>) a b =
    if Process.isWin ()
    then a + @"\" + b
    else a + "/" + b

let localPaketDir = vscode.workspace.rootPath </> ".paket"

let isProject (fileName:string) = fileName.EndsWith(".fsproj") || fileName.EndsWith(".csproj") || fileName.EndsWith(".vbproj")

let localPaket    = localPaketDir </>  "paket.exe"
let localBootstrapper = localPaketDir </> "paket.bootstrapper.exe"

let pluginPath = VSCode.getPluginPath "Ionide.Ionide-Paket"

let pluginBootstrapper = pluginPath </> "bin" </> "paket.bootstrapper.exe"

let pluginPaket = pluginPath </> "bin" </> "paket.exe"

let outputChannel = vscode.window.createOutputChannel "Paket"

let private location, private bootstrapperLocation, private localTools =
    if fs.existsSync localPaketDir then
        localPaket, localBootstrapper, true
    else
        pluginPaket, pluginBootstrapper, false

let getConfig () =
    let cfg = vscode.workspace.getConfiguration()
    cfg.get ("Paket.autoshow", true)

let UpdatePaketSilent () = Process.exec bootstrapperLocation "mono" ""

let private spawnPaket cmd =
    if not (fs.existsSync location) then UpdatePaketSilent () else Promise.empty
    |> Promise.onSuccess (fun _ ->
        outputChannel.clear ()
        outputChannel.append (location+"\n")
        let startedMessage = vscode.window.setStatusBarMessage "Paket started"
        if getConfig () then outputChannel.show ()

        Process.spawnWithNotification location "mono" cmd outputChannel
        |> Process.onExit(fun (code) ->
            startedMessage.dispose() |> ignore
            if code.ToString() ="0" then
                vscode.window.setStatusBarMessage ("Paket completed", 10000.0) |> ignore
            else
                vscode.window.showErrorMessage("Paket failed", "Show")
                |> Promise.map (fun n -> if n = "Show" then outputChannel.show () )
                |> ignore)
        |> ignore
    ) |> ignore

let private execPaket cmd =
    if not (fs.existsSync location) then
        Process.exec bootstrapperLocation "mono" ""
        |> Promise.bind (fun _ -> Process.exec location "mono" cmd)
    else
    Process.exec location "mono" cmd

let private handlePaketList (error : Error, stdout : Buffer, stderr : Buffer) =
    if(stdout.toString() = "") then
        [||]
    else
        stdout.toString().Split('\n')
        |> Array.filter((<>) "" )

let Init () = "init" |> spawnPaket
let GenerateIncludeScripts () = "generate-include-scripts" |> spawnPaket
let Update () = "update" |> spawnPaket
let Install () = "install" |> spawnPaket
let Outdated () = "outdated" |> spawnPaket
let Restore () = "restore" |> spawnPaket
let AutoRestoreOn () = "auto-restore on" |> spawnPaket
let AutoRestoreOff () = "auto-restore off" |> spawnPaket
let ConvertFromNuget () = "convert-from-nuget" |> spawnPaket
let Simplify () = "simplify" |> spawnPaket

let inputOptions = createEmpty<vscode.InputBoxOptions>

let Add () =
    (vscode.window.showInputBox inputOptions)
    |> Promise.map (fun n ->
        if JS.isDefined n then sprintf "add nuget %s" n  |> spawnPaket)
    |> ignore

let AddToCurrent () =
    let fn = vscode.window.activeTextEditor.document.fileName
    if isProject fn then
        (vscode.window.showInputBox inputOptions)
        |> Promise.map (fun n ->
            if JS.isDefined n then sprintf "add nuget %s project \"%s\"" n fn |> spawnPaket)
        |> ignore
    else
        vscode.window.showErrorMessage "project file needs to be opened" |> ignore

let UpdateGroup () =
    "show-groups -s"
    |> execPaket
    |> Promise.map (handlePaketList)
    |> (unbox >> vscode.window.showQuickPick)
    |> Promise.map (fun n ->
        if JS.isDefined n then sprintf "update group %s" n |> spawnPaket)
    |> ignore

let UpdatePackage () =
    "show-installed-packages -s"
    |> execPaket
    |> Promise.map (handlePaketList)
    |> (unbox >> vscode.window.showQuickPick)
    |> Promise.map (fun n ->
        if JS.isDefined n then
            let group = n.Split(' ').[0].Trim()
            let name = n.Split(' ').[1].Trim()
            sprintf "update nuget %s group %s" name group |> spawnPaket)
    |> ignore

let UpdatePackageCurrent () =
    let fn = vscode.window.activeTextEditor.document.fileName
    if isProject fn then
        "show-installed-packages -s"
        |> execPaket
        |> Promise.map (handlePaketList)
        |> (unbox >> vscode.window.showQuickPick)
        |> Promise.map (fun n ->
            if JS.isDefined n then
                let group = n.Split(' ').[0].Trim()
                let name = n.Split(' ').[1].Trim()
                sprintf "update nuget %s project \"%s\" group %s" name fn group |> spawnPaket)
        |> ignore
    else
        vscode.window.showErrorMessage "project file needs to be opened" |> ignore

let RemovePackage () =
    "show-installed-packages -s"
    |> execPaket
    |> Promise.map (handlePaketList)
    |> (unbox >> vscode.window.showQuickPick)
    |> Promise.map (fun (n :string) ->
        if JS.isDefined n then
            let group = n.Split(' ').[0].Trim()
            let name = n.Split(' ').[1].Trim()
            sprintf "remove nuget %s group %s" name group |> spawnPaket)
    |> ignore

let RemovePackageCurrent () =
    let fn = vscode.window.activeTextEditor.document.fileName
    if isProject fn then
        "show-installed-packages -s"
        |> execPaket
        |> Promise.map (handlePaketList)
        |> (unbox >> vscode.window.showQuickPick)
        |> Promise.map (fun n ->
            if JS.isDefined n then
                let group = n.Split(' ').[0].Trim()
                let name = n.Split(' ').[1].Trim()
                sprintf "remove nuget %s project \"%s\" group %s" name fn group |> spawnPaket)
        |> ignore
    else
        vscode.window.showErrorMessage "project file needs to be opened" |> ignore

let UpdatePaketToAlpha () =
    Process.spawn pluginBootstrapper "mono" "prerelease" |> ignore

[<Emit("setTimeout($0,$1)")>]
let setTimeout(cb, delay) : obj = failwith "JS Only"

let private createDependenciesProvider () =
    let mutable resolve : (string -> unit) option = Some ignore
    let mutable reject : (string -> unit) option = Some ignore
    let mutable answer = ""
    let mutable busy = true

    let proc =
        Process.spawn pluginPaket "mono" "find-packages"
        |> Process.onOutput (fun n ->
            resolve |> Option.iter (fun res ->
                let output = n.ToString()
                answer <- answer + output
                if answer.Contains "Please enter search text" then
                    res answer
                    resolve <- None
                    reject <- None
                    answer <- ""
                    busy <-false ))
        |> Process.onErrorOutput (fun n ->
            reject |> Option.iter (fun rej ->
                let output = n.ToString()
                rej answer
                resolve <- None
                reject <- None
                answer <- ""
                busy <-false ))

    let delay ms =
        Promise.create(fun res rej -> setTimeout(res, ms) |> ignore )

    let rec send (cmd : string) =
        if not busy then
            busy <- true
            proc.stdin?write $ (cmd + "\n")
            Promise.create (fun res rej ->
                resolve <- Some res
                reject <- Some rej )
        else
            delay 100
            |> Promise.bind (fun _ -> send cmd)



    {   new CompletionItemProvider
        with
            member this.provideCompletionItems(doc, pos, ct) =
                promise {
                    let range = doc.getWordRangeAtPosition pos
                    let line = doc.getText( Range(pos.line,0.,pos.line,1000.) )
                    let tags = line.Split(' ') |> Array.filter ((<>) "") |> Array.toList
                    let word = doc.getText range
                    let! response =
                        let isRangeDefined = JS.isDefined range
                        let (|PaketTag|_|) items =
                            match items, isRangeDefined with
                            | [ word; _ ], true -> Some(PaketTag word)
                            | _ -> None
                        let concatAndLift = String.concat "\n" >> Promise.lift
                        match tags with
                        | [ _ ] ->
                            ["nuget"; "git"; "github"; "http"; "gist"; "versions"; "source"; "group"
                             "references: strinct"; "framework:"; "content: none"; "copy_content_to_output_dir: always"
                             "import_targets:"; "copy_local:"; "redirects:"; "strategy:"; "lowest_matching:" ]
                            |> concatAndLift
                        | PaketTag "nuget" -> send word
                        | PaketTag "source" -> [ "https://api.nuget.org/v3/index.json"; "https://nuget.org/api/v2" ] |> concatAndLift
                        | PaketTag "framework:" -> [ "net35"; "net40"; "net45"; "net46"; "auto-detect" ] |> concatAndLift
                        | PaketTag "redirects:" -> [ "on"; "off"; "force" ] |> concatAndLift
                        | PaketTag "strategy:" -> [ "min"; "max" ] |> concatAndLift
                        | PaketTag "lowest_matching:" -> [ "true"; "false" ] |> concatAndLift
                        | PaketTag "import_targets:" -> [ "true"; "false" ] |> concatAndLift
                        | PaketTag "copy_local:" -> [ "true"; "false" ] |> concatAndLift
                        | _ -> Promise.lift ""
                    return
                        response.Split '\n'
                        |> Seq.map(fun n -> CompletionItem <| n.Trim())
                        |> ResizeArray
                } |> Case2

            member this.resolveCompletionItem(sug, ct) =
                promise {
                    return sug
                } |> Case2
    }

let private createReferencesProvider () =
    {   new CompletionItemProvider
        with
            member this.provideCompletionItems(doc, pos, ct) =
                "show-installed-packages -s"
                |> execPaket
                |> Promise.map (handlePaketList)
                |> Promise.map (Seq.map(fun n -> n.Trim().Split(' ').[1] |> CompletionItem ))
                |> Promise.map (ResizeArray)
                |> Case2

            member this.resolveCompletionItem(sug, ct) =
                promise {
                    return sug
                } |> Case2
    }

let activate(context: vscode.ExtensionContext) =
    let registerCommand com (f: unit->unit) =
        vscode.commands.registerCommand(com, unbox<Func<obj,obj>> f)
        |> context.subscriptions.Add


    let df = createEmpty<DocumentFilter>
    df.language <- Some "paket-dependencies"
    let selector : DocumentSelector = df |> U3.Case2

    let df = createEmpty<DocumentFilter>
    df.language <- Some "paket-references"
    let referencesSelector : DocumentSelector = df |> U3.Case2


    languages.registerCompletionItemProvider(selector, createDependenciesProvider())
    |> ignore

    languages.registerCompletionItemProvider(referencesSelector, createReferencesProvider())
    |> ignore


    registerCommand "paket.Init" Init
    registerCommand "paket.Install" Install
    registerCommand "paket.Update" Update
    registerCommand "paket.Outdated" Outdated
    registerCommand "paket.Restore" Restore
    registerCommand "paket.AutoRestoreOn" AutoRestoreOn
    registerCommand "paket.AutoRestoreOff" AutoRestoreOff
    registerCommand "paket.ConvertFromNuget" ConvertFromNuget
    registerCommand "paket.Simplify" Simplify
    registerCommand "paket.Add" Add
    registerCommand "paket.AddToCurrent" AddToCurrent
    registerCommand "paket.UpdateGroup" UpdateGroup
    registerCommand "paket.UpdatePackage" UpdatePackage
    registerCommand "paket.UpdatePackageCurrent" UpdatePackageCurrent
    registerCommand "paket.RemovePackage" RemovePackage
    registerCommand "paket.RemovePackageCurrent" RemovePackageCurrent
    registerCommand "paket.GenerateIncludeScripts" GenerateIncludeScripts

    registerCommand "paket.UpdatePaketToPrerelease" UpdatePaketToAlpha
