module Ionide.VSCode.PaketService

#r "../release/node_modules/fable-core/Fable.Core.dll"
#load "../release/node_modules/fable-import-vscode/Fable.Import.VSCode.fs"

open System
open System.Text.RegularExpressions

open Fable.Core
open Fable.Import
open Fable.Import.Browser
open Fable.Import.Node
open Fable.Import.Node.child_process

// HELPERS ----------------------------------------------
module Helpers =
    module Toml =
        [<Emit("toml.parse($0)")>]
        let parse (str : string) : 'a = failwith "JS"

    module JS =
        [<Emit("($0[$1] != undefined)")>]
        let isPropertyDefined (o: obj) (key: string) : bool = failwith "JS"

        [<Emit("(global[$0] != undefined)")>]
        let isGloballyDefined (key: string) : bool = failwith "never"

        [<Emit("($0 != undefined)")>]
        let isDefined (o: obj) : bool = failwith "never"

    // [<AutoOpen>]
    // module Bindings =
    //     type EventDelegate<'T> with
    //         [<Emit("($0($1, $2, $3))")>]
    //         member __.Add(f : 'T -> _, args : obj, disposables : Disposable[]) : unit = failwith "JS"

    // module EventHandler =
    //     let add (f : 'T -> _) (args : obj) (disposables : Disposable[]) (ev : EventDelegate<'T>) =
    //         ev.Add(f,args,disposables)

    module Promise =
        let success (a : 'T -> 'R) (pr : Promise<'T>) : Promise<'R> =
            pr?``then`` $ a |> unbox

        let bind (a : 'T -> Promise<'R>) (pr : Promise<'T>) : Promise<'R> =
            pr?bind $ a |> unbox

        let fail (a : obj -> 'T)  (pr : Promise<'T>) : Promise<'T> =
            pr.catch(unbox a)

        let either (a : 'T -> 'R) (b : obj -> 'R)  (pr : Promise<'T>) : Promise<'R> =
            pr?``then`` $ (a, b) |> unbox

        let lift<'T> (a : 'T) : Promise<'T> =
            Promise.resolve(U2.Case1 a)

        let toPromise (a : Thenable<'T>) = a |> unbox<Promise<'T>>

        let toThenable (a : Promise<'T>) = a |> unbox<Thenable<'T>>

    module VSCode =
        let getPluginPath pluginName =
            let ext = vscode.extensions.getExtension pluginName
            ext.extensionPath

    module Process =
        let isWin () = ``process``.platform = "win32"
        let isMono () = ``process``.platform = "win32" |> not

        let onExit (f : obj -> _) (proc : ChildProcess) =
            proc.on("exit", f |> unbox) |> ignore
            proc

        let onOutput (f : obj -> _) (proc : ChildProcess) =
            (proc.stdout :> NodeJS.EventEmitter).on("data", f |> unbox) |> ignore
            proc

        let onError (f : obj -> _) (proc : ChildProcess) =
            (proc.stderr :> NodeJS.EventEmitter).on("data", f |> unbox) |> ignore
            proc

        let spawn location linuxCmd (cmd : string) =
            let cmd' = if cmd = "" then [||] else cmd.Split(' ')
            let options = obj ()
            options?cwd <- vscode.workspace.rootPath
            if isWin () || linuxCmd = "" then
                child_process.spawn(location, unbox cmd', options)
            else
                let prms = Array.concat [ [|location|]; cmd']
                child_process.spawn(linuxCmd, unbox prms, options)

        let spawnWithNotification location linuxCmd (cmd : string) (outputChannel : vscode.OutputChannel) =
            spawn location linuxCmd cmd
            |> onOutput(fun e -> e.ToString () |> outputChannel.append)
            |> onError (fun e -> e.ToString () |> outputChannel.append)

        let exec location linuxCmd cmd : Promise<Error * Buffer *Buffer> =
            let options = createObj ["cwd" ==> vscode.workspace.rootPath]
            Promise.Create<Error * Buffer *Buffer>(fun resolve (error : Func<obj,_>) ->
                child_process.exec(
                    sprintf "%s%s %s" (if isWin() then "" else linuxCmd + " ") location cmd,
                    options,
                    Func<_,_,_,_>(fun e i o -> resolve$(e,i,o) |> ignore)) |> ignore)
// HELPERS ----------------------------------------------

let (</>) a b =
    if Helpers.Process.isWin ()
    then a + @"\" + b
    else a + "/" + b

let localPaketDir = vscode.workspace.rootPath </> ".paket"
let localPaket    = localPaketDir </>  "paket.exe"
let localBootstrapper = localPaketDir </> "paket.bootstrapper.exe"

let private location =
    if fs.existsSync localPaketDir then  localPaket else
    (Helpers.VSCode.getPluginPath "Ionide.Ionide-Paket") </> "bin" </> "paket.exe"

let private bootstrapperLocation =
    if fs.existsSync localPaketDir then  localBootstrapper else
    (Helpers.VSCode.getPluginPath "Ionide.Ionide-Paket") </> "bin" </> "paket.bootstrapper.exe"

let private spawnPaket cmd =
    let outputChannel = vscode.window.createOutputChannel "Paket"
    outputChannel.clear ()
    outputChannel.append (location+"\n")
    vscode.window.showInformationMessage ("Paket started", "Open")
    |> Helpers.Promise.toPromise
    |> Helpers.Promise.success(fun n ->
        if n = "Open" then outputChannel.show (2 |> unbox) )
    |> ignore

    Helpers.Process.spawnWithNotification location "mono" cmd outputChannel
    |> Helpers.Process.onExit(fun (code) ->
        if code.ToString() ="0" then
            vscode.window.showInformationMessage "Paket completed" |> ignore
        else
            vscode.window.showErrorMessage "Paket failed" |> ignore)
    |> ignore

let private execPaket cmd =
    if not (fs.existsSync location) then
        Helpers.Process.exec bootstrapperLocation "mono" ""
        |> Helpers.Promise.bind (fun _ -> Helpers.Process.exec location "mono" cmd)
    else
    Helpers.Process.exec location "mono" cmd

let private handlePaketList (error : Error, stdout : Buffer, stderr : Buffer) =
    if(stdout.toString() = "") then
        [||]
    else
        stdout.toString().Split('\n')
        |> Array.filter((<>) "" )

let UpdatePaketSilent () = Helpers.Process.spawn bootstrapperLocation "mono" ""
let Init () = "init" |> spawnPaket
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
    |> Helpers.Promise.toPromise
    |> Helpers.Promise.success (fun n ->
        if Helpers.JS.isDefined n then  sprintf "add nuget %s" n  |> spawnPaket)
    |> ignore

let AddToCurrent () =
    let fn = vscode.window.activeTextEditor.document.fileName
    if fn.EndsWith(".fsproj") then
        (vscode.window.showInputBox inputOptions)
        |> Helpers.Promise.toPromise
        |> Helpers.Promise.success (fun n ->
            if Helpers.JS.isDefined n then sprintf "add nuget %s project \"%s\"" n fn |> spawnPaket)
        |> ignore
    else
        vscode.window.showErrorMessage "fsproj file needs to be opened" |> ignore

let UpdateGroup () =
    "show-groups -s"
    |> execPaket
    |> Helpers.Promise.success (handlePaketList)
    |> (unbox >> vscode.window.showQuickPick)
    |> Helpers.Promise.toPromise
    |> Helpers.Promise.success (fun n ->
        if Helpers.JS.isDefined n then sprintf "update group %s" n |> spawnPaket)
    |> ignore

let UpdatePackage () =
    "show-installed-packages -s"
    |> execPaket
    |> Helpers.Promise.success (handlePaketList)
    |> (unbox >> vscode.window.showQuickPick)
    |> Helpers.Promise.toPromise
    |> Helpers.Promise.success (fun n -> 
        if Helpers.JS.isDefined n then 
            let group = n.Split(' ').[0].Trim()
            let name = n.Split(' ').[1].Trim()
            sprintf "update nuget %s group %s" name group |> spawnPaket)
    |> ignore

let UpdatePackageCurrent () =
    let fn = vscode.window.activeTextEditor.document.fileName
    if fn.EndsWith(".fsproj") then
        "show-installed-packages -s"
        |> execPaket
        |> Helpers.Promise.success (handlePaketList)
        |> (unbox >> vscode.window.showQuickPick)
        |> Helpers.Promise.toPromise
        |> Helpers.Promise.success (fun n -> 
            if Helpers.JS.isDefined n then 
                let group = n.Split(' ').[0].Trim()
                let name = n.Split(' ').[1].Trim()
                sprintf "update nuget %s project \"%s\" group %s" name fn group |> spawnPaket)
        |> ignore
    else
        vscode.window.showErrorMessage "fsproj file needs to be opened" |> ignore

let RemovePackage () =
    "show-installed-packages -s"
    |> execPaket
    |> Helpers.Promise.success (handlePaketList)
    |> (unbox >> vscode.window.showQuickPick)
    |> Helpers.Promise.toPromise
    |> Helpers.Promise.success (fun (n :string) -> 
        if Helpers.JS.isDefined n then 
            let group = n.Split(' ').[0].Trim()
            let name = n.Split(' ').[1].Trim()
            sprintf "remove nuget %s group %s" name group |> spawnPaket)
    |> ignore

let RemovePackageCurrent () =
    let fn = vscode.window.activeTextEditor.document.fileName
    if fn.EndsWith(".fsproj") then
        "show-installed-packages -s"
        |> execPaket
        |> Helpers.Promise.success (handlePaketList)
        |> (unbox >> vscode.window.showQuickPick)
        |> Helpers.Promise.toPromise
        |> Helpers.Promise.success (fun n -> 
            if Helpers.JS.isDefined n then 
                let group = n.Split(' ').[0].Trim()
                let name = n.Split(' ').[1].Trim()
                sprintf "remove nuget %s project \"%s\" group %s" name fn group |> spawnPaket)
        |> ignore
    else
        vscode.window.showErrorMessage "fsproj file needs to be opened" |> ignore

let activate(context: vscode.ExtensionContext) =
    let registerCommand com (f: unit->unit) =
        vscode.commands.registerCommand(com, unbox f)
        |> context.subscriptions.Add
    UpdatePaketSilent () |> ignore
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
