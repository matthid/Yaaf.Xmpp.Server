// ----------------------------------------------------------------------------
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.
// ----------------------------------------------------------------------------
module Yaaf.Xmpp.Server.LoggingHelper

open Yaaf.Logging

let level_off =  System.Diagnostics.SourceLevels.Off
let level_verb = System.Diagnostics.SourceLevels.Verbose
let level_all =  System.Diagnostics.SourceLevels.All
let level_warn = System.Diagnostics.SourceLevels.Warning
let level_info = System.Diagnostics.SourceLevels.Information
let newName () = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")
let startLogging runName levelConsole levelFile =
    //System.IO.Directory.CreateDirectory("logs") |> ignore
    let prepare level (logger:System.Diagnostics.TraceListener) =
        logger.Filter <- new System.Diagnostics.EventTypeFilter(level)
        logger.TraceOutputOptions <- System.Diagnostics.TraceOptions.None
        logger // :> System.Diagnostics.TraceListener 

    let listeners = 
        [| 
            if levelConsole <> level_off then
                yield Log.ConsoleLogger level_warn |> prepare levelConsole
            if levelFile <> level_off then
                yield new SimpleXmlWriterTraceListener("Yaaf.Xmpp.Server.Run.svclog") |> prepare levelFile
        |] : System.Diagnostics.TraceListener[] 

    let cache = System.Diagnostics.TraceEventCache()
    let name = CopyListenerHelper.cleanName runName
    let runListeners =
        listeners
        |> Array.map (Yaaf.Logging.CopyListenerHelper.duplicateListener "Yaaf.Xmpp.Run" cache name)

    let addLogging (sourceName:string) = 
        sourceName 
        |> Log.SetupNamespace (fun source ->
            match source with
            | :? IExtendedTraceSource as ext ->
                ext.Wrapped.Switch.Level <- System.Diagnostics.SourceLevels.All
                if listeners.Length > 0 then
                    source.Wrapped.Listeners.Clear()
                    //if not <| source.Listeners.Contains(MyTestClass.Listeners.[0]) then
                source.Wrapped.Listeners.AddRange(runListeners)
                source
            | _ -> failwith "unknown trace source")
    let defSource = addLogging "Yaaf.Logging"
    Log.SetUnhandledSource defSource
    let sources = [|
        addLogging "Yaaf.Xmpp"
        addLogging "Yaaf.Xmpp.Server"
        addLogging "Yaaf.Xmpp.Server.Run"
        addLogging "Yaaf.Xmpp.Server.Scripting"
        addLogging "Yaaf.Xmpp.Runtime"
        addLogging "Yaaf.Xmpp.Runtime.Server"
        addLogging "Yaaf.Xmpp.Runtime.Features"
        addLogging "Yaaf.Xmpp.MessageArchiveManager"
        addLogging "Yaaf.Xmpp.MessageArchiveManager.IMAP"
        addLogging "Yaaf.Xmpp.MessageArchiveManager.IMAP.GoogleChat"
        addLogging "Yaaf.Xmpp.IM"
        addLogging "Yaaf.Xmpp.IM.Server"
        addLogging "Yaaf.Xmpp.IM.Sql"
        addLogging "Yaaf.Xmpp.IM.Sql.Model"
        addLogging "Yaaf.Xmpp.IM.Sql.MySql"
        addLogging "Yaaf.Xmpp.IM.MessageArchiving"
        addLogging "Yaaf.Xmpp.IM.MessageArchiving.Server"
        addLogging "Yaaf.Xmpp.Configuration"
        addLogging "Yaaf.Sasl" 
        addLogging "Yaaf.Xml"
        addLogging "Yaaf.IO"
        addLogging "Yaaf"
        defSource
        addLogging "Yaaf.RefactorOut"
        addLogging "Yaaf.TestHelper"
        addLogging "Yaaf.RunHelper"
        addLogging "Yaaf.XmppTest" |]
    
    let tracer = Log.StartActivity (runName)
    { new System.IDisposable with
        member x.Dispose () =
            tracer.Dispose()
            sources |> Seq.iter (fun s -> s.Wrapped.Listeners |> Seq.cast |> Seq.iter (fun (l:System.Diagnostics.TraceListener) -> l.Dispose() |> ignore)) 
    }