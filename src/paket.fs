[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Ionide.VSCode.PaketService

open System
open System.Text.RegularExpressions

open Fable.Core
open Fable.Import
open Fable.Import.Browser
open Fable.Import.Node
open Fable.Import.Node.child_process_types
open Fable.Core.JsInterop

open Ionide.VSCode
let (</>) a b =
    if Helpers.Process.isWin ()
    then a + @"\" + b
    else a + "/" + b

let localPaketDir = vscode.workspace.rootPath </> ".paket"

let isProject (fileName:string) = fileName.EndsWith(".fsproj") || fileName.EndsWith(".csproj") || fileName.EndsWith(".vbproj")

let localPaket    = localPaketDir </>  "paket.exe"
let localBootstrapper = localPaketDir </> "paket.bootstrapper.exe"

let pluginPath = Helpers.VSCode.getPluginPath "Ionide.Ionide-Paket"

let pluginBootstrapper = pluginPath </> "bin" </> "paket.bootstrapper.exe"

let pluginPaket = pluginPath </> "bin" </> "paket.exe"

let outputChannel = vscode.window.createOutputChannel "Paket"

let private location, private bootstrapperLocation, private localTools =
    if fs.existsSync localPaketDir then
        localPaket, localBootstrapper, true
    else
        pluginPaket, pluginBootstrapper, false

let private spawnPaket cmd =

    outputChannel.clear ()
    outputChannel.append (location+"\n")
    let startedMessage = vscode.window.setStatusBarMessage "Paket started"

    Helpers.Process.spawnWithNotification location "mono" cmd outputChannel
    |> Helpers.Process.onExit(fun (code) ->
        startedMessage.dispose() |> ignore
        if code.ToString() ="0" then
            vscode.window.setStatusBarMessage ("Paket completed", 10000.0) |> ignore
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
    |> Helpers.Promise.map (fun n ->
        if Helpers.JS.isDefined n then sprintf "add nuget %s" n  |> spawnPaket)
    |> ignore

let AddToCurrent () =
    let fn = vscode.window.activeTextEditor.document.fileName
    if isProject fn then
        (vscode.window.showInputBox inputOptions)
        |> Helpers.Promise.map (fun n ->
            if Helpers.JS.isDefined n then sprintf "add nuget %s project \"%s\"" n fn |> spawnPaket)
        |> ignore
    else
        vscode.window.showErrorMessage "project file needs to be opened" |> ignore

let UpdateGroup () =
    "show-groups -s"
    |> execPaket
    |> Helpers.Promise.map (handlePaketList)
    |> (unbox >> vscode.window.showQuickPick)
    |> Helpers.Promise.map (fun n ->
        if Helpers.JS.isDefined n then sprintf "update group %s" n |> spawnPaket)
    |> ignore

let UpdatePackage () =
    "show-installed-packages -s"
    |> execPaket
    |> Helpers.Promise.map (handlePaketList)
    |> (unbox >> vscode.window.showQuickPick)
    |> Helpers.Promise.map (fun n ->
        if Helpers.JS.isDefined n then
            let group = n.Split(' ').[0].Trim()
            let name = n.Split(' ').[1].Trim()
            sprintf "update nuget %s group %s" name group |> spawnPaket)
    |> ignore

let UpdatePackageCurrent () =
    let fn = vscode.window.activeTextEditor.document.fileName
    if isProject fn then
        "show-installed-packages -s"
        |> execPaket
        |> Helpers.Promise.map (handlePaketList)
        |> (unbox >> vscode.window.showQuickPick)
        |> Helpers.Promise.map (fun n ->
            if Helpers.JS.isDefined n then
                let group = n.Split(' ').[0].Trim()
                let name = n.Split(' ').[1].Trim()
                sprintf "update nuget %s project \"%s\" group %s" name fn group |> spawnPaket)
        |> ignore
    else
        vscode.window.showErrorMessage "project file needs to be opened" |> ignore

let RemovePackage () =
    "show-installed-packages -s"
    |> execPaket
    |> Helpers.Promise.map (handlePaketList)
    |> (unbox >> vscode.window.showQuickPick)
    |> Helpers.Promise.map (fun (n :string) ->
        if Helpers.JS.isDefined n then
            let group = n.Split(' ').[0].Trim()
            let name = n.Split(' ').[1].Trim()
            sprintf "remove nuget %s group %s" name group |> spawnPaket)
    |> ignore

let RemovePackageCurrent () =
    let fn = vscode.window.activeTextEditor.document.fileName
    if isProject fn then
        "show-installed-packages -s"
        |> execPaket
        |> Helpers.Promise.map (handlePaketList)
        |> (unbox >> vscode.window.showQuickPick)
        |> Helpers.Promise.map (fun n ->
            if Helpers.JS.isDefined n then
                let group = n.Split(' ').[0].Trim()
                let name = n.Split(' ').[1].Trim()
                sprintf "remove nuget %s project \"%s\" group %s" name fn group |> spawnPaket)
        |> ignore
    else
        vscode.window.showErrorMessage "project file needs to be opened" |> ignore

let UpdatePaketToAlpha () =
    Helpers.Process.spawn pluginBootstrapper "mono" "prerelease" |> ignore

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
    registerCommand "paket.GenerateIncludeScripts" GenerateIncludeScripts

    registerCommand "paket.UpdatePaketToPrerelease" UpdatePaketToAlpha
